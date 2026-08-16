using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;

namespace CSharpGameEngine.VSExtension;

internal sealed class SceneWorkspaceEditContext {
    internal SceneWorkspaceEditContext(
        DocumentId DocumentId,string FilePath,SceneModel Model,SyntaxNode Root,SourceText Text){
        this.DocumentId=DocumentId;
        this.FilePath=FilePath;
        this.Model=Model;
        this.Root=Root;
        this.Text=Text;
    }

    internal DocumentId DocumentId { get; }

    internal string FilePath { get; }

    internal SceneModel Model { get; }

    internal SyntaxNode Root { get; }

    internal SourceText Text { get; }
}

internal readonly struct SceneLiveBufferWriteResult {
    internal SceneLiveBufferWriteResult(bool Succeeded,string?ErrorMessage,bool LiveTextChanged=false){
        this.Succeeded=Succeeded;
        this.ErrorMessage=ErrorMessage;
        this.LiveTextChanged=LiveTextChanged;
    }

    internal bool Succeeded { get; }

    internal string?ErrorMessage { get; }

    internal bool LiveTextChanged { get; }
}

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
    private readonly IVsEditorAdaptersFactoryService?EditorAdapters;
    private readonly SceneTextBufferWriter?TextBufferWriter;
    private readonly object Gate=new();
    private readonly SceneAnalysisGenerationGate<DocumentId>AnalysisGeneration=new();
    private System.Threading.Timer?DebounceTimer;
    private CancellationTokenSource?PendingAnalysis;
    private readonly Dictionary<ProjectId,ProjectSceneCatalogCache>ProjectSceneCatalogs=[];

    /// <summary>
    /// The in-flight analysis. Held rather than discarded so the fire-and-forget stays observable
    /// (VSTHRD110) and so <see cref="Dispose"/> can join it instead of letting a callback run
    /// against a disposed service.
    /// </summary>
    private JoinableTask?RunningAnalysis;

    private DocumentId?ActiveDocumentId;
    private string?ActiveDocumentPath;
    private SceneDocumentMenuSnapshot ActiveMenuSnapshot=new(SceneDesignerTargetState.Unavailable,[],[]);

    // ソリューションエクスプローラーで選択中のファイル用、アクティブ文書とは独立した解析スロット
    // (ADR-0041 / M0020: SEの右クリックはアクティブなコードエディタ文書と一致するとは限らないため、
    // Hierarchy/ModelChanged が依存するアクティブ文書側の状態には一切触れない別系統を持つ)。
    private DocumentId?SolutionExplorerDocumentId;
    private string?SolutionExplorerDocumentPath;
    private SceneDocumentMenuSnapshot SolutionExplorerMenuSnapshot=new(SceneDesignerTargetState.Unavailable,[],[]);
    private readonly SceneAnalysisGenerationGate<DocumentId>SolutionExplorerAnalysisGeneration=new();
    private CancellationTokenSource?PendingSolutionExplorerAnalysis;
    private JoinableTask?RunningSolutionExplorerAnalysis;

    private bool Disposed;

    private SceneWorkspaceService(
        VisualStudioWorkspace Workspace,JoinableTaskFactory TaskFactory,
        IVsEditorAdaptersFactoryService?EditorAdapters,ITextUndoHistoryRegistry?UndoHistoryRegistry){
        this.Workspace=Workspace;
        this.TaskFactory=TaskFactory;
        this.EditorAdapters=EditorAdapters;
        this.TextBufferWriter=UndoHistoryRegistry is null?null:new SceneTextBufferWriter(UndoHistoryRegistry);
        this.Workspace.WorkspaceChanged+=this.OnWorkspaceChanged;
    }

    internal static SceneWorkspaceService?Instance { get;private set;}

    /// <summary>The most recent successful analysis. Kept so a transient failure never blanks the UI.</summary>
    internal SceneModel?CurrentModel { get;private set;}

    internal event Action<SceneModel?>?ModelChanged;

    /// <summary>
    /// Every <c>IScene</c> implementation found in the active document (ADR-0040 Phase 9), not just
    /// the first - Scene Designer needs to target one specific class by name when a file declares
    /// several (e.g. PrimitiveShowcaseScene.cs). <see cref="CurrentModel"/>/<see cref="ModelChanged"/>
    /// stay unchanged (still "the first one found") so Hierarchy's existing behavior is untouched;
    /// this is computed in the same analysis pass, not a second one.
    /// </summary>
    internal IReadOnlyList<SceneModel>AllModels { get;private set;}=[];

    internal event Action<IReadOnlyList<SceneModel>>?AllModelsChanged;
    /// <summary>
    /// UIコマンド状態判定専用の解析済みスナップショット。Roslyn処理を開始・待機しない。
    /// </summary>
    internal SceneDocumentMenuSnapshot GetMenuSnapshot(string FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        lock(this.Gate){
            if(string.Equals(this.ActiveDocumentPath,FilePath,StringComparison.OrdinalIgnoreCase)){
                return this.ActiveMenuSnapshot;
            }

            return string.Equals(this.SolutionExplorerDocumentPath,FilePath,StringComparison.OrdinalIgnoreCase)
                ?this.SolutionExplorerMenuSnapshot
                :new SceneDocumentMenuSnapshot(SceneDesignerTargetState.Unavailable,[],[]);
        }
    }

    /// <summary>
    /// ソリューションエクスプローラーの選択が変わるたびに<see cref="ActiveDocumentTracker"/>から
    /// 呼ばれる。アクティブ文書(<see cref="SetActiveDocument"/>)とは完全に独立しており、
    /// <see cref="CurrentModel"/>/<see cref="AllModels"/>やそのイベントには触れない
    /// (Hierarchy等の既存の消費者に一切影響を与えないため)。
    /// </summary>
    internal void SetSolutionExplorerSelection(string?FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();

        DocumentId?DocumentId=FilePath is null?null:this.FindDocument(FilePath);
        ScheduledSolutionExplorerAnalysis?Scheduled=null;
        lock(this.Gate){
            if(string.Equals(this.SolutionExplorerDocumentPath,FilePath,StringComparison.OrdinalIgnoreCase)
                &&Equals(this.SolutionExplorerDocumentId,DocumentId)){
                return;
            }

            this.SolutionExplorerDocumentPath=FilePath;
            this.SolutionExplorerDocumentId=DocumentId;
            SceneAnalysisRequest<DocumentId>Request=this.SolutionExplorerAnalysisGeneration.ActivateDocument(DocumentId);
            this.CancelPendingSolutionExplorerAnalysisNoLock();
            this.SolutionExplorerMenuSnapshot=new(
                DocumentId is null?SceneDesignerTargetState.Unavailable:SceneDesignerTargetState.Analyzing,[],[]);

            if(DocumentId is not null){
                CancellationTokenSource Source=new();
                this.PendingSolutionExplorerAnalysis=Source;
                Scheduled=new ScheduledSolutionExplorerAnalysis(this,Request,Source);
            }
        }

        // アクティブ文書側のScheduleAnalysisはタイピングの連打を吸収するために250msデバウンスするが、
        // ソリューションエクスプローラーの選択変更は「選択→即座に右クリックでメニュー表示」という
        // 単発操作であり、デバウンスするとBeforeQueryStatusが呼ばれる時点で解析がまだ始まって
        // すらいない(実機確認で、Scene実装クラスが複数あるファイルでも汎用的な単一項目しか
        // 出ない不具合として顕在化した)。連打耐性は上のAnalysisGeneration+CancellationTokenSource
        // のキャンセル機構が担うため、遅延を挟まず選択変更を検知した直後に解析を開始する。
        if(Scheduled is not null){
            this.StartSolutionExplorerAnalysis(Scheduled);
        }
    }

    private void StartSolutionExplorerAnalysis(ScheduledSolutionExplorerAnalysis Scheduled){
        this.RunningSolutionExplorerAnalysis=this.TaskFactory.RunAsync(async ()=>{
            try {
                SceneDocumentAnalysisResult Result=await this.AnalyzeAllAsync(
                    Scheduled.Request.Document,
                    Scheduled.Token).ConfigureAwait(false);

                if(Scheduled.Token.IsCancellationRequested){
                    return;
                }

                await this.TaskFactory.SwitchToMainThreadAsync(Scheduled.Token);
                lock(this.Gate){
                    if(this.Disposed
                        ||Scheduled.Token.IsCancellationRequested
                        ||!this.SolutionExplorerAnalysisGeneration.IsCurrent(Scheduled.Request)){
                        return;
                    }

                    this.SolutionExplorerMenuSnapshot=Result.MenuSnapshot;
                }
            } catch(OperationCanceledException){
                // Superseded by a newer selection.
            } catch(Exception Ex){
                ActivityLog.LogError(nameof(SceneWorkspaceService),Ex.ToString());
                await this.TaskFactory.SwitchToMainThreadAsync();
                lock(this.Gate){
                    if(this.Disposed||!this.SolutionExplorerAnalysisGeneration.IsCurrent(Scheduled.Request)){
                        return;
                    }

                    this.SolutionExplorerMenuSnapshot=new(SceneDesignerTargetState.Failed,[],[]);
                }
            }
        });
    }

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

        Instance=new SceneWorkspaceService(
            Workspace,
            ThreadHelper.JoinableTaskFactory,
            ComponentModel.GetService<IVsEditorAdaptersFactoryService>(),
            ComponentModel.GetService<ITextUndoHistoryRegistry>());
        return Instance;
    }

    /// <summary>Points the service at a document; passing null clears the model.</summary>
    internal void SetActiveDocument(string?FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();

        DocumentId?DocumentId=FilePath is null?null:this.FindDocument(FilePath);
        lock(this.Gate){
            if(string.Equals(this.ActiveDocumentPath,FilePath,StringComparison.OrdinalIgnoreCase)
                &&Equals(this.ActiveDocumentId,DocumentId)){
                return;
            }

            this.ActiveDocumentPath=FilePath;
            this.ActiveDocumentId=DocumentId;
            this.AnalysisGeneration.ActivateDocument(DocumentId);
            this.CancelPendingAnalysisNoLock();
            this.ActiveMenuSnapshot=new(this.ActiveDocumentPath is null?SceneDesignerTargetState.Unavailable:SceneDesignerTargetState.Analyzing,[],[]);
        }

        // A document switch invalidates the old tree immediately. The command no longer consumes
        // this transient cache; it analyses its captured file directly, so this cannot recreate the
        // old BeforeQueryStatus/Execute race.
        this.CurrentModel=null;
        this.AllModels=[];

        this.ModelChanged?.Invoke(null);
        this.AllModelsChanged?.Invoke(this.AllModels);
        this.ScheduleAnalysis();
    }

    /// <summary>Resolves a file path to a document in the current solution. Null when the file is not part of it.</summary>
    internal DocumentId?FindDocument(string FilePath){
        return this.Workspace.CurrentSolution.GetDocumentIdsWithFilePath(FilePath).FirstOrDefault();
    }
    /// <summary>
    /// Roslyn の実 ProjectId と完全修飾 Scene 名から、Designer session 用の不変キーを作成します。
    /// ファイルパスや単純クラス名をキーにすると、同名 partial/別プロジェクトの Scene を誤って結合する
    /// ためです。ソリューション外の Document は session に載せません。
    /// </summary>
    internal SceneKey?TryCreateSceneKey(SceneModel Model){
        DocumentId?DocumentId=this.FindDocument(Model.FilePath);
        Document?Document=DocumentId is { } Id
            ?this.Workspace.CurrentSolution.GetDocument(Id)
            :null;
        if(Document is null){
            return null;
        }

        return new SceneKey(Document.Project.Id.Id.ToString("N"),Model.ClassFullyQualifiedName);
    }

    /// <summary>
    /// Analyses the exact file captured by a command invocation instead of reading the debounced,
    /// mutable active-document cache. This makes the command's 0/1/N decision independent of focus
    /// changes while its asynchronous work is in flight.
    /// </summary>
    internal async Task<IReadOnlyList<SceneModel>>AnalyzeFileNowAsync(string FilePath,CancellationToken CancellationToken){
        DocumentId?DocumentId=this.FindDocument(FilePath);
        if(DocumentId is null){
            throw new InvalidOperationException($"Roslyn workspace に対象ファイルがありません: {FilePath}");
        }

        SceneDocumentAnalysisResult Result=await this.AnalyzeAllAsync(DocumentId,CancellationToken).ConfigureAwait(false);
        return Result.Models;
    }

    /// <summary>
    /// 現在（または ActiveDocumentTracker が保持している最後）の C# 文書と同じ Project だけから
    /// Scene を集めます。ここは Roslyn のみを背景で読むため、ComboBox の展開が UI thread を
    /// ブロックしたり、描画用 bridge process を起動したりしません。
    /// </summary>
    internal Task<ProjectSceneCatalog?>GetActiveProjectSceneCatalogAsync(CancellationToken CancellationToken)=>
        this.GetProjectSceneCatalogAsync(PreferredSceneKey:null,CancellationToken);

    /// <summary>Pin済みSceneを優先し、そのSceneを所有するProjectの選択肢を取得します。</summary>
    internal async Task<ProjectSceneCatalog?>GetProjectSceneCatalogAsync(
        SceneKey?PreferredSceneKey,CancellationToken CancellationToken){
        DocumentId?ActiveId;
        lock(this.Gate){
            ActiveId=this.ActiveDocumentId;
        }

        string?ActiveProjectId=ActiveId is { } Id
            ?this.Workspace.CurrentSolution.GetDocument(Id)?.Project.Id.Id.ToString("N")
            :null;
        string?ProjectId=SceneCatalogTargetPolicy.SelectProjectId(PreferredSceneKey,ActiveProjectId);
        Project?Project=ProjectId is null?null:this.Workspace.CurrentSolution.Projects.FirstOrDefault(Candidate=>
            string.Equals(Candidate.Id.Id.ToString("N"),ProjectId,StringComparison.Ordinal));
        if(Project is null){
            return null;
        }

        lock(this.Gate){
            if(this.ProjectSceneCatalogs.TryGetValue(Project.Id,out ProjectSceneCatalogCache?Cached)
                &&Cached.Version.Equals(Project.Version)){
                return Cached.Catalog;
            }
        }

        Compilation?Compilation=await Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        if(Compilation is null){
            return null;
        }

        string ProjectKey=Project.Id.Id.ToString("N");
        List<ProjectSceneCatalogEntry>Entries=[];
        foreach(INamedTypeSymbol Type in EnumerateTypes(Compilation.Assembly.GlobalNamespace)){
            CancellationToken.ThrowIfCancellationRequested();
            if(!SceneTypePredicates.ImplementsSceneInterface(Type)){
                continue;
            }

            TypeDeclarationSyntax?Declaration=Type.DeclaringSyntaxReferences
                .Select(Reference=>Reference.GetSyntax(CancellationToken) as TypeDeclarationSyntax)
                .Where(static Candidate=>Candidate is not null)
                .Cast<TypeDeclarationSyntax>()
                .Where(Candidate=>!IsGeneratedSceneFile(Candidate.SyntaxTree,CancellationToken))
                .OrderBy(Candidate=>Candidate.SyntaxTree.FilePath,StringComparer.OrdinalIgnoreCase)
                .ThenBy(Candidate=>Candidate.SpanStart)
                .FirstOrDefault();
            if(Declaration is null||string.IsNullOrWhiteSpace(Declaration.SyntaxTree.FilePath)){
                continue;
            }

            Entries.Add(new ProjectSceneCatalogEntry(
                new SceneKey(ProjectKey,SceneDeclarationAnalyzer.ComputeClassFullyQualifiedName(Type)),
                Type.Name,
                Declaration.SyntaxTree.FilePath,
                Declaration.SpanStart));
        }

        ProjectSceneCatalog Catalog=new(ProjectKey,ProjectSceneCatalogPresentation.Create(ProjectKey,Entries));
        lock(this.Gate){
            if(!this.Disposed){
                this.ProjectSceneCatalogs[Project.Id]=new(Project.Version,Catalog);
            }
        }

        return Catalog;
    }

    /// <summary>ComboBox が保持する <see cref="SceneKey"/> を、現在の Project で再解決します。</summary>
    internal async Task<SceneModel?>ResolveSceneAsync(SceneKey Key,CancellationToken CancellationToken){
        Project?Project=this.Workspace.CurrentSolution.Projects.FirstOrDefault(Candidate=>
            string.Equals(Candidate.Id.Id.ToString("N"),Key.ProjectId,StringComparison.Ordinal));
        if(Project is null){
            return null;
        }

        Compilation?Compilation=await Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        if(Compilation is null){
            return null;
        }

        INamedTypeSymbol?Type=EnumerateTypes(Compilation.Assembly.GlobalNamespace).FirstOrDefault(Candidate=>
            string.Equals(SceneDeclarationAnalyzer.ComputeClassFullyQualifiedName(Candidate),Key.ClassFullyQualifiedName,StringComparison.Ordinal));
        return Type is null||!SceneTypePredicates.ImplementsSceneInterface(Type)
            ?null
            :SceneDeclarationAnalyzer.Analyze(Type,Compilation,CancellationToken);
    }

    /// <summary>commit直前のCompilationからModel・Root・SourceTextを同じ世代で取得する。</summary>
    internal async Task<SceneWorkspaceEditContext?>ResolveSceneEditContextAsync(
        SceneKey Key,CancellationToken CancellationToken){
        Project?Project=this.Workspace.CurrentSolution.Projects.FirstOrDefault(Candidate=>
            string.Equals(Candidate.Id.Id.ToString("N"),Key.ProjectId,StringComparison.Ordinal));
        if(Project is null){
            return null;
        }

        Compilation?Compilation=await Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        if(Compilation is null){
            return null;
        }
        INamedTypeSymbol?Type=EnumerateTypes(Compilation.Assembly.GlobalNamespace).FirstOrDefault(Candidate=>
            string.Equals(
                SceneDeclarationAnalyzer.ComputeClassFullyQualifiedName(Candidate),
                Key.ClassFullyQualifiedName,
                StringComparison.Ordinal));
        if(Type is null||!SceneTypePredicates.ImplementsSceneInterface(Type)){
            return null;
        }

        TypeDeclarationSyntax?Declaration=Type.DeclaringSyntaxReferences
            .Select(Reference=>Reference.GetSyntax(CancellationToken)as TypeDeclarationSyntax)
            .Where(static Candidate=>Candidate is not null)
            .Cast<TypeDeclarationSyntax>()
            .Where(Candidate=>!IsGeneratedSceneFile(Candidate.SyntaxTree,CancellationToken))
            .OrderBy(Candidate=>Candidate.SyntaxTree.FilePath,StringComparer.OrdinalIgnoreCase)
            .ThenBy(static Candidate=>Candidate.SpanStart)
            .FirstOrDefault();
        if(Declaration is null||string.IsNullOrWhiteSpace(Declaration.SyntaxTree.FilePath)){
            return null;
        }

        Document?Document=this.Workspace.CurrentSolution.GetDocument(Declaration.SyntaxTree);
        if(Document is null){
            return null;
        }
        SyntaxNode Root=await Declaration.SyntaxTree.GetRootAsync(CancellationToken).ConfigureAwait(false);
        SourceText Text=await Declaration.SyntaxTree.GetTextAsync(CancellationToken).ConfigureAwait(false);
        SceneModel Model=SceneDeclarationAnalyzer.Analyze(Type,Compilation,CancellationToken);
        return new SceneWorkspaceEditContext(Document.Id,Declaration.SyntaxTree.FilePath,Model,Root,Text);
    }

    /// <summary>対象文書をbackgroundでopenし、RDTのlive bufferへ1 Undoで反映する。</summary>
    internal async Task<SceneLiveBufferWriteResult>ApplyTextAsync(
        SceneWorkspaceEditContext Context,SourceText After,string UndoDescription,
        CancellationToken CancellationToken){
        await this.TaskFactory.SwitchToMainThreadAsync(CancellationToken);
        if(this.EditorAdapters is null||this.TextBufferWriter is null){
            return new SceneLiveBufferWriteResult(false,"Visual StudioのEditor/Undoサービスを取得できません。");
        }
        if(!this.Workspace.IsDocumentOpen(Context.DocumentId)){
            if(!this.Workspace.CanOpenDocuments){
                return new SceneLiveBufferWriteResult(false,"対象SceneのC#文書をlive bufferとして開けません。");
            }
            this.Workspace.OpenDocument(Context.DocumentId,activate:false);
        }
        if(Package.GetGlobalService(typeof(SVsRunningDocumentTable))is not IVsRunningDocumentTable Rdt){
            return new SceneLiveBufferWriteResult(false,"Visual StudioのRunning Document Tableを取得できません。");
        }

        IntPtr DocDataPointer=IntPtr.Zero;
        try {
            int HResult=Rdt.FindAndLockDocument(
                (uint)_VSRDTFLAGS.RDT_NoLock,Context.FilePath,
                out _,out _,out DocDataPointer,out _);
            if(ErrorHandler.Failed(HResult)||DocDataPointer==IntPtr.Zero){
                return new SceneLiveBufferWriteResult(false,"対象Sceneのlive documentを取得できません。");
            }

            object DocData=Marshal.GetObjectForIUnknown(DocDataPointer);
            Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer?VsBuffer=
                DocData as Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer;
            if(VsBuffer is null&&DocData is IVsTextBufferProvider Provider
                &&ErrorHandler.Succeeded(Provider.GetTextBuffer(out Microsoft.VisualStudio.TextManager.Interop.IVsTextLines Lines))){
                VsBuffer=Lines as Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer;
            }
            if(VsBuffer is null){
                return new SceneLiveBufferWriteResult(false,"対象SceneのVisual Studio text bufferを取得できません。");
            }

            ITextBuffer?Buffer=this.EditorAdapters.GetDocumentBuffer(VsBuffer);
            if(Buffer is null){
                return new SceneLiveBufferWriteResult(false,"対象Sceneのmanaged text bufferを取得できません。");
            }
            return this.TextBufferWriter.TryApply(Buffer,Context.Text,After,UndoDescription)
                ?new SceneLiveBufferWriteResult(true,ErrorMessage:null)
                :new SceneLiveBufferWriteResult(
                    false,"編集中にC#ソースが変化したため上書きを中止しました。",LiveTextChanged:true);
        } finally {
            if(DocDataPointer!=IntPtr.Zero){
                Marshal.Release(DocDataPointer);
            }
        }
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

            this.CancelPendingAnalysisNoLock();
            this.ActiveMenuSnapshot=new(this.ActiveDocumentPath is null?SceneDesignerTargetState.Unavailable:SceneDesignerTargetState.Analyzing,[],[]);
            DocumentId?ResolvedDocumentId=this.ActiveDocumentPath is null
                ?null
                :this.FindDocument(this.ActiveDocumentPath);
            SceneAnalysisRequest<DocumentId>Request;
            if(!Equals(this.ActiveDocumentId,ResolvedDocumentId)){
                this.ActiveDocumentId=ResolvedDocumentId;
                Request=this.AnalysisGeneration.ActivateDocument(ResolvedDocumentId);
            } else {
                Request=this.AnalysisGeneration.Refresh();
            }

            // 古い型Spanで別クラスを有効化しない。BeforeQueryStatusはこの状態を見るだけで、
            // ここからRoslynを待機しない。
            this.ActiveMenuSnapshot=new(SceneDesignerTargetState.Analyzing,[],[]);
            CancellationTokenSource Source=new();
            this.PendingAnalysis=Source;
            this.DebounceTimer=new System.Threading.Timer(
                static State=>((ScheduledAnalysis)State).Service.StartAnalysis((ScheduledAnalysis)State),
                new ScheduledAnalysis(this,Request,Source),
                DebounceDelay,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void StartAnalysis(ScheduledAnalysis Scheduled){
        // Started from a timer callback, so there is nothing to await it here. RunAsync keeps the
        // work joinable, and the handle is stored so VS can still block on it during shutdown.
        this.RunningAnalysis=this.TaskFactory.RunAsync(async ()=>{
            try {
                SceneDocumentAnalysisResult Result=await this.AnalyzeAllAsync(
                    Scheduled.Request.Document,
                    Scheduled.Token).ConfigureAwait(false);

                // A cancelled analysis must not overwrite the newer one that cancelled it.
                if(Scheduled.Token.IsCancellationRequested){
                    return;
                }

                await this.TaskFactory.SwitchToMainThreadAsync(Scheduled.Token);
                SceneModel?First=Result.Models.Count>0?Result.Models[0]:null;
                lock(this.Gate){
                    if(this.Disposed
                        ||Scheduled.Token.IsCancellationRequested
                        ||!this.AnalysisGeneration.IsCurrent(Scheduled.Request)){
                        return;
                    }

                    this.CurrentModel=First;
                    this.AllModels=Result.Models;
                    this.ActiveMenuSnapshot=Result.MenuSnapshot;
                }

                this.ModelChanged?.Invoke(First);
                this.AllModelsChanged?.Invoke(Result.Models);
            } catch(OperationCanceledException){
                // Superseded by a newer analysis.
            } catch(Exception Ex){
                ActivityLog.LogError(nameof(SceneWorkspaceService),Ex.ToString());
                await this.TaskFactory.SwitchToMainThreadAsync();
                lock(this.Gate){
                    if(this.Disposed||!this.AnalysisGeneration.IsCurrent(Scheduled.Request)){
                        return;
                    }

                    this.ActiveMenuSnapshot=new(SceneDesignerTargetState.Failed,[],[]);
                }
            }
        });
    }

    /// <summary>
    /// アクティブ文書に宣言された全<c>IScene</c>実装を宣言順で解析する。並行して保持する型範囲は
    /// コードウィンドウの右クリック専用のVSIX内部メタデータであり、Source Generatorと共有する
    /// <c>SceneModel</c>契約は変更しない。
    /// </summary>
    private async Task<SceneDocumentAnalysisResult>AnalyzeAllAsync(DocumentId?DocumentId,CancellationToken CancellationToken){
        if(DocumentId is null){
            return new([],new SceneDocumentMenuSnapshot(SceneDesignerTargetState.Unavailable,[],[]));
        }

        Document?Document=this.Workspace.CurrentSolution.GetDocument(DocumentId);
        if(Document is null){
            throw new InvalidOperationException($"Roslyn workspace に対象Documentがありません: {DocumentId}");
        }

        Compilation?Compilation=await Document.Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        SyntaxNode?Root=await Document.GetSyntaxRootAsync(CancellationToken).ConfigureAwait(false);
        SemanticModel?SemanticModel=await Document.GetSemanticModelAsync(CancellationToken).ConfigureAwait(false);
        if(Compilation is null||Root is null||SemanticModel is null){
            throw new InvalidOperationException($"Roslyn解析に必要な情報を取得できませんでした: {Document.FilePath}");
        }

        List<SceneModel>Models=[];
        List<SceneDesignerMenuItem>Items=[];
        List<SceneDocumentTypeRange>TypeRanges=[];
        foreach(TypeDeclarationSyntax Declaration in Root.DescendantNodes().OfType<TypeDeclarationSyntax>().OrderBy(Node=>Node.SpanStart)){
            CancellationToken.ThrowIfCancellationRequested();
            bool IsScene=false;
            string?FullyQualifiedName=null;
            if(SceneDeclarationAnalyzer.IsSceneCandidate(Declaration)
                &&SemanticModel.GetDeclaredSymbol(Declaration,CancellationToken)is INamedTypeSymbol Type
                &&SceneTypePredicates.ImplementsSceneInterface(Type)){
                SceneModel Model=SceneDeclarationAnalyzer.Analyze(Type,Compilation,CancellationToken);
                Models.Add(Model);
                Items.Add(new SceneDesignerMenuItem(Model.ClassName,Model.ClassFullyQualifiedName));
                IsScene=true;
                FullyQualifiedName=Model.ClassFullyQualifiedName;
            }

            TypeRanges.Add(new SceneDocumentTypeRange(
                Declaration.SpanStart,
                Declaration.Span.End,
                IsScene,
                FullyQualifiedName));
        }

        return new(
            Models,
            new SceneDocumentMenuSnapshot(SceneDesignerTargetState.SceneType,TypeRanges,Items));
    }
    public void Dispose(){
        lock(this.Gate){
            if(this.Disposed){
                return;
            }

            this.Disposed=true;
            this.CancelPendingAnalysisNoLock();
            this.CancelPendingSolutionExplorerAnalysisNoLock();
        }

        this.Workspace.WorkspaceChanged-=this.OnWorkspaceChanged;
        this.ProjectSceneCatalogs.Clear();
        Instance=null;
    }

    private static IEnumerable<INamedTypeSymbol>EnumerateTypes(INamespaceSymbol Namespace){
        foreach(INamedTypeSymbol Type in Namespace.GetTypeMembers()){
            yield return Type;
            foreach(INamedTypeSymbol Nested in EnumerateNestedTypes(Type)){
                yield return Nested;
            }
        }

        foreach(INamespaceSymbol Child in Namespace.GetNamespaceMembers()){
            foreach(INamedTypeSymbol Type in EnumerateTypes(Child)){
                yield return Type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol>EnumerateNestedTypes(INamedTypeSymbol Type){
        foreach(INamedTypeSymbol Nested in Type.GetTypeMembers()){
            yield return Nested;
            foreach(INamedTypeSymbol Descendant in EnumerateNestedTypes(Nested)){
                yield return Descendant;
            }
        }
    }

    private static bool IsGeneratedSceneFile(SyntaxTree SyntaxTree,CancellationToken CancellationToken){
        string FilePath=SyntaxTree.FilePath??string.Empty;
        if(FilePath.EndsWith(".g.cs",StringComparison.OrdinalIgnoreCase)
            ||FilePath.EndsWith(".generated.cs",StringComparison.OrdinalIgnoreCase)){
            return true;
        }

        Microsoft.CodeAnalysis.Text.SourceText Text=SyntaxTree.GetText(CancellationToken);
        int PrefixLength=Math.Min(Text.Length,256);
        return Text.ToString(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(0,PrefixLength))
            .IndexOf("<auto-generated",StringComparison.OrdinalIgnoreCase)>=0;
    }

    private sealed class ProjectSceneCatalogCache {
        internal ProjectSceneCatalogCache(VersionStamp Version,ProjectSceneCatalog Catalog){
            this.Version=Version;
            this.Catalog=Catalog;
        }

        internal VersionStamp Version { get; }
        internal ProjectSceneCatalog Catalog { get; }
    }

    private void CancelPendingAnalysisNoLock(){
        this.DebounceTimer?.Dispose();
        this.DebounceTimer=null;
        this.PendingAnalysis?.Cancel();
        this.PendingAnalysis?.Dispose();
        this.PendingAnalysis=null;
    }

    private void CancelPendingSolutionExplorerAnalysisNoLock(){
        this.PendingSolutionExplorerAnalysis?.Cancel();
        this.PendingSolutionExplorerAnalysis?.Dispose();
        this.PendingSolutionExplorerAnalysis=null;
    }

    private sealed class ScheduledSolutionExplorerAnalysis {
        internal ScheduledSolutionExplorerAnalysis(
            SceneWorkspaceService Service,
            SceneAnalysisRequest<DocumentId>Request,
            CancellationTokenSource Source){
            this.Service=Service;
            this.Request=Request;
            this.Token=Source.Token;
        }

        internal SceneWorkspaceService Service { get;}

        internal SceneAnalysisRequest<DocumentId>Request { get;}

        internal CancellationToken Token { get;}
    }

    private sealed class ScheduledAnalysis {
        internal ScheduledAnalysis(
            SceneWorkspaceService Service,
            SceneAnalysisRequest<DocumentId>Request,
            CancellationTokenSource Source){
            this.Service=Service;
            this.Request=Request;
            this.Token=Source.Token;
        }

        internal SceneWorkspaceService Service { get;}

        internal SceneAnalysisRequest<DocumentId>Request { get;}

        internal CancellationToken Token { get;}
    }
}
