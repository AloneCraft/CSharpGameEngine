using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// The C# Game Engine toolbar's "Open in Scene Designer": opens the scene of the active
/// <c>*.scene.json</c> in the Scene Designer. Scenes also open by double-click (ADR-0071); the
/// code-editor and Solution Explorer context menus that used to carry this command are retired.
/// </summary>
internal sealed class OpenSceneDesignerCommand {
    public const int ToolbarCommandId=0x0131;

    /// <summary>
    /// IDs retired with the context menus (ADR-0071): 0x0109 (the menu command), 0x0110-0x012F (the
    /// scene slots) and 0x0130 (the overflow line). Never reused: installed Visual Studio instances
    /// keep key bindings and customizations for them.
    /// </summary>
    internal static readonly int[]RetiredCommandIds=[0x0109,.. Enumerable.Range(0x0110,0x21)];

    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage Package;
    private readonly SceneEditorSessionService Session;
    private readonly ActiveDocumentTracker DocumentTracker;
    private readonly SceneDesignerDocumentManager Documents;

    private OpenSceneDesignerCommand(
        AsyncPackage Package,
        OleMenuCommandService CommandService,
        SceneEditorSessionService Session,
        ActiveDocumentTracker DocumentTracker,
        SceneDesignerDocumentManager Documents){
        this.Package=Package??throw new ArgumentNullException(nameof(Package));
        this.Session=Session??throw new ArgumentNullException(nameof(Session));
        this.DocumentTracker=DocumentTracker??throw new ArgumentNullException(nameof(DocumentTracker));
        this.Documents=Documents??throw new ArgumentNullException(nameof(Documents));
        if(CommandService is null){
            throw new ArgumentNullException(nameof(CommandService));
        }
        CommandService.AddCommand(new OleMenuCommand(this.Execute,new CommandID(CommandSet,ToolbarCommandId)));
    }

    public static async Task<OpenSceneDesignerCommand>InitializeAsync(
        AsyncPackage Package,
        SceneEditorSessionService Session,
        ActiveDocumentTracker DocumentTracker,
        SceneDesignerDocumentManager Documents){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var Service=await Package.GetServiceAsync(typeof(IMenuCommandService));
        if(Service is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        return new OpenSceneDesignerCommand(
            Package,CommandService,Session,DocumentTracker,Documents);
    }

    private void Execute(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();
        // The tracker keeps only a *.scene.json as the current document.
        if(this.DocumentTracker.CurrentFilePath is not { }FilePath){
            this.SetStatusBarText(VsixText.Get("SceneDesigner_OpenSceneJsonFirst"));
            return;
        }
        this.OpenScene(FilePath);
    }

    private void OpenScene(string FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.SetStatusBarText(VsixText.Get("SceneDesigner_Analyzing"));

        _=this.Package.JoinableTaskFactory.RunAsync(async delegate {
            try {
                var Models=await this.Session.AnalyzeFileNowAsync(
                                                                 FilePath,
                                                                 this.Package.DisposalToken).ConfigureAwait(false);
                await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);

                // A scene JSON declares exactly one scene.
                if(Models.Count!=1){
                    this.SetStatusBarText(VsixText.Get("SceneDesigner_SelectedSceneMissing"));
                    return;
                }
                var Chosen=Models[0];
                var Snapshot=this.Session.Pin(Chosen);
                try {
                    this.Documents.Open(this.Session,Snapshot);
                    this.SetStatusBarText(VsixText.Format("SceneDesigner_OpenedInDocumentWell",Chosen.ClassName));
                } catch(Exception DocumentWellException){
                    SceneDesignerDocumentWindowDiagnostics.ReportFailure(DocumentWellException);
                    ActivityLog.LogWarning(
                        nameof(OpenSceneDesignerCommand),
                        $"Document Well failed; source='{FilePath}'; scene='{Chosen.ClassFullyQualifiedName}'; exception='{DocumentWellException.GetType().FullName}'; message='{DocumentWellException.Message}'. Falling back to Scene Designer tool window.");
                    await this.OpenToolWindowFallbackAsync(Snapshot);
                    string Diagnostic=SceneDesignerDocumentWindowDiagnostics.CompactDiagnostic(DocumentWellException);
                    this.SetStatusBarText(
                        VsixText.Format("SceneDesigner_FallbackOpened",Chosen.ClassName,Diagnostic,SceneDesignerDocumentWindowDiagnostics.PersistentLogPath));
                    ActivityLog.LogInformation(nameof(OpenSceneDesignerCommand),$"Scene Designer tool window fallback opened; scene='{Chosen.ClassFullyQualifiedName}'.");
                }
            } catch(OperationCanceledException) when(this.Package.DisposalToken.IsCancellationRequested){
                // Package shutdown.
            } catch(Exception Ex){
                await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ActivityLog.LogError(
                    nameof(OpenSceneDesignerCommand),
                    $"Scene Designer document open failed; source='{FilePath}'; exception='{Ex.GetType().FullName}'; message='{Ex.Message}'{Environment.NewLine}{Ex}");
                this.SetStatusBarText(
                    VsixText.Format("SceneDesigner_OpenFailed",FilePath,Ex.GetType().Name,Ex.Message));
            }
        });
    }

    private async Task OpenToolWindowFallbackAsync(SceneDocumentSnapshot Snapshot){
        await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);
        var Window=await this.Package.FindToolWindowAsync(
            typeof(SceneDesignerToolWindowPane),0,true,this.Package.DisposalToken);
        if(Window is not SceneDesignerToolWindowPane Pane||Pane.Frame is not IVsWindowFrame Frame){
            throw new NotSupportedException(VsixText.Get("SceneDesigner_FallbackWindowUnavailable"));
        }

        await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);
        Pane.SetTarget(this.Session,Snapshot);
        ErrorHandler.ThrowOnFailure(Frame.Show());
    }

    private void SetStatusBarText(string Text){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
            StatusBar.SetText(Text);
        }
    }
}
