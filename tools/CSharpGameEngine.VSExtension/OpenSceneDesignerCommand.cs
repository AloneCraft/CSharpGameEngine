using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// C# Game Engine ツールバーの「Open in Scene Designer」。アクティブなシーン JSON を Scene Editor の Design view（ADR-0071）に表示します。これはダブルクリックで開く editor です。以前このコマンドがあったコードエディターと Solution Explorer のコンテキストメニューは廃止されました。
/// </summary>
internal sealed class OpenSceneDesignerCommand {
    public const int ToolbarCommandId=0x0131;

    /// <summary>
    /// コンテキストメニューとともに廃止された ID（ADR-0071）：0x0109（メニューコマンド）、0x0110-0x012F（シーンスロット）、0x0130（オーバーフロー行）。Visual Studio のインストール済みインスタンスにはキー割り当てやカスタマイズが残るため、再利用しません。
    /// </summary>
    internal static readonly int[]RetiredCommandIds=[0x0109,.. Enumerable.Range(0x0110,0x21)];

    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage Package;
    private readonly SceneEditorSessionService Session;
    private readonly ActiveDocumentTracker DocumentTracker;

    private OpenSceneDesignerCommand(
        AsyncPackage Package,
        OleMenuCommandService CommandService,
        SceneEditorSessionService Session,
        ActiveDocumentTracker DocumentTracker){
        this.Package=Package??throw new ArgumentNullException(nameof(Package));
        this.Session=Session??throw new ArgumentNullException(nameof(Session));
        this.DocumentTracker=DocumentTracker??throw new ArgumentNullException(nameof(DocumentTracker));
        if(CommandService is null){
            throw new ArgumentNullException(nameof(CommandService));
        }
        CommandService.AddCommand(new OleMenuCommand(this.Execute,new CommandID(CommandSet,ToolbarCommandId)));
    }

    public static async Task<OpenSceneDesignerCommand>InitializeAsync(
        AsyncPackage Package,
        SceneEditorSessionService Session,
        ActiveDocumentTracker DocumentTracker){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var Service=await Package.GetServiceAsync(typeof(IMenuCommandService));
        if(Service is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        return new OpenSceneDesignerCommand(Package,CommandService,Session,DocumentTracker);
    }

    private void Execute(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();
        // tracker が現在のドキュメントとして保持するのはシーン JSON だけです。
        if(this.DocumentTracker.CurrentFilePath is not { }FilePath){
            this.SetStatusBarText(VsixText.Get("SceneDesigner_OpenSceneJsonFirst"));
            return;
        }
        try {
            VsShellUtilities.OpenDocumentWithSpecificEditor(
                this.Package,FilePath,new Guid(SceneJsonEditorFactory.FactoryGuidString),VSConstants.LOGVIEWID_Designer,
                out _,out _,out IVsWindowFrame Frame);
            ErrorHandler.ThrowOnFailure(Frame.Show());
        } catch(Exception Error){
            ActivityLog.LogWarning(
                nameof(OpenSceneDesignerCommand),
                $"Scene Editor failed to open '{FilePath}': {Error}. Falling back to the Scene Designer tool window.");
            this.OpenToolWindowFallback(FilePath,Error);
        }
    }

    /// <summary>復旧経路専用：ファイルのシーンに固定された Scene Designer tool window。</summary>
    private void OpenToolWindowFallback(string FilePath,Exception EditorError){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.SetStatusBarText(VsixText.Get("SceneDesigner_Analyzing"));
        _=this.Package.JoinableTaskFactory.RunAsync(async delegate {
            try {
                var Models=await this.Session.AnalyzeFileNowAsync(FilePath,this.Package.DisposalToken).ConfigureAwait(false);
                await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);
                // シーン JSON が宣言するシーンは1つだけです。
                if(Models.Count!=1){
                    this.SetStatusBarText(VsixText.Get("SceneDesigner_SelectedSceneMissing"));
                    return;
                }
                var Snapshot=this.Session.Pin(Models[0]);
                var Window=await this.Package.FindToolWindowAsync(
                    typeof(SceneDesignerToolWindowPane),0,true,this.Package.DisposalToken);
                await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);
                if(Window is not SceneDesignerToolWindowPane Pane||Pane.Frame is not IVsWindowFrame Frame){
                    throw new NotSupportedException(VsixText.Get("SceneDesigner_FallbackWindowUnavailable"));
                }
                Pane.SetTarget(this.Session,Snapshot);
                ErrorHandler.ThrowOnFailure(Frame.Show());
                this.SetStatusBarText(VsixText.Format(
                    "SceneDesigner_FallbackOpened",Models[0].ClassName,$"{EditorError.GetType().Name}, HRESULT 0x{EditorError.HResult:X8}"));
            } catch(OperationCanceledException) when(this.Package.DisposalToken.IsCancellationRequested){
                // パッケージの終了処理。
            } catch(Exception Ex){
                await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ActivityLog.LogError(
                    nameof(OpenSceneDesignerCommand),
                    $"Scene Designer open failed; source='{FilePath}'; exception='{Ex.GetType().FullName}'; message='{Ex.Message}'{Environment.NewLine}{Ex}");
                this.SetStatusBarText(
                    VsixText.Format("SceneDesigner_OpenFailed",FilePath,Ex.GetType().Name,Ex.Message));
            }
        });
    }

    private void SetStatusBarText(string Text){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
            StatusBar.SetText(Text);
        }
    }
}
