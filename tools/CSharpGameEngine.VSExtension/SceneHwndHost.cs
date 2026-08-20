using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// In-process D3D12 embedding (same pattern as <see cref="AssetPreviewHwndHost"/>, ADR-0013) for the
/// Scene Designer tool window (ADR-0040 Phase 9). Targets a specific <c>IScene</c>-implementing class
/// via <see cref="SetTarget"/>, which receives the <see cref="SceneDocumentSnapshot"/> that
/// <see cref="SceneEditorSessionService"/> publishes from live Roslyn (no build step), converts it via
/// <see cref="SceneModelRenderObjectBuilder"/> and renders every node via
/// <see cref="SceneRenderer"/>. Replaces the earlier <c>.scene</c>-JSON-based version of this class
/// (ADR-0020/ADR-0021), whose remaining write path was deleted in M0020 Phase 0.
///
/// <para>
/// This host owns the editing view state that must survive renderer re-creation: the
/// <see cref="SceneRenderer"/> is disposed and rebuilt for mesh/structural changes, so the camera lives here
/// and is pushed in through <see cref="SceneRenderer.SetCamera"/> (ADR-0041 decision 1). It is also
/// the only place that learns the real surface size, via <c>Surface.Resized</c>.
/// </para>
/// </summary>
internal sealed class SceneHwndHost:HwndHost {
    private string?RepoRoot;
    private string?TargetFilePath;
    private string?TargetClassName;
    private SceneModel?TargetModel;
    private SceneKey?TargetKey;
    private SceneDocumentSnapshot?TargetSnapshot;
    private long RenderedAnalysisGeneration=-1;
    private NodePath?SelectedNodePath;
    private RenderSurfaceWindow?Surface;
    private SceneRenderer?Renderer;
    private LatestOnlyThreadWorker<SceneRenderBuildRequest,SceneRenderBuildResult>?BuildWorker;
    private EventHandler?RenderingHandler;
    private Action<int,int>?ResizeHandler;

    // 編集ビュー状態。Renderer は再解析ごとに破棄・再生成されるのでここに置く(ADR-0041 決定事項1)。
    private int SurfaceWidth=800;
    private int SurfaceHeight=600;
    private Vector3 SceneBoundsCenter;
    private float SceneBoundsRadius=1f;

    // M0020 Phase 2: ピッキング用。RenderObjectsはOnBuildCompletedのローカル変数だったが、
    // クリックのたびに参照する必要があるためフィールドへ格納する(事前調査事実2)。
    private IReadOnlyList<SceneRenderObject>CurrentRenderObjects=[];
    private IReadOnlyList<NodePath>SelectedNodePaths=[];
    private SceneSelection?CurrentSelection;

    // M0020 Phase 1: ナビゲーション状態機械とカメラ/永続ツール状態(ADR-0041 決定事項1)。
    private SceneViewState?ViewState;
    private SceneInputStateMachine?InputMachine;
    private bool PendingCameraReset=true;
    private float PendingLookDeltaX;
    private float PendingLookDeltaY;
    private TimeSpan?LastFlyTickTime;

    // M0020追加: ピクセル完全一致ドラッグ(反転設定OFFの間だけ有効)。アンカーはRMB/Alt+LMBの
    // Down時に捕捉し、Up/CaptureLostまで保持する。SceneInputStateMachineはVector3/世界座標を
    // 知らない純粋な状態機械のままにするため、この管理はSceneHwndHostに閉じる。
    private bool LookUsesPixelLock;
    private Vector3 LookAnchorWorldDirection;
    private bool OrbitUsesPixelLock;
    private Vector3 OrbitAnchorWorldPoint;

    // M0020 Phase 3: Moveギズモと1操作=1編集トランザクション。
    private readonly EditTransactionController EditTransaction=new();
    private MoveGizmoFrame?CurrentMoveGizmo;
    private GizmoHandleId?HoveredGizmoHandle;
    private GizmoHandleId?ActiveGizmoHandle;
    private MoveGizmoDrag?ActiveMoveDrag;
    private RotateGizmoDrag?ActiveRotateDrag;
    private ScaleGizmoDrag?ActiveScaleDrag;
    private PropertyGizmoFrame?CurrentPropertyGizmo;
    private PropertyGizmoDrag?ActivePropertyDrag;
    private bool PropertyCommitInProgress;
    private SceneActiveTool?ActiveTransformTool;
    private ICollection<GizmoHandleId>DisabledTransformHandles=Array.Empty<GizmoHandleId>();
    private IReadOnlyList<SceneTransformPreview>PreviewDescendantOriginals=[];
    private IReadOnlyList<NodePath>ActiveDraggedRootPaths=[];
    private IReadOnlyList<DuplicateNodePlanItem>ActiveDuplicatePlan=[];
    private IReadOnlyList<DuplicateRenderPreview>DuplicateRenderPreviews=[];
    private MoveGizmoFrame?ActiveGizmoStartFrame;
    private Quaternion ActivePrimaryRotation=Quaternion.Identity;
    private SceneTransformMemberMask ActiveCommitMembers;
    private SceneViewFollowSnapshot ActiveFollowViewSnapshot;
    private bool HasActiveFollowViewSnapshot;
    private bool FollowViewApplied;

    // M0037: ドラッグ中HUD(開始点マーカー・破線・角度の扇・数値読み取り)。RotateはAtan2由来の
    // (-π,π]角度が180°を跨ぐと符号反転するため、生の角度(Previous)と折り返し無しの累積値を別々に持つ。
    private GizmoDragHudState?ActiveDragHud;
    private float ActiveRotateAccumulatedRadians;
    private float ActiveRotatePreviousRadians;

    // M0036: シーンビューのCtrl+Z/Yで、実際のC#ソースUndo/Redo(唯一の編集履歴、ADR-0040)を待たず
    // 3Dビューを即座にパッチするための軽量キャッシュ。どこにも書き込みを行わない表示の先読みで、
    // 外れても実害は無い(Roslyn再解析が終われば必ず正しい状態へ収束する)。対象はMove/Rotate/Scale/
    // SurfaceSnapのTransformコミットのみ(CompleteEditCommitの!WasProperty&&!WasDuplicateと同じ絞り)。
    private IReadOnlyList<SceneTransformPreview>ActiveDragOriginalRoots=[];
    private Stack<TransformUndoRecord>RecentTransformCommits=new();
    private Stack<TransformUndoRecord>RecentTransformUndone=new();
    private const int MaxRecentTransformCommits=20;

    // M0034: Shift+Ctrl押下中(マウス静止中でも1フレーム以内に四角を出すため、Renderingティックで
    // GetKeyStateポーリングする。既存のFly Fast/Precisionポーリングと同じ方式)。
    private bool SurfaceSnapArmed;
    private bool ActiveSurfaceSnapDrag;
    private(Vector3 Position,Vector3 Normal)?CurrentSurfaceSnapHit;

    // M0020後続Task C: 空間から開始したLMBドラッグだけが保持するスクリーン矩形。
    private ScreenRectangle?ActiveMarqueeRectangle;

    private float AspectRatio=> this.SurfaceWidth/(float)this.SurfaceHeight;

    internal event Action<string>?StatusChanged;

    /// <summary>Pointerの<c>Down</c>で発火。WPF側は購読して<c>Keyboard.Focus</c>をここでだけ呼ぶ(ADR-0041 決定事項2)。</summary>
    internal event Action?ViewportActivated;

    /// <summary>
    /// LMBクリックでの選択確定(M0020 Phase 2)。ヒットNodePath(空間クリックならnull)とCtrl押下を運ぶ。
    /// <see cref="SceneEditorSessionService.Selection"/>への変換・発行はWPF側(<see cref="SceneHwndHost"/>は
    /// セッションを知らない)。
    /// </summary>
    internal event Action<NodePath?,bool>?ObjectPicked;

    /// <summary>
    /// 空間ドラッグで確定した複数NodePathとDown時のCtrl状態。セッション選択への変換は
    /// 単体クリックと同じくWPF境界が担う。
    /// </summary>
    internal event Action<IReadOnlyList<NodePath>,bool>?ObjectsMarqueePicked;

    /// <summary>MouseUpで確定したsource編集要求。WPF/session境界がlive bufferへ1 Undoで反映する。</summary>
    internal event Action<SceneEditCommitRequest>?EditCommitRequested;

    public SceneHwndHost(){
        // Surfaceは描画先でありキーボード入力を持つコントロールではない。HwndHostの既定
        // TabInto経路を残すと、float/dockやマウスクリック後にVSのTab等がSurfaceへ入り、
        // WPFとWin32のフォーカスがずれてコードエディタの制御キーが効かなくなる。
        this.Focusable=false;
        KeyboardNavigation.SetIsTabStop(this,RenderSurfaceFocusPolicy.AllowTabInto);
    }

    protected override bool TabIntoCore(TraversalRequest Request)
        =>RenderSurfaceFocusPolicy.AllowTabInto;

    protected override HandleRef BuildWindowCore(HandleRef HwndParent){
        ThreadHelper.ThrowIfNotOnUIThread();

        // Rendererが最初のPresentを行えるまで子HWNDを隠す。NOREDIRECTIONBITMAPの空Surfaceを
        // 表示すると、ドッキング前に同じ領域にいたSolution Explorerが残像のように露出する。
        this.Surface=new RenderSurfaceWindow(HwndParent.Handle,800,600,InitiallyVisible:false);
        this.ViewState=SceneViewState.CreateFromSettings();
        this.InputMachine=new SceneInputStateMachine();
        SceneEditorSettings.SettingsChanged+=this.OnSettingsChanged;
        this.Surface.PointerInput+= this.OnPointerInput;
        this.Surface.WheelInput+= this.HandleWheel;
        SynchronizationContext UiContext=SynchronizationContext.Current
            ??new DispatcherSynchronizationContext(this.Dispatcher);
#pragma warning disable VSTHRD001 // 生workerの完了を非同期Postするだけで、同期wait/message pumpは行わない。
        this.BuildWorker=new LatestOnlyThreadWorker<SceneRenderBuildRequest,SceneRenderBuildResult>(
                                                                                                 Work: this.BuildRenderObjects,
                                                                                                 Dispatch:Callback=>UiContext.Post(_=>Callback(),null),
                                                                                                 Completed:this.OnBuildCompleted);
#pragma warning restore VSTHRD001
        this.LoadIntoSurface();
        return new HandleRef(this, this.Surface.Handle);
    }

    protected override void DestroyWindowCore(HandleRef Hwnd){
        ThreadHelper.ThrowIfNotOnUIThread();

        this.BuildWorker?.Dispose();
        this.BuildWorker=null;
        this.CancelEditTransaction();
        SceneEditorSettings.SettingsChanged-=this.OnSettingsChanged;
        this.DetachSubscriptions();
        this.DisposeRenderer();

        if(this.Surface is not null){
            this.Surface.PointerInput-= this.OnPointerInput;
            this.Surface.WheelInput-= this.HandleWheel;
        }

        // 保険の書き込み(通常はActiveTool等を変更した時点で即時書き込み済み)。
        // ここは7項目すべてを書き戻すため、Tools>Optionsでの変更をViewStateへ反映していないと
        // 古い値で上書き消滅させてしまう。OnSettingsChangedがその同期を担っている。
        this.InputMachine?.Reset();
        this.ViewState?.SaveToSettings();
        this.InputMachine=null;
        this.ViewState=null;

        this.Surface?.Dispose();
        this.Surface=null;
    }

    /// <summary>
    /// Session が pin した snapshot を描画対象にします。解析中の blocking error は Last Known Good
    /// projection を保持するので、ここでrendererを空へ戻しません。
    /// </summary>
    internal void SetTarget(SceneDocumentSnapshot Snapshot){
        if(this.EditTransaction.State!=EditTransactionState.Idle){
            if(this.TargetKey is { }EditingKey&&EditingKey.Equals(Snapshot.Key)){
                this.EditTransaction.QueueSnapshot(Snapshot);
                return;
            }

            this.CancelEditTransaction();
        }

        // コンボボックスでのシーン切替はSceneHwndHostを再生成せず(EnsureHostが既存インスタンスを
        // 返すだけ)、このSetTargetの呼び直しだけが届く。TargetKeyが変わったことをここで検知し、
        // 次のOnBuildCompletedで新シーンへ再フレーミングする(ADR-0041、事前調査事実3)。
        SceneDocumentSnapshot?PreviousSnapshot=this.TargetSnapshot;
        bool SameScene=this.TargetKey is { }PreviousKey&&PreviousKey.Equals(Snapshot.Key);
        if(!SameScene){
            this.PendingCameraReset=true;
            // M0036: 別Sceneへ切り替えたら、先読みUndoキャッシュ(NodePathが別ファイルのものになる)を破棄する。
            this.RecentTransformCommits.Clear();
            this.RecentTransformUndone.Clear();
        }

        this.TargetKey=Snapshot.Key;
        this.TargetSnapshot=Snapshot;
        SceneModel?Model=Snapshot.LastKnownGoodModel??Snapshot.CurrentModel;
        if(Model is not null){
            this.TargetFilePath=Model.FilePath;
            this.TargetClassName=Model.ClassName;
            this.TargetModel=Model;
        }

        if(this.Surface is not null
            &&SameScene
            &&PreviousSnapshot is not null
            && this.Renderer is not null
            && this.RenderedAnalysisGeneration==PreviousSnapshot.AnalysisGeneration
            && this.TryApplyIncrementalSnapshot(Snapshot,Model)){
            return;
        }

        if(this.Surface is not null){
            this.Rebuild();
        }
    }

    private bool TryApplyIncrementalSnapshot(SceneDocumentSnapshot Snapshot,SceneModel? Model){
        SceneRenderUpdateKind UpdateKind=SceneIncrementalUpdatePolicy.Classify(Snapshot.Diff);
        if(UpdateKind==SceneRenderUpdateKind.Rebuild){ return false; }
        if(UpdateKind==SceneRenderUpdateKind.None){
            this.RenderedAnalysisGeneration=Snapshot.AnalysisGeneration;
            this.UpdateMoveGizmo();
            return true;
        }
        if(Model is null|| this.Renderer is not { }Active
            ||!SceneRenderIncrementalUpdater.TryUpdateTransforms(
                Model, this.CurrentRenderObjects,out IReadOnlyList<SceneRenderObject>Updated)){
            return false;
        }

        foreach(SceneRenderObject Object in Updated){
            if(!Active.UpdateObjectTransform(Object.NodeId,Object.World)){ return false; }
        }

        this.CurrentRenderObjects=Updated;
        (this.SceneBoundsCenter,this.SceneBoundsRadius)=SceneCameraFraming.ComputeBounds(Updated);
        if(this.ViewState is { }State){
            State.LastKnownBoundsRadius=this.SceneBoundsRadius;
        }

        this.RenderedAnalysisGeneration=Snapshot.AnalysisGeneration;
        this.UpdateMoveGizmo();
        this.Surface?.SetVisible(true);
        this.SetStatus($"表示中: {Model.ClassName}");
        return true;
    }

    /// <summary>EditorNodeIdを現在snapshotのNodePathへ解決して既存Rendererへ渡します。</summary>
    internal void SetSelection(SceneDocumentSnapshot Snapshot,SceneSelection Selection){
        if(this.TargetKey is not { } Key||!Key.Equals(Snapshot.Key)){
            return;
        }

        this.CurrentSelection=Selection;
        this.SelectedNodePath=Selection.PrimaryNodeId is { } Id
            &&Snapshot.Projection?.TryGetNode(Id,out SceneProjectionNode Node)==true
            ?Node.Path
            :null;

        // 複数選択の全体。FocusSelection(F)の境界球(design doc §3「選択ノード群の境界球」)と、
        // ハイライト(primaryを濃く、副選択を薄く — SceneSelectionTint)の両方がこれを使う。
        // M0020 Phase 2までハイライトはprimary1件のみで、Ctrl+LMBで複数選択しても見た目が変わらず
        // 「複数選択ができない」と観測されていた(実機検証#5)。
        List<NodePath>ResolvedPaths=[];
        foreach(EditorNodeId NodeId in Selection.NodeIds){
            if(Snapshot.Projection?.TryGetNode(NodeId,out SceneProjectionNode ResolvedNode)==true){
                ResolvedPaths.Add(ResolvedNode.Path);
            }
        }

        this.SelectedNodePaths=ResolvedPaths;
        this.Renderer?.SetSelectedNode(this.SelectedNodePath,ResolvedPaths);
        this.UpdateMoveGizmo();
    }

    private void LoadIntoSurface(){
        if(this.Surface is null){
            return;
        }

        if(this.RenderingHandler is null){
            this.RenderingHandler=(_,Args)=>{
                                      this.TickFlyIfNeeded(Args);
                                      this.TickUndoRedoIfNeeded();
                                      this.UpdateSurfaceSnapArmed();
                                      this.ApplyCamera();
                                      this.Renderer?.RenderFrame();
                                  };
            CompositionTarget.Rendering+= this.RenderingHandler;

            // Subscribed once here (not inside Rebuild, which runs on every re-analysis) - it always
            // resizes whatever Renderer currently holds via the field, so re-subscribing per rebuild
            // would just accumulate redundant duplicate calls per resize.
            // これはサーフェスの実サイズを知る唯一の場所でもあるので、アスペクト比の元もここで更新する。
            this.ResizeHandler=(W,H)=>{
                                   this.SurfaceWidth=Math.Max(1,W);
                                   this.SurfaceHeight=Math.Max(1,H);
                                   this.Renderer?.Resize(W,H);
                                   this.ApplyCamera();
                                   this.RefreshOverlay();
                               };
            this.Surface.Resized+= this.ResizeHandler;
        }

        this.Rebuild();
    }

    /// <summary>
    /// 現在の<see cref="ViewState"/>を<see cref="Renderer"/>へ注入する(ADR-0041 決定事項1、
    /// M0020 Phase 1)。値が変わらなければ<see cref="SceneRenderer.SetCamera"/>は何もしないので
    /// 毎フレーム呼んで構わない。
    /// </summary>
    private void ApplyCamera(){
        if(this.Renderer is not { } Active|| this.ViewState is not { } State){
            return;
        }

        Active.SetCamera(State.GetView(),State.GetProjection(this.AspectRatio));
        this.UpdateMoveGizmo();
    }

    /// <summary>
    /// Fly(RMB+WASDQE)は押しっぱなしのキーでマウスの実移動が無いフレームも前進し続ける必要があるため、
    /// マウスのLook量は<see cref="OnPointerInput"/>が<see cref="PendingLookDeltaX"/>/
    /// <see cref="PendingLookDeltaY"/>へ蓄積するだけに留め、Renderingティックごとにここで1回だけ
    /// <see cref="SceneViewState.TickFly"/>へまとめて渡す。Orbit/PanはMoveイベントごとに即時適用で
    /// 十分(時間積分の要素が無いため、<see cref="OnPointerInput"/>参照)。
    /// </summary>
    /// <summary>M0045: <see cref="SceneDesignerToolWindowControl"/>が購読し、Ctrl+Z/Ctrl+Yを検知した時点で発火する。</summary>
    internal event Action?UndoRequested;
    /// <summary>M0045: 前項の逆。</summary>
    internal event Action?RedoRequested;
    /// <summary>M0047: ビューポートにフォーカスがある間のCtrl+Sで発火する(<see cref="TickUndoRedoIfNeeded"/>と同じGetKeyState経路)。</summary>
    internal event Action?SaveRequested;
    /// <summary>M0045: ビューポートのWPFキーボードフォーカス有無。実体は<c>RenderHostGrid.IsKeyboardFocusWithin</c>で、Controlが設定する。</summary>
    internal Func<bool>?IsViewportFocusedProvider{ get; set; }

    private bool PreviousUndoKeyDown;
    private bool PreviousRedoKeyDown;
    private bool PreviousSaveKeyDown;

    /// <summary>
    /// M0045: Ctrl+ZはVS標準キーバインド(Edit.Undo)のため、VSシェルのアクセラレータテーブルが
    /// コマンドとして先に消費してしまい、WPFの<c>KeyDown</c>には一切届かない(M0036で実機確認済み)。
    /// M0037では標準コマンドID(<c>VSStd97CmdID.Undo</c>等)をペインへ登録して対処したが、ペインの
    /// 表示・フォーカス状態に関わらずVS全体のUndo管理を壊す実機回帰を招いた(M0045)。
    ///
    /// <para>
    /// <see cref="TickFlyIfNeeded"/>のShift/Ctrl修飾検出と同じ<c>GetKeyState</c>ポーリングは
    /// メッセージのコマンド消費経路と無関係に物理押下状態を反映するため、標準コマンドテーブルに
    /// 一切触れずにCtrl+Z/Ctrl+Yを検知できる。エッジ追跡自体はフォーカスと独立させ、フォーカスは
    /// 発火の可否にのみ使う — ビューポート外で押しっぱなしのままフォーカスが戻ったときに、
    /// 継続押下を新規押下として誤検知しないため。
    /// </para>
    /// </summary>
    private void TickUndoRedoIfNeeded(){
        bool Ctrl=(NativeMethods.GetKeyState(NativeMethods.VK_CONTROL)&0x8000)!=0;
        bool ZDown=Ctrl&&(NativeMethods.GetKeyState(NativeMethods.VK_Z)&0x8000)!=0;
        bool YDown=Ctrl&&(NativeMethods.GetKeyState(NativeMethods.VK_Y)&0x8000)!=0;
        // M0047: Ctrl+SもEdit.SaveSelectedItems等の標準キーバインドに割り当て済みのため、Ctrl+Z/Yと
        // 同じ理由でWPFのKeyDownには届かない。同じGetKeyStateポーリングで検知する。
        bool SDown=Ctrl&&(NativeMethods.GetKeyState(NativeMethods.VK_S)&0x8000)!=0;
        // M0051 Step 1-5: Undo / Redo / Save の3つとも同じ門番を通す。Step 1-4ではSaveだけを
        // Hierarchy/Inspectorまで広げ、Undo/Redoはビューポート限定のまま残したため
        // 「ヒエラルキーは取り消しできない」という実機報告になった。範囲判定は
        // SceneDesignerInputRoutingPolicyへ集約する — Step 1-4はここで生の論理和を書いてしまい、
        // 純粋関数として切り出したSceneSaveTriggerPolicyが製品コードから呼ばれない配線漏れを作った。
        bool ViewportFocused= this.IsViewportFocusedProvider?.Invoke()==true;
        bool AuxiliaryFocused=SceneEditorHub.AnyAuxiliarySurfaceHasFocus;
        bool RouteSave=SceneDesignerInputRoutingPolicy.ShouldRouteSaveToDesigner(
            ViewportFocused,AuxiliaryFocused);
        // Ctrl+Z/YだけはTextBox入力中に降りる(インラインリネーム中の文字取り消しを奪わないため)。
        bool RouteUndoRedo=SceneDesignerInputRoutingPolicy.ShouldRouteUndoRedoToDesigner(
            ViewportFocused,AuxiliaryFocused,
            Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase);

        if(RouteUndoRedo&&SceneUndoRedoKeyPolicy.IsRisingEdge(ZDown, this.PreviousUndoKeyDown)){ Raise(this.UndoRequested); }
        if(RouteUndoRedo&&SceneUndoRedoKeyPolicy.IsRisingEdge(YDown, this.PreviousRedoKeyDown)){ Raise(this.RedoRequested); }
        if(RouteSave&&SceneUndoRedoKeyPolicy.IsRisingEdge(SDown, this.PreviousSaveKeyDown)){ Raise(this.SaveRequested); }

        this.PreviousUndoKeyDown=ZDown;
        this.PreviousRedoKeyDown=YDown;
        this.PreviousSaveKeyDown=SDown;
    }

    /// <summary>
    /// M0051 Step 1-5(実機修正): ここは<c>CompositionTarget.Rendering</c>のハンドラの中なので、
    /// 例外を逃がすとWPFのレンダリングループごと落ちる。実際に
    /// <c>ITextUndoHistory.LastUndoTransaction</c>が<see cref="NotSupportedException"/>を投げて
    /// Ctrl+Zのたびにその形で落ちた。VS SDKの想定外はログに残して<b>そのキーが何もしなかった</b>
    /// 扱いに留め、描画とナビゲーションは生かし続ける。
    /// </summary>
    private static void Raise(Action?Handler){
        try {
            Handler?.Invoke();
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(SceneHwndHost),Ex.ToString());
        }
    }

    private void TickFlyIfNeeded(EventArgs Args){
        if(this.InputMachine is not { } Machine|| this.ViewState is not { } State||Machine.State!=SceneNavState.Flying){
            this.LastFlyTickTime=null; // Fly再突入時に古い時刻からの巨大なDeltaTimeが出ないようにする。
            return;
        }

        double DeltaSeconds=0;
        if(Args is RenderingEventArgs RenderArgs){
            if(this.LastFlyTickTime is { } Previous){
                DeltaSeconds=(RenderArgs.RenderingTime-Previous).TotalSeconds;
            }

            this.LastFlyTickTime=RenderArgs.RenderingTime;
        }

        bool Shift=(NativeMethods.GetKeyState(NativeMethods.VK_SHIFT)&0x8000)!=0;
        bool Ctrl=(NativeMethods.GetKeyState(NativeMethods.VK_CONTROL)&0x8000)!=0;

        // ピクセル完全一致中は回転をムーブイベントごとに即時確定済み(OnPointerInput参照)なので、
        // ここではWASDQEの移動だけを現在のYaw/Pitchで適用する。
        if(this.LookUsesPixelLock){
            State.TickFlyMovementOnly(Machine.FlyHeldKeys,(float)DeltaSeconds,Shift,Ctrl);
            return;
        }

        State.TickFly(Machine.FlyHeldKeys, this.PendingLookDeltaX, this.PendingLookDeltaY,(float)DeltaSeconds,Shift,Ctrl);
        this.PendingLookDeltaX=0f;
        this.PendingLookDeltaY=0f;
    }

    /// <summary>
    /// M0034: マウス静止中でも1フレーム以内にSurfaceSnap四角の武装状態を反映するため、
    /// <see cref="TickFlyIfNeeded"/>のFast/Precisionポーリングと同じ方式でRenderingティックごとに
    /// <c>GetKeyState</c>を読む。Idle以外(ドラッグ・Fly・Orbit等)やギズモが無い(未選択)時は武装しない。
    /// </summary>
    private void UpdateSurfaceSnapArmed(){
        this.SurfaceSnapArmed= this.InputMachine is { State:SceneNavState.Idle }
                            && this.CurrentMoveGizmo is not null
                            &&(NativeMethods.GetKeyState(NativeMethods.VK_SHIFT)&0x8000)!=0
                            &&(NativeMethods.GetKeyState(NativeMethods.VK_CONTROL)&0x8000)!=0;
    }

    /// <summary>
    /// <see cref="RenderSurfaceWindow.PointerInput"/>の購読先。キャプチャの実行
    /// (<see cref="RenderSurfaceWindow.SetPointerCapture"/>/
    /// <see cref="RenderSurfaceWindow.ReleasePointerCapture"/>)はここが担う —
    /// <see cref="SceneInputStateMachine"/>は純粋でWin32を知らないため、キャプチャすべきかどうかの
    /// 判断だけを<see cref="SceneCaptureAction"/>として返す。
    /// </summary>
    private void OnPointerInput(PointerEvent Event){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.InputMachine is not { } Machine){
            return;
        }

        if(Event.Kind==PointerEventKind.Down){
            this.ViewportActivated?.Invoke();
        }

        if(Event.Kind==PointerEventKind.Move&&Machine.State==SceneNavState.Idle){
            this.TryHitTransformGizmoAt(Event.X,Event.Y,IncludeProperty:!Event.Modifiers.Alt);
        }

        // ヒットテストはLMB Down・Alt無し・Idle中(Orbit開始やナビゲーション中の無駄な走査を避ける)の
        // 時だけ行う(M0020 Phase 2)。状態機械自身はScenePicker/SceneRenderObjectを知らないため、
        // 結果だけを渡す。
        GizmoHandleId?HitGizmoHandle=Event.Kind==PointerEventKind.Down&&Event.Button==PointerButton.Left
            &&Machine.State==SceneNavState.Idle
            ? this.TryHitTransformGizmoAt(Event.X,Event.Y,IncludeProperty:!Event.Modifiers.Alt)
            :null;
        NodePath?HitNodePath=HitGizmoHandle is null
            &&Event.Kind==PointerEventKind.Down&&Event.Button==PointerButton.Left
            &&!Event.Modifiers.Alt&&Machine.State==SceneNavState.Idle
            ? this.TryPickAt(Event.X,Event.Y)
            :null;

        SceneInputStepResult Result=Machine.Handle(Event,HitNodePath,HitGizmoHandle);

        if(Result.GizmoDragBegan&&Result.GizmoHandle is { }BeganHandle
            &&!this.BeginGizmoDrag(BeganHandle,Event.X,Event.Y,Result.GizmoDuplicate)){
            SceneInputStepResult Canceled=Machine.CancelCurrent();
            if(Canceled.Capture==SceneCaptureAction.End){
                this.Surface?.ReleasePointerCapture();
            }
            return;
        }

        // M0020追加: RMB/Alt+LMBでFlying/Orbitingへ入った瞬間だけアンカーを捕捉する
        // (反転設定がONならピクセル完全一致は使わず、以降は既存のdelta*sensitivity経路のまま)。
        if(Result.Capture==SceneCaptureAction.Begin){
            if(Result.State==SceneNavState.Flying){
                this.CaptureLookAnchor(Event.X,Event.Y); }
            else if(Result.State==SceneNavState.Orbiting){
                this.CaptureOrbitAnchor(Event.X,Event.Y); }
        }

        switch(Result.Capture){
            case SceneCaptureAction.Begin:
                this.Surface?.SetPointerCapture();
                break;
            case SceneCaptureAction.End:
                this.Surface?.ReleasePointerCapture();
                break;
        }

        if(Result.SelectionConfirmed){
            this.ObjectPicked?.Invoke(Result.ConfirmedNodePath,Result.ConfirmedCtrl);
        }

        if(Result.MarqueeUpdated){
            this.ActiveMarqueeRectangle=Result.MarqueeRectangle;
            this.RefreshOverlay();
        }
        if(Result.MarqueeCanceled){
            this.ActiveMarqueeRectangle=null;
            this.RefreshOverlay();
        }
        if(Result.MarqueeSelectionConfirmed){
            this.PublishMarqueeSelection(Result.MarqueeRectangle,Result.ConfirmedCtrl);
        }

        if(Result.GizmoCanceled){
            this.CancelEditTransaction();
            return;
        }
        if(Result.GizmoCommitRequested){
            this.CommitEditTransaction();
            return;
        }
        if(Result.State==SceneNavState.GizmoDragging&&Event.Kind==PointerEventKind.Move){
            if(this.ActivePropertyDrag is not null){
                this.UpdatePropertyGizmoDrag(Event.X,Event.Y,Event.Modifiers.Ctrl);
            } else {
                this.UpdateTransformGizmoDrag(Event.X,Event.Y,Event.Modifiers.Ctrl,Event.Modifiers.Shift);
            }
            return;
        }

        if(Result.DeltaX==0f&&Result.DeltaY==0f){
            return;
        }

        switch(Result.State){
            case SceneNavState.Flying:
                if(this.LookUsesPixelLock&& this.ViewState is { } LookState){
                    (_,Vector3 Direction)=SceneViewCamera.ScreenPointToRay(
                        LookState.GetView(),LookState.GetProjection(this.AspectRatio),
                        Event.X,Event.Y, this.SurfaceWidth, this.SurfaceHeight);
                    Vector3 LocalAxes=SceneViewCamera.DecomposeDirection(Direction,LookState.Yaw,LookState.Pitch);
                    LookState.ApplyLookPixelLock(this.LookAnchorWorldDirection,LocalAxes);
                } else {
                    this.PendingLookDeltaX+=Result.DeltaX;
                    this.PendingLookDeltaY+=Result.DeltaY;
                }
                break;
            case SceneNavState.Orbiting:
                if(this.OrbitUsesPixelLock&& this.ViewState is { } OrbitState){
                    OrbitState.ApplyOrbitPixelLock(this.OrbitAnchorWorldPoint,Event.X,Event.Y, this.SurfaceWidth, this.SurfaceHeight,this.AspectRatio);
                } else {
                    this.ViewState?.ApplyOrbit(Result.DeltaX,Result.DeltaY);
                }
                break;
            case SceneNavState.Panning:
                this.ViewState?.ApplyPan(Result.DeltaX,Result.DeltaY);
                break;
        }
    }

    /// <summary>
    /// RMB Down時のアンカー捕捉。Lookはカメラ位置(Eye)が回転中固定なので、方向さえ覚えておけば
    /// 十分(<see cref="SceneViewCamera.SolveLookOrientation"/>参照、ヒットテスト不要)。
    /// </summary>
    private void CaptureLookAnchor(int X,int Y){
        this.LookUsesPixelLock= this.ViewState is { InvertLookX:false,InvertLookY:false };
        if(!this.LookUsesPixelLock|| this.ViewState is not { } State){
            return;
        }

        (_, this.LookAnchorWorldDirection)=SceneViewCamera.ScreenPointToRay(
                                                                            State.GetView(),State.GetProjection(this.AspectRatio),X,Y, this.SurfaceWidth, this.SurfaceHeight);
    }

    /// <summary>
    /// Alt+LMB Down時のアンカー捕捉。Orbitはカメラ位置自体が動くため、世界座標の点(距離込み)が
    /// 必要(<see cref="SceneViewCamera.TrySolveOrbitYawPitch"/>参照)。何もヒットしなければ
    /// 現在の注視平面との交点を仮のアンカーにする。
    /// </summary>
    private void CaptureOrbitAnchor(int X,int Y){
        this.OrbitUsesPixelLock= this.ViewState is { InvertOrbitX:false,InvertOrbitY:false };
        if(!this.OrbitUsesPixelLock|| this.ViewState is not { } State){
            return;
        }

        (Vector3 Origin,Vector3 Direction)=SceneViewCamera.ScreenPointToRay(
            State.GetView(),State.GetProjection(this.AspectRatio),X,Y, this.SurfaceWidth, this.SurfaceHeight);
        this.OrbitAnchorWorldPoint=ScenePicker.TryPick(this.CurrentRenderObjects,Origin,Direction,out _,out float Distance)
                                       ?Origin+Direction*Distance
                                       :SceneViewCamera.ComputeOrbitAnchorFallbackPoint(
                                        Origin,Direction,State.FocusPosition,SceneViewCamera.GetForward(State.Yaw,State.Pitch));
    }

    private NodePath?TryPickAt(int X,int Y){
        if(this.ViewState is not { } State|| this.CurrentRenderObjects.Count==0){
            return null;
        }

        (Vector3 Origin,Vector3 Direction)=SceneViewCamera.ScreenPointToRay(
            State.GetView(),State.GetProjection(this.AspectRatio),X,Y, this.SurfaceWidth, this.SurfaceHeight);
        return ScenePicker.TryPick(this.CurrentRenderObjects,Origin,Direction,out NodePath Hit)?Hit:(NodePath?)null;
    }

    private GizmoHandleId?TryHitTransformGizmoAt(int X,int Y,bool IncludeProperty=true){
        if(this.ViewState is not { }State||(this.CurrentMoveGizmo is null&& this.CurrentPropertyGizmo is null)){
            return null;
        }

        (Vector3 Origin,Vector3 Direction)=SceneViewCamera.ScreenPointToRay(
            State.GetView(),State.GetProjection(this.AspectRatio),X,Y, this.SurfaceWidth, this.SurfaceHeight);
        Vector3 CameraForward=SceneViewCamera.GetForward(State.Yaw,State.Pitch);
        GizmoHandleId?Hit=IncludeProperty&& this.CurrentPropertyGizmo is { }PropertyFrame
            ?PropertyGizmo.TryHit(PropertyFrame,Origin,Direction)
            :null;
        if(Hit is null&& this.CurrentMoveGizmo is { }Frame){
            Vector3 CameraRight=SceneViewCamera.GetFlatRight(State.Yaw);
            Vector3 CameraUp=SceneViewCamera.GetUp(State.Yaw,State.Pitch);
            Hit=TransformGizmo.TryHitTransform(
                Frame,Origin,Direction,CameraForward,CameraRight,CameraUp, this.SurfaceSnapArmed, this.DisabledTransformHandles);
        }
        if(Hit!= this.HoveredGizmoHandle){
            this.HoveredGizmoHandle=Hit;
            this.RefreshOverlay();
        }
        return Hit;
    }

    private void UpdateMoveGizmo(){
        if(this.Renderer is null|| this.ViewState is not { }State
                                || this.SelectedNodePath is not { }PrimaryPath
                                || this.TargetSnapshot?.Projection is not { }Projection
                                ||!Projection.TryGetId(PrimaryPath,out EditorNodeId PrimaryId)
                                ||!Projection.TryGetNode(PrimaryId,out SceneProjectionNode PrimaryNode)){
            this.CurrentMoveGizmo=null;
            this.CurrentPropertyGizmo=null;
            this.HoveredGizmoHandle=null;
            this.DisabledTransformHandles=Array.Empty<GizmoHandleId>();
            this.RefreshOverlay();
            return;
        }
        if(this.EditTransaction.State!=EditTransactionState.Idle
            &&(this.CurrentMoveGizmo is not null|| this.CurrentPropertyGizmo is not null)){
            this.RefreshOverlay();
            return;
        }

        SceneRenderObject?PrimaryObject= this.CurrentRenderObjects.FirstOrDefault(Object=>Object.NodeId.Equals(PrimaryPath));
        Matrix4x4 PrimaryWorld=PrimaryObject?.World??Matrix4x4.Identity;
        if(!SceneTransformMath.TryDecompose(PrimaryWorld,out SceneTransformValue WorldTransform)){
            this.CurrentMoveGizmo=null;
            this.CurrentPropertyGizmo=null;
            this.RefreshOverlay();
            return;
        }

        float Scale=TransformGizmo.ComputeWorldScale(
            State.ProjectionMode,
            Vector3.Distance(WorldTransform.Position,State.GetEyePosition()),
            State.FovYRadians,
            State.OrthographicSize);
        this.CurrentPropertyGizmo=PropertyGizmoBinding.TryCreate(
                                                                 PrimaryNode.Node,PrimaryWorld,Scale,out PropertyGizmoFrame PropertyFrame)
                                      ?PropertyFrame
                                      :null;
        if(!SceneTransformBinding.TryRead(PrimaryNode.Node,out _)){
            this.CurrentMoveGizmo=null;
            this.DisabledTransformHandles=Array.Empty<GizmoHandleId>();
            this.RefreshOverlay();
            return;
        }

        this.CurrentMoveGizmo=MoveGizmoFrame.Create(
                                                    WorldTransform.Position,WorldTransform.Rotation,State.TransformSpace,Scale);
        List<SceneProjectionNode>Roots= this.GetSelectedMoveRoots(Projection);
        this.DisabledTransformHandles=AllTransformHandles
                                     .Where(Handle=>Roots.Count==0
                                                  ||Roots.Any(Node=>!SceneTransformBinding.CanEditTransform(Node.Node,GetHandleFamily(Handle),Handle)))
                                     .ToArray();
        this.RefreshOverlay();
    }

    /// <summary>Transformギズモと矩形選択をPhase Aの同じoverlay PSO入力へ合成する。</summary>
    private void RefreshOverlay(){
        if(this.Renderer is not { }Active){
            return;
        }

        List<GizmoOverlayPrimitive>Primitives=[];
        IReadOnlyList<GizmoOverlayPrimitive>TransformPrimitives=Array.Empty<GizmoOverlayPrimitive>();
        IReadOnlyList<GizmoOverlayPrimitive>PropertyPrimitives=Array.Empty<GizmoOverlayPrimitive>();
        if(this.CurrentMoveGizmo is { }Frame&& this.ViewState is { }GizmoState){
            Vector3 CameraForward=SceneViewCamera.GetForward(GizmoState.Yaw,GizmoState.Pitch);
            Vector3 CameraRight=SceneViewCamera.GetFlatRight(GizmoState.Yaw);
            Vector3 CameraUp=SceneViewCamera.GetUp(GizmoState.Yaw,GizmoState.Pitch);
            List<GizmoOverlayPrimitive>Combined=[
                ..TransformGizmo.BuildMoveOverlay(Frame, this.HoveredGizmoHandle, this.ActiveGizmoHandle, this.DisabledTransformHandles),
                ..TransformGizmo.BuildRotateOverlay(
                    Frame,CameraForward, this.HoveredGizmoHandle, this.ActiveGizmoHandle, this.DisabledTransformHandles),
                ..TransformGizmo.BuildScaleOverlay(Frame, this.HoveredGizmoHandle, this.ActiveGizmoHandle, this.DisabledTransformHandles),
                ..TransformSpaceToggleGizmo.BuildOverlay(Frame,CameraRight,CameraUp,GizmoState.TransformSpace, this.HoveredGizmoHandle),
            ];
            if(this.SurfaceSnapArmed|| this.ActiveSurfaceSnapDrag){
                (Vector3 SnapPosition,Vector3 SnapNormal)= this.ActiveSurfaceSnapDrag&& this.CurrentSurfaceSnapHit is { }Hit
                    ?Hit
                    :SurfaceSnapGizmo.GetArmedPlacement(Frame,CameraForward);
                bool SnapDisabled= this.DisabledTransformHandles.Contains(GizmoHandleId.SurfaceSnapPlane);
                Combined.AddRange(SurfaceSnapGizmo.BuildOverlay(Frame,SnapPosition,SnapNormal,SnapDisabled));
            }
            TransformPrimitives=Combined;
        }
        if(this.CurrentPropertyGizmo is { }PropertyFrame){
            PropertyPrimitives=PropertyGizmo.BuildOverlay(
                PropertyFrame, this.HoveredGizmoHandle, this.ActiveGizmoHandle);
        }
        Primitives.AddRange(CompoundGizmo.Compose(TransformPrimitives,PropertyPrimitives));
        if(this.ActiveMarqueeRectangle is { }Rectangle&& this.ViewState is { }State){
            Primitives.AddRange(MarqueeOverlay.Build(
                Rectangle,State.GetView(),State.GetProjection(this.AspectRatio), this.SurfaceWidth, this.SurfaceHeight));
        }
        if(this.ActiveDragHud is { }HudState&& this.ViewState is { }HudViewState){
            IReadOnlyList<GizmoOverlayPrimitive>Hud=GizmoDragHud.Build(
                HudState,HudViewState.GetView(),HudViewState.GetProjection(this.AspectRatio), this.SurfaceWidth, this.SurfaceHeight);
            int ExistingVertices=Primitives.Sum(static Primitive=>Primitive.Vertices.Count);
            int HudVertices=Hud.Sum(static Primitive=>Primitive.Vertices.Count);
            // M0037: SceneRenderer.SetOverlayは頂点予算超過で例外を投げる。HUDは表示専用の付加情報
            // なので、超過するくらいならギズモ本体を守るためHUDだけを落とす。
            if(ExistingVertices+HudVertices<=SceneRenderer.MaxOverlayVertices){
                Primitives.AddRange(Hud);
            }
        }

        Active.SetOverlay(Primitives.Count==0?null:Primitives);
    }

    private void PublishMarqueeSelection(ScreenRectangle Rectangle,bool CtrlHeld){
        IReadOnlyList<NodePath>Hits= this.ViewState is { }State
            ?ScenePicker.PickMarquee(this.CurrentRenderObjects,State.GetView(),State.GetProjection(this.AspectRatio),
                Rectangle, this.SurfaceWidth, this.SurfaceHeight)
            :Array.Empty<NodePath>();
        this.ActiveMarqueeRectangle=null;
        this.RefreshOverlay();
        this.ObjectsMarqueePicked?.Invoke(Hits,CtrlHeld);
    }

    private bool BeginGizmoDrag(GizmoHandleId Handle,int X,int Y,bool Duplicate){
        ThreadHelper.ThrowIfNotOnUIThread();
        return Handle==GizmoHandleId.TransformSpaceToggle
            ? this.ToggleTransformSpaceHandle()
            :PropertyGizmo.IsPropertyHandle(Handle)
                ? this.BeginPropertyGizmoDrag(Handle,X,Y,Duplicate)
                : this.BeginTransformGizmoDrag(Handle,X,Y,Duplicate);
    }

    /// <summary>
    /// M0033: ギズモ上のLocal/Globalアイコン。ドラッグを開始せず即座に空間を切り替える。
    /// falseを返すことで<see cref="OnPointerInput"/>の既存経路(<c>Machine.CancelCurrent()</c>)が
    /// そのままIdleへ戻す — SceneInputStateMachine側の変更は不要。
    /// </summary>
    private bool ToggleTransformSpaceHandle(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.SetTransformSpace(this.CurrentTransformSpace==SceneTransformSpace.World
                                   ?SceneTransformSpace.Local:SceneTransformSpace.World);
        return false;
    }

    private bool BeginPropertyGizmoDrag(GizmoHandleId Handle,int X,int Y,bool Duplicate){
        if(Duplicate|| this.CurrentPropertyGizmo is not { }Frame
            || this.TargetSnapshot is not { }Snapshot||Snapshot.Projection is not { }Projection
            || this.CurrentSelection?.PrimaryNodeId is not { }PrimaryId
            ||!Projection.TryGetNode(PrimaryId,out SceneProjectionNode PrimaryNode)
            ||!Frame.Handles.Contains(Handle)||!Frame.CanEdit(Handle)){
            return false;
        }

        this.ActivePropertyDrag=new PropertyGizmoDrag(Frame,Handle,X,Y);
        this.PropertyCommitInProgress=false;
        this.EditTransaction.BeginDirect(Snapshot);
        this.ActiveGizmoHandle=Handle;
        this.HoveredGizmoHandle=Handle;
        this.RefreshOverlay();
        this.SetStatus($"プロパティ編集中: {PrimaryNode.Node.DisplayName}");
        return true;
    }

    private bool BeginTransformGizmoDrag(GizmoHandleId Handle,int X,int Y,bool Duplicate){
        if(this.CurrentMoveGizmo is not { }Frame|| this.ViewState is not { }State
                                                || this.TargetSnapshot is not { }Snapshot||Snapshot.Projection is not { }Projection
                                                || this.CurrentSelection is not { }Selection||Selection.PrimaryNodeId is not { }PrimaryId
                                                ||!Projection.TryGetNode(PrimaryId,out SceneProjectionNode PrimaryNode)){
            return false;
        }

        SceneActiveTool Family=GetHandleFamily(Handle);
        List<SceneProjectionNode>Roots= this.GetSelectedMoveRoots(Projection);
        IReadOnlyList<DuplicateNodePlanItem>DuplicatePlan=Array.Empty<DuplicateNodePlanItem>();
        if(Duplicate&&!DuplicateNodePlanner.TryCreate(Roots,Projection,out DuplicatePlan)){
            this.SetStatus("Scene Rootまたは親Childrenを解決できないNodeは複製できません。");
            return false;
        }
        if(Roots.Any(Node=>!SceneTransformBinding.CanEditTransform(Node.Node,Family,Handle))){
            return false;
        }

        List<SceneTransformTarget>Targets=[];
        foreach(SceneProjectionNode Node in Roots){
            SceneRenderObject?Object= this.CurrentRenderObjects.FirstOrDefault(Item=>Item.NodeId.Equals(Node.Path));
            if(Object is null){
                continue;
            }
            Matrix4x4 ParentWorld=Node.Path.Parent is { }ParentPath
                ? this.CurrentRenderObjects.FirstOrDefault(Item=>Item.NodeId.Equals(ParentPath))?.World??Matrix4x4.Identity
                :Matrix4x4.Identity;
            Targets.Add(new SceneTransformTarget(Node.Id,Node.Path,Object.World,ParentWorld));
        }
        if(Targets.Count==0){
            return false;
        }

        // M0036: コミット前のRoot Worldを退避しておく。Ctrl+Zの表示先読みパッチ(TryFastUndo)専用で、
        // EditTransactionController.Targets(private)には外部からアクセスできないためここで複製する。
        this.ActiveDragOriginalRoots=Targets
                                    .Select(static Target=>new SceneTransformPreview(default,Target.NodePath,Target.OriginalWorld))
                                    .ToArray();
        // M0037: 角度累積をドラッグ開始時にリセットする(Rotate以外では未使用のまま無害)。
        this.ActiveRotateAccumulatedRadians=0f;
        this.ActiveRotatePreviousRadians=0f;

        (Vector3 Origin,Vector3 Direction)=SceneViewCamera.ScreenPointToRay(
            State.GetView(),State.GetProjection(this.AspectRatio),X,Y, this.SurfaceWidth, this.SurfaceHeight);
        Vector3 CameraForward=SceneViewCamera.GetForward(State.Yaw,State.Pitch);
        this.ActiveMoveDrag=null;
        this.ActiveRotateDrag=null;
        this.ActiveScaleDrag=null;
        // M0034: SurfaceSnapPlaneはMove/Rotate/ScaleGizmoDragのどれとも噛み合わない専用ハンドル
        // (Frame.GetAxis/GetPlaneAxesが未知Handleで例外を投げるため、Family switchの評価自体を
        // ||短絡で回避する)。編集可否は既に上のCanEditTransform(Family,Handle)チェックで
        // 保証済みなので、ここは無条件でtrueにしてよい。
        this.ActiveSurfaceSnapDrag=Handle==GizmoHandleId.SurfaceSnapPlane;
        this.CurrentSurfaceSnapHit=null;
        bool Began= this.ActiveSurfaceSnapDrag||Family switch {
                                                    SceneActiveTool.Move=>MoveGizmoDrag.TryBegin(
                                                     Frame,Handle,Origin,Direction,CameraForward,out this.ActiveMoveDrag),
                                                    SceneActiveTool.Rotate=>RotateGizmoDrag.TryBegin(
                                                     Frame,Handle,Origin,Direction,CameraForward,out this.ActiveRotateDrag),
                                                    SceneActiveTool.Scale=>ScaleGizmoDrag.TryBegin(
                                                     Frame,Handle,Origin,Direction,CameraForward,out this.ActiveScaleDrag),
                                                    _=>false,
                                                };
        if(!Began){
            return false;
        }

        this.EditTransaction.Begin(Snapshot,Targets);
        this.ActiveDraggedRootPaths=Targets.Select(static Target=>Target.NodePath).ToArray();
        if(Duplicate){
            if(!this.TryStartDuplicatePreview(DuplicatePlan)){
                this.EditTransaction.Cancel();
                this.ActiveDraggedRootPaths=[];
                this.SetStatus("複製preview用の描画リソースを確保できませんでした。");
                return false;
            }

            this.PreviewDescendantOriginals=[];
        } else {
            this.PreviewDescendantOriginals= this.CurrentRenderObjects
                                                 .Where(Object=>Targets.Any(Target=>Object.NodeId.IsDescendantOf(Target.NodePath)))
                                                 .Select(Object=>new SceneTransformPreview(default,Object.NodeId,Object.World))
                                                 .ToArray();
        }
        SceneRenderObject?PrimaryObject= this.CurrentRenderObjects.FirstOrDefault(Item=>Item.NodeId.Equals(PrimaryNode.Path));
        this.ActivePrimaryRotation=PrimaryObject is not null
                                 &&SceneTransformMath.TryDecompose(PrimaryObject.World,out SceneTransformValue PrimaryWorld)
                                       ?PrimaryWorld.Rotation
                                       :Quaternion.Identity;
        this.ActiveGizmoStartFrame=Frame;
        this.ActiveCommitMembers=GetTransformMembers(Family);
        this.ActiveFollowViewSnapshot=SceneViewFollowSnapshot.Capture(State);
        this.HasActiveFollowViewSnapshot=true;
        this.FollowViewApplied=false;
        this.ActiveTransformTool=Family;
        this.ActiveGizmoHandle=Handle;
        this.HoveredGizmoHandle=Handle;
        this.RefreshOverlay();
        this.SetStatus($"{GetToolDisplayName(Family)}中: {PrimaryNode.Node.DisplayName}");
        return true;
    }

    private bool TryStartDuplicatePreview(IReadOnlyList<DuplicateNodePlanItem>Plan){
        if(this.Renderer is not { }Active){ return false; }

        List<SceneRenderObject>RenderObjects=[];
        List<(NodePath SourcePath,Matrix4x4 OriginalWorld)>Sources=[];
        foreach(SceneRenderObject Object in this.CurrentRenderObjects){
            foreach(DuplicateNodePlanItem Item in Plan){
                if(!Object.NodeId.Equals(Item.SourcePath)&&!Object.NodeId.IsDescendantOf(Item.SourcePath)){
                    continue;
                }

                NodePath ResultPath=DuplicateNodePlanner.RemapPath(Object.NodeId,Item.SourcePath,Item.ResultPath);
                RenderObjects.Add(new SceneRenderObject {
                    Mesh=Object.Mesh,
                    World=Object.World,
                    NodeId=ResultPath,
                    IncludeInBounds=Object.IncludeInBounds,
                    IsPickable=Object.IsPickable,
                });
                Sources.Add((Object.NodeId,Object.World));
                break;
            }
        }

        if(RenderObjects.Count==0||!Active.TryAddPreviewObjects(RenderObjects,out IReadOnlyList<int>Handles)){
            return false;
        }

        this.DuplicateRenderPreviews=Sources.Select((Source,Index)=>new DuplicateRenderPreview(
                                                     Source.SourcePath,Source.OriginalWorld,Handles[Index])).ToArray();
        this.ActiveDuplicatePlan=Plan.ToArray();
        DuplicateNodePlanItem PrimaryItem=Plan.FirstOrDefault(Item=> this.SelectedNodePath is { }Selected&&Item.SourcePath.Equals(Selected));
        NodePath PrimaryResult=PrimaryItem.ResultPath.Equals(default(NodePath))
            ?Plan[^1].ResultPath
            :PrimaryItem.ResultPath;
        Active.SetSelectedNode(PrimaryResult,Plan.Select(static Item=>Item.ResultPath).ToArray());
        return true;
    }

    private void RemoveDuplicatePreview(bool RestoreOriginalSelection){
        if(this.DuplicateRenderPreviews.Count>0&& this.Renderer is { }Active){
            Active.RemovePreviewObjects(this.DuplicateRenderPreviews.Select(static Item=>Item.Handle).ToArray());
            if(RestoreOriginalSelection){
                Active.SetSelectedNode(this.SelectedNodePath, this.SelectedNodePaths);
            }
        }

        this.DuplicateRenderPreviews=[];
        this.ActiveDuplicatePlan=[];
    }

    private void UpdateTransformGizmoDrag(int X,int Y,bool Snap,bool FollowView){
        if(this.ActiveTransformTool is not { }Tool|| this.ViewState is not { }State
                                                  || this.CurrentMoveGizmo is null|| this.ActiveGizmoStartFrame is not { }StartFrame){
            return;
        }
        (Vector3 Origin,Vector3 Direction)=SceneViewCamera.ScreenPointToRay(
            State.GetView(),State.GetProjection(this.AspectRatio),X,Y, this.SurfaceWidth, this.SurfaceHeight);
        IReadOnlyList<SceneTransformPreview>Previews;
        Quaternion RotationDelta=Quaternion.Identity;
        Vector3 FollowMoveDelta=Vector3.Zero;
        // M0037: HUD(GizmoDragHud)がswitchの外から読めるよう、caseセクション内のout変数だった
        // Radians/Factorsをメソッド先頭のローカルへ引き上げる(RotationDeltaと同じ扱い)。
        float RotateRadians=0f;
        Vector3 ScaleFactors=Vector3.One;
        this.ActiveCommitMembers=GetTransformMembers(Tool);
        switch(Tool){
            // M0034: Shift+Ctrlで武装した中心の平面四角をドラッグ中。カーソル直下の別オブジェクト
            // 表面へPosition+Rotationを吸着させる(旧Nキー方式と同じ数学、起動方法だけ変更)。
            case SceneActiveTool.Move when this.ActiveSurfaceSnapDrag:
                if(ScenePicker.TrySurfaceSnapQuery(this.CurrentRenderObjects,Origin,Direction, this.ActiveDraggedRootPaths,out SurfaceSnapHit SurfaceHit)){
                    this.CurrentSurfaceSnapHit=(SurfaceHit.Position,SurfaceHit.Normal);
                    FollowMoveDelta=SurfaceHit.Position-StartFrame.Pivot;
                    Previews= this.EditTransaction.PreviewSurfaceSnap(
                                                                      StartFrame.Pivot,SurfaceHit.Position, this.ActivePrimaryRotation,SurfaceHit.Normal);
                    this.ActiveCommitMembers=SceneTransformMemberMask.Position|SceneTransformMemberMask.Rotation;
                } else {
                    // 面が見つからないティックは直前のプレビュー(初回なら空、つまり未移動)を維持する。
                    Previews= this.EditTransaction.CurrentPreviews;
                }
                break;
            case SceneActiveTool.Move when this.ActiveMoveDrag is { }MoveDrag
                &&MoveDrag.TryUpdate(Origin,Direction,out Vector3 Delta):
                bool VertexSnap=(NativeMethods.GetKeyState(NativeMethods.VK_V)&0x8000)!=0;
                if(VertexSnap&&ScenePicker.TryVertexSnapQuery(this.CurrentRenderObjects,State.GetView(),State.GetProjection(this.AspectRatio),
                    X,Y, this.SurfaceWidth, this.SurfaceHeight, this.ActiveDraggedRootPaths,out VertexSnapHit VertexHit)){
                    FollowMoveDelta=VertexHit.Position-StartFrame.Pivot;
                    Previews= this.EditTransaction.PreviewTranslation(FollowMoveDelta);
                } else {
                    if(Snap){ Delta=TransformSnap.SnapTranslation(Delta,StartFrame, this.ActiveGizmoHandle!.Value); }
                    FollowMoveDelta=Delta;
                    Previews= this.EditTransaction.PreviewTranslation(Delta);
                }
                break;
            case SceneActiveTool.Rotate when this.ActiveRotateDrag is { }RotateDrag
                &&RotateDrag.TryUpdate(Origin,Direction,out RotateRadians,out _):
                if(Snap){ RotateRadians=TransformSnap.SnapRotationRadians(RotateRadians); }
                RotationDelta=RotateDrag.CreateDelta(RotateRadians);
                Previews= this.EditTransaction.PreviewRotation(StartFrame.Pivot,RotationDelta);
                this.ActiveRotateAccumulatedRadians=GizmoDragHud.AccumulateAngle(this.ActiveRotateAccumulatedRadians, this.ActiveRotatePreviousRadians,RotateRadians);
                this.ActiveRotatePreviousRadians=RotateRadians;
                break;
            case SceneActiveTool.Scale when this.ActiveScaleDrag is { }ScaleDrag
                &&ScaleDrag.TryUpdate(Origin,Direction,out ScaleFactors):
                if(Snap){ ScaleFactors=TransformSnap.SnapScaleFactors(ScaleFactors, this.ActiveGizmoHandle!.Value); }
                Previews= this.EditTransaction.PreviewScale(StartFrame.Pivot,StartFrame.BasisRotation,ScaleFactors);
                break;
            default:
                return;
        }

        if(this.DuplicateRenderPreviews.Count>0){
            foreach(DuplicateRenderPreview DuplicatePreview in this.DuplicateRenderPreviews){
                SceneTransformPreview?RootPreview=null;
                foreach(SceneTransformPreview Preview in Previews){
                    if(Preview.NodePath.Equals(DuplicatePreview.SourcePath)){
                        RootPreview=Preview;
                        break;
                    }
                }
                Matrix4x4 World=RootPreview is { }Matched
                    ?Matched.World
                    :DuplicatePreview.OriginalWorld* this.EditTransaction.CurrentWorldDelta;
                this.Renderer?.UpdatePreviewObjectTransform(DuplicatePreview.Handle,World);
            }
        } else {
            foreach(SceneTransformPreview Preview in Previews){
                this.Renderer?.UpdateObjectTransform(Preview.NodePath,Preview.World);
            }
            foreach(SceneTransformPreview Descendant in this.PreviewDescendantOriginals){
                this.Renderer?.UpdateObjectTransform(Descendant.NodePath,Descendant.World* this.EditTransaction.CurrentWorldDelta);
            }
        }

        // M0034: サーフェススナップ中はカーソルレイでヒット面を探すため、FollowViewでカメラが
        // 動くとレイが変わり吸着先が動くフィードバックループになる。明示的に抑止する。
        if(FollowView&& this.HasActiveFollowViewSnapshot&&Tool!=SceneActiveTool.Scale&&!this.ActiveSurfaceSnapDrag){
            if(Tool==SceneActiveTool.Move){
                this.ActiveFollowViewSnapshot.ApplyMove(State,FollowMoveDelta);
            } else {
                this.ActiveFollowViewSnapshot.ApplyRotation(State,StartFrame.Pivot,RotationDelta);
            }

            this.FollowViewApplied=true;
            this.Renderer?.SetCamera(State.GetView(),State.GetProjection(this.AspectRatio));
        }
        SceneTransformPreview?Primary=null;
        foreach(SceneTransformPreview Preview in Previews){
            if(this.SelectedNodePath is { }Path&&Preview.NodePath.Equals(Path)){
                Primary=Preview;
                break;
            }
        }
        if(Primary is { }PrimaryPreview
            &&SceneTransformMath.TryDecompose(PrimaryPreview.World,out SceneTransformValue WorldTransform)
            && this.CurrentMoveGizmo is { }CurrentFrame){
            this.CurrentMoveGizmo=MoveGizmoFrame.Create(
                                                        WorldTransform.Position,WorldTransform.Rotation,State.TransformSpace,CurrentFrame.WorldScale);
            if(this.TargetSnapshot?.Projection is { }Projection
                && this.CurrentSelection?.PrimaryNodeId is { }PrimaryId
                &&Projection.TryGetNode(PrimaryId,out SceneProjectionNode PrimaryNode)
                && this.CurrentPropertyGizmo is { }PropertyFrame
                &&PropertyGizmoBinding.TryCreate(
                    PrimaryNode.Node,PrimaryPreview.World,PropertyFrame.WorldScale,
                    out PropertyGizmoFrame UpdatedPropertyFrame)){
                this.CurrentPropertyGizmo=UpdatedPropertyFrame;
            }
        }
        // M0037: ドラッグ中HUD(開始点マーカー・破線・角度の扇・数値読み取り)用の状態。
        this.ActiveDragHud= this.ActiveGizmoHandle is { }DragHandle
                               ?new GizmoDragHudState(
                                                      Tool,DragHandle,StartFrame.Pivot,StartFrame.Pivot,StartFrame.Pivot+FollowMoveDelta, this.ActiveRotateDrag?.RingNormal??Vector3.UnitY, this.ActiveRotateDrag?.StartDirectionVector??Vector3.UnitX, this.ActiveRotateAccumulatedRadians,
                                                      DragHandle==GizmoHandleId.RotateView?TransformGizmo.RotateViewRingRadius:TransformGizmo.RotateRingRadius,
                                                      ScaleFactors,StartFrame.WorldScale,X,Y)
                               :null;
        if(this.CurrentMoveGizmo is not null){
            this.RefreshOverlay();
        }
    }

    private void UpdatePropertyGizmoDrag(int X,int Y,bool Snap){
        if(this.ActivePropertyDrag is null){
            return;
        }

        this.CurrentPropertyGizmo= this.ActivePropertyDrag.Update(X,Y,Snap);
        this.RefreshOverlay();
    }

    private List<SceneProjectionNode>GetSelectedMoveRoots(SceneProjection Projection){
        if(this.CurrentSelection is not { }Selection){
            return [];
        }

        List<SceneProjectionNode>Ordered=Selection.NodeIds
            .Select(Id=>Projection.TryGetNode(Id,out SceneProjectionNode Node)?Node:null)
            .Where(static Node=>Node is not null)
            .Cast<SceneProjectionNode>()
            .OrderBy(static Node=>Node.Path.Depth)
            .ToList();
        List<SceneProjectionNode>Roots=[];
        foreach(SceneProjectionNode Node in Ordered){
            if(!Roots.Any(Root=>Node.Path.IsDescendantOf(Root.Path))){
                Roots.Add(Node);
            }
        }
        return Roots;
    }

    private void CommitEditTransaction(){
        if(this.ActivePropertyDrag is not null&& this.CurrentPropertyGizmo is { }PropertyFrame
                                              && this.ActiveGizmoHandle is { }PropertyHandle
                                              && this.TargetSnapshot is { }Snapshot&&Snapshot.Projection is { }Projection
                                              && this.CurrentSelection?.PrimaryNodeId is { }PrimaryId
                                              &&Projection.TryGetNode(PrimaryId,out _)){
            PropertyGizmoFrame OriginalFrame= this.ActivePropertyDrag.StartFrame;
            SceneEditOperation[]Operations=PropertyGizmo.CreateOperations(
                PropertyFrame,PropertyHandle,Snapshot.Key,PrimaryId,Snapshot.DocumentVersion,
                OriginalFrame);
            this.ActivePropertyDrag=null;
            this.ActiveGizmoHandle=null;
            this.PropertyCommitInProgress=true;
            bool PropertyCreated= this.EditTransaction.TryCreateDirectCommit(
                                                                             "Edit Scene Property",Operations,out SceneEditCommitRequest?PropertyRequest);
            if(!PropertyCreated||PropertyRequest is null|| this.EditCommitRequested is null){
                this.CancelEditTransaction();
                this.SetStatus("プロパティ編集を確定できなかったため変更を破棄しました。");
                return;
            }

            this.EditCommitRequested.Invoke(PropertyRequest);
            this.SetStatus("C#ソースへプロパティ編集を反映しています...");
            return;
        }

        SceneActiveTool Tool= this.ActiveTransformTool??SceneActiveTool.Move;
        this.ActiveMoveDrag=null;
        this.ActiveRotateDrag=null;
        this.ActiveScaleDrag=null;
        this.ActiveTransformTool=null;
        this.ActiveGizmoHandle=null;
        SceneTransformMemberMask Members= this.ActiveCommitMembers;
        SceneEditCommitRequest?Request;
        bool Created= this.ActiveDuplicatePlan.Count>0
            ? this.EditTransaction.TryCreateDuplicateCommit(
                                                            $"Duplicate and {GetToolDisplayName(Tool)} Scene Selection",Members, this.ActiveDuplicatePlan.Select(static Item=>Item.ResultPath).ToArray(),out Request)
            : this.EditTransaction.TryCreateCommit(
                                                   $"{GetToolDisplayName(Tool)} Scene Selection",Members,out Request);
        if(!Created
            ||Request is null|| this.EditCommitRequested is null){
            this.CancelEditTransaction();
            this.SetStatus($"{GetToolDisplayName(Tool)}を確定できなかったため変更を破棄しました。");
            return;
        }

        this.EditCommitRequested.Invoke(Request);
        this.SetStatus($"C#ソースへ{GetToolDisplayName(Tool)}を反映しています...");
    }

    private void CancelEditTransaction(){
        if(this.EditTransaction.State==EditTransactionState.Idle){
            return;
        }
        bool WasProperty= this.ActivePropertyDrag is not null|| this.PropertyCommitInProgress;
        bool WasDuplicate= this.ActiveDuplicatePlan.Count>0;
        SceneEditCancellation Cancellation= this.EditTransaction.Cancel();
        if(WasProperty){
            this.ActivePropertyDrag=null;
            this.PropertyCommitInProgress=false;
            this.ActiveGizmoHandle=null;
            this.HoveredGizmoHandle=null;
            this.CurrentPropertyGizmo=null;
            if(Cancellation.PendingSnapshot is { }PropertyPending){
                this.SetTarget(PropertyPending);
            } else {
                this.UpdateMoveGizmo();
            }
            return;
        }
        if(WasDuplicate){
            this.RemoveDuplicatePreview(RestoreOriginalSelection:true);
        } else {
            foreach(SceneTransformPreview Original in Cancellation.RestoredTransforms){
                this.Renderer?.UpdateObjectTransform(Original.NodePath,Original.World);
            }
            foreach(SceneTransformPreview Descendant in this.PreviewDescendantOriginals){
                this.Renderer?.UpdateObjectTransform(Descendant.NodePath,Descendant.World);
            }
        }
        if(this.FollowViewApplied&& this.HasActiveFollowViewSnapshot&& this.ViewState is { }State){
            this.ActiveFollowViewSnapshot.Restore(State);
            this.Renderer?.SetCamera(State.GetView(),State.GetProjection(this.AspectRatio));
        }

        this.PreviewDescendantOriginals=[];
        this.ActiveDraggedRootPaths=[];
        this.ActiveDragOriginalRoots=[];
        this.ActiveGizmoStartFrame=null;
        this.HasActiveFollowViewSnapshot=false;
        this.FollowViewApplied=false;
        this.ActiveCommitMembers=SceneTransformMemberMask.None;
        this.ActiveSurfaceSnapDrag=false;
        this.CurrentSurfaceSnapHit=null;
        this.ActiveDragHud=null;
        this.ActiveMoveDrag=null;
        this.ActiveRotateDrag=null;
        this.ActiveScaleDrag=null;
        this.ActiveTransformTool=null;
        this.ActiveGizmoHandle=null;
        this.HoveredGizmoHandle=null;
        this.CurrentMoveGizmo=null;
        if(Cancellation.PendingSnapshot is { }Pending){
            this.SetTarget(Pending);
        } else {
            this.UpdateMoveGizmo();
        }
    }

    /// <summary>
    /// M0038: 予測でトランザクションを閉じた後にソース書き戻しが失敗したときの通知専用。
    /// 表示の同期自体は<see cref="ResyncAfterCommitFailure"/>が別途行う(呼び出し側が両方呼ぶ)。
    /// </summary>
    internal void ReportCommitFailure(string?ErrorMessage){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.SetStatus(ErrorMessage??"C#ソースへの反映に失敗しました。編集は取り消されます。");
    }

    /// <summary>
    /// M0039: 直列の書き戻しチェーンのどこかが失敗したとき、呼び出し側
    /// (<see cref="SceneDesignerToolWindowControl"/>)が
    /// <c>Session.RefreshPinnedSnapshotFromSourceAsync</c>で画面をソースの真実へ強制同期した直後に
    /// 呼ぶ。失敗より後の編集は「本当は起きていない前段の編集」を前提に予測されていた可能性があり
    /// 信用できないため、先読みUndo/Redoスタックを丸ごと破棄する — 既存のProperty編集・複製・
    /// 別Scene切替時のClearと同じ確立済みのidiom(このメソッド自体は表示の再構築は行わない。
    /// それは<c>SetTarget</c>経由で<c>PinnedSnapshotChanged</c>が届いたときに起きる)。
    /// </summary>
    internal void ResyncAfterCommitFailure(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.RecentTransformCommits.Clear();
        this.RecentTransformUndone.Clear();
    }

    internal void CompleteEditCommit(bool Succeeded,string?ErrorMessage){
        if(this.EditTransaction.State!=EditTransactionState.Committing){
            return;
        }
        if(!Succeeded){
            this.CancelEditTransaction();
            this.SetStatus(ErrorMessage??"C#ソースへの反映に失敗しました。");
            return;
        }

        bool WasProperty= this.PropertyCommitInProgress;
        bool WasDuplicate= this.ActiveDuplicatePlan.Count>0;
        // Complete()がPreviews/WorldDeltaをクリアする前に、コミットされた最終Transformを退避する。
        // Move/Rotate/Scaleのときだけ使う(プロパティ編集はPrimaryObjectの位置を動かさない。複製は
        // 新規Nodeが対象でCurrentRenderObjectsにまだ存在しないため、既存のTryRestoreDuplicateSelection
        // 経路が別途扱う)。
        IReadOnlyList<SceneTransformPreview>FinalPreviews=
            !WasProperty&&!WasDuplicate? this.EditTransaction.CurrentPreviews:Array.Empty<SceneTransformPreview>();
        IReadOnlyList<SceneTransformPreview>FinalDescendants= this.PreviewDescendantOriginals;
        Matrix4x4 FinalWorldDelta= this.EditTransaction.CurrentWorldDelta;
        SceneDocumentSnapshot?Pending= this.EditTransaction.Complete();
        if(WasProperty){
            this.ActivePropertyDrag=null;
            this.PropertyCommitInProgress=false;
            this.ActiveGizmoHandle=null;
            this.HoveredGizmoHandle=null;
            this.CurrentPropertyGizmo=null;
            // M0036: プロパティ編集はTransform用の先読みキャッシュの対象外。古い記録を残すと
            // 別コミット種別のUndoに誤って使われかねないので丸ごと破棄する。
            this.RecentTransformCommits.Clear();
            this.RecentTransformUndone.Clear();
            if(Pending is not null){
                this.SetTarget(Pending);
            } else {
                this.UpdateMoveGizmo();
            }

            this.SetStatus("プロパティをC#ソースへ反映しました。Ctrl+Zで元に戻せます。");
            return;
        }

        this.RemoveDuplicatePreview(RestoreOriginalSelection:false);
        // M0036: 直近コミットを先読みUndo用に記録する(複製はNodePathが新規のため対象外、
        // Transform自体が変わらないケースはFinalPreviews.Count==0で自然に除外される)。新しい
        // コミットが起きたのでredo系列は破棄する(標準的なUndo/Redoの慣習)。
        if(WasDuplicate){
            this.RecentTransformCommits.Clear();
            this.RecentTransformUndone.Clear();
        } else if(FinalPreviews.Count>0){
            this.RecentTransformCommits.Push(new TransformUndoRecord(this.ActiveDragOriginalRoots,FinalPreviews,FinalDescendants,FinalWorldDelta));
            while(this.RecentTransformCommits.Count>MaxRecentTransformCommits){
                this.RecentTransformCommits=new Stack<TransformUndoRecord>(this.RecentTransformCommits.Reverse().Skip(1));
            }

            this.RecentTransformUndone.Clear();
        }

        this.ActiveDragOriginalRoots=[];
        this.PreviewDescendantOriginals=[];
        this.ActiveDraggedRootPaths=[];
        this.ActiveGizmoStartFrame=null;
        this.HasActiveFollowViewSnapshot=false;
        this.FollowViewApplied=false;
        this.ActiveCommitMembers=SceneTransformMemberMask.None;
        this.ActiveSurfaceSnapDrag=false;
        this.CurrentSurfaceSnapHit=null;
        this.ActiveDragHud=null;
        this.ActiveMoveDrag=null;
        this.ActiveRotateDrag=null;
        this.ActiveScaleDrag=null;
        this.ActiveTransformTool=null;
        this.ActiveGizmoHandle=null;
        this.HoveredGizmoHandle=null;
        // UpdateMoveGizmoはCurrentRenderObjectsからpivotを再計算するが、再解析(PinnedSnapshotChanged)
        // が終わるまでCurrentRenderObjectsはまだドラッグ前の位置のまま。ここで先にコミット済みの
        // 最終Transformへパッチしておくことで、ギズモが一瞬元の位置へ戻ってから追いつく見た目のズレを防ぐ。
        this.ApplyFinalTransformsToRenderObjects(FinalPreviews,FinalDescendants,FinalWorldDelta);
        this.CurrentMoveGizmo=null;
        if(Pending is not null){
            this.SetTarget(Pending);
        } else {
            this.UpdateMoveGizmo();
        }

        this.SetStatus("TransformをC#ソースへ反映しました。Ctrl+Zで元に戻せます。");
    }

    /// <summary>
    /// <see cref="CompleteEditCommit"/>・<see cref="TryFastUndo"/>・<see cref="TryFastRedo"/>専用。
    /// 指定したNodePathのRenderObjectだけを、指定のWorld行列へ差し替える(<see cref="SceneRenderObject"/>は
    /// initプロパティのみのクラスのため複製が必要)。対象が無ければ何もしない。
    /// </summary>
    private void ApplyFinalTransformsToRenderObjects(
        IReadOnlyList<SceneTransformPreview>FinalPreviews,IReadOnlyList<SceneTransformPreview>FinalDescendants,
        Matrix4x4 FinalWorldDelta){
        if(FinalPreviews.Count==0&&FinalDescendants.Count==0){
            return;
        }

        Dictionary<NodePath,Matrix4x4>FinalWorlds=[];
        foreach(SceneTransformPreview Preview in FinalPreviews){
            FinalWorlds[Preview.NodePath]=Preview.World;
        }
        foreach(SceneTransformPreview Descendant in FinalDescendants){
            FinalWorlds[Descendant.NodePath]=Descendant.World*FinalWorldDelta;
        }

        this.CurrentRenderObjects= this.CurrentRenderObjects.Select(Object=>
                                                                        FinalWorlds.TryGetValue(Object.NodeId,out Matrix4x4 FinalWorld)
                                                                            ?new SceneRenderObject {
                                                                                 Mesh=Object.Mesh,World=FinalWorld,NodeId=Object.NodeId,
                                                                                 IncludeInBounds=Object.IncludeInBounds,IsPickable=Object.IsPickable }
                                                                            :Object).ToArray();
    }

    /// <summary>
    /// M0036: シーンビューのCtrl+Zから呼ばれる。実際のC#ソースUndoが成功した後に呼び出し側が呼ぶ想定
    /// (<see cref="SceneDesignerToolWindowControl"/>参照)。直近のTransformコミットを、Roslyn再解析を
    /// 待たずに3Dビューへ即座に巻き戻す表示専用の先読みパッチ。対応する記録が無ければ何もしない
    /// (この場合は通常どおりRoslyn再解析待ちで反映される)。
    /// </summary>
    internal bool TryFastUndo(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.InputMachine is not { State:SceneNavState.Idle}|| this.RecentTransformCommits.Count==0){
            return false;
        }

        TransformUndoRecord Record= this.RecentTransformCommits.Pop();
        this.RecentTransformUndone.Push(Record);
        this.ApplyFinalTransformsToRenderObjects(Record.RootsBefore,Record.Descendants,Matrix4x4.Identity);
        this.CurrentMoveGizmo=null;
        this.UpdateMoveGizmo();
        return true;
    }

    /// <summary><see cref="TryFastUndo"/>の逆。元のコミットと同じ引数で<see cref="ApplyFinalTransformsToRenderObjects"/>を再生するだけ。</summary>
    internal bool TryFastRedo(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.InputMachine is not { State:SceneNavState.Idle}|| this.RecentTransformUndone.Count==0){
            return false;
        }

        TransformUndoRecord Record= this.RecentTransformUndone.Pop();
        this.RecentTransformCommits.Push(Record);
        this.ApplyFinalTransformsToRenderObjects(Record.RootsAfter,Record.Descendants,Record.WorldDelta);
        this.CurrentMoveGizmo=null;
        this.UpdateMoveGizmo();
        return true;
    }

    /// <summary>
    /// <see cref="RenderSurfaceWindow.WheelInput"/>の購読先。設計時はWPFの<c>PreviewMouseWheel</c>で
    /// 受ける計画だったが、実機検証で発火しないことが判明したため、他のマウス入力と同じく
    /// <see cref="RenderSurfaceWindow"/>のWndProcから受け取る(M0020 Phase 1訂正、ADR-0041)。
    /// Deltaは<c>WHEEL_DELTA</c>=120単位。Fly中(RMB押下中)はAdjustNavigationSpeed、それ以外は
    /// Dolly(Perspective)/Zoom(Orthographic)。
    /// </summary>
    private void HandleWheel(int Delta){
        if(this.InputMachine is not { } Machine|| this.ViewState is not { } State){
            return;
        }

        int Notches=Delta/120;
        if(Notches==0){
            return;
        }

        if(Machine.HandleWheel()==SceneWheelAction.AdjustSpeed){
            State.ApplyNavigationSpeedAdjust(Notches);
        } else if(State.ProjectionMode==SceneProjectionMode.Orthographic){
            State.ApplyZoom(Notches);
        } else {
            State.ApplyDolly(Notches);
        }
    }

    /// <summary>
    /// WPFの<c>KeyDown</c>から呼ぶ。Fly中(RMB押下中)以外は<see cref="SceneInputStateMachine"/>が
    /// 無視する。M0033でMove/Rotate/Scaleを常時同時表示にしたため、以前ここでIdle中のW/Eを
    /// ツール切替に横取りしていた分岐は撤廃した(W/E/Rはもう何もしない。Idle中のW/Eは元々
    /// Fly移動にも使われていなかったため観測できる退行は無い)。
    /// </summary>
    internal void HandleKeyDown(NavigationKey Key)=> this.InputMachine?.HandleKeyDown(Key);

    internal void HandleKeyUp(NavigationKey Key)=> this.InputMachine?.HandleKeyUp(Key);

    internal SceneTransformSpace CurrentTransformSpace {
        get=> this.ViewState?.TransformSpace??SceneEditorSettings.DefaultTransformSpace;
    }

    internal void SetTransformSpace(SceneTransformSpace Space){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.InputMachine is not { State:SceneNavState.Idle }|| this.ViewState is not { }State){
            return;
        }
        State.TransformSpace=Space;
        State.SaveToSettings();
        this.UpdateMoveGizmo();
    }

    // M0034: SurfaceSnapPlaneはGetToolHandles(Tool)のどの系統表にも属さない専用ハンドルなので、
    // disabled-handle集合の和集合(UpdateMoveGizmo参照)へ入れるためここへ直接追加する。
    private static readonly IReadOnlyList<GizmoHandleId>AllTransformHandles=[
        ..GetToolHandles(SceneActiveTool.Move),..GetToolHandles(SceneActiveTool.Rotate),..GetToolHandles(SceneActiveTool.Scale),
        GizmoHandleId.SurfaceSnapPlane,
    ];

    /// <summary>
    /// M0033: ヒットしたHandleからMove/Rotate/Scaleのうちどの系統かを逆引きする。M0034:
    /// SurfaceSnapPlaneは本質的に移動操作なのでMoveへ寄せる(MemberMask/ドラッグ経路はどのみち
    /// 専用分岐で扱うため、ここでのMove判定は主にCanEditTransformの既定フォールバック用)。
    /// </summary>
    private static SceneActiveTool GetHandleFamily(GizmoHandleId Handle)=>Handle switch {
        GizmoHandleId.AxisX or GizmoHandleId.AxisY or GizmoHandleId.AxisZ
            or GizmoHandleId.PlaneXY or GizmoHandleId.PlaneYZ or GizmoHandleId.PlaneZX
            or GizmoHandleId.SurfaceSnapPlane=>SceneActiveTool.Move,
        GizmoHandleId.RotateX or GizmoHandleId.RotateY or GizmoHandleId.RotateZ or GizmoHandleId.RotateView=>SceneActiveTool.Rotate,
        GizmoHandleId.ScaleX or GizmoHandleId.ScaleY or GizmoHandleId.ScaleZ or GizmoHandleId.ScaleUniform=>SceneActiveTool.Scale,
        _=>SceneActiveTool.Select,
    };

    private static IReadOnlyList<GizmoHandleId>GetToolHandles(SceneActiveTool Tool)=>Tool switch {
        SceneActiveTool.Move=>[GizmoHandleId.AxisX,GizmoHandleId.AxisY,GizmoHandleId.AxisZ,
            GizmoHandleId.PlaneXY,GizmoHandleId.PlaneYZ,GizmoHandleId.PlaneZX],
        SceneActiveTool.Rotate=>[GizmoHandleId.RotateX,GizmoHandleId.RotateY,GizmoHandleId.RotateZ,GizmoHandleId.RotateView],
        SceneActiveTool.Scale=>[GizmoHandleId.ScaleX,GizmoHandleId.ScaleY,GizmoHandleId.ScaleZ,GizmoHandleId.ScaleUniform],
        _=>Array.Empty<GizmoHandleId>(),
    };

    private static SceneTransformMemberMask GetTransformMembers(SceneActiveTool Tool)=>Tool switch {
        SceneActiveTool.Move=>SceneTransformMemberMask.Position,
        SceneActiveTool.Rotate=>SceneTransformMemberMask.Position|SceneTransformMemberMask.Rotation,
        SceneActiveTool.Scale=>SceneTransformMemberMask.All,
        _=>SceneTransformMemberMask.None,
    };

    private static string GetToolDisplayName(SceneActiveTool Tool)=>Tool switch {
        SceneActiveTool.Move=>"移動",
        SceneActiveTool.Rotate=>"回転",
        SceneActiveTool.Scale=>"スケール",
        _=>"Transform",
    };

    /// <summary>WPFの<c>LostKeyboardFocus</c>から呼ぶ。RMBは離されていないので状態遷移はしない。</summary>
    internal void ClearHeldFlyKeys()=> this.InputMachine?.HandleLostKeyboardFocus();

    internal void CancelCurrentOperation(){
        if(this.InputMachine is not { }Machine){
            return;
        }
        SceneInputStepResult Result=Machine.CancelCurrent();
        if(Result.Capture==SceneCaptureAction.End){
            this.Surface?.ReleasePointerCapture();
        }
        if(Result.GizmoCanceled|| this.EditTransaction.State==EditTransactionState.Dragging){
            this.CancelEditTransaction();
        }
        if(Result.MarqueeCanceled){
            this.ActiveMarqueeRectangle=null;
            this.RefreshOverlay();
        }

        this.PendingLookDeltaX=0f;
        this.PendingLookDeltaY=0f;
        this.LookUsesPixelLock=false;
        this.OrbitUsesPixelLock=false;
    }

    /// <summary>
    /// WPFのFキーから呼ぶ(M0020 Phase 2)。design doc §4.2「ツール切替キーはIdle時のみ有効」に
    /// 従いIdle以外では無視する。視線方向は変えず、選択ノード群の境界球へ再センタリングのみ行う
    /// (<see cref="SceneViewState.FocusOnBounds"/>)。
    /// </summary>
    internal void RequestFocusSelection(){
        if(this.InputMachine is not { } Machine||Machine.State!=SceneNavState.Idle){
            return;
        }

        if(this.ViewState is not { } State|| this.SelectedNodePaths.Count==0){
            return;
        }

        List<SceneRenderObject>Selected=[..this.CurrentRenderObjects.Where(Object=> this.SelectedNodePaths.Contains(Object.NodeId))];
        if(Selected.Count==0){
            return;
        }

        (Vector3 Center,float Radius)=SceneCameraFraming.ComputeSelectionBounds(Selected);
        State.FocusOnBounds(Center,Radius,this.AspectRatio);
    }

    /// <summary>(Re)builds <see cref="Renderer"/> from the pin 済み <see cref="SceneDocumentSnapshot"/>.</summary>
    private void Rebuild(){
        if(this.Surface is null|| this.TargetKey is null|| this.TargetSnapshot is null|| this.TargetFilePath is null|| this.TargetClassName is null){
            return;
        }

        if(this.TargetModel is not { } Model){
            // Not (yet) found - e.g. analysis hasn't caught up with a just-opened tool window, or
            // the class was renamed/deleted out from under us. Leave the last good frame on screen.
            this.SetStatus($"解析結果を待っています: {this.TargetClassName}");
            return;
        }

        if(Model.Root is null){
            this.DisposeRenderer();
            Microsoft.CodeAnalysis.Diagnostic?Diagnostic=Model.Diagnostics.FirstOrDefault();
            string Detail=Diagnostic is null
                ?"Rootを解析できませんでした。"
                :$"{Diagnostic.Id}: {Diagnostic.GetMessage()}";
            ActivityLog.LogError(nameof(SceneHwndHost),$"{this.TargetClassName}: {Detail}");
            this.SetStatus($"シーン解析エラー: {Detail}");
            return;
        }

        this.SetStatus($"読み込み中: {this.TargetClassName}");

        // 旧 .scene 実装の WarmMeshCache は、WPF の BuildWindowCore 内でJTF/Taskを同期wait
        // するデッドロックを避けるため、生ThreadへI/Oを隔離してからUI側でJoinしていた。
        // Scene DSL版ではさらに一歩進め、Primitive.Cli生成と後続のGLTF MeshLoaderを含む
        // Build全体をこの単一workerへ渡し、UIスレッドではJoin/Waitを一切行わない。完了後の
        // SceneRenderer生成だけをDispatcherへ戻す。live解析が連続してもworkerはlatest-winsで
        // 未処理要求を置換するため、ブリッジプロセスが無制限に増えることもない。
        this.BuildWorker?.Request(new SceneRenderBuildRequest(
                                                              Model, this.TargetKey.Value, this.TargetSnapshot.AnalysisGeneration, this.TargetFilePath, this.TargetClassName, this.Surface.Handle));
    }

    private SceneRenderBuildResult BuildRenderObjects(SceneRenderBuildRequest Request){
        this.RepoRoot??=RepoLocator.ResolveRepoRoot();
        if(this.RepoRoot is null){
            throw new InvalidOperationException($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
        }

        string RepoRoot=this.RepoRoot;
        IReadOnlyList<SceneRenderObject>RenderObjects=SceneModelRenderObjectBuilder.Build(
            Request.Model,
            EnumMemberName=>SceneModelRenderObjectBuilder.ResolveViaBridge(EnumMemberName,RepoRoot),
            AssetPath=>SceneModelRenderObjectBuilder.ResolveStaticMeshViaBridge(AssetPath,RepoRoot));
        return new SceneRenderBuildResult(Request,RenderObjects);
    }

    private void OnBuildCompleted(SceneRenderBuildRequest Request,SceneRenderBuildResult?Result,Exception?Error){
        if(this.Surface is null
            || this.Surface.Handle!=Request.SurfaceHandle
            || this.TargetKey is not { } Key
            ||!Key.Equals(Request.Key)
            || this.TargetSnapshot?.AnalysisGeneration!=Request.AnalysisGeneration){
            return;
        }

        if(Error is not null){
            ActivityLog.LogError(nameof(SceneHwndHost),Error.ToString());
            if(this.Renderer is null){
                this.Surface.SetVisible(false);
            }

            this.SetStatus($"表示に失敗しました: {Error.Message}");
            return;
        }

        if(Result is null){
            return;
        }

        // 描画オブジェクト0件でもレンダラーは作る(グリッド/ギズモだけのフレームは正当、
        // M0020 Phase 0/Phase 3)。ここでサーフェスを隠すと前フレームの残像や背面ペインの露出を
        // 招くだけで、0件でも RenderFrame は clear + Present を行う。
        try {
            this.DisposeRenderer();
            (this.SceneBoundsCenter,this.SceneBoundsRadius)=SceneCameraFraming.ComputeBounds(Result.RenderObjects);
            this.CurrentRenderObjects=Result.RenderObjects;

            Matrix4x4 View;
            Matrix4x4 Projection;
            if(this.ViewState is { } State){
                // Far平面はライブ編集での規模変化に追従させるため、リビルドのたびに無条件で更新する。
                State.LastKnownBoundsRadius=this.SceneBoundsRadius;
                if(this.PendingCameraReset){
                    // 初回ビルド成功時、またはコンボボックスでシーンが切り替わった直後
                    // (SetTargetがPendingCameraReset=trueを立てる、事前調査事実3)。
                    State.ResetToBounds(
                        this.SceneBoundsCenter,this.SceneBoundsRadius,
                        SceneCameraFraming.ViewDirection,SceneCameraFraming.DefaultFovYRadians,this.AspectRatio);
                    this.PendingCameraReset=false;
                }

                View=State.GetView();
                Projection=State.GetProjection(this.AspectRatio);
            } else {
                (View,Projection)=SceneCameraFraming.FrameBounds(
                    this.SceneBoundsCenter,this.SceneBoundsRadius,SceneCameraFraming.DefaultFovYRadians,this.AspectRatio);
            }

            // 実サイズは Surface.Resized で既に届いているので、800x600 のハードコードは使わない。
            this.Renderer=new SceneRenderer(this.Surface.Handle, this.SurfaceWidth, this.SurfaceHeight,Result.RenderObjects,View,Projection);
            this.RenderedAnalysisGeneration=Request.AnalysisGeneration;
            // 再解析でRendererを作り直しても選択の見た目を保つ。複数選択(SelectedNodePaths)も
            // 一緒に渡さないと、primary以外のハイライトだけが再ビルドで消える。
            this.Renderer.SetSelectedNode(this.SelectedNodePath,this.SelectedNodePaths);
            this.UpdateMoveGizmo();
            this.Surface.SetVisible(true);
            this.SetStatus($"表示中: {Result.Request.ClassName}");
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(SceneHwndHost),Ex.ToString());
            this.DisposeRenderer();
            this.SetStatus($"表示に失敗しました: {Ex.Message}");
        }
    }

    private void DisposeRenderer(){
        this.Renderer?.Dispose();
        this.Renderer=null;
        this.RenderedAnalysisGeneration=-1;
        this.CurrentRenderObjects=[];
        this.Surface?.SetVisible(false);
    }

    /// <summary>
    /// Tools&gt;Options(<see cref="SceneEditorOptionsPage"/>)で設定が変わったときに、**既に開いている**
    /// Scene View の生きた<see cref="SceneViewState"/>へ反映する。
    ///
    /// <para>
    /// 反映するのはオプション画面が公開している5項目だけ。カメラ姿勢や<c>ActiveTool</c>等の
    /// セッション内状態は触らない(ユーザーの現在の視点を設定変更で吹き飛ばさない)。
    /// </para>
    ///
    /// <para>
    /// これは見た目の即時反映のためだけでなく<b>正しさのために必須</b>である:
    /// <see cref="DestroyWindowCore"/>が<c>SaveToSettings</c>で9項目すべてを書き戻すので、
    /// ここで同期しておかないとビュー破棄時に古い値でオプション画面の変更を上書き消滅させてしまう。
    /// </para>
    /// </summary>
    private void OnSettingsChanged(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.ViewState is not { } State){
            return;
        }

        State.NavigationSpeedMultiplier=SceneEditorSettings.ReadNavigationSpeedMultiplier();
        State.InvertLookX=SceneEditorSettings.ReadInvertLookX();
        State.InvertLookY=SceneEditorSettings.ReadInvertLookY();
        State.InvertOrbitX=SceneEditorSettings.ReadInvertOrbitX();
        State.InvertOrbitY=SceneEditorSettings.ReadInvertOrbitY();
    }

    private void DetachSubscriptions(){
        if(this.RenderingHandler is not null){
            CompositionTarget.Rendering-= this.RenderingHandler;
            this.RenderingHandler=null;
        }

        if(this.ResizeHandler is not null&& this.Surface is not null){
            this.Surface.Resized-= this.ResizeHandler;
            this.ResizeHandler=null;
        }
    }

    private void SetStatus(string Status)=> this.StatusChanged?.Invoke(Status);

    private sealed record SceneRenderBuildRequest(
        SceneModel Model,
        SceneKey Key,
        long AnalysisGeneration,
        string FilePath,
        string ClassName,
        IntPtr SurfaceHandle);

    private sealed record SceneRenderBuildResult(
        SceneRenderBuildRequest Request,
        IReadOnlyList<SceneRenderObject>RenderObjects);

    /// <summary>M0036: <see cref="RecentTransformCommits"/>/<see cref="RecentTransformUndone"/>の1件分。</summary>
    private readonly struct TransformUndoRecord(
        IReadOnlyList<SceneTransformPreview>RootsBefore,IReadOnlyList<SceneTransformPreview>RootsAfter,
        IReadOnlyList<SceneTransformPreview>Descendants,Matrix4x4 WorldDelta){
        internal IReadOnlyList<SceneTransformPreview>RootsBefore { get; }=RootsBefore;
        internal IReadOnlyList<SceneTransformPreview>RootsAfter { get; }=RootsAfter;
        internal IReadOnlyList<SceneTransformPreview>Descendants { get; }=Descendants;
        internal Matrix4x4 WorldDelta { get; }=WorldDelta;
    }
}
