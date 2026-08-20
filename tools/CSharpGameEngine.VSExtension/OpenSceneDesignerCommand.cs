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

    // 複数Sceneの一覧は、固有IDを持つスロットを.vsctに固定で並べて表現する(0x0110〜0x012F)。
    // VSのDynamicItemStart(可変長)は使わない: root ID(先頭項目)だけはOleMenuCommandServiceが
    // 辞書の完全一致で解決してDynamicItemMatchを通さないため、共有されるMatchedCommandIdが更新
    // されず「1番目を押したのに2番目のシーンが開く」誘導ミスが起きた。rootを実項目から外して常に
    // 非表示のアンカーにする案も試したが、rootを非表示にするとVSはそこで列挙を打ち切り項目が1つも
    // 出なかった(どちらも実機で確認済み)。スロットごとに固有IDを持たせればクリックされたIDが
    // そのままハンドラを特定するので、共有可変状態が無くなり誘導ミスが原理的に起こり得ない。
    public const int SceneItemCommandIdFirst=0x0110;
    public const int SceneItemCommandCount=32;

    // 33件目以降がある場合だけ出す、押せない情報行。上限はサブメニューでは回避できない
    // (リーフ1項目につきIDが1つ必要で、入れ子は見た目のグルーピングにすぎない)ため、
    // 「存在するのに表示されない」を黙らせずコンボボックスへ誘導する。
    public const int OverflowCommandId=0x0130;

    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage Package;
    private readonly SceneEditorSessionService Session;
    private readonly ActiveDocumentTracker DocumentTracker;
    private readonly OleMenuCommand DirectCommand;
    private readonly OleMenuCommand OverflowCommand;
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

        for(var Slot=0;Slot<SceneItemCommandCount;Slot++){
            // ループ変数をそのままクロージャへ取り込むと全スロットが最後のindexを指す。必ずコピーする。
            var ItemIndex=Slot;

            // ラムダの中はVSTHRD010の解析対象外(UIスレッドで呼ばれることを解析器が追えない)ため、
            // 他のハンドラと同じく明示的にアサートする。
            OleMenuCommand SceneItemCommand=new(
                (Sender,Args)=>{
                    ThreadHelper.ThrowIfNotOnUIThread();
                    this.ExecuteSceneItem(ItemIndex);
                },
                new CommandID(CommandSet,SceneItemCommandIdFirst+ItemIndex));
            SceneItemCommand.BeforeQueryStatus+=(Sender,Args)=>{
                ThreadHelper.ThrowIfNotOnUIThread();
                this.UpdateSceneItem(ItemIndex,(OleMenuCommand)Sender);
            };
            CommandService.AddCommand(SceneItemCommand);
        }

        // 情報行なので押しても何もしない(Enabled=falseでもコマンド自体の登録は必要)。
        this.OverflowCommand=new OleMenuCommand((Sender,Args)=>{ },new CommandID(CommandSet,OverflowCommandId));
        this.OverflowCommand.BeforeQueryStatus+=this.UpdateOverflowItem;
        CommandService.AddCommand(this.OverflowCommand);
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

        var Presentation=this.RefreshPresentation();
        this.DirectCommand.Visible=Presentation.Mode is SceneDesignerMenuMode.Direct or SceneDesignerMenuMode.Disabled;
        this.DirectCommand.Enabled=Presentation.Mode==SceneDesignerMenuMode.Direct;
        this.DirectCommand.Text=Presentation.Text;

    }

    /// <summary>
    /// スロット1つ分の表示状態。1回のメニュー表示で32スロット分呼ばれるが、
    /// <see cref="RefreshPresentation"/> は解析済みスナップショットの読み出しとキャレット位置の取得
    /// だけで Roslyn を動かさないため、そのまま毎回読み直す(✓は Pin、表示名は再解析に追従する
    /// 必要があり、メモ化すると staleness の risk が勝る)。
    /// </summary>
    private void UpdateSceneItem(int ItemIndex,OleMenuCommand Command){
        ThreadHelper.ThrowIfNotOnUIThread();

        var Presentation=this.RefreshPresentation();
        var IsVisible=SceneDesignerContextMenuPolicy.IsItemSlotVisible(
                                                                       Presentation.Mode,ItemIndex,Presentation.Items.Count);
        Command.Visible=IsVisible;
        Command.Enabled=IsVisible;
        if(!IsVisible){
            // 使わないスロットに状態を残さない(次に表示されたとき前回の文字や✓が見えないように)。
            Command.Checked=false;
            Command.Text=string.Empty;
            return;
        }

        var Item=Presentation.Items[ItemIndex];

        // ✓はシーン デザイナーが今開いているシーン基準。以前はキャレット位置基準だったため、
        // デザイナーが別ファイルのシーンを表示しているのに手元のシーンへ✓が付いていた。
        // 一覧の項目は右クリックしたファイル内のシーンだけなので、別ファイルのシーンを開いて
        // いる場合はどの項目にも✓が付かない(それが正しい)。
        Command.Checked=SceneDesignerMenuSelectionPolicy.IsLatched(
            this.Session.PinnedSceneKey?.ClassFullyQualifiedName,
            Item);
        Command.Text=SceneDesignerContextMenuPolicy.ItemText(
            Item,
            this.CurrentInvocation is { } Invocation
            &&SceneDesignerMenuSelectionPolicy.IsAtCaret(
                Invocation.TargetSceneFullyQualifiedName,
                Item));
    }

    private void UpdateOverflowItem(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();

        var Presentation=this.RefreshPresentation();
        var HiddenCount=Presentation.Mode==SceneDesignerMenuMode.SceneList
                            ?SceneDesignerContextMenuPolicy.HiddenItemCount(Presentation.Items.Count,SceneItemCommandCount)
                            :0;
        this.OverflowCommand.Visible=HiddenCount>0;
        this.OverflowCommand.Enabled=false;
        this.OverflowCommand.Text=HiddenCount>0
            ?SceneDesignerContextMenuPolicy.OverflowText(HiddenCount)
            :string.Empty;
    }

    private SceneDesignerCommandPresentation RefreshPresentation(){
        ThreadHelper.ThrowIfNotOnUIThread();

        // ソリューションエクスプローラーの右クリックはキャレットの概念が無いため常に位置0を渡す。
        // SceneDocumentMenuSnapshot.GetTargetStateは位置0がどの型宣言にも属さない前提で自然に
        // OutsideTypeへ落ち、ファイル内の全Sceneを一覧表示する(コードエディタ側と同じロジックを
        // 無改造で再利用できる)。
        if(this.DocumentTracker.TryGetSolutionExplorerInvocationFilePath(out var SolutionExplorerFilePath)){
            return this.BuildPresentation(SolutionExplorerFilePath,CaretPosition:0,EditorView:null);
        }

        var FilePath=this.DocumentTracker.CurrentFilePath;
        if(FilePath is null){
            this.CurrentInvocation=null;
            return SceneDesignerContextMenuPolicy.Create(null,SceneDesignerTargetState.Unavailable,[]);
        }

        if(!this.DocumentTracker.TryCaptureActiveCaretPosition(FilePath,out var CaretPosition)){
            this.CurrentInvocation=null;
            return SceneDesignerContextMenuPolicy.Create(FilePath,SceneDesignerTargetState.Unavailable,[]);
        }

        return this.BuildPresentation(FilePath,CaretPosition,this.DocumentTracker.CaptureActiveTextView(FilePath));
    }

    private SceneDesignerCommandPresentation BuildPresentation(string?FilePath,int CaretPosition,IVsTextView?EditorView){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(FilePath is null){
            this.CurrentInvocation=null;
            return SceneDesignerContextMenuPolicy.Create(null,SceneDesignerTargetState.Unavailable,[]);
        }

        var Snapshot=this.Session.GetMenuSnapshot(FilePath);
        var TargetState=Snapshot.GetTargetState(CaretPosition);
        var TargetSceneFullyQualifiedName=Snapshot.GetTargetSceneFullyQualifiedName(CaretPosition);
        var Presentation=SceneDesignerContextMenuPolicy.Create(
                                                               FilePath,
                                                               TargetState,
                                                               Snapshot.Items);
        this.CurrentInvocation=Presentation.Enabled
            ?new SceneMenuInvocation(
                FilePath,
                Presentation.Items,
                TargetSceneFullyQualifiedName,
                EditorView)
            :null;
        return Presentation;
    }

    private void ExecuteDirect(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();

        var Presentation=this.RefreshPresentation();
        if(Presentation.Mode!=SceneDesignerMenuMode.Direct
            ||this.CurrentInvocation is not { } Invocation
            ||Invocation.Items.Count!=1){
            return;
        }

        this.OpenScene(Invocation,Invocation.Items[0].FullyQualifiedName);
    }

    /// <summary>
    /// スロットの実行。<paramref name="ItemIndex"/> は登録時のクロージャで固定されており、共有される
    /// 可変状態から導出しない — ここが「1番目を押したのに2番目のシーンが開く」誘導ミスを構造的に
    /// 起こり得なくしている点。件数が減った直後のクリックに備えて範囲だけは確認する。
    /// </summary>
    private void ExecuteSceneItem(int ItemIndex){
        ThreadHelper.ThrowIfNotOnUIThread();

        SceneMenuInvocation?Invocation=this.CurrentInvocation;
        if(Invocation is null
            ||ItemIndex<0
            ||ItemIndex>=Invocation.Items.Count){
            return;
        }

        this.OpenScene(Invocation,Invocation.Items[ItemIndex].FullyQualifiedName);
    }

    private void OpenScene(SceneMenuInvocation Invocation,string FullyQualifiedName){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.SetStatusBarText("シーンを解析しています...");

        _=this.Package.JoinableTaskFactory.RunAsync(async delegate {
            try {
                var Models=await this.Session.AnalyzeFileNowAsync(
                                                                 Invocation.FilePath,
                                                                 this.Package.DisposalToken).ConfigureAwait(false);
                await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);

                var Chosen=Models.SingleOrDefault(Model=>
                                                     string.Equals(Model.ClassFullyQualifiedName,FullyQualifiedName,StringComparison.Ordinal));
                if(Chosen is null){
                    this.SetStatusBarText("選択した IScene 実装クラスは編集後に見つからなくなりました。");
                    return;
                }

                var Window=await this.Package.FindToolWindowAsync(
                                                                  typeof(SceneDesignerToolWindowPane),
                                                                  0,
                                                                  true,
                                                                  this.Package.DisposalToken);
                if(Window is not SceneDesignerToolWindowPane Pane
                    ||Pane.Frame is not IVsWindowFrame Frame){
                    throw new NotSupportedException("Cannot create the Scene Designer tool window.");
                }

                var Snapshot=this.Session.Pin(Chosen);
                Pane.SetTarget(this.Session,Snapshot);
                ToolWindowFramePresenter.ShowWithoutActivation(
                    ()=>ShowFrameWithoutActivation(Frame),
                    Invocation.EditorView is null?null:()=>RestoreEditorFocus(Invocation.EditorView));
                this.SetStatusBarText("シーン デザイナーを開きました。");
            } catch(OperationCanceledException) when(this.Package.DisposalToken.IsCancellationRequested){
                // Package shutdown.
            } catch(Exception Ex){
                await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ActivityLog.LogError(nameof(OpenSceneDesignerCommand),Ex.ToString());
                this.SetStatusBarText("シーンの解析またはデザイナー表示に失敗しました。詳細は ActivityLog.xml を確認してください。");
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
