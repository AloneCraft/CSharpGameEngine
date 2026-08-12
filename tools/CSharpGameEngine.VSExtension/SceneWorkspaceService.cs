using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Supplies the editor with a <see cref="SceneModel"/> built from <b>live Roslyn</b> (ADR-0040).
///
/// <para>
/// This replaces the previous execution-based path, which had to build the target project, run the
/// generated CreateRuntime in a separate process and read a WorldSnapshot reflectively. Reading the
/// declaration directly means the scene is visible while the user is still typing it, and there is
/// nothing to keep in sync with the Source Generator: both run the same shared
/// <see cref="SceneDeclarationAnalyzer"/> source.
/// </para>
///
/// <para>
/// Threading, in order of what has already gone wrong in this repo: Roslyn work never runs on the
/// UI thread; the result is marshaled back with SwitchToMainThreadAsync before anything touches WPF;
/// and nothing here is awaited from inside AsyncPackage.InitializeAsync's own chain. Analysis is
/// debounced because a keystroke-rate rebuild of every scene would make typing feel heavy, and the
/// previous CancellationTokenSource is cancelled so a slow analysis cannot overwrite a newer one.
/// </para>
/// </summary>
internal sealed class SceneWorkspaceService : IDisposable {
    /// <summary>
    /// Long enough that a burst of keystrokes collapses into one analysis, short enough that the
    /// tree still feels attached to the code being typed.
    /// </summary>
    private static readonly TimeSpan DebounceDelay=TimeSpan.FromMilliseconds(250);

    private readonly JoinableTaskFactory TaskFactory;
    private readonly VisualStudioWorkspace Workspace;
    private readonly object Gate=new();
    private System.Threading.Timer?DebounceTimer;
    private CancellationTokenSource?PendingAnalysis;

    /// <summary>
    /// The in-flight analysis. Held rather than discarded so the fire-and-forget stays observable
    /// (VSTHRD110) and so <see cref="Dispose"/> can join it instead of letting a callback run
    /// against a disposed service.
    /// </summary>
    private JoinableTask?RunningAnalysis;

    private DocumentId?ActiveDocumentId;
    private bool Disposed;

    private SceneWorkspaceService(VisualStudioWorkspace Workspace,JoinableTaskFactory TaskFactory){
        this.Workspace=Workspace;
        this.TaskFactory=TaskFactory;
        this.Workspace.WorkspaceChanged+=this.OnWorkspaceChanged;
    }

    internal static SceneWorkspaceService?Instance { get;private set;}

    /// <summary>The most recent successful analysis. Kept so a transient failure never blanks the UI.</summary>
    internal SceneModel?CurrentModel { get;private set;}

    internal event Action<SceneModel?>?ModelChanged;

    /// <summary>
    /// Acquires the workspace through <c>SComponentModel</c> rather than exporting a MEF part - this
    /// VSIX has no MEF components and adding one just to pull a service would be a larger change
    /// than the service is worth. Returns null (rather than throwing) when the workspace is not
    /// available, so a VS instance without the managed language service simply has no Scene editor.
    /// </summary>
    internal static SceneWorkspaceService?Initialize(){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(Instance is not null){
            return Instance;
        }

        if(Package.GetGlobalService(typeof(SComponentModel))is not IComponentModel ComponentModel){
            return null;
        }

        VisualStudioWorkspace?Workspace=ComponentModel.GetService<VisualStudioWorkspace>();

        if(Workspace is null){
            return null;
        }

        Instance=new SceneWorkspaceService(Workspace,ThreadHelper.JoinableTaskFactory);
        return Instance;
    }

    /// <summary>Points the service at a document; passing null clears the model.</summary>
    internal void SetActiveDocument(DocumentId?DocumentId){
        this.ActiveDocumentId=DocumentId;
        this.ScheduleAnalysis();
    }

    /// <summary>Resolves a file path to a document in the current solution. Null when the file is not part of it.</summary>
    internal DocumentId?FindDocument(string FilePath){
        return this.Workspace.CurrentSolution.GetDocumentIdsWithFilePath(FilePath).FirstOrDefault();
    }

    private void OnWorkspaceChanged(object Sender,WorkspaceChangeEventArgs Args){
        // Only the kinds that can change a scene declaration. Reacting to everything would rebuild
        // on unrelated solution churn.
        switch(Args.Kind){
            case WorkspaceChangeKind.DocumentChanged:
            case WorkspaceChangeKind.DocumentAdded:
            case WorkspaceChangeKind.DocumentRemoved:
            case WorkspaceChangeKind.DocumentReloaded:
            case WorkspaceChangeKind.ProjectChanged:
            case WorkspaceChangeKind.ProjectReloaded:
            case WorkspaceChangeKind.SolutionReloaded:
            case WorkspaceChangeKind.SolutionChanged:
                this.ScheduleAnalysis();
                break;
        }
    }

    private void ScheduleAnalysis(){
        lock(this.Gate){
            if(this.Disposed){
                return;
            }

            this.DebounceTimer?.Dispose();
            this.DebounceTimer=new System.Threading.Timer(
                static State=>((SceneWorkspaceService)State).StartAnalysis(),
                this,
                DebounceDelay,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void StartAnalysis(){
        CancellationTokenSource Source;

        lock(this.Gate){
            if(this.Disposed){
                return;
            }

            this.PendingAnalysis?.Cancel();
            this.PendingAnalysis?.Dispose();
            this.PendingAnalysis=new CancellationTokenSource();
            Source=this.PendingAnalysis;
        }

        // Started from a timer callback, so there is nothing to await it here. RunAsync keeps the
        // work joinable, and the handle is stored so VS can still block on it during shutdown.
        this.RunningAnalysis=this.TaskFactory.RunAsync(async ()=>{
            try {
                SceneModel?Model= await this.AnalyzeAsync(Source.Token).ConfigureAwait(false);

                // A cancelled analysis must not overwrite the newer one that cancelled it.
                if(Source.Token.IsCancellationRequested){
                    return;
                }

                await this.TaskFactory.SwitchToMainThreadAsync(Source.Token);
                this.CurrentModel=Model;
                this.ModelChanged?.Invoke(Model);
            } catch(OperationCanceledException){
                // Superseded by a newer analysis.
            } catch(Exception){
                // Never let a transient analysis failure take down VS or blank the tree; the last
                // good model stays visible. Incomplete syntax is expected here, not exceptional -
                // SceneDeclarationAnalyzer is built not to throw on it, and this is the backstop.
            }
        });
    }

    private async Task<SceneModel?>AnalyzeAsync(CancellationToken CancellationToken){
        DocumentId?DocumentId=this.ActiveDocumentId;

        if(DocumentId is null){
            return null;
        }

        Document?Document=this.Workspace.CurrentSolution.GetDocument(DocumentId);

        if(Document is null){
            return null;
        }

        Compilation?Compilation= await Document.Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        SyntaxNode?Root= await Document.GetSyntaxRootAsync(CancellationToken).ConfigureAwait(false);
        SemanticModel?SemanticModel= await Document.GetSemanticModelAsync(CancellationToken).ConfigureAwait(false);

        if(Compilation is null||Root is null||SemanticModel is null){
            return null;
        }

        // Only scenes declared in *this* document. A project may hold several, and showing whichever
        // one happened to be found first would make the tree depend on compilation order.
        foreach(INamedTypeSymbol Type in EnumerateDeclaredTypes(Root,SemanticModel,CancellationToken)){
            if(SceneTypePredicates.ImplementsSceneInterface(Type)){
                return SceneDeclarationAnalyzer.Analyze(Type,Compilation,CancellationToken);
            }
        }

        return null;
    }

    private static IEnumerable<INamedTypeSymbol>EnumerateDeclaredTypes(SyntaxNode Root,SemanticModel SemanticModel,CancellationToken CancellationToken){
        foreach(SyntaxNode Node in Root.DescendantNodes()){
            CancellationToken.ThrowIfCancellationRequested();

            if(!SceneDeclarationAnalyzer.IsSceneCandidate(Node)){
                continue;
            }

            if(SemanticModel.GetDeclaredSymbol(Node,CancellationToken)is INamedTypeSymbol Type){
                yield return Type;
            }
        }
    }

    public void Dispose(){
        lock(this.Gate){
            if(this.Disposed){
                return;
            }

            this.Disposed=true;
            this.DebounceTimer?.Dispose();
            this.DebounceTimer=null;
            this.PendingAnalysis?.Cancel();
            this.PendingAnalysis?.Dispose();
            this.PendingAnalysis=null;
        }

        this.Workspace.WorkspaceChanged-=this.OnWorkspaceChanged;
        Instance=null;
    }
}
