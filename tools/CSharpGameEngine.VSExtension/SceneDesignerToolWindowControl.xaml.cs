using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene Designer の WPF content。Scene が未選択の間は <see cref="SceneHwndHost"/> を作らず、
/// WPF の空状態だけを表示します。HwndHost を非表示のまま残すと Win32 airspace が前フレームを
/// 覆い、空状態でもごみ画像が残るため、Host の attach / dispose を選択状態と一対一にします。
/// </summary>
public partial class SceneDesignerToolWindowControl:UserControl,IDisposable {
    private SceneHwndHost?Host;
    private SceneEditorSessionService?Session;
    private CancellationTokenSource?CatalogCancellation;
    private long CatalogGeneration;
    private bool UpdatingSelector;
    private PendingDuplicateSelection?PendingDuplicateSelectionRestore;
    /// <summary>
    /// M0038: ソース書き戻しを直列に保つためのチェーン(<see cref="OnEditCommitRequested"/>参照)。
    /// M0039: 値は「この編集を含め、これまでの書き戻しが全て成功したか」を運ぶ。前段が不健全な
    /// (=既に失敗している)ときは、後続の予測がもう信用できない基準の上に積み上がっているため、
    /// 書き込みを試みず失敗として連鎖伝播する(<see cref="CommitEditAsync"/>参照)。
    /// </summary>
    private Task<bool>PendingCommitWrite=Task.FromResult(true);
    private bool Disposed;

    public SceneDesignerToolWindowControl(){
        InitializeComponent();
        // M0033: World/Local切り替えはギズモ上のアイコン(TransformSpaceToggleGizmo)へ移った。
        // M0020 Phase 1(ADR-0041 決定事項2): ナビゲーション(WASDQE)を受けるにはWPFの
        // キーボードフォーカスが要る(Wheelはネイティブ側で受けるためフォーカス不要、
        // SceneHwndHost.HandleWheel参照)。IsTabStopは変えない — Tab巡回でここへ止まらせず、
        // ビューポート内をクリックしたときだけ(OnViewportActivated)フォーカスを与える。
        RenderHostGrid.Focusable=true;
        KeyboardNavigation.SetIsTabStop(RenderHostGrid,false);
        this.Loaded+=this.OnLoaded;
        RenderHostGrid.KeyDown+=this.OnViewportKeyDown;
        RenderHostGrid.KeyUp+=this.OnViewportKeyUp;
        RenderHostGrid.LostKeyboardFocus+=this.OnViewportLostKeyboardFocus;
    }

    /// <summary>
    /// M0037: <see cref="SceneDesignerToolWindowPane"/>がEdit.Undoの<c>QueryStatus</c>で使う。
    /// M0036はCtrl+ZをWPFの<c>PreviewKeyDown</c>で横取りしようとしたが、Ctrl+ZはGlobalスコープで
    /// Edit.Undoに割り当て済みのためVSシェルがコマンドとして先に消費してしまい、WPFの入力系には
    /// 一切届かなかった(実機で無反応だった根本原因)。正しい経路はペイン自身がシェルコマンドとして
    /// Undo/Redoを引き受けること — 判定にはWPFフォーカスの有無だけを使う。
    /// </summary>
    internal bool IsViewportFocused=>RenderHostGrid.IsKeyboardFocusWithin;

    internal bool HasPinnedSnapshot=>this.Session?.PinnedSnapshot is not null;

    /// <summary>M0038: 背後のC#ソース書き戻しがまだ飛行中か(<see cref="SceneDesignerUndoCommandPolicy.CanExecute"/>参照)。</summary>
    internal bool HasPendingSourceWrite=>!this.PendingCommitWrite.IsCompleted;

    /// <summary>実際のC#ソースUndoが成功した後に呼び出し側(ペイン)が呼ぶ想定。</summary>
    internal bool TryUndoFromCommand(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Session is not { }Session||!SceneDesignerUndoCommandPolicy.CanExecute(
            this.IsViewportFocused,Session.PinnedSnapshot is not null,this.HasPendingSourceWrite)){
            return false;
        }

        bool Applied=Session.TryUndo();
        if(Applied){ this.Host?.TryFastUndo(); }
        return Applied;
    }

    internal bool TryRedoFromCommand(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Session is not { }Session||!SceneDesignerUndoCommandPolicy.CanExecute(
            this.IsViewportFocused,Session.PinnedSnapshot is not null,this.HasPendingSourceWrite)){
            return false;
        }

        bool Applied=Session.TryRedo();
        if(Applied){ this.Host?.TryFastRedo(); }
        return Applied;
    }

    internal void SetSession(SceneEditorSessionService Session){
        if(this.Disposed||ReferenceEquals(this.Session,Session)){
            return;
        }

        this.DetachSession();
        this.Session=Session;
        Session.PinnedSnapshotChanged+=this.OnPinnedSnapshotChanged;
        Session.Selection.SelectionChanged+=this.OnSelectionChanged;
        Session.ActiveDocumentModelsChanged+=this.OnActiveDocumentModelsChanged;

        if(Session.PinnedSnapshot is { } Snapshot){
            this.ShowSnapshot(Snapshot);
        } else {
            this.ShowEmpty("シーンを選択してください。");
        }

        this.RefreshSceneCatalog();
    }

    internal void SetTarget(SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot){
        this.SetSession(Session);
        this.ShowSnapshot(Snapshot);

        // SetSessionは同一セッションだと早期returnするため、Designerが既に開いている状態で
        // コンテキストメニュー(OpenSceneDesignerCommand)から開くとカタログ再取得が走らず、
        // SceneSelectorが表示中のシーンと食い違ったままになる(空欄のまま、あるいは直前に
        // 選んだ別シーンのまま)。Pinは呼び出し側で既に済んでいるのでここで選択を合わせ、
        // カタログが未取得のときだけ取得し直して完了時に反映させる。
        if(!this.TrySyncSelectorToPinnedScene()){
            this.RefreshSceneCatalog();
        }
    }

    private void OnLoaded(object Sender,RoutedEventArgs Args){
        if(this.Session is not null){
            this.RefreshSceneCatalog();
        }
    }

    private void OnPinnedSnapshotChanged(SceneDocumentSnapshot?Snapshot){
        this.DispatchToUi(()=>{
            if(Snapshot is null){
                this.ShowEmpty("選択中のシーンは解決できなくなりました。");
            } else {
                this.ShowSnapshot(Snapshot);
                this.TryRestoreDuplicateSelection(Snapshot);
            }

            // Pinはコンテキストメニュー経由やClearPinからも変わる。ComboBoxの選択はPinの変化に
            // 追従させ、「ビューポートが表示しているシーン＝選択されている項目」を常に保つ。
            this.TrySyncSelectorToPinnedScene();
        });
    }

    private void OnActiveDocumentModelsChanged(System.Collections.Generic.IReadOnlyList<SceneModel> Models)=>
        this.DispatchToUi(this.RefreshSceneCatalog);

    private void OnSelectionChanged(SceneSelection Selection){
        this.DispatchToUi(()=>{
            if(this.Session?.PinnedSnapshot is { } Snapshot&&Snapshot.Key.Equals(Selection.Key)){
                this.Host?.SetSelection(Snapshot,Selection);
            }
        });
    }

    private void ShowSnapshot(SceneDocumentSnapshot Snapshot){
        SceneModel?Model=Snapshot.LastKnownGoodModel??Snapshot.CurrentModel;
        if(Model?.Root is null){
            this.ShowEmpty(Snapshot.HasBlockingErrors
                ?"シーンの解析に失敗しました。C# のエラーを修正してください。"
                :"描画できるシーンがありません。");
            return;
        }

        SceneHwndHost Host=this.EnsureHost();
        Host.SetTarget(Snapshot);
        if(this.Session?.Selection.CurrentSelection is { } Selection){
            Host.SetSelection(Snapshot,Selection);
        }
    }

    private SceneHwndHost EnsureHost(){
        if(this.Host is not null){
            return this.Host;
        }

        // SceneReady のときだけ HwndHost を attach する。未選択状態で BuildWindowCore を呼ばない。
        if(!SceneDesignerHostAttachmentPolicy.ShouldAttach(SceneDesignerHostState.SceneReady)){
            throw new InvalidOperationException("Scene Designer host の attach 状態が不正です。");
        }

        SceneHwndHost Host=new();
        Host.StatusChanged+=this.OnStatusChanged;
        Host.ViewportActivated+=this.OnViewportActivated;
        Host.ObjectPicked+=this.OnObjectPicked;
        Host.ObjectsMarqueePicked+=this.OnObjectsMarqueePicked;
        Host.EditCommitRequested+=this.OnEditCommitRequested;
        this.Host=Host;
        EmptyStateText.Visibility=Visibility.Collapsed;
        RenderHostGrid.Children.Add(Host);
        return Host;
    }

    private void ShowEmpty(string Message){
        this.DetachHost();
        StatusText.Text=Message;
        EmptyStateText.Text=Message;
        EmptyStateText.Visibility=Visibility.Visible;
    }

    private void DetachHost(){
        SceneHwndHost?Host=this.Host;
        if(Host is null){
            return;
        }

        this.Host=null;
        RenderHostGrid.Children.Remove(Host);
        Host.StatusChanged-=this.OnStatusChanged;
        Host.ViewportActivated-=this.OnViewportActivated;
        Host.ObjectPicked-=this.OnObjectPicked;
        Host.ObjectsMarqueePicked-=this.OnObjectsMarqueePicked;
        Host.EditCommitRequested-=this.OnEditCommitRequested;
        // Dispose triggers DestroyWindowCore, which cancels the latest-wins worker and disposes the
        // native child HWND. Its stale callback is additionally rejected by SceneHwndHost's surface
        // and generation checks, so it cannot repaint this WPF-only empty state.
        Host.Dispose();
    }

    private void RefreshSceneCatalog(){
        SceneEditorSessionService?Session=this.Session;
        if(this.Disposed||Session is null){
            return;
        }

        this.CatalogCancellation?.Cancel();
        this.CatalogCancellation?.Dispose();
        CancellationTokenSource Cancellation=new();
        this.CatalogCancellation=Cancellation;
        long Generation=++this.CatalogGeneration;
        SceneKey?CatalogSceneKey=Session.PinnedSceneKey;
        SceneSelector.IsEnabled=false;
        _=this.RefreshSceneCatalogAsync(Session,CatalogSceneKey,Generation,Cancellation.Token);
    }

#pragma warning disable VSTHRD001 // WPF Dispatcher is the owning UI dispatcher of this control; no AsyncPackage/JTF chain is available here.
    private async Task RefreshSceneCatalogAsync(
        SceneEditorSessionService Session,SceneKey?CatalogSceneKey,long Generation,CancellationToken CancellationToken){
        try {
            ProjectSceneCatalog?Catalog=await Session.GetProjectSceneCatalogAsync(CatalogSceneKey,CancellationToken).ConfigureAwait(false);
            if(CancellationToken.IsCancellationRequested){
                return;
            }

            await this.Dispatcher.InvokeAsync(()=>{
                if(this.Disposed||!ReferenceEquals(this.Session,Session)||Generation!=this.CatalogGeneration){
                    return;
                }

                this.UpdatingSelector=true;
                try {
                    SceneSelector.ItemsSource=Catalog?.Entries;
                    SceneSelector.IsEnabled=Catalog is { Entries.Count:>0 };
                } finally {
                    this.UpdatingSelector=false;
                }

                bool Selected=this.TrySyncSelectorToPinnedScene();
                if(!SceneCatalogTargetPolicy.KeepsPresentedScene(
                    Session.PinnedSnapshot is not null,Catalog is not null)){
                    this.ShowEmpty("シーンを選択してください。");
                } else if(Catalog is not null&&Session.PinnedSceneKey is { } Key
                    &&string.Equals(Key.ProjectId,Catalog.ProjectId,StringComparison.Ordinal)
                    &&!Selected){
                    Session.ClearPin();
                }
            });
        } catch(OperationCanceledException){
            // A newer active document/project request superseded this catalog.
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(SceneDesignerToolWindowControl),Ex.ToString());
            this.DispatchToUi(()=>{
                if(!this.Disposed&&Generation==this.CatalogGeneration){
                    SceneSelector.ItemsSource=null;
                    SceneSelector.IsEnabled=false;
                    if(!SceneCatalogTargetPolicy.KeepsPresentedScene(
                        Session.PinnedSnapshot is not null,HasCatalog:false)){
                        this.ShowEmpty("シーン一覧の取得に失敗しました。ActivityLog.xml を確認してください。");
                    }
                }
            });
        }
    }

#pragma warning restore VSTHRD001

    /// <summary>
    /// ComboBoxの選択を、現在Pinされているシーンへ合わせる。<c>SelectedItem</c>を書き換える唯一の
    /// 経路にすることで、カタログ再取得経路とPin変化経路で選択の決め方がずれないようにしている。
    /// 一致するEntryを選択できたかを返す(カタログ未取得、または別プロジェクトのシーンがPinされて
    /// いる場合はfalse)。
    /// </summary>
    private bool TrySyncSelectorToPinnedScene(){
        if(this.Disposed
            ||this.Session is not { } Session
            ||SceneSelector.ItemsSource is not IEnumerable<ProjectSceneCatalogEntry>Entries){
            return false;
        }

        ProjectSceneCatalogEntry?Match=Session.PinnedSceneKey is { } Key
            ?Entries.FirstOrDefault(Entry=>Entry.Key.Equals(Key))
            :null;

        // 自分で入れた選択でOnSceneSelectionChangedが発火すると、Pin→選択→Pinと往復してしまう。
        this.UpdatingSelector=true;
        try {
            SceneSelector.SelectedItem=Match;
        } finally {
            this.UpdatingSelector=false;
        }

        return Match is not null;
    }

    private void OnSceneSelectionChanged(object Sender,SelectionChangedEventArgs Args){
        if(this.UpdatingSelector||this.Disposed||this.Session is not { } Session
            ||SceneSelector.SelectedItem is not ProjectSceneCatalogEntry Entry){
            return;
        }

        _=this.SelectSceneAsync(Session,Entry.Key);
    }

    private async Task SelectSceneAsync(SceneEditorSessionService Session,SceneKey Key){
        try {
            StatusText.Text="シーンを解析中...";
            SceneModel?Model=await Session.ResolveSceneAsync(Key,CancellationToken.None).ConfigureAwait(true);
            if(this.Disposed||!ReferenceEquals(this.Session,Session)){
                return;
            }

            if(Model?.Root is null){
                Session.ClearPin();
                this.ShowEmpty("選択したシーンは解決できないか、解析エラーがあります。");
                return;
            }

            // Pin is Designer 内だけの切替であり、DTE/TextView を操作しない。コードエディタの
            // キャレット、タブ、キーボードフォーカスはこの選択では移動しない。
            this.ShowSnapshot(Session.Pin(Model));
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(SceneDesignerToolWindowControl),Ex.ToString());
            if(!this.Disposed){
                Session.ClearPin();
                this.ShowEmpty("シーンの選択に失敗しました。ActivityLog.xml を確認してください。");
            }
        }
    }

    private void DetachSession(){
        this.PendingDuplicateSelectionRestore=null;
        if(this.Session is null){
            return;
        }

        this.Session.PinnedSnapshotChanged-=this.OnPinnedSnapshotChanged;
        this.Session.Selection.SelectionChanged-=this.OnSelectionChanged;
        this.Session.ActiveDocumentModelsChanged-=this.OnActiveDocumentModelsChanged;
        this.Session=null;
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;
        this.CatalogCancellation?.Cancel();
        this.CatalogCancellation?.Dispose();
        this.CatalogCancellation=null;
        this.Loaded-=this.OnLoaded;
        this.DetachSession();
        this.DetachHost();
    }

    private void OnStatusChanged(string Status){
        StatusText.Text=Status;
    }

    /// <summary>
    /// <see cref="SceneHwndHost.ViewportActivated"/>の購読先。ビューポート内をクリックした
    /// 瞬間だけキーボードフォーカスを与える(ADR-0041 決定事項2) — <see cref="OnLoaded"/>や
    /// <see cref="ShowSnapshot"/>/<see cref="SelectSceneAsync"/>では絶対に呼ばない。VS標準の
    /// ツールウィンドウフォーカスモデルを踏襲し、開いただけ・シーンを切り替えただけでは
    /// コードエディタからフォーカスを奪わない。
    /// </summary>
    private void OnViewportActivated(){
        if(!RenderHostGrid.IsKeyboardFocusWithin){
            Keyboard.Focus(RenderHostGrid);
        }
    }

    /// <summary>
    /// <see cref="SceneHwndHost.ObjectPicked"/>の購読先(M0020 Phase 2)。ヒットNodePathを
    /// <see cref="EditorNodeId"/>へ解決し、<see cref="SceneSelectionService.Select"/>へ発行する
    /// (Origin=Viewport)。空クリックは<c>Clear</c>ではなく空リストの<c>Select</c>で表す —
    /// 唯一の既存前例<c>CSharpGameEngineViewerPackage.OnLegacyHierarchySelectionChanged</c>に倣う。
    /// </summary>
    private void OnObjectPicked(NodePath?HitPath,bool CtrlHeld){
        if(this.Session is not { } Session||Session.PinnedSnapshot is not { } Snapshot){
            return;
        }

        EditorNodeId?HitId=HitPath is { } Path&&Snapshot.Projection?.TryGetId(Path,out EditorNodeId Id)==true
            ?Id
            :null;

        // どのノードが選択されたか実機で目視確認できるようにする(選択のハイライト以外に
        // フィードバックが無かったため)。
        StatusText.Text=HitId is { } DisplayId&&Snapshot.Projection?.TryGetNode(DisplayId,out SceneProjectionNode DisplayNode)==true
            ?$"選択: {DisplayNode.Node.DisplayName}({DisplayNode.Path.ToDisplayString()})"
            :"選択解除";

        if(!CtrlHeld){
            Session.Selection.Select(
                Snapshot.Key,HitId is { } Single?[Single]:[],HitId,
                ComponentType:null,MemberName:null,SceneSelectionOrigin.Viewport,Guid.NewGuid());
            return;
        }

        if(HitId is not { } ToggledId){
            return; // Ctrl+空クリックは現状維持(仕様に明記無し)。
        }

        IReadOnlyList<EditorNodeId>Current=Session.Selection.CurrentSelection is { } Existing
            &&Existing.Key.Equals(Snapshot.Key)
            ?Existing.NodeIds
            :[];
        List<EditorNodeId>Updated=[..Current];
        bool Removed=Updated.Remove(ToggledId);
        if(!Removed){
            Updated.Add(ToggledId);
        }

        EditorNodeId?NewPrimary=Removed?(Updated.Count>0?Updated[^1]:null):ToggledId;
        Session.Selection.Select(
            Snapshot.Key,Updated,NewPrimary,ComponentType:null,MemberName:null,SceneSelectionOrigin.Viewport,Guid.NewGuid());
    }

    /// <summary>
    /// Task Cの矩形選択を共通selectionへ発行する。通常は置換、Ctrlはトグルではなく加算。
    /// Viewportは複数NodeIdsを保持する一方、現行WPF TreeViewはprimary 1件だけを表示する。
    /// この非対称性は意図的で、Hierarchyを複数選択UIへ変更するまではprimaryを最終ヒットに固定する。
    /// </summary>
    private void OnObjectsMarqueePicked(IReadOnlyList<NodePath>HitPaths,bool CtrlHeld){
        if(this.Session is not { }Session||Session.PinnedSnapshot is not { }Snapshot
            ||Snapshot.Projection is not { }Projection){
            return;
        }

        List<EditorNodeId>HitIds=[];
        foreach(NodePath Path in HitPaths){
            if(Projection.TryGetId(Path,out EditorNodeId Id)){
                HitIds.Add(Id);
            }
        }

        SceneSelection?Existing=Session.Selection.CurrentSelection is { }Current&&Current.Key.Equals(Snapshot.Key)
            ?Current
            :null;
        MarqueeSelectionUpdate Update=MarqueeSelectionPolicy.Combine(
            Existing?.NodeIds??[],HitIds,CtrlHeld,Existing?.PrimaryNodeId);
        StatusText.Text=Update.NodeIds.Count==0
            ?"選択解除"
            :$"矩形選択: {Update.NodeIds.Count}件";
        Session.Selection.Select(
            Snapshot.Key,Update.NodeIds,Update.PrimaryNodeId,
            ComponentType:null,MemberName:null,SceneSelectionOrigin.Viewport,Guid.NewGuid());
    }

    /// <summary>
    /// M0038: 編集後モデルを<b>先に予測して配信し、ローカルのトランザクションを即座に閉じる</b>。
    /// これにより「手を離してからC#書き戻し(プロジェクト全体のコンパイル取得を含む)が終わるまで
    /// 次の操作を受け付けない」という最大の待ちが消える。ソース書き戻しは背後で直列に継続し、
    /// ADR-0040どおりC#ソースが唯一の正本であり続ける。
    ///
    /// <para>
    /// 予測できなかった場合(構造編集など対応外の操作)は<c>Predicted=false</c>となり、
    /// 書き戻し完了で<c>CompleteEditCommit</c>を呼ぶ従来どおりの挙動に落ちる。
    /// </para>
    /// </summary>
    private void OnEditCommitRequested(SceneEditCommitRequest Request){
        ThreadHelper.ThrowIfNotOnUIThread();
        SceneEditorSessionService?Session=this.Session;
        SceneHwndHost?Host=this.Host;
        if(Session is null||Host is null){
            Host?.CompleteEditCommit(false,"Scene Editorセッションが利用できません。");
            return;
        }

        if(Request.IsDuplicate){
            this.PendingDuplicateSelectionRestore=new PendingDuplicateSelection(
                Request.SceneKey,Request.DuplicateResultPaths.ToArray());
        }
        bool Predicted=Session.TryPublishPrediction(Request);
        if(Predicted){
            Host.CompleteEditCommit(true,null);
        }
        // 書き戻しは直列に保つ。並行させると、後続コミットが前のコミットを含まない古いソースを
        // 基準にリベースしてしまい、直前の編集を打ち消しかねない。Task<bool>はチェーンの健全性
        // (直前までの書き戻しが全て成功したか)を運ぶ(M0039、詳細はCommitEditAsync参照)。
        this.PendingCommitWrite=this.CommitEditAsync(
            Session,Host,Request,this.PendingCommitWrite,Predicted);
    }

    /// <summary>
    /// M0039: 戻り値は「この編集を含め、これまでの書き戻しが全て成功したか」。
    ///
    /// <para>
    /// 前段(<paramref name="PreviousWrite"/>)が既に不健全なら、この編集の予測はもう信用できない
    /// 基準(=本当は反映されていない前段の編集)の上に積み上がっている — 実際に書き込むとその欠落分
    /// だけ内容がずれた誤ったソースを生成しうるため、書き込みを試みず失敗として扱い連鎖的に
    /// 後段へ伝播する。<c>SceneHwndHost.RecentTransformCommits</c>(先読みUndoスタック)と実際の
    /// <c>ITextUndoHistory</c>が食い違って「Undoが直前の1回しかできない」状態になっていたのが
    /// M0038出荷直後に見つかった不具合で、この中断伝播がその修正の中心。
    /// </para>
    ///
    /// <para>
    /// このチェーンで<b>最初に</b>不健全化した箇所だけ、Roslynへ直接再解決した真実を
    /// pin中snapshotへ強制配信し(<see cref="SceneEditorSessionService.RefreshPinnedSnapshotFromSourceAsync"/>)、
    /// 先読みUndo/Redoスタックを丸ごと破棄する(<see cref="SceneHwndHost.ResyncAfterCommitFailure"/>)。
    /// 既に不健全なチェーンを渡ってきた後続の呼び出しは、再同期を二重に行わない。
    /// </para>
    /// </summary>
#pragma warning disable VSTHRD003 // 先行する書き戻しは同じControlがこのメソッドで開始したTaskであり、UIスレッドをブロックせずConfigureAwait(false)で待つだけなのでデッドロックしない。順序保証のためチェーンは外せない。
    private async Task<bool>CommitEditAsync(
        SceneEditorSessionService Session,SceneHwndHost Host,SceneEditCommitRequest Request,
        Task<bool>PreviousWrite,bool AlreadyCompletedLocally){
        bool ChainHealthy=await PreviousWrite.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        SceneEditCommitResult Result;
        if(!ChainHealthy){
            Result=new SceneEditCommitResult(
                false,"直前の編集の反映に失敗したため、この編集も反映されませんでした。",WasRebased:false);
        } else {
            try {
                Result=await Session.CommitAsync(Request,CancellationToken.None).ConfigureAwait(false);
            } catch(Exception Ex){
                ActivityLog.LogError(nameof(SceneDesignerToolWindowControl),Ex.ToString());
                Result=new SceneEditCommitResult(false,"C#ソースへの反映中にエラーが発生しました。",WasRebased:false);
            }
        }

        // このチェーンで最初に不健全化した箇所だけ、画面をソースの真実へ強制同期する。
        bool IsFaultOrigin=ChainHealthy&&!Result.Succeeded;
        if(IsFaultOrigin){
            try {
                await Session.RefreshPinnedSnapshotFromSourceAsync(CancellationToken.None).ConfigureAwait(false);
            } catch(Exception Ex){
                ActivityLog.LogError(nameof(SceneDesignerToolWindowControl),Ex.ToString());
            }
        }

        this.DispatchToUi(()=>{
            ThreadHelper.ThrowIfNotOnUIThread();
            if(!Result.Succeeded){
                this.PendingDuplicateSelectionRestore=null;
            }
            if(this.Disposed||!ReferenceEquals(this.Session,Session)||!ReferenceEquals(this.Host,Host)){
                return;
            }
            // 予測で既にトランザクションを閉じている場合、CompleteEditCommitはStateがIdleなので
            // 何もしない(意図的)。
            if(!AlreadyCompletedLocally){
                Host.CompleteEditCommit(Result.Succeeded,Result.ErrorMessage);
            } else if(!Result.Succeeded){
                if(IsFaultOrigin){ Host.ResyncAfterCommitFailure(); }
                Host.ReportCommitFailure(Result.ErrorMessage);
            }
            if(!Result.Succeeded
                &&Package.GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
                StatusBar.SetText(Result.ErrorMessage??"Scene Designerの編集を反映できませんでした。");
            }
        });
        return Result.Succeeded;
    }

    private void TryRestoreDuplicateSelection(SceneDocumentSnapshot Snapshot){
        if(this.PendingDuplicateSelectionRestore is not { }Pending
            ||this.Session is not { }Session||!Pending.Key.Equals(Snapshot.Key)
            ||Snapshot.Projection is not { }Projection){
            return;
        }

        if(!DuplicateNodePlanner.TryResolveResultSelection(Projection,Pending.Paths,out IReadOnlyList<EditorNodeId>Ids)){
            return;
        }

        this.PendingDuplicateSelectionRestore=null;
        Session.Selection.Select(
            Snapshot.Key,Ids,Ids[^1],ComponentType:null,MemberName:null,
            SceneSelectionOrigin.Viewport,Guid.NewGuid());
    }

    private void OnViewportKeyDown(object Sender,KeyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Args.Key==Key.Escape){
            this.Host?.CancelCurrentOperation();
            Args.Handled=true;
            return;
        }
        if(TryMapNavigationKey(Args.Key,out NavigationKey NavKey)){
            this.Host?.HandleKeyDown(NavKey);
            Args.Handled=true;
            return;
        }

        if(Args.Key==Key.F){
            this.Host?.RequestFocusSelection();
            Args.Handled=true;
        }
    }

    private void OnViewportKeyUp(object Sender,KeyEventArgs Args){
        if(TryMapNavigationKey(Args.Key,out NavigationKey NavKey)){
            this.Host?.HandleKeyUp(NavKey);
        }
    }

    /// <summary>Alt+Tab等でVS自体からフォーカスが抜けた場合もここが発火し、Held Keyの張り付きを防ぐ。</summary>
    private void OnViewportLostKeyboardFocus(object Sender,KeyboardFocusChangedEventArgs Args)=>
        this.Host?.ClearHeldFlyKeys();

    private static bool TryMapNavigationKey(Key WpfKey,out NavigationKey NavKey){
        switch(WpfKey){
            case Key.W: NavKey=NavigationKey.W; return true;
            case Key.A: NavKey=NavigationKey.A; return true;
            case Key.S: NavKey=NavigationKey.S; return true;
            case Key.D: NavKey=NavigationKey.D; return true;
            case Key.Q: NavKey=NavigationKey.Q; return true;
            case Key.E: NavKey=NavigationKey.E; return true;
            default: NavKey=default; return false;
        }
    }

#pragma warning disable VSTHRD001 // WPF Dispatcher is the owning UI dispatcher of this control; no AsyncPackage/JTF chain is available here.
    private void DispatchToUi(Action Action){
        if(this.Disposed){
            return;
        }

        if(this.Dispatcher.CheckAccess()){
            Action();
        } else {
            _=this.Dispatcher.BeginInvoke(Action);
        }
    }
#pragma warning restore VSTHRD001
    private sealed record PendingDuplicateSelection(SceneKey Key,IReadOnlyList<NodePath>Paths);
}
