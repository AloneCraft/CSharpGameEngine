using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CSharpGameEngine.Contracts;
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
    private CancellationTokenSource?SceneSelectionCancellation;
    private CancellationTokenSource?PhysicsBakeCancellation;
    private AuthoringOperationSnapshot?PhysicsBakeOperation;
    private long CatalogGeneration;
    private bool UpdatingSelector;
    private bool UpdatingSpatialDebugProcessSelector;
    private PendingDuplicateSelection?PendingDuplicateSelectionRestore;
    private bool Disposed;
    private readonly SceneEditorEscapeRouter EscapeRouter;
    private readonly SceneSelectionRequestLifecycle SelectionRequestLifecycle=new();

    public SceneDesignerToolWindowControl(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.InitializeComponent();
        this.EscapeRouter=new(this,this.HandleEscape);
        this.VsixIdentityText.Text=VsixBuildIdentity.DisplayText;
        this.VsixIdentityText.ToolTip=VsixBuildIdentity.ToolTipText;
        // M0033: World/Local切り替えはギズモ上のアイコン(TransformSpaceToggleGizmo)へ移った。
        // M0020 Phase 1(ADR-0041 決定事項2): ナビゲーション(WASDQE)を受けるにはWPFの
        // キーボードフォーカスが要る(Wheelはネイティブ側で受けるためフォーカス不要、
        // SceneHwndHost.HandleWheel参照)。IsTabStopは変えない — Tab巡回でここへ止まらせず、
        // ビューポート内をクリックしたときだけ(OnViewportActivated)フォーカスを与える。
        this.RenderHostGrid.Focusable=true;
        KeyboardNavigation.SetIsTabStop(this.RenderHostGrid,false);
        this.Loaded+=this.OnLoaded;
        this.RenderHostGrid.KeyDown+=this.OnViewportKeyDown;
        this.RenderHostGrid.KeyUp+=this.OnViewportKeyUp;
        this.RenderHostGrid.LostKeyboardFocus+=this.OnViewportLostKeyboardFocus;
        this.CameraPreviewToggle.Click+=this.OnCameraPreviewToggleClicked;
        this.ViewModeSelector.SelectedValue=SceneEditorSettings.DefaultRenderMode.ToString();
        this.GlobalSpatialOctreeToggle.IsChecked=SceneEditorSettings.DefaultShowGlobalSpatialOctree;
        this.SetPhysicsPanelVisible(SceneEditorSettings.ReadShowPhysicsPanel(),Persist:false);
    }

    /// <summary>
    /// M0037/M0045: <see cref="TryUndoFromCommand"/>/<see cref="TryRedoFromCommand"/>と
    /// <see cref="SceneHwndHost.IsViewportFocusedProvider"/>が使う。
    /// M0036はCtrl+ZをWPFの<c>PreviewKeyDown</c>で横取りしようとしたが、Ctrl+ZはGlobalスコープで
    /// Edit.Undoに割り当て済みのためVSシェルがコマンドとして先に消費してしまい、WPFの入力系には
    /// 一切届かなかった(実機で無反応だった根本原因)。M0037はペイン自身をシェルコマンドとして
    /// 登録したが、それがVS全体のUndo管理を壊す実機回帰を招いたためM0045で撤回し、
    /// <see cref="SceneHwndHost"/>の<c>GetKeyState</c>ポーリングへ切り替えた
    /// (判定にはWPFフォーカスの有無だけを使う点は変わらない)。
    /// </summary>
    internal bool IsViewportFocused=> this.RenderHostGrid.IsKeyboardFocusWithin;

    /// <summary>
    /// M0051 Step 1-5: Undo / Redo / Save のいずれもこの範囲で引き受ける。ビューポートだけでなく
    /// Hierarchy / Inspector にフォーカスがある場合も含む — Hierarchyで編集した直後はフォーカスが
    /// そちらにあり、ビューポート限定だと編集した本人がその場で取り消せなかった。
    /// テキスト入力中(インラインリネーム等)の除外は
    /// <see cref="SceneDesignerInputRoutingPolicy"/>が一段目で済ませている。
    /// </summary>
    internal bool AnyDesignerSurfaceHasFocus=>
        this.IsViewportFocused||SceneEditorHub.AnyAuxiliarySurfaceHasFocus;

    internal bool HasPinnedSnapshot=>this.Session?.PinnedSnapshot is not null;

    /// <summary>
    /// M0047: 保存(<see cref="SceneEditorSessionService.SaveAsync"/>)が同じSceneのDocumentへ
    /// 非同期でアクセスしている間だけ true。値編集自体はもう非同期往復を挟まないため、
    /// このガードが塞ぐ窓は保存中の一瞬に限られる(<see cref="SceneDesignerUndoCommandPolicy.CanExecute"/>参照)。
    /// </summary>
    internal bool HasPendingSourceWrite=>
        this.Session is { }Session&&Session.PinnedSceneKey is { }Key&&Session.IsSaving(Key);

    /// <summary>
    /// M0047: <see cref="SceneEditorSessionService.TryUndo"/>はDocumentを同期で戻し、その場で
    /// 正しい状態を配信する(<see cref="SceneEditorSessionService.PinnedSnapshotChanged"/>)ため、
    /// 表示だけを先に戻す先読みは持たない。
    /// </summary>
    internal bool TryUndoFromCommand(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Session is not { }Session||!SceneDesignerUndoCommandPolicy.CanExecute(
            this.AnyDesignerSurfaceHasFocus,Session.PinnedSnapshot is not null,this.HasPendingSourceWrite)){
            return false;
        }

        return this.RunUndoStep(Session,Redo:false);
    }

    internal bool TryRedoFromCommand(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Session is not { }Session||!SceneDesignerUndoCommandPolicy.CanExecute(
            this.AnyDesignerSurfaceHasFocus,Session.PinnedSnapshot is not null,this.HasPendingSourceWrite)){
            return false;
        }

        return this.RunUndoStep(Session,Redo:true);
    }

    /// <summary>
    /// M0051 Step 1-6: Undo/Redoが「戻せるはずなのに戻せない」で止まったときだけステータスバーへ理由を出す。
    /// 戻すものが無いだけなら普通のことなので黙っている。
    /// </summary>
    private bool RunUndoStep(SceneEditorSessionService Session,bool Redo){
        ThreadHelper.ThrowIfNotOnUIThread();
        bool Stepped=Redo?Session.TryRedo(out string?BlockedReason):Session.TryUndo(out BlockedReason);
        if(!Stepped&&BlockedReason is { }Reason
            &&Package.GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
            StatusBar.SetText(Reason);
        }

        return Stepped;
    }

    /// <summary>M0045: <see cref="SceneHwndHost.UndoRequested"/>の購読先。</summary>
    private void OnUndoKeyRequested(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.OwnsKeyboard){ this.TryUndoFromCommand(); }
    }

    /// <summary>M0045: <see cref="SceneHwndHost.RedoRequested"/>の購読先。</summary>
    private void OnRedoKeyRequested(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.OwnsKeyboard){ this.TryRedoFromCommand(); }
    }

    /// <summary>M0095 1-2: of all open scene tabs, only the one showing the pinned scene acts on a key press.</summary>
    private bool OwnsKeyboard=>SceneDesignerUndoCommandPolicy.IsKeyboardOwner(this.FixedSceneKey,this.Session?.PinnedSceneKey);

    /// <summary>
    /// M0047: <see cref="SceneHwndHost.SaveRequested"/>の購読先。Documentのセッション全履歴を
    /// 実C#構文へ一度だけ再生してlive bufferへ書き戻す(<see cref="SceneEditorSessionService.SaveAsync"/>)。
    /// </summary>
    private void OnSaveKeyRequested(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(!this.OwnsKeyboard||this.Session is not { }Session||Session.PinnedSceneKey is not { }Key){
            return;
        }

        // M0051 Step 1-5: Step 1-4はSceneSaveTriggerPolicyを純粋関数として切り出しながら、
        // ここから呼ばずにSceneHwndHost側の生の論理和だけで済ませていた(テストは通るが製品コードは
        // ポリシーを経由しない配線漏れ)。Undo/Redo(SceneDesignerUndoCommandPolicy)と同じく、
        // Session側の状態を含む最終判定は必ずポリシーを通す。
        if(!SceneSaveTriggerPolicy.ShouldSave(
            this.AnyDesignerSurfaceHasFocus,HasPinnedSnapshot:true,Session.IsSaving(Key))){
            return;
        }

        _=this.SaveAsync(Session,Key);
    }

#pragma warning disable VSTHRD100 // async void はイベントハンドラからのfire-and-forget起点であり、ここでは意図的。
    private async Task SaveAsync(SceneEditorSessionService Session,SceneKey Key){
#pragma warning restore VSTHRD100
        SceneEditCommitResult Result;
        try {
            Result=await Session.SaveAsync(Key,CancellationToken.None).ConfigureAwait(false);
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(SceneDesignerToolWindowControl),Ex.ToString());
            Result=new SceneEditCommitResult(false,VsixText.Get("SessionSaveFailed"),WasRebased:false);
        }

        this.DispatchToUi(()=>{
            ThreadHelper.ThrowIfNotOnUIThread();
            if(this.Disposed||!ReferenceEquals(this.Session,Session)){
                return;
            }
            if(Package.GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
                StatusBar.SetText(Result.Succeeded?VsixText.Get("SceneDesigner_SaveSucceeded"):Result.ErrorMessage??VsixText.Get("SessionSaveFailed"));
            }
        });
    }

    /// <summary>
    /// ADR-0071: a Scene Editor tab shows only its own file's scene. The session pins one scene at a
    /// time, so a tab ignores the pins and selections of other scenes (another tab brought to front).
    /// </summary>
    internal SceneKey?FixedSceneKey { get;set;}

    /// <summary>A Scene Editor tab has one scene per file and hides the scene switcher.</summary>
    internal bool ShowsSceneSelector {
        get=>this.SceneSelector.Visibility==Visibility.Visible;
        set=>this.SceneSelector.Visibility=value?Visibility.Visible:Visibility.Collapsed;
    }

    internal void SetSession(SceneEditorSessionService Session){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed||ReferenceEquals(this.Session,Session)){
            return;
        }

        this.DetachSession();
        this.Session=Session;
        Session.PinnedSnapshotChanged+=this.OnPinnedSnapshotChanged;
        Session.Selection.SelectionChanged+=this.OnSelectionChanged;
        Session.ActiveDocumentModelsChanged+=this.OnActiveDocumentModelsChanged;

        if(Session.PinnedSnapshot is { } Snapshot&&SceneDesignerTargetFilter.Accepts(this.FixedSceneKey,Snapshot.Key)){
            this.ShowSnapshot(Snapshot);
        } else {
            this.ShowEmpty(VsixText.Get("SceneSelectPrompt"));
        }

        this.RefreshSceneCatalog();
    }

    internal void SetTarget(SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.SetSession(Session);
        this.CancelSceneSelectionRequest();
        this.ShowSnapshot(Snapshot);

        // SetSessionは同一セッションだと早期returnするため、Designerが既に開いている状態で
        // 別の経路(ツールバーのOpenSceneDesignerCommand等)から開くとカタログ再取得が走らず、
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
            ThreadHelper.ThrowIfNotOnUIThread();
            if(this.FixedSceneKey is not null&&!SceneDesignerTargetFilter.Accepts(this.FixedSceneKey,Snapshot?.Key)){
                return;
            }
            if(Snapshot is null){
                this.CancelSceneSelectionRequest();
                this.ShowEmpty(VsixText.Get("SceneDesigner_NoScene"));
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
            if(this.Session?.PinnedSnapshot is { } Snapshot&&Snapshot.Key.Equals(Selection.Key)
                &&SceneDesignerTargetFilter.Accepts(this.FixedSceneKey,Snapshot.Key)){
                this.Host?.SetSelection(Snapshot,Selection);
            }
        });
    }

    private void ShowSnapshot(SceneDocumentSnapshot Snapshot){
        ThreadHelper.ThrowIfNotOnUIThread();
        SceneModel?Model=Snapshot.LastKnownGoodModel??Snapshot.CurrentModel;
        if(Model?.Root is null){
            this.ShowEmpty(Snapshot.HasBlockingErrors
                ?VsixText.Get("SceneDesigner_BlockingErrors")
                :VsixText.Get("SceneDesigner_NoRenderableScene"));
            return;
        }

        SceneHwndHost Host=this.EnsureHost();
        Host.SetTarget(Snapshot,this.Session?.TryGetRuntimeProcessName(Snapshot.Key));
        if(this.Session?.Selection.CurrentSelection is { } Selection){
            Host.SetSelection(Snapshot,Selection);
        }
    }

    private SceneHwndHost EnsureHost(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Host is not null){
            return this.Host;
        }

        // SceneReady のときだけ HwndHost を attach する。未選択状態で BuildWindowCore を呼ばない。
        if(!SceneDesignerHostAttachmentPolicy.ShouldAttach(SceneDesignerHostState.SceneReady)){
            throw new InvalidOperationException(VsixText.Get("SceneDesigner_HostAttachmentInvalid"));
        }

        SceneHwndHost Host=new();
        Host.StatusChanged+=this.OnStatusChanged;
        Host.SpatialDebugStatusChanged+=this.OnSpatialDebugStatusChanged;
        Host.SpatialDebugProcessCandidatesChanged+=this.OnSpatialDebugProcessCandidatesChanged;
        Host.ViewportActivated+=this.OnViewportActivated;
        Host.CameraPreviewSelectionRequested+=this.OnCameraPreviewSelectionRequested;
        Host.ObjectPicked+=this.OnObjectPicked;
        Host.ObjectsMarqueePicked+=this.OnObjectsMarqueePicked;
        Host.EditCommitRequested+=this.OnEditCommitRequested;
        Host.IsViewportFocusedProvider=()=> this.RenderHostGrid.IsKeyboardFocusWithin;
        Host.UndoRequested+=this.OnUndoKeyRequested;
        Host.RedoRequested+=this.OnRedoKeyRequested;
        Host.SaveRequested+=this.OnSaveKeyRequested;
        this.Host=Host;
        this.ViewModeSelector.SelectedValue=SceneEditorSettings.ReadRenderMode().ToString();
        this.GlobalSpatialOctreeToggle.IsChecked=SceneEditorSettings.ReadShowGlobalSpatialOctree();
        this.EmptyStateText.Visibility=Visibility.Collapsed;
        this.RenderHostGrid.Children.Add(Host);
        Host.SetRenderMode(SceneEditorSettings.ReadRenderMode());
        Host.SetGlobalSpatialOctreeEnabled(SceneEditorSettings.ReadShowGlobalSpatialOctree());
        Host.SetPhysicsDebugEnabled(this.PhysicsDebugToggle.IsChecked==true);
        this.ApplyPhysicsDebugOptions(Host);
        return Host;
    }

    private void ShowEmpty(string Message){
        this.DetachHost();
        System.Windows.Automation.AutomationProperties.SetHelpText(this.SceneSelector,string.Empty);
        this.StatusText.Text=Message;
        this.EmptyStateText.Text=Message;
        this.EmptyStateText.Visibility=Visibility.Visible;
    }

    private void DetachHost(){
        SceneHwndHost?Host=this.Host;
        if(Host is null){
            return;
        }

        this.Host=null;
        this.ClearRuntimeDebugPresentation();
        this.RenderHostGrid.Children.Remove(Host);
        Host.StatusChanged-=this.OnStatusChanged;
        Host.SpatialDebugStatusChanged-=this.OnSpatialDebugStatusChanged;
        Host.SpatialDebugProcessCandidatesChanged-=this.OnSpatialDebugProcessCandidatesChanged;
        Host.ViewportActivated-=this.OnViewportActivated;
        Host.CameraPreviewSelectionRequested-=this.OnCameraPreviewSelectionRequested;
        Host.ObjectPicked-=this.OnObjectPicked;
        Host.ObjectsMarqueePicked-=this.OnObjectsMarqueePicked;
        Host.EditCommitRequested-=this.OnEditCommitRequested;
        Host.UndoRequested-=this.OnUndoKeyRequested;
        Host.RedoRequested-=this.OnRedoKeyRequested;
        Host.SaveRequested-=this.OnSaveKeyRequested;
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
        this.SceneSelector.IsEnabled=false;
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
                ThreadHelper.ThrowIfNotOnUIThread();
                if(this.Disposed||!ReferenceEquals(this.Session,Session)||Generation!=this.CatalogGeneration){
                    return;
                }

                this.UpdatingSelector=true;
                try {
                    this.SceneSelector.ItemsSource=Catalog?.Entries;
                    this.SceneSelector.IsEnabled=Catalog is { Entries.Count:>0 };
                    this.UpdatePhysicsBakeButtons();
                } finally {
                    this.UpdatingSelector=false;
                }

                bool Selected=this.TrySyncSelectorToPinnedScene();
                if(!SceneCatalogTargetPolicy.KeepsPresentedScene(
                    Session.PinnedSnapshot is not null,Catalog is not null)){
                    this.ShowEmpty(VsixText.Get("SceneSelectPrompt"));
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
                ThreadHelper.ThrowIfNotOnUIThread();
                if(!this.Disposed&&Generation==this.CatalogGeneration){
                    this.SceneSelector.ItemsSource=null;
                    this.SceneSelector.IsEnabled=false;
                    this.UpdatePhysicsBakeButtons();
                    if(!SceneCatalogTargetPolicy.KeepsPresentedScene(
                        Session.PinnedSnapshot is not null,HasCatalog:false)){
                        this.ShowEmpty(VsixText.Get("SceneDesigner_CatalogFailed"));
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
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed
            ||this.Session is not { } Session
            || this.SceneSelector.ItemsSource is not IEnumerable<ProjectSceneCatalogEntry>Entries){
            return false;
        }

        ProjectSceneCatalogEntry?Match=Session.PinnedSceneKey is { } Key
            ?Entries.FirstOrDefault(Entry=>Entry.Key.Equals(Key))
            :null;

        // 自分で入れた選択でOnSceneSelectionChangedが発火すると、Pin→選択→Pinと往復してしまう。
        this.UpdatingSelector=true;
        try {
            this.SceneSelector.SelectedItem=Match;
        } finally {
            this.UpdatingSelector=false;
        }
        this.UpdatePhysicsBakeButtons();

        return Match is not null;
    }

    private void OnSceneSelectionChanged(object Sender,SelectionChangedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.UpdatingSelector||this.Disposed||this.Session is not { } Session
            || this.SceneSelector.SelectedItem is not ProjectSceneCatalogEntry Entry){
            return;
        }

        this.CancelSceneSelectionRequest();
        CancellationTokenSource Cancellation=new();
        this.SceneSelectionCancellation=Cancellation;
        long Epoch=this.SelectionRequestLifecycle.Begin();
        ActivityLog.LogInformation(nameof(SceneDesignerToolWindowControl),
            $"Scene selection request {Epoch} started for {Entry.Key}.");
        _=this.SelectSceneAsync(Session,Entry.Key,Epoch,Cancellation,Cancellation.Token);
        this.UpdatePhysicsBakeButtons();
    }

    private async Task SelectSceneAsync(SceneEditorSessionService Session,SceneKey Key,long Epoch,
        CancellationTokenSource CancellationSource,CancellationToken CancellationToken){
        try {
            this.StatusText.Text=VsixText.Get("SceneDesigner_Analyzing");
            SceneModel?Model=await Session.ResolveSceneAsync(Key,CancellationToken).ConfigureAwait(true);
            if(CancellationToken.IsCancellationRequested||this.Disposed
                ||!ReferenceEquals(this.Session,Session)||!this.SelectionRequestLifecycle.IsCurrent(Epoch)){
                return;
            }
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(!this.TryCompleteSceneSelection(Epoch,CancellationSource)){
                return;
            }
            if(Model?.Root is null){
                this.StatusText.Text=VsixText.Get("SceneDesigner_SelectedSceneUnresolved");
                this.TrySyncSelectorToPinnedScene();
                return;
            }
            // Pin is Designer 内だけの切替であり、DTE/TextView を操作しない。コードエディタの
            // キャレット、タブ、キーボードフォーカスはこの選択では移動しない。
            ActivityLog.LogInformation(nameof(SceneDesignerToolWindowControl),
                $"Scene selection request {Epoch} completed for {Key}.");
            Session.Pin(Model);
        } catch(OperationCanceledException)when(CancellationToken.IsCancellationRequested){
            ActivityLog.LogInformation(nameof(SceneDesignerToolWindowControl),
                $"Scene selection request {Epoch} was superseded for {Key}.");
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(SceneDesignerToolWindowControl),Ex.ToString());
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(!this.Disposed&&ReferenceEquals(this.Session,Session)
                &&this.TryCompleteSceneSelection(Epoch,CancellationSource)){
                this.StatusText.Text=VsixText.Get("SceneDesigner_SelectFailed");
                this.TrySyncSelectorToPinnedScene();
            }
        }
    }

    private bool TryCompleteSceneSelection(long Epoch,CancellationTokenSource Cancellation){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(!this.SelectionRequestLifecycle.TryComplete(Epoch)){
            return false;
        }
        if(ReferenceEquals(this.SceneSelectionCancellation,Cancellation)){
            this.SceneSelectionCancellation=null;
        }
        Cancellation.Dispose();
        return true;
    }

    private void CancelSceneSelectionRequest(){
        this.SelectionRequestLifecycle.Invalidate();
        CancellationTokenSource?Cancellation=this.SceneSelectionCancellation;
        this.SceneSelectionCancellation=null;
        Cancellation?.Cancel();
        Cancellation?.Dispose();
    }

    private void DetachSession(){
        this.PendingDuplicateSelectionRestore=null;
        this.CancelSceneSelectionRequest();
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
        this.EscapeRouter.Dispose();
        this.CatalogCancellation?.Cancel();
        this.CatalogCancellation?.Dispose();
        this.CatalogCancellation=null;
        this.PhysicsBakeCancellation?.Cancel();
        this.PhysicsBakeCancellation?.Dispose();
        this.PhysicsBakeCancellation=null;
        this.Loaded-=this.OnLoaded;
        this.DetachSession();
        this.DetachHost();
    }

    private void OnStatusChanged(string Status){
        this.StatusText.Text=Status;
        if(this.Host is { HasFatalRenderFailure:false,IsBuildPending:false }
            &&this.Session?.PinnedSceneKey is { }DisplayedKey){
            System.Windows.Automation.AutomationProperties.SetHelpText(
                this.SceneSelector,DisplayedKey.ClassFullyQualifiedName);
        }
        if(this.Host is { }Host
            &&SceneRenderFailurePolicy.ShouldShowFailure(Host.HasFatalRenderFailure,Host.IsBuildPending)){
            // A failed build/Present leaves an invisible child HWND behind. Remove it so the WPF
            // empty-state message is painted instead of exposing a white HwndHost airspace.
            this.ShowEmpty(Status);
        }
    }

    private void OnViewModeSelectionChanged(object Sender,SelectionChangedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.ViewModeSelector.SelectedValue is string Value&&Enum.TryParse(Value,out SceneViewRenderMode Mode)){
            this.Host?.SetRenderMode(Mode);
        }
    }
    private void OnPhysicsPanelToggleClicked(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.SetPhysicsPanelVisible(this.PhysicsPanelBorder.Visibility!=Visibility.Visible);
    }

    private void SetPhysicsPanelVisible(bool Visible,bool Persist=true){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.PhysicsPanelToggle.Visibility=Visible?Visibility.Collapsed:Visibility.Visible;
        this.PhysicsPanelBorder.Visibility=Visible?Visibility.Visible:Visibility.Collapsed;
        if(Persist){
            SceneEditorSettings.WriteShowPhysicsPanel(Visible);
        }
    }

    private void OnPhysicsDebugChanged(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Host?.SetPhysicsDebugEnabled(this.PhysicsDebugToggle.IsChecked==true);
    }
    private void OnPhysicsDebugOptionsChanged(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Host is { }Host){ this.ApplyPhysicsDebugOptions(Host); }
    }
    private void ApplyPhysicsDebugOptions(SceneHwndHost Host){
        ThreadHelper.ThrowIfNotOnUIThread();
        Host.SetPhysicsDebugOptions(new PhysicsDebugOverlayOptions(
            Bounds:this.PhysicsBoundsToggle.IsChecked==true,
            Velocity:this.PhysicsVelocityToggle.IsChecked==true,
            CenterOfMass:this.PhysicsCenterOfMassToggle.IsChecked==true,
            Sleep:this.PhysicsSleepToggle.IsChecked==true,
            Contacts:this.PhysicsContactsToggle.IsChecked==true,
            Tree:this.PhysicsTreeToggle.IsChecked==true,
            Bonds:this.PhysicsBondsToggle.IsChecked==true));
    }
    private void OnPhysicsSimulate(object Sender,RoutedEventArgs Args){ ThreadHelper.ThrowIfNotOnUIThread();this.Host?.RequestPhysicsControl("simulate"); }
    private void OnPhysicsPause(object Sender,RoutedEventArgs Args){ ThreadHelper.ThrowIfNotOnUIThread();this.Host?.RequestPhysicsControl("pause"); }
    private void OnPhysicsStep(object Sender,RoutedEventArgs Args){ ThreadHelper.ThrowIfNotOnUIThread();this.Host?.RequestPhysicsControl("step"); }
    private void OnPhysicsReset(object Sender,RoutedEventArgs Args){ ThreadHelper.ThrowIfNotOnUIThread();this.Host?.RequestPhysicsControl("reset"); }
    private void OnPhysicsBakeSelected(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.StartPhysicsBake(BakeAll:false);
    }
    private void OnRuntimeDebugRefresh(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Host is not { }Host){
            this.ClearRuntimeDebugPresentation();
            return;
        }
        try {
            RuntimeDebugSnapshotPresentation Presentation=
                RuntimeDebugSnapshotPresentation.Create(
                    Host.CaptureRuntimeDebugSnapshot());
            this.RuntimeDebugSummaryText.Text=Presentation.Summary;
            this.RuntimeDebugRows.ItemsSource=Presentation.Rows;
        } catch(Exception Error){
            this.RuntimeDebugRows.ItemsSource=null;
            this.RuntimeDebugSummaryText.Text=
                VsixText.Format("RuntimeDebugFailed",Error.Message);
        }
    }
    private void ClearRuntimeDebugPresentation(){
        this.RuntimeDebugRows.ItemsSource=null;
        this.RuntimeDebugSummaryText.Text=VsixText.Get("RuntimeDebugNoSnapshot");
    }
    private void OnPhysicsBakeAll(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.StartPhysicsBake(BakeAll:true);
    }
    private void OnPhysicsBakeCancel(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.PhysicsBakeCancellation is not { }Cancellation
            ||this.PhysicsBakeOperation is not { CanCancel:true }Operation){ return; }
        this.PhysicsBakeOperation=Operation.RequestCancellation(
            VsixText.Get("PhysicsBakeCancelling"));
        this.ApplyPhysicsBakePresentation(this.PhysicsBakeOperation);
        Cancellation.Cancel();
    }
    private void StartPhysicsBake(bool BakeAll){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.PhysicsBakeOperation is { IsTerminal:false }
            ||this.SceneSelector.ItemsSource is not IEnumerable<ProjectSceneCatalogEntry>Source){ return; }
        ProjectSceneCatalogEntry[]Entries=BakeAll
            ?Source.ToArray()
            :this.SceneSelector.SelectedItem is ProjectSceneCatalogEntry Selected?[Selected]:[];
        if(Entries.Length==0){ this.StatusText.Text=VsixText.Get("PhysicsBakeNoScene");return; }
        // The exact Bake Host is built from the engine sources (ADR-0065): SourceMode projects only.
        string?RepoRoot=CgeProjectContext.Resolve(Entries[0].FilePath)?.EngineRoot;
        if(RepoRoot is null){ this.StatusText.Text=VsixText.Get("PhysicsBake_RequiresEngineSource");return; }
        this.PhysicsBakeCancellation?.Dispose();
        CancellationTokenSource Cancellation=new();
        this.PhysicsBakeCancellation=Cancellation;
        Guid RequestId=Guid.NewGuid();
        this.PhysicsBakeOperation=AuthoringOperationSnapshot.Start(
            RequestId,AuthoringHostOperation.Bake,"Preparing",
            VsixText.Format("PhysicsBakeStarting",Entries.Length));
        this.PhysicsBakeArtifactList.ItemsSource=null;
        this.PhysicsBakeArtifactList.Visibility=Visibility.Collapsed;
        this.PhysicsBakeOpenArtifactButton.Visibility=Visibility.Collapsed;
        this.PhysicsBakeOpenArtifactButton.IsEnabled=false;
        this.UpdatePhysicsBakeButtons();
        this.ApplyPhysicsBakePresentation(this.PhysicsBakeOperation);
        _=this.RunPhysicsBakeAsync(RepoRoot,Entries,RequestId,Cancellation);
    }

    private async Task RunPhysicsBakeAsync(
        string RepoRoot,ProjectSceneCatalogEntry[]Entries,Guid RequestId,
        CancellationTokenSource Cancellation){
        PhysicsSceneBakeBatchResult Result;
        try {
            if(this.Session is not { }Session){
                Result=CreatePhysicsBakeFailure(
                    RequestId,"BAKE_SESSION_ENDED",VsixText.Get("SessionEnded"));
            } else {
                SceneEditCommitResult Prepared=await Session
                    .PrepareForBakeAsync(Entries,Cancellation.Token)
                    .ConfigureAwait(false);
                Result=Prepared.Succeeded
                    ?await Task.Run(
                        ()=>PhysicsSceneBakeService.Bake(
                            RepoRoot,Entries,RequestId,
                            Progress=>this.OnPhysicsBakeProgress(Cancellation,Progress),
                            Cancellation.Token),
                        Cancellation.Token).ConfigureAwait(false)
                    :CreatePhysicsBakeFailure(
                        RequestId,"BAKE_SOURCE_SAVE_FAILED",
                        Prepared.ErrorMessage??VsixText.Get("SessionSaveFailed"));
            }
        } catch(OperationCanceledException){
            Result=CreatePhysicsBakeCancellation(RequestId);
        } catch(Exception Exception){
            ActivityLog.LogError(nameof(SceneDesignerToolWindowControl),Exception.ToString());
            Result=CreatePhysicsBakeFailure(RequestId,"BAKE_UI_EXCEPTION",Exception.Message);
        }
        this.DispatchToUi(()=>{
            ThreadHelper.ThrowIfNotOnUIThread();
            if(!ReferenceEquals(this.PhysicsBakeCancellation,Cancellation)){ return; }
            this.PhysicsBakeCancellation=null;
            Cancellation.Dispose();
            this.PhysicsBakeOperation=Result.Operation;
            this.UpdatePhysicsBakeButtons();
            this.ApplyPhysicsBakePresentation(Result.Operation);
            string[]Artifacts=[..Result.OutputPaths];
            this.PhysicsBakeArtifactList.ItemsSource=Artifacts;
            this.PhysicsBakeArtifactList.Visibility=Artifacts.Length==0
                ?Visibility.Collapsed
                :Visibility.Visible;
            this.PhysicsBakeOpenArtifactButton.Visibility=Artifacts.Length==0
                ?Visibility.Collapsed
                :Visibility.Visible;
            if(Artifacts.Length>0){ this.PhysicsBakeArtifactList.SelectedIndex=0; }
            this.StatusText.Text=Result.Operation.State switch {
                AuthoringOperationUiState.Succeeded=>
                    VsixText.Format("PhysicsBakeSucceeded",Result.CompletedCount),
                AuthoringOperationUiState.Cancelled=>VsixText.Get("PhysicsBakeCancelled"),
                _=>VsixText.Format("PhysicsBakeFailed",Result.Message),
            };
            if(Package.GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
                StatusBar.SetText(this.StatusText.Text);
            }
        });
    }

    private void UpdatePhysicsBakeButtons(){
        ThreadHelper.ThrowIfNotOnUIThread();
        bool HasEntries=this.SceneSelector.ItemsSource is IEnumerable<ProjectSceneCatalogEntry>Entries&&Entries.Any();
        bool Running=this.PhysicsBakeOperation is { IsTerminal:false };
        this.PhysicsBakeAllButton.IsEnabled=!Running&&HasEntries;
        this.PhysicsBakeSelectedButton.IsEnabled=!Running
            &&this.SceneSelector.SelectedItem is ProjectSceneCatalogEntry;
        this.PhysicsBakeCancelButton.Visibility=Running?Visibility.Visible:Visibility.Collapsed;
        this.PhysicsBakeCancelButton.IsEnabled=this.PhysicsBakeOperation?.CanCancel==true;
    }

    private void OnPhysicsBakeProgress(
        CancellationTokenSource Cancellation,AuthoringHostProgress Progress){
        this.DispatchToUi(()=>{
            ThreadHelper.ThrowIfNotOnUIThread();
            if(!ReferenceEquals(this.PhysicsBakeCancellation,Cancellation)
                ||this.PhysicsBakeOperation is not {
                    State:AuthoringOperationUiState.Running,
                }Operation){ return; }
            this.PhysicsBakeOperation=Operation.ApplyProgress(Progress,Progress.Phase);
            this.ApplyPhysicsBakePresentation(this.PhysicsBakeOperation);
        });
    }

    private void OnPhysicsBakeArtifactSelectionChanged(
        object Sender,SelectionChangedEventArgs Args){
        this.PhysicsBakeOpenArtifactButton.IsEnabled=
            this.PhysicsBakeArtifactList.SelectedItem is string;
    }

    private void OnPhysicsBakeOpenArtifact(object Sender,RoutedEventArgs Args){
        if(this.PhysicsBakeArtifactList.SelectedItem is not string ArtifactPath
            ||string.IsNullOrWhiteSpace(ArtifactPath)){ return; }
        string FullPath=System.IO.Path.GetFullPath(ArtifactPath);
        if(!System.IO.File.Exists(FullPath)){
            this.StatusText.Text=VsixText.Format("PhysicsBakeArtifactMissing",FullPath);
            return;
        }
        try {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName="explorer.exe",Arguments="/select,"+(char)34+FullPath+(char)34,
                UseShellExecute=true,
            })?.Dispose();
        } catch(Exception Exception)when(Exception is InvalidOperationException
            or System.ComponentModel.Win32Exception){
            this.StatusText.Text=VsixText.Format(
                "PhysicsBakeArtifactOpenFailed",Exception.Message);
        }
    }

    private void ApplyPhysicsBakePresentation(AuthoringOperationSnapshot Operation){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.PhysicsBakeProgressBar.Visibility=Visibility.Visible;
        this.PhysicsBakeProgressBar.Value=Operation.Fraction;
        this.PhysicsBakePhaseText.Visibility=Visibility.Visible;
        this.PhysicsBakePhaseText.Text=VsixText.Format(
            "AuthoringOperationProgress",Operation.Phase,
            Operation.Fraction.ToString("P0",CultureInfo.CurrentCulture));
        string Diagnostics=string.Join(Environment.NewLine,Operation.Diagnostics.Select(
            static Diagnostic=>$"{Diagnostic.Code}: {Diagnostic.Message}"));
        this.PhysicsBakeDiagnosticText.Text=Diagnostics;
        this.PhysicsBakeDiagnosticText.Visibility=string.IsNullOrEmpty(Diagnostics)
            ?Visibility.Collapsed
            :Visibility.Visible;
        this.StatusText.Text=Operation.Summary;
    }

    private static PhysicsSceneBakeBatchResult CreatePhysicsBakeFailure(
        Guid RequestId,string Code,string Message){
        AuthoringOperationSnapshot Operation=AuthoringOperationSnapshot.Start(
            RequestId,AuthoringHostOperation.Bake,"Preparing",Message).Fail(Code,Message);
        return new PhysicsSceneBakeBatchResult(false,0,Message,[],Operation);
    }

    private static PhysicsSceneBakeBatchResult CreatePhysicsBakeCancellation(Guid RequestId){
        string Message=VsixText.Get("PhysicsBakeCancelled");
        AuthoringOperationSnapshot Started=AuthoringOperationSnapshot.Start(
            RequestId,AuthoringHostOperation.Bake,"Cancelling",Message);
        AuthoringOperationSnapshot Operation=Started.Complete(new AuthoringHostResult {
            RequestId=RequestId,State=AuthoringHostTerminalState.Cancelled,ExitCode=1,
            Output=Message,
            Diagnostics=[new AuthoringHostDiagnostic {
                Severity=AuthoringHostDiagnosticSeverity.Error,
                Code="HOST_CANCELLED",Message=Message,
            }],
        });
        return new PhysicsSceneBakeBatchResult(false,0,Message,[],Operation);
    }
    private void OnGlobalSpatialOctreeChanged(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        bool Enabled=this.GlobalSpatialOctreeToggle.IsChecked==true;
        this.Host?.SetGlobalSpatialOctreeEnabled(Enabled);
        this.SpatialDebugProcessSelector.Visibility=Enabled&&this.SpatialDebugProcessSelector.Items.Count>1?Visibility.Visible:Visibility.Collapsed;
    }
    private void OnSpatialDebugStatusChanged(string Status){
        this.SpatialDebugStatusText.Text=Status;
    }

    private void OnSpatialDebugProcessCandidatesChanged(IReadOnlyList<int>CandidateIds,int?SelectedProcessId){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.UpdatingSpatialDebugProcessSelector=true;
        try {
            this.SpatialDebugProcessSelector.ItemsSource=CandidateIds;
            this.SpatialDebugProcessSelector.SelectedItem=SelectedProcessId;
            this.SpatialDebugProcessSelector.Visibility=this.GlobalSpatialOctreeToggle.IsChecked==true
                &&SpatialDebugProcessSelectionPolicy.ShouldShowPicker(CandidateIds)?Visibility.Visible:Visibility.Collapsed;
        } finally {
            this.UpdatingSpatialDebugProcessSelector=false;
        }
    }
    private void OnSpatialDebugProcessSelectionChanged(object Sender,SelectionChangedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.UpdatingSpatialDebugProcessSelector){
            return;
        }
        int?ProcessId=this.SpatialDebugProcessSelector.SelectedItem is int SelectedProcessId?SelectedProcessId:null;
        this.Host?.SetSpatialDebugProcessId(ProcessId);
    }
    private void OnCameraPreviewToggleClicked(object Sender,RoutedEventArgs Args){
        this.Host?.SetCameraPreviewEnabled(this.CameraPreviewToggle.IsChecked==true);
    }

    private void OnCameraPreviewSelectionRequested(NodePath Path){
        if(this.Session is not { }Session||Session.PinnedSnapshot is not { }Snapshot
            ||Snapshot.Projection?.TryGetId(Path,out EditorNodeId Id)!=true){
            return;
        }

        SceneSelection?Existing=Session.Selection.CurrentSelection is { }Selection
            &&Selection.Key.Equals(Snapshot.Key)?Selection:null;
        IReadOnlyList<EditorNodeId>NodeIds=Existing?.NodeIds??[Id];
        if(!NodeIds.Contains(Id)){
            NodeIds=[..NodeIds,Id];
        }

        Session.Selection.Select(
            Snapshot.Key,NodeIds,Id,ComponentType:null,MemberName:null,
            SceneSelectionOrigin.Viewport,Guid.NewGuid());
    }

    /// <summary>
    /// <see cref="SceneHwndHost.ViewportActivated"/>の購読先。ビューポート内をクリックした
    /// 瞬間だけキーボードフォーカスを与える(ADR-0041 決定事項2) — <see cref="OnLoaded"/>や
    /// <see cref="ShowSnapshot"/>/<see cref="SelectSceneAsync"/>では絶対に呼ばない。VS標準の
    /// ツールウィンドウフォーカスモデルを踏襲し、開いただけ・シーンを切り替えただけでは
    /// コードエディタからフォーカスを奪わない。
    /// </summary>
    private void OnViewportActivated(){
        if(!this.RenderHostGrid.IsKeyboardFocusWithin){
            Keyboard.Focus(this.RenderHostGrid);
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
        this.StatusText.Text=HitId is { } DisplayId&&Snapshot.Projection?.TryGetNode(DisplayId,out SceneProjectionNode DisplayNode)==true
                                 ?VsixText.Format("SceneDesigner_Selection",DisplayNode.Node.DisplayName,DisplayNode.Path.ToDisplayString())
                                 :VsixText.Get("SceneDesigner_SelectionCleared");

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
        this.StatusText.Text=Update.NodeIds.Count==0
                                 ?VsixText.Get("SceneDesigner_SelectionCleared")
                                 :VsixText.Format("SceneDesigner_MarqueeSelection",Update.NodeIds.Count);
        Session.Selection.Select(
            Snapshot.Key,Update.NodeIds,Update.PrimaryNodeId,
            ComponentType:null,MemberName:null,SceneSelectionOrigin.Viewport,Guid.NewGuid());
    }

    /// <summary>Commits the final preview to the live C# buffer before closing the transaction.</summary>
    #pragma warning disable VSTHRD100 // SceneHwndHost event handler; exceptions are observed below.
    private async void OnEditCommitRequested(SceneEditCommitRequest Request){
    #pragma warning restore VSTHRD100
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        SceneEditorSessionService?Session=this.Session;
        SceneHwndHost?Host=this.Host;
        if(Session is null||Host is null){
            Host?.CompleteEditCommit(false,VsixText.Get("SessionEnded"));
            return;
        }

        if(Request.IsDuplicate){
            this.PendingDuplicateSelectionRestore=new PendingDuplicateSelection(
                Request.SceneKey,Request.DuplicateResultPaths.ToArray());
        }

        SceneEditCommitResult Commit;
        try {
            Commit=await Session.CommitAsync(Request,CancellationToken.None);
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(SceneDesignerToolWindowControl),Ex.ToString());
            Commit=new SceneEditCommitResult(
                false,VsixText.Get("SceneDesigner_EditApplyFailed"),WasRebased:false);
        }
        if(this.Disposed||!ReferenceEquals(this.Session,Session)||!ReferenceEquals(this.Host,Host)){
            return;
        }
        if(!Commit.Succeeded){
            this.PendingDuplicateSelectionRestore=null;
        }
        Host.CompleteEditCommit(Commit.Succeeded,Commit.ErrorMessage);
        if(!Commit.Succeeded&&Package.GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
            StatusBar.SetText(Commit.ErrorMessage??VsixText.Get("SceneDesigner_EditApplyFailed"));
        }
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

    private void HandleEscape(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.TryCloseSceneComboBox()){
            return;
        }
        if(this.Host?.CancelCurrentOperation()==true){
            return;
        }
        if(this.Session?.Selection.TryClearCurrent(SceneSelectionOrigin.Viewport,Guid.NewGuid())==true){
            this.StatusText.Text=VsixText.Get("SceneDesigner_SelectionCleared");
        }
    }
    private bool TryCloseSceneComboBox(){
        foreach(ComboBox Box in new[]{this.SceneSelector,this.ViewModeSelector,this.SpatialDebugProcessSelector}){
            if(Box.IsDropDownOpen){
                Box.IsDropDownOpen=false;
                return true;
            }
        }
        return false;
    }
    private void OnViewportKeyDown(object Sender,KeyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Args.Key==Key.N){
            this.SetPhysicsPanelVisible(this.PhysicsPanelBorder.Visibility!=Visibility.Visible);
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
