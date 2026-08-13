using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// コードエディタの右クリックメニューから、現在の文書に宣言されたSceneを開く。
/// BeforeQueryStatusは解析済みスナップショットだけを読み、Roslyn解析やJTF待機を絶対に行わない。
/// </summary>
internal sealed class OpenSceneDesignerCommand {
    public const int CommandId=0x0109;
    // これを登録すると BeforeQueryStatus が実際に表示されるサブメニューへ届かなかった。
    public const int DynamicSceneCommandStart=0x0110;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage Package;
    private readonly SceneEditorSessionService Session;
    private readonly ActiveDocumentTracker DocumentTracker;
    private readonly OleMenuCommand DirectCommand;
    private readonly DynamicSceneMenuCommand DynamicSceneCommand;
    private SceneMenuInvocation?CurrentInvocation;

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

        this.DirectCommand=new OleMenuCommand(this.ExecuteDirect,new CommandID(CommandSet,CommandId));
        this.DirectCommand.BeforeQueryStatus+=this.UpdateMenuCommands;
        CommandService.AddCommand(this.DirectCommand);


        this.DynamicSceneCommand=new DynamicSceneMenuCommand(
            this.ExecuteDynamic,CommandSet,DynamicSceneCommandStart,this.GetDynamicSceneCount);
        this.DynamicSceneCommand.BeforeQueryStatus+=this.UpdateDynamicItem;
        CommandService.AddCommand(this.DynamicSceneCommand);
    }

    public static async Task InitializeAsync(
        AsyncPackage Package,
        SceneEditorSessionService Session,
        ActiveDocumentTracker DocumentTracker){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var Service=await Package.GetServiceAsync(typeof(IMenuCommandService));
        if(Service is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        _=new OpenSceneDesignerCommand(Package,CommandService,Session,DocumentTracker);
    }

    private void UpdateMenuCommands(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();

        SceneDesignerCommandPresentation Presentation=this.RefreshPresentation();
        this.DirectCommand.Visible=Presentation.Mode is SceneDesignerMenuMode.Direct or SceneDesignerMenuMode.Disabled;
        this.DirectCommand.Enabled=Presentation.Mode==SceneDesignerMenuMode.Direct;
        this.DirectCommand.Text=Presentation.Text;

    }

    /// <summary>
    /// DynamicItemMatch は QueryStatus より先に呼ばれるため、ここで同じ UI スレッド上の
    /// 解析済みスナップショットを読み直す。Roslyn/JTF/bridge は起動せず、現在の Scene 件数だけを
    /// 返す。最後の次の ID を一致させないことが動的リストの終了通知になる。
    /// </summary>
    private int GetDynamicSceneCount(){
        ThreadHelper.ThrowIfNotOnUIThread();
        SceneDesignerCommandPresentation Presentation=this.RefreshPresentation();
        return Presentation.Mode==SceneDesignerMenuMode.Submenu?Presentation.Items.Count:0;
    }

    private void UpdateDynamicItem(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();

        SceneDesignerCommandPresentation Presentation=this.RefreshPresentation();
        int ItemIndex=this.DynamicSceneCommand.ItemIndex;
        bool IsVisible=Presentation.Mode==SceneDesignerMenuMode.Submenu
            &&ItemIndex>=0
            &&ItemIndex<Presentation.Items.Count;
        this.DynamicSceneCommand.Visible=IsVisible;
        this.DynamicSceneCommand.Enabled=IsVisible;
        this.DynamicSceneCommand.Checked=IsVisible
            &&this.CurrentInvocation is { } Invocation
            &&SceneDesignerMenuSelectionPolicy.IsLatched(
                Invocation.TargetSceneFullyQualifiedName,
                Presentation.Items[ItemIndex]);
        this.DynamicSceneCommand.Text=IsVisible?Presentation.Items[ItemIndex].Text:string.Empty;
    }

    private SceneDesignerCommandPresentation RefreshPresentation(){
        ThreadHelper.ThrowIfNotOnUIThread();

        string?FilePath=this.DocumentTracker.CurrentFilePath;
        if(FilePath is null){
            this.CurrentInvocation=null;
            return SceneDesignerContextMenuPolicy.Create(null,SceneDesignerTargetState.Unavailable,[]);
        }

        if(!this.DocumentTracker.TryCaptureActiveCaretPosition(FilePath,out int CaretPosition)){
            this.CurrentInvocation=null;
            return SceneDesignerContextMenuPolicy.Create(FilePath,SceneDesignerTargetState.Unavailable,[]);
        }

        SceneDocumentMenuSnapshot Snapshot=this.Session.GetMenuSnapshot(FilePath);
        SceneDesignerTargetState TargetState=Snapshot.GetTargetState(CaretPosition);
        string?TargetSceneFullyQualifiedName=Snapshot.GetTargetSceneFullyQualifiedName(CaretPosition);
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            FilePath,
            TargetState,
            Snapshot.Items);
        this.CurrentInvocation=Presentation.Enabled
            ?new SceneMenuInvocation(
                FilePath,
                Presentation.Items,
                TargetSceneFullyQualifiedName,
                this.DocumentTracker.CaptureActiveTextView(FilePath))
            :null;
        return Presentation;
    }

    private void ExecuteDirect(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();

        SceneDesignerCommandPresentation Presentation=this.RefreshPresentation();
        if(Presentation.Mode!=SceneDesignerMenuMode.Direct
            ||this.CurrentInvocation is not { } Invocation
            ||Invocation.Items.Count!=1){
            return;
        }

        this.OpenScene(Invocation,Invocation.Items[0].FullyQualifiedName);
    }

    private void ExecuteDynamic(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();

        SceneMenuInvocation?Invocation=this.CurrentInvocation;
        int ItemIndex=this.DynamicSceneCommand.ItemIndex;
        if(Invocation is null
            ||ItemIndex<0
            ||ItemIndex>=Invocation.Items.Count){
            return;
        }

        this.OpenScene(Invocation,Invocation.Items[ItemIndex].FullyQualifiedName);
    }

    private void OpenScene(SceneMenuInvocation Invocation,string FullyQualifiedName){
        ThreadHelper.ThrowIfNotOnUIThread();
        SetStatusBarText("シーンを解析しています...");

        _=this.Package.JoinableTaskFactory.RunAsync(async delegate {
            try {
                IReadOnlyList<SceneModel>Models=await this.Session.AnalyzeFileNowAsync(
                    Invocation.FilePath,
                    this.Package.DisposalToken).ConfigureAwait(false);
                await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);

                SceneModel?Chosen=Models.SingleOrDefault(Model=>
                    string.Equals(Model.ClassFullyQualifiedName,FullyQualifiedName,StringComparison.Ordinal));
                if(Chosen is null){
                    SetStatusBarText("選択した IScene 実装クラスは編集後に見つからなくなりました。");
                    return;
                }

                ToolWindowPane Window=await this.Package.FindToolWindowAsync(
                    typeof(SceneDesignerToolWindowPane),
                    0,
                    true,
                    this.Package.DisposalToken);
                if(Window is not SceneDesignerToolWindowPane Pane
                    ||Pane.Frame is not IVsWindowFrame Frame){
                    throw new NotSupportedException("Cannot create the Scene Designer tool window.");
                }

                SceneDocumentSnapshot Snapshot=this.Session.Pin(Chosen);
                Pane.SetTarget(this.Session,Snapshot);
                ToolWindowFramePresenter.ShowWithoutActivation(
                    ()=>ShowFrameWithoutActivation(Frame),
                    Invocation.EditorView is null?null:()=>RestoreEditorFocus(Invocation.EditorView));
                SetStatusBarText("シーン デザイナーを開きました。");
            } catch(OperationCanceledException) when(this.Package.DisposalToken.IsCancellationRequested){
                // Package shutdown.
            } catch(Exception Ex){
                await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ActivityLog.LogError(nameof(OpenSceneDesignerCommand),Ex.ToString());
                SetStatusBarText("シーンの解析またはデザイナー表示に失敗しました。詳細は ActivityLog.xml を確認してください。");
            }
        });
    }

    private static void ShowFrameWithoutActivation(IVsWindowFrame Frame){
        ThreadHelper.ThrowIfNotOnUIThread();
        ErrorHandler.ThrowOnFailure(Frame.ShowNoActivate());
    }

    private static void RestoreEditorFocus(IVsTextView?EditorView){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(EditorView is null){
            return;
        }

        try {
            ErrorHandler.ThrowOnFailure(EditorView.SendExplicitFocus());
        } catch(COMException Ex){
            ActivityLog.LogWarning(nameof(OpenSceneDesignerCommand),$"元のコードエディタへフォーカスを戻せませんでした: {Ex.Message}");
        }
    }

    private void SetStatusBarText(string Text){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
            StatusBar.SetText(Text);
        }
    }

    private sealed class SceneMenuInvocation {
        internal SceneMenuInvocation(
            string FilePath,
            IReadOnlyList<SceneDesignerMenuItem> Items,
            string?TargetSceneFullyQualifiedName,
            IVsTextView?EditorView){
            this.FilePath=FilePath;
            this.Items=Items;
            this.TargetSceneFullyQualifiedName=TargetSceneFullyQualifiedName;
            this.EditorView=EditorView;
        }

        internal string FilePath { get; }

        internal IReadOnlyList<SceneDesignerMenuItem> Items { get; }

        internal string?TargetSceneFullyQualifiedName { get; }

        internal IVsTextView?EditorView { get; }
    }
}
