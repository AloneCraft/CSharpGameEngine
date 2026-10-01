using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.IO.Pipes;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene Designer tool window host (ADR-0040 Phase 9). Targets a specific <c>IScene</c>-implementing class
/// via <see cref="SetTarget"/>, which receives the <see cref="SceneDocumentSnapshot"/> that
/// <see cref="SceneEditorSessionService"/> publishes from live Roslyn (no build step), converts it via
/// <see cref="SceneModelRenderObjectBuilder"/> and sends the resulting immutable scene/frame DTOs to
/// the out-of-process Preview Host. Replaces the earlier <c>.scene</c>-JSON-based version of this class
/// (ADR-0020/ADR-0021), whose remaining write path was deleted in M0020 Phase 0.
///
/// <para>
/// This host owns the editing view state that must survive Preview Host session replacement. Camera and
/// authoring state live here and are replayed to a fresh host/surface generation (ADR-0041 decision 1). It is also
/// the only place that learns the real surface size, via <c>Surface.Resized</c>.
/// </para>
/// </summary>
internal sealed class SceneHwndHost:HwndHost {
    private string?RepoRoot;
    private string?TargetFilePath;
    private string?TargetClassName;
    private string?SpatialDebugRuntimeProcessName;
    private SceneModel?TargetModel;
    private SceneKey?TargetKey;
    private SceneDocumentSnapshot?TargetSnapshot;
    private long RenderedAnalysisGeneration=-1;
    private NodePath?SelectedNodePath;
    private RenderSurfaceHostWindow?SurfaceHost;
    private RenderPreviewHostClient?PreviewClient;
    private LatestOnlyThreadWorker<SceneRenderBuildRequest,SceneRenderBuildResult>?BuildWorker;
    private readonly SceneRenderBuildLifecycle BuildLifecycle=new();
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
    private IReadOnlyList<SceneRenderObject>CurrentPreviewRenderObjects=[];
    private IReadOnlyList<NodePath>SelectedNodePaths=[];
    private IReadOnlyList<NodePath>PreviewSelectedNodePaths=[];
    private IReadOnlyList<GizmoOverlayPrimitive>CurrentOverlayPrimitives=[];
    private IReadOnlyList<RenderPreviewCameraPass>CurrentCameraPreviewPasses=[];
    private Matrix4x4 CurrentView=Matrix4x4.Identity;
    private Matrix4x4 CurrentProjection=Matrix4x4.Identity;
    private Vector3 CurrentCameraPosition;
    private bool PreviewFrameDirty;
    private bool HostCrashRecoveryUsed;
    private SceneSelection?CurrentSelection;
    private bool CameraPreviewEnabled=true;
    private IReadOnlyList<SceneCameraPreviewLayoutItem>CameraPreviewLayout=[];
    private IReadOnlyList<GizmoOverlayPrimitive>CurrentSpatialDebugOverlay=[];
    private SceneSpatialDebugSnapshot?CurrentSpatialDebugSnapshot;
    private IReadOnlyList<GizmoOverlayPrimitive>CurrentPhysicsDebugOverlay=[];
    private ScenePhysicsDebugSnapshot?CurrentPhysicsDebugSnapshot;
    private IReadOnlyList<ScenePhysicsFractureBond>CurrentPhysicsFractureBonds=[];
    private CancellationTokenSource?PhysicsFractureCancellation;
    private int PhysicsDebugRequestInFlight;
    private bool PhysicsDebugEnabled;
    private PhysicsDebugOverlayOptions PhysicsDebugOptions=new(Bounds:true,Velocity:false,CenterOfMass:false,Sleep:true,Contacts:false,Tree:false,Bonds:false);
    private string LastPhysicsDebugStatus="";
    private DispatcherTimer?SpatialDebugTimer;
    private int SpatialDebugRequestInFlight;
    private bool GlobalSpatialOctreeEnabled;
    private string LastSpatialDebugStatus="";
    private IReadOnlyList<int>SpatialDebugProcessIds=[];
    private int?SelectedSpatialDebugProcessId;
    private Process?EditorPreviewProcess;
    private string?EditorPreviewSceneName;
    private string?EditorPreviewTempDirectory;
    /// <summary>The game build the editor world runs a copy of, and its write time when it started.</summary>
    private string?EditorPreviewBuild;
    private DateTime EditorPreviewBuildTime;
    private bool PreviewIsRuntime;
    /// <summary>M0095 3-4: the runtime view open in flight off the UI thread; a newer request or a disposal bumps the generation.</summary>
    private long RuntimeOpenGeneration;
    private bool RuntimeOpenPending;
    /// <summary>Held so the open stays observable and joinable by Visual Studio's shutdown.</summary>
    private Microsoft.VisualStudio.Threading.JoinableTask?RuntimeOpenTask;
    // The game build a JSON scene's runtime view is made from, and the write time of the one shown
    // (MinValue when the scene preview stands in for it): a newer build replaces the view.
    private string?WatchedBuild;
    private DateTime ShownBuildTime;
    private DateTime PendingBuildTime;
    private DateTime NextBuildCheck;
    private string?RuntimeViewTempDirectory;
    // Why the Scene view is not showing the current edit in the game's runtime, kept over "displayed".
    private string?RuntimeViewNote;
    /// <summary>The scene JSON the runtime view last received; the document text it must keep matching.</summary>
    private string?RuntimeViewSentSceneJson;

    // M0020 Phase 1: ナビゲーション状態機械とカメラ/永続ツール状態(ADR-0041 決定事項1)。
    private SceneViewState?ViewState;
    private SceneInputStateMachine?InputMachine;
    private bool PendingCameraReset=true;
    private float PendingLookDeltaX;
    private float PendingLookDeltaY;
    private TimeSpan?LastFlyTickTime;

    // M0020追加: ピクセル完全一致ドラッグ(反転設定OFFの間だけ有効)。アンカーはRMB Lookの
    // Down時に捕捉し、Up/CaptureLostまで保持する。SceneInputStateMachineはVector3/世界座標を
    // 知らない純粋な状態機械のままにするため、この管理はSceneHwndHostに閉じる。
    private bool LookUsesPixelLock;
    private Vector3 LookAnchorWorldDirection;
    /// <summary>M0095: the selection's center captured when an Alt+LMB orbit begins; null orbits around the focus.</summary>
    private Vector3?OrbitPivot;

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

    // M0034: Shift+Ctrl押下中(マウス静止中でも1フレーム以内に四角を出すため、Renderingティックで
    // GetKeyStateポーリングする。既存のFly Fast/Precisionポーリングと同じ方式)。
    private bool SurfaceSnapArmed;
    private bool ActiveSurfaceSnapDrag;
    private(Vector3 Position,Vector3 Normal)?CurrentSurfaceSnapHit;

    // M0020後続Task C: 空間から開始したLMBドラッグだけが保持するスクリーン矩形。
    private ScreenRectangle?ActiveMarqueeRectangle;

    private float AspectRatio=> this.SurfaceWidth/(float)this.SurfaceHeight;
    private RenderSurfaceWindow?Surface=>this.SurfaceHost?.Surface;

    internal event Action<string>?StatusChanged;
    internal event Action<string>?SpatialDebugStatusChanged;
    internal event Action<IReadOnlyList<int>,int?>?SpatialDebugProcessCandidatesChanged;

    internal RenderPreviewDebugSnapshot CaptureRuntimeDebugSnapshot(){
        RenderPreviewHostClient Client=this.PreviewClient
            ??throw new InvalidOperationException(
                VsixText.Get("RuntimeDebugPreviewUnavailable"));
        try {
            return Client.GetDebugSnapshot();
        } catch(RenderPreviewHostException Error)when(!Error.SessionTerminated){
            throw;
        } catch(Exception Error){
            this.HandleRenderFailure(Error);
            throw;
        }
    }

    /// <summary>Pointerの<c>Down</c>で発火。WPF側は購読して<c>Keyboard.Focus</c>をここでだけ呼ぶ(ADR-0041 決定事項2)。</summary>
    internal event Action?ViewportActivated;

    /// <summary>
    /// LMBクリックでの選択確定(M0020 Phase 2)。ヒットNodePath(空間クリックならnull)とCtrl押下を運ぶ。
    /// <see cref="SceneEditorSessionService.Selection"/>への変換・発行はWPF側(<see cref="SceneHwndHost"/>は
    /// セッションを知らない)。
    /// </summary>
    internal event Action<NodePath?,bool>?ObjectPicked;

    internal event Action<NodePath>?CameraPreviewSelectionRequested;

    /// <summary>
    /// 空間ドラッグで確定した複数NodePathとDown時のCtrl状態。セッション選択への変換は
    /// 単体クリックと同じくWPF境界が担う。
    /// </summary>
    internal event Action<IReadOnlyList<NodePath>,bool>?ObjectsMarqueePicked;

    /// <summary>MouseUpで確定したsource編集要求。WPF/session境界がlive bufferへ1 Undoで反映する。</summary>
    internal event Action<SceneEditCommitRequest>?EditCommitRequested;


    /// <summary>True while the current target owns an asynchronous scene-build request.</summary>
    internal bool IsBuildPending=>this.BuildLifecycle.IsBuildPending;

    /// <summary>True only when the current target has an unrecoverable build/present failure.</summary>
    internal bool HasFatalRenderFailure { get; private set; }

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
        this.SurfaceHost=new RenderSurfaceHostWindow(
            HwndParent.Handle,800,600,InitiallyVisible:false);
        RenderSurfaceWindow Surface=this.SurfaceHost.Surface;
        this.ViewState=SceneViewState.CreateFromSettings();
        this.InputMachine=new SceneInputStateMachine();
        SceneEditorSettings.SettingsChanged+=this.OnSettingsChanged;
        Surface.PointerInput+= this.OnPointerInput;
        Surface.WheelInput+= this.HandleWheel;
        SynchronizationContext UiContext=SynchronizationContext.Current
            ??new DispatcherSynchronizationContext(this.Dispatcher);
#pragma warning disable VSTHRD001 // 生workerの完了を非同期Postするだけで、同期wait/message pumpは行わない。
        this.BuildWorker=new LatestOnlyThreadWorker<SceneRenderBuildRequest,SceneRenderBuildResult>(
                                                                                                 Work: this.BuildRenderObjects,
                                                                                                 Dispatch:Callback=>UiContext.Post(_=>Callback(),null),
                                                                                                 Completed:this.OnBuildCompleted);
#pragma warning restore VSTHRD001
        this.LoadIntoSurface();
        this.GlobalSpatialOctreeEnabled=this.ViewState?.ShowGlobalSpatialOctree??false;
        this.SpatialDebugTimer=new DispatcherTimer(DispatcherPriority.Background){ Interval=TimeSpan.FromMilliseconds(66) };
        this.SpatialDebugTimer.Tick+=this.PollSpatialDebug;
        this.SpatialDebugTimer.Tick+=this.PollPhysicsDebug;
        this.SpatialDebugTimer.Start();
        return new HandleRef(this, this.SurfaceHost.Handle);
    }

    protected override void DestroyWindowCore(HandleRef Hwnd){
        ThreadHelper.ThrowIfNotOnUIThread();
        ActivityLog.LogInformation(
            nameof(SceneHwndHost),"Scene HWND teardown started.");

        this.BuildLifecycle.Invalidate();
        if(this.SpatialDebugTimer is not null){
            this.SpatialDebugTimer.Stop();
            this.SpatialDebugTimer.Tick-=this.PollSpatialDebug;
            this.SpatialDebugTimer.Tick-=this.PollPhysicsDebug;
            this.SpatialDebugTimer=null;
        }
        this.BuildWorker?.Dispose();
        this.BuildWorker=null;
        this.PhysicsFractureCancellation?.Cancel();
        this.PhysicsFractureCancellation?.Dispose();
        this.PhysicsFractureCancellation=null;
        this.StopEditorPreviewHost(WaitForExit:false);
        this.CancelEditTransaction();
        SceneEditorSettings.SettingsChanged-=this.OnSettingsChanged;
        this.DetachSubscriptions();
        this.DisposePreviewClient(ClearScene:true,DisposeInBackground:true);
        ActivityLog.LogInformation(
            nameof(SceneHwndHost),
            "Preview Host cleanup transferred off the Visual Studio UI thread.");

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

        this.SurfaceHost?.Dispose();
        this.SurfaceHost=null;
        ActivityLog.LogInformation(
            nameof(SceneHwndHost),"Scene HWND teardown completed.");
    }

    /// <summary>
    /// Session が pin した snapshot を描画対象にします。解析中の blocking error は Last Known Good
    /// projection を保持するので、ここでrendererを空へ戻しません。
    /// </summary>
    internal void SetTarget(SceneDocumentSnapshot Snapshot)=>this.SetTarget(Snapshot,this.SpatialDebugRuntimeProcessName);

    internal void SetTarget(SceneDocumentSnapshot Snapshot,string?RuntimeProcessName){
        if(!string.Equals(this.SpatialDebugRuntimeProcessName,RuntimeProcessName,StringComparison.Ordinal)){
            this.SpatialDebugRuntimeProcessName=RuntimeProcessName;
            this.CurrentSpatialDebugSnapshot=null;
            this.CurrentSpatialDebugOverlay=[];
            this.SpatialDebugProcessIds=[];
            this.SelectedSpatialDebugProcessId=null;
            this.SpatialDebugProcessCandidatesChanged?.Invoke([],null);
            this.SetSpatialDebugStatus(VsixText.Get("SceneSpatialWaiting"));
        }
        if(this.EditTransaction.State!=EditTransactionState.Idle){
            if(this.TargetKey is { }EditingKey&&EditingKey.Equals(Snapshot.Key)){
                this.EditTransaction.QueueSnapshot(Snapshot);
                return;
            }

            this.CancelEditTransaction();
        }

        // A queued worker result from the previous snapshot must never clear the pending state or
        // recreate a renderer after this target is accepted. The new Rebuild below receives a
        // distinct lease; an incremental update deliberately leaves no build pending at all.
        this.BuildLifecycle.Invalidate();

        // コンボボックスでのシーン切替はSceneHwndHostを再生成せず(EnsureHostが既存インスタンスを
        // 返すだけ)、このSetTargetの呼び直しだけが届く。TargetKeyが変わったことをここで検知し、
        // 次のOnBuildCompletedで新シーンへ再フレーミングする(ADR-0041、事前調査事実3)。
        SceneDocumentSnapshot?PreviousSnapshot=this.TargetSnapshot;
        bool SameScene=this.TargetKey is { }PreviousKey&&PreviousKey.Equals(Snapshot.Key);
        if(!SameScene){
            this.PendingCameraReset=true;
        }

        this.TargetKey=Snapshot.Key;
        this.TargetSnapshot=Snapshot;
        SceneModel?Model=Snapshot.LastKnownGoodModel??Snapshot.CurrentModel;
        if(Model is not null){
            this.TargetFilePath=Model.FilePath;
            this.TargetClassName=Model.ClassName;
            this.TargetModel=Model;
            this.QueuePhysicsFractureRefresh();
        }

        if(this.Surface is not null
            &&SameScene
            &&PreviousSnapshot is not null
            && this.PreviewClient is not null
            && this.RenderedAnalysisGeneration==PreviousSnapshot.AnalysisGeneration
            && this.TryApplyIncrementalSnapshot(Snapshot,Model)){
            return;
        }

        this.EnsureEditorPreviewHost();

        if(this.Surface is not null){
            this.Rebuild();
        }
    }

    private bool TryApplyIncrementalSnapshot(SceneDocumentSnapshot Snapshot,SceneModel? Model){
        SceneRenderUpdateKind UpdateKind=SceneIncrementalUpdatePolicy.Classify(Snapshot.Diff);
        if(UpdateKind==SceneRenderUpdateKind.Rebuild){ return false; }
        if(UpdateKind==SceneRenderUpdateKind.None){
            // The model diff only sees what the designer models; the runtime view draws the whole document.
            if(this.PreviewIsRuntime&&this.RuntimeViewSentSceneJson is { }Sent
                &&!string.Equals(Sent,this.LiveSceneJson(),StringComparison.Ordinal)
                &&!this.TryReplacePreviewScene()){
                return false;
            }
            this.RenderedAnalysisGeneration=Snapshot.AnalysisGeneration;
            this.RefreshCameraPreviews();
            this.UpdateMoveGizmo();
            return true;
        }
        if(Model is null|| this.PreviewClient is null
            ||!SceneRenderIncrementalUpdater.TryUpdateTransforms(
                Model, this.CurrentRenderObjects,out IReadOnlyList<SceneRenderObject>Updated)){
            return false;
        }

        this.CurrentRenderObjects=Updated;
        this.PreviewFrameDirty=true;
        // A runtime view draws the game's own world, not these render objects: a value edit reaches it
        // only as the edited document (ADR-0070 B1 step 4), so send it as the full path does.
        if(this.PreviewIsRuntime&&!this.TryReplacePreviewScene()){
            return false;
        }
        (this.SceneBoundsCenter,this.SceneBoundsRadius)=SceneCameraFraming.ComputeBounds(Updated);
        if(this.ViewState is { }State){
            State.LastKnownBoundsRadius=this.SceneBoundsRadius;
        }

        this.RenderedAnalysisGeneration=Snapshot.AnalysisGeneration;
        this.RefreshCameraPreviews();
        this.UpdateMoveGizmo();
        this.Surface?.SetVisible(true);
        this.SetStatus(VsixText.Format("SceneStatusDisplayed",Model.ClassName));
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
        this.PreviewSelectedNodePaths=[];
        this.PreviewFrameDirty=true;
        this.RefreshCameraPreviews();
        this.UpdateMoveGizmo();
    }

    internal void SetCameraPreviewEnabled(bool Enabled){
        if(this.CameraPreviewEnabled==Enabled){
            return;
        }

        this.CameraPreviewEnabled=Enabled;
        this.RefreshCameraPreviews();
        this.RefreshOverlay();
    }

    internal void SetRenderMode(SceneViewRenderMode Mode){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.ViewState is not { }State){
            return;
        }
        State.RenderMode=Mode;
        SceneEditorSettings.WriteRenderMode(Mode);
        this.PreviewFrameDirty=true;
    }

    internal void SetGlobalSpatialOctreeEnabled(bool Enabled){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.GlobalSpatialOctreeEnabled=Enabled;
        if(this.ViewState is { }State){
            State.ShowGlobalSpatialOctree=Enabled;
            SceneEditorSettings.WriteShowGlobalSpatialOctree(Enabled);
        }
        if(!Enabled){
            this.CurrentSpatialDebugSnapshot=null;
            this.CurrentSpatialDebugOverlay=[];
            this.SetSpatialDebugStatus(VsixText.Get("SceneSpatialOff"));
        } else {
            this.SetSpatialDebugStatus(VsixText.Get("SceneSpatialWaiting"));
        }
        this.RefreshOverlay();
    }

    internal void SetPhysicsDebugEnabled(bool Enabled){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.PhysicsDebugEnabled=Enabled;
        if(!Enabled){
            this.CurrentPhysicsDebugSnapshot=null;this.CurrentPhysicsDebugOverlay=[];
            this.CurrentPhysicsFractureBonds=[];
            this.PhysicsFractureCancellation?.Cancel();
        } else { this.QueuePhysicsFractureRefresh(); }
        this.RefreshOverlay();
    }

    internal void SetPhysicsDebugOptions(in PhysicsDebugOverlayOptions Options){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.PhysicsDebugOptions=Options;
        this.QueuePhysicsFractureRefresh();
        this.RefreshOverlay();
    }

    private void QueuePhysicsFractureRefresh(){
        this.PhysicsFractureCancellation?.Cancel();
        this.PhysicsFractureCancellation=null;
        this.CurrentPhysicsFractureBonds=[];
        if(!this.PhysicsDebugEnabled||!this.PhysicsDebugOptions.Bonds||this.TargetModel is not { }Model){ return; }
        string?Root=this.RepoRoot??CgeProjectContext.Resolve(this.TargetFilePath)?.WorkspaceRoot;
        if(Root is null){ this.SetStatus(VsixText.Get("Common_GameProjectNotFound"));return; }
        this.RepoRoot=Root;
        CancellationTokenSource Cancellation=new();
        this.PhysicsFractureCancellation=Cancellation;
        this.LoadPhysicsFractureDebug(Model,Root,Cancellation);
    }

#pragma warning disable VSTHRD100,VSTHRD109
    private async void LoadPhysicsFractureDebug(
        SceneModel Model,string Root,CancellationTokenSource Cancellation){
        try {
            PrefractureDebugLoadResult Result=await Task.Run(
                ()=>PrefractureDebugService.Load(Model,Root,Cancellation.Token));
            if(!ReferenceEquals(this.PhysicsFractureCancellation,Cancellation)){ return; }
            this.PhysicsFractureCancellation=null;
            this.CurrentPhysicsFractureBonds=Result.Bonds;
            if(Result.Diagnostic is { Length:>0 }Diagnostic){ this.SetStatus(Diagnostic); }
            this.RefreshOverlay();
        }
        catch(OperationCanceledException){ }
        catch(Exception Exception){
            ActivityLog.LogError(nameof(SceneHwndHost),Exception.ToString());
            if(ReferenceEquals(this.PhysicsFractureCancellation,Cancellation)){
                this.PhysicsFractureCancellation=null;
                this.CurrentPhysicsFractureBonds=[];
                this.SetStatus(Exception.Message);
                this.RefreshOverlay();
            }
        }
        finally { Cancellation.Dispose(); }
    }
#pragma warning restore VSTHRD100,VSTHRD109

#pragma warning disable VSTHRD100,VSTHRD109
    internal async void RequestPhysicsControl(string Command){
        ThreadHelper.ThrowIfNotOnUIThread();
        int? ProcessId=this.ResolveDebugProcessId(out _);
        if(ProcessId is null){ this.SetStatus(DebugStatusText.PhysicsProcessUnavailable());return; }
        Guid? WorldId=this.CurrentPhysicsDebugSnapshot?.WorldId;
        DebugClientStatus Status=DebugClientStatus.TransportFailure(DebugStatusReason.Disconnected);
        await Task.Run(()=>PhysicsDebugClient.TryControl(ProcessId.Value,Command,WorldId,out Status));
        this.SetStatus(DebugStatusText.Physics(Status));
    }
#pragma warning restore VSTHRD100,VSTHRD109
    internal void SetSpatialDebugProcessId(int?ProcessId){
        ThreadHelper.ThrowIfNotOnUIThread();
        int?ResolvedProcessId=ProcessId is { }Requested&&this.SpatialDebugProcessIds.Contains(Requested)?Requested:null;
        if(this.SelectedSpatialDebugProcessId==ResolvedProcessId){
            return;
        }
        this.SelectedSpatialDebugProcessId=ResolvedProcessId;
        this.CurrentSpatialDebugSnapshot=null;
        this.CurrentSpatialDebugOverlay=[];
        this.SpatialDebugProcessCandidatesChanged?.Invoke(this.SpatialDebugProcessIds,this.SelectedSpatialDebugProcessId);
        this.RefreshOverlay();
    }

    private void EnsureEditorPreviewHost(){
        var sceneName=this.TargetModel?.ClassFullyQualifiedName;
        if(string.IsNullOrWhiteSpace(sceneName)){
            return;
        }
        if(this.EditorPreviewProcess is { HasExited:false }&&string.Equals(this.EditorPreviewSceneName,sceneName,StringComparison.Ordinal)){
            // M0095 3-6: the editor world runs a copy of the game build, so a new build restarts it;
            // the old process is stopped off the UI thread.
            if(this.EditorPreviewBuild is not { }Build||File.GetLastWriteTimeUtc(Build)==this.EditorPreviewBuildTime){
                return;
            }
            ActivityLog.LogInformation(nameof(SceneHwndHost),$"The game build changed; restarting the editor world (pid {this.EditorPreviewProcess.Id}).");
            this.StopEditorPreviewHost(WaitForExit:false);
        }
        this.StopEditorPreviewHost();
        CgeProjectContext? context;
        try{ context=CgeProjectContext.Resolve(this.TargetFilePath); }catch{ context=null; }
        if(context is null){
            return;
        }
        string root=this.RepoRoot??context.WorkspaceRoot;
        string? assemblyPath=this.ResolveBuiltGameAssembly(context);
        string hostPath;
        try { hostPath=RenderPreviewSceneBuilder.ResolveHostPath(root); }
        catch(FileNotFoundException){ return; }
        if(assemblyPath is null||!File.Exists(assemblyPath)){
            return;
        }
        try{
            this.EditorPreviewBuild=assemblyPath;
            this.EditorPreviewBuildTime=File.GetLastWriteTimeUtc(assemblyPath);
            assemblyPath=CopyGameBuild(assemblyPath,out string TempDirectory);
            this.EditorPreviewTempDirectory=TempDirectory;
            ProcessStartInfo StartInfo=PreviewHostProcessStartInfo.Create(
                hostPath,
                $"--assembly {PreviewHostProcessStartInfo.Quote(assemblyPath)} "
                +$"--scene {PreviewHostProcessStartInfo.Quote(sceneName!)}",
                Path.GetDirectoryName(assemblyPath)??root);
            this.EditorPreviewProcess=Process.Start(StartInfo);
            this.EditorPreviewSceneName=sceneName;
            ActivityLog.LogInformation(nameof(SceneHwndHost),$"Editor world of '{sceneName}' started (pid {this.EditorPreviewProcess?.Id}).");
        }catch(Exception Ex) when(Ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception){
            this.EditorPreviewProcess=null;
            this.EditorPreviewSceneName=null;
        }
    }

    /// <summary>The built game assembly of the target scene's project (Debug), or the override in CGE_EDITOR_PREVIEW_ASSEMBLY.</summary>
    private string?ResolveBuiltGameAssembly(CgeProjectContext context){
        string? assemblyPath=Environment.GetEnvironmentVariable("CGE_EDITOR_PREVIEW_ASSEMBLY");
        if(assemblyPath is null){
            string assemblyName=Path.GetFileNameWithoutExtension(this.SpatialDebugRuntimeProcessName??context.AssemblyName);
            string? projectDirectory=Path.GetDirectoryName(this.TargetFilePath??string.Empty);
            while(projectDirectory is not null&&!Directory.GetFiles(projectDirectory,"*.csproj").Any()){
                projectDirectory=Directory.GetParent(projectDirectory)?.FullName;
            }
            if(projectDirectory is not null){
                string debugDirectory=Path.Combine(projectDirectory,"bin","Debug");
                if(Directory.Exists(debugDirectory)){
                    assemblyPath=Directory.GetFiles(debugDirectory,$"{assemblyName}.dll",SearchOption.AllDirectories)
                        .FirstOrDefault(static PathValue=>!PathValue.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",StringComparison.OrdinalIgnoreCase));
                }
            }
        }
        return assemblyPath??context.OutputAssemblyPath("Debug");
    }

    /// <summary>
    /// Copies a game build's assemblies to a temporary folder and returns the copied assembly, so the
    /// preview host never locks the build output a rebuild writes.
    /// </summary>
    private static string CopyGameBuild(string AssemblyPath,out string TempDirectory){
        string SourceDirectory=Path.GetDirectoryName(AssemblyPath)??throw new DirectoryNotFoundException();
        TempDirectory=Path.Combine(Path.GetTempPath(),"CSharpGameEngine.EditorPreview",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);
        foreach(string FilePath in Directory.GetFiles(SourceDirectory)){
            string Extension=Path.GetExtension(FilePath);
            if(!string.Equals(Extension,".dll",StringComparison.OrdinalIgnoreCase)
                &&!string.Equals(Extension,".json",StringComparison.OrdinalIgnoreCase)
                &&!string.Equals(Extension,".pdb",StringComparison.OrdinalIgnoreCase)){
                continue;
            }
            File.Copy(FilePath,Path.Combine(TempDirectory,Path.GetFileName(FilePath)),overwrite:true);
        }
        return Path.Combine(TempDirectory,Path.GetFileName(AssemblyPath));
    }

    /// <summary>
    /// ADR-0070 B1: a runtime view of the built game for a JSON scene, so the Scene view shows what
    /// the game renders. Null (the scene preview) when the scene is not JSON or the game is not built.
    /// </summary>
    private RuntimeViewPlan?TryPrepareRuntimeView(){
        this.WatchedBuild=null;
        this.ShownBuildTime=DateTime.MinValue;
        if(!SceneJsonCommitPlanner.IsJsonDocument(this.TargetFilePath)
            ||this.TargetModel?.ClassFullyQualifiedName is not { Length:>0 }SceneName){
            return null;
        }
        CgeProjectContext? Context;
        try{ Context=CgeProjectContext.Resolve(this.TargetFilePath); }catch{ Context=null; }
        if(Context is null||this.ResolveBuiltGameAssembly(Context) is not { }Build){
            return null;
        }
        this.WatchedBuild=Build;
        if(!File.Exists(Build)){
            return null;
        }
        this.ShownBuildTime=File.GetLastWriteTimeUtc(Build);
        return new RuntimeViewPlan(Build,SceneName,
            string.Equals(Environment.GetEnvironmentVariable("CGE_GRAPHICS_ADAPTER"),"warp",StringComparison.OrdinalIgnoreCase));
    }

    private sealed record RuntimeViewPlan(string Build,string SceneName,bool UseWarp);

    /// <summary>The outcome of an off-thread runtime view open; exactly one of Client and Error is set.</summary>
    private sealed record RuntimeViewOpening(RenderPreviewHostClient?Client,string?TempDirectory,RenderPreviewHostResponse?Response,Exception?Error,bool BuildStillWriting);

    /// <summary>
    /// M0095 3-4: copies the game build, starts the preview host and opens the runtime view off the UI
    /// thread; together they took 0.6 to 1.9 s and froze Visual Studio on every scene open and game build.
    /// The view shows the pending state meanwhile, and <see cref="CompleteRuntimeViewOpen"/> takes the
    /// result unless a newer request or a disposal superseded it.
    /// </summary>
    private void BeginRuntimeViewOpen(RuntimeViewPlan Plan,long SurfaceHandle,string HostPath,string ShaderArtifactPath,RenderPreviewScene Scene){
        long Generation=++this.RuntimeOpenGeneration;
        this.RuntimeOpenPending=true;
        int Width=this.SurfaceWidth;
        int Height=this.SurfaceHeight;
        string SceneJson=this.LiveSceneJson();
        this.RuntimeViewSentSceneJson=SceneJson;
        this.RuntimeOpenTask=ThreadHelper.JoinableTaskFactory.RunAsync(async ()=>{
            RuntimeViewOpening Opening=await Task.Run(()=>OpenRuntimeView(Plan,SurfaceHandle,Width,Height,HostPath,ShaderArtifactPath,Scene,SceneJson));
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            this.CompleteRuntimeViewOpen(Generation,Opening);
        });
    }

    private static RuntimeViewOpening OpenRuntimeView(
        RuntimeViewPlan Plan,long SurfaceHandle,int Width,int Height,string HostPath,string ShaderArtifactPath,RenderPreviewScene Scene,string SceneJson){
        string?TempDirectory=null;
        string Copy;
        try{
            Copy=CopyGameBuild(Plan.Build,out string CopyDirectory);
            TempDirectory=CopyDirectory;
        }catch(Exception Error)when(Error is IOException or UnauthorizedAccessException){
            // A build still writing its output: the scene preview until the build settles.
            DeleteEditorPreviewTempDirectory(TempDirectory);
            return new RuntimeViewOpening(null,null,null,Error,BuildStillWriting:true);
        }
        RenderPreviewHostClient?Client=null;
        try {
            var Clock=Stopwatch.StartNew();
            Client=RenderPreviewHostClient.Start(HostPath,TimeSpan.FromSeconds(120));
            RenderPreviewHostResponse Opened=Client.Open(SurfaceHandle,Width,Height,ShaderArtifactPath,Scene,
                new RenderPreviewRuntimeView {
                    AssemblyPath=Copy,ContentDirectory=Path.GetDirectoryName(Plan.Build)!,SceneName=Plan.SceneName,UseWarp=Plan.UseWarp,
                },SceneJson);
            ActivityLog.LogInformation(nameof(SceneHwndHost),$"Runtime view of '{Plan.SceneName}' opened on '{Plan.Build}' in {Clock.ElapsedMilliseconds} ms, off the UI thread.");
            return new RuntimeViewOpening(Client,TempDirectory,Opened,null,BuildStillWriting:false);
        } catch(Exception Error)when(Error is RenderPreviewHostException or IOException or InvalidOperationException or TimeoutException){
            Client?.Dispose();
            DeleteEditorPreviewTempDirectory(TempDirectory);
            return new RuntimeViewOpening(null,null,null,Error,BuildStillWriting:false);
        }
    }

    /// <summary>Runs on the UI thread (<see cref="BeginRuntimeViewOpen"/> switches to it first).</summary>
    private void CompleteRuntimeViewOpen(long Generation,RuntimeViewOpening Opening){
        if(Generation!=this.RuntimeOpenGeneration||this.Surface is null||this.PreviewClient is not null){
            // Superseded by a newer open or a disposal: the result is nobody's.
            if(Opening.Client is { }Stale){
                string?StaleTemp=Opening.TempDirectory;
                _=ThreadPool.QueueUserWorkItem(_=>{
                    Stale.Dispose();
                    DeleteEditorPreviewTempDirectory(StaleTemp);
                });
            }
            return;
        }
        this.RuntimeOpenPending=false;
        if(Opening.Client is not { }Client){
            if(Opening.BuildStillWriting){
                this.ShownBuildTime=DateTime.MinValue;
            } else {
                ActivityLog.LogWarning(nameof(SceneHwndHost),Opening.Error?.ToString()??"The runtime view did not open.");
                this.ShowRuntimeViewNote(VsixText.Format("SceneRuntimeViewUnavailable",Opening.Error?.Message??string.Empty));
            }
            try {
                this.OpenScenePreview();
            } catch(Exception Error){
                this.HandleRenderFailure(Error);
                return;
            }
        } else {
            this.PreviewClient=Client;
            this.PreviewIsRuntime=true;
            this.RuntimeViewTempDirectory=Opening.TempDirectory;
            this.ShowRuntimeViewState(Opening.Response!);
        }
        // Catch up with what changed while the view opened: the scene, the camera and the document.
        if(!this.TryReplacePreviewScene()){
            return;
        }
        this.RefreshCameraPreviews();
        this.UpdateMoveGizmo();
        this.SetStatus(this.RuntimeViewNote??VsixText.Format("SceneStatusDisplayed",this.TargetClassName??string.Empty));
    }

    /// <summary>
    /// Once a second: when the game build differs from the one shown (a first build included) and has
    /// kept its write time for a check, the view reopens on it, so a build shows without another edit.
    /// </summary>
    private void ReopenOnNewBuild(){
        if(this.WatchedBuild is not { }Build||this.PreviewClient is null||DateTime.UtcNow<this.NextBuildCheck){
            return;
        }
        this.NextBuildCheck=DateTime.UtcNow.AddSeconds(1);
        DateTime BuildTime=File.Exists(Build)?File.GetLastWriteTimeUtc(Build):DateTime.MinValue;
        bool Settled=BuildTime==this.PendingBuildTime;
        this.PendingBuildTime=BuildTime;
        if(BuildTime==DateTime.MinValue||BuildTime==this.ShownBuildTime||!Settled){
            return;
        }
        ActivityLog.LogInformation(nameof(SceneHwndHost),$"The game build '{Build}' changed; reopening the runtime view.");
        try {
            this.DisposePreviewClient(ClearScene:false);
            this.OpenOrReplacePreviewScene();
            this.PreviewFrameDirty=true;
            this.EnsureEditorPreviewHost();
        } catch(Exception Error){
            this.HandleRenderFailure(Error);
        }
    }

    private string LiveSceneJson()=>
        this.TargetFilePath is { }Path
            ?SceneWorkspaceService.Instance?.TryGetLiveText(Path)??(File.Exists(Path)?File.ReadAllText(Path):string.Empty)
            :string.Empty;

    private void ShowRuntimeViewState(RenderPreviewHostResponse Response){
        this.ShowRuntimeViewNote(Response.BuildRequired?VsixText.Get("SceneRuntimeViewBuildRequired"):null);
    }

    private void ShowRuntimeViewNote(string?Note){
        this.RuntimeViewNote=Note;
        if(Note is not null){ this.SetStatus(Note); }
    }
    private void StopEditorPreviewHost(bool WaitForExit=true){
        Process? process=this.EditorPreviewProcess;
        this.EditorPreviewProcess=null;
        this.EditorPreviewSceneName=null;
        string? tempDirectory=this.EditorPreviewTempDirectory;
        this.EditorPreviewTempDirectory=null;
        if(process is null){
            DeleteEditorPreviewTempDirectory(tempDirectory);
            return;
        }

        if(WaitForExit){
            CleanupEditorPreviewHost(process,tempDirectory);
            return;
        }
        _=ThreadPool.QueueUserWorkItem(
            _=>CleanupEditorPreviewHost(process,tempDirectory));
    }

    private static void CleanupEditorPreviewHost(
        Process ProcessValue,string?TempDirectory){
        try{
            EditorWorldProcess.Stop(ProcessValue);
        }finally{
            ProcessValue.Dispose();
            DeleteEditorPreviewTempDirectory(TempDirectory);
        }
    }

    private static void DeleteEditorPreviewTempDirectory(string?TempDirectory){
        if(TempDirectory is not null){
            try{ Directory.Delete(TempDirectory,recursive:true); }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
    }

    #pragma warning disable VSTHRD100
    private async void PollPhysicsDebug(object?Sender,EventArgs Args){
        if(!this.PhysicsDebugEnabled||this.PhysicsDebugRequestInFlight!=0){ return; }
        int? ProcessId=this.ResolveDebugProcessId(out bool UseEditorPreview);
        if(ProcessId is null){ this.ApplyPhysicsDebugResult(null,DebugStatusText.PhysicsProcessUnavailable());return; }
        Interlocked.Exchange(ref this.PhysicsDebugRequestInFlight,1);
        try {
            ScenePhysicsDebugSnapshot? Snapshot=null;
            DebugClientStatus Status=DebugClientStatus.TransportFailure(DebugStatusReason.Disconnected);
            bool Success=await Task.Run(()=>PhysicsDebugClient.TryGetSnapshot(ProcessId.Value,this.CurrentPhysicsDebugSnapshot?.WorldId,out Snapshot,out Status));
            if(!Success){ this.ApplyPhysicsDebugResult(null,DebugStatusText.Physics(Status));return; }
            string SnapshotStatus=UseEditorPreview
                ?DebugStatusText.PhysicsEditorSnapshot(Snapshot!.Tick,Snapshot.Bodies.Count,Snapshot.ExcludedBodyCount)
                :DebugStatusText.PhysicsRuntimeSnapshot(ProcessId.Value,Snapshot!.Tick,Snapshot.Bodies.Count,Snapshot.ExcludedBodyCount);
            this.ApplyPhysicsDebugResult(Snapshot,SnapshotStatus);
        }
        finally { Interlocked.Exchange(ref this.PhysicsDebugRequestInFlight,0); }
    }

    private int? ResolveDebugProcessId(out bool UseEditorPreview){
        IReadOnlyList<int> RuntimeIds=string.IsNullOrWhiteSpace(this.SpatialDebugRuntimeProcessName)?[]:SpatialDebugClient.FindProcessIds(this.SpatialDebugRuntimeProcessName);
        int? ProcessId=SpatialDebugProcessSelectionPolicy.Resolve(RuntimeIds,this.SelectedSpatialDebugProcessId);
        UseEditorPreview=false;
        if((RuntimeIds.Count==0||ProcessId is null)&&this.EditorPreviewProcess is { HasExited:false }){ ProcessId=this.EditorPreviewProcess.Id;UseEditorPreview=true; }
        this.UpdateSpatialDebugProcessCandidates(RuntimeIds,UseEditorPreview?null:ProcessId);
        return ProcessId;
    }










    private void ApplyPhysicsDebugResult(ScenePhysicsDebugSnapshot? Snapshot,string Status){
        if(!this.PhysicsDebugEnabled){ return; }
        this.CurrentPhysicsDebugSnapshot=Snapshot;
        if(!string.Equals(this.LastPhysicsDebugStatus,Status,StringComparison.Ordinal)){ this.LastPhysicsDebugStatus=Status;this.SetStatus(Status); }
        this.RefreshOverlay();
    }
    private async void PollSpatialDebug(object?Sender,EventArgs Args){
        if(!this.GlobalSpatialOctreeEnabled||this.SpatialDebugRequestInFlight!=0){
            return;
        }
        if(string.IsNullOrWhiteSpace(this.SpatialDebugRuntimeProcessName)&&this.EditorPreviewProcess is not { HasExited:false }){
            this.UpdateSpatialDebugProcessCandidates([],null);
            this.ApplySpatialDebugResult(null,VsixText.Get("SceneSpatialRuntimeProjectUnavailable"));
            return;
        }
        IReadOnlyList<int>RuntimeCandidateIds=string.IsNullOrWhiteSpace(this.SpatialDebugRuntimeProcessName)?[]:SpatialDebugClient.FindProcessIds(this.SpatialDebugRuntimeProcessName);
        int?ProcessId=SpatialDebugProcessSelectionPolicy.Resolve(RuntimeCandidateIds,this.SelectedSpatialDebugProcessId);
        bool UseEditorPreview=false;
        if((RuntimeCandidateIds.Count==0||ProcessId is null)&&this.EditorPreviewProcess is { HasExited:false }){
            ProcessId=this.EditorPreviewProcess.Id;
            UseEditorPreview=true;
        }
        this.UpdateSpatialDebugProcessCandidates(RuntimeCandidateIds,UseEditorPreview?null:ProcessId);
        if(ProcessId is null){
            this.ApplySpatialDebugResult(null,VsixText.Get(RuntimeCandidateIds.Count==0
                ?"SceneSpatialPreviewUnavailable"
                :"SceneSpatialSelectGamePid"));
            return;
        }
        Interlocked.Exchange(ref this.SpatialDebugRequestInFlight,1);
        try {
            SceneSpatialDebugSnapshot?Snapshot=null;
            DebugClientStatus Status=DebugClientStatus.TransportFailure(DebugStatusReason.Disconnected);
            bool Success=await Task.Run(()=>SpatialDebugClient.TryGetSnapshot(ProcessId.Value,null,out Snapshot,out Status));
            if(!Success){
                this.ApplySpatialDebugResult(null,DebugStatusText.Spatial(Status));
                return;
            }

            if(Snapshot is null||Snapshot.Nodes.Count==0){
                this.ApplySpatialDebugResult(Snapshot,UseEditorPreview
                    ?DebugStatusText.SpatialEditorEmpty()
                    :DebugStatusText.SpatialRuntimeEmpty(ProcessId.Value));
                return;
            }
            string SnapshotStatus=UseEditorPreview
                ?DebugStatusText.SpatialEditorSnapshot(Snapshot.Nodes.Count,Snapshot.Truncated,Snapshot.ExcludedNodeCount)
                :DebugStatusText.SpatialRuntimeSnapshot(ProcessId.Value,Snapshot.Nodes.Count,Snapshot.Truncated,Snapshot.ExcludedNodeCount);
            this.ApplySpatialDebugResult(Snapshot,SnapshotStatus);
        } catch(Exception Ex) when(Ex is IOException or InvalidOperationException or UnauthorizedAccessException){
            this.ApplySpatialDebugResult(null,DebugStatusText.Spatial(DebugClientStatus.TransportFailure(DebugStatusReason.Disconnected)));
        } finally {
            Interlocked.Exchange(ref this.SpatialDebugRequestInFlight,0);
        }
    }

    #pragma warning restore VSTHRD100

    private void UpdateSpatialDebugProcessCandidates(IReadOnlyList<int>CandidateIds,int?ProcessId){
        if(this.SpatialDebugProcessIds.SequenceEqual(CandidateIds)&&this.SelectedSpatialDebugProcessId==ProcessId){
            return;
        }
        this.SpatialDebugProcessIds=CandidateIds;
        this.SelectedSpatialDebugProcessId=ProcessId;
        this.SpatialDebugProcessCandidatesChanged?.Invoke(CandidateIds,ProcessId);
    }

    private void ApplySpatialDebugResult(SceneSpatialDebugSnapshot?Snapshot,string Status){
        if(!this.GlobalSpatialOctreeEnabled){
            return;
        }
        this.CurrentSpatialDebugSnapshot=Snapshot;
        if(!string.Equals(this.LastSpatialDebugStatus,Status,StringComparison.Ordinal)){
            this.SetSpatialDebugStatus(Status);
        }
        this.RefreshOverlay();
    }
    private void SetSpatialDebugStatus(string Status){
        if(string.Equals(this.LastSpatialDebugStatus,Status,StringComparison.Ordinal)){
            return;
        }
        this.LastSpatialDebugStatus=Status;
        this.SpatialDebugStatusChanged?.Invoke(Status);
    }
    private void RefreshCameraPreviews(){
        if(this.PreviewClient is null||!this.CameraPreviewEnabled
            ||this.TargetModel is not { }Model||this.SelectedNodePaths.Count==0){
            this.CameraPreviewLayout=[];
            this.CurrentCameraPreviewPasses=[];
            this.PreviewFrameDirty=true;
            return;
        }

        IReadOnlyList<SceneCameraPreview>Cameras=SceneCameraPreviewBinding.Build(
            Model,this.SelectedNodePaths,this.AspectRatio,
            RenderPreviewHostProtocol.MaximumCameraPreviewCount);
        IReadOnlyList<SceneCameraPreviewLayoutItem>Layout=CameraPreviewLayoutPolicy.Build(
            this.SurfaceWidth,this.SurfaceHeight,Cameras);
        List<SceneCameraPreviewLayoutItem>EffectiveLayout=[];
        List<RenderPreviewCameraPass>Passes=[];
        foreach(SceneCameraPreviewLayoutItem Item in Layout){
            if(!Item.Camera.IsAvailable){
                EffectiveLayout.Add(Item);
                continue;
            }

            SceneCameraPreviewRect Rect=Item.Rect;
            float CardAspect=Rect.Width/Math.Max(Rect.Height,1f);
            SceneCameraPreview Camera=Item.Camera.WithAspectRatio(CardAspect);
            EffectiveLayout.Add(new SceneCameraPreviewLayoutItem(Camera,Rect,Item.Index));
            string CameraObjectId=Camera.NodePath.ToDisplayString();
            Passes.Add(new RenderPreviewCameraPass {
                X=(int)FloatMath.Round(Rect.X),Y=(int)FloatMath.Round(Rect.Y),
                Width=Math.Max(1,(int)FloatMath.Round(Rect.Width)),
                Height=Math.Max(1,(int)FloatMath.Round(Rect.Height)),
                Camera=new RenderPreviewCamera {
                    ViewProjection=RenderPreviewSceneBuilder.Matrix(Camera.View*Camera.Projection),
                    Position=[Camera.World.M41,Camera.World.M42,Camera.World.M43],
                },
                ExcludedObjectId=this.GetPreviewRoster().Any(
                    Object=>Object.NodeId.Equals(Camera.NodePath))
                    ?CameraObjectId
                    :string.Empty,
            });
        }

        this.CameraPreviewLayout=EffectiveLayout;
        this.CurrentCameraPreviewPasses=Passes;
        this.PreviewFrameDirty=true;
        SceneCameraPreviewLayoutItem?Unavailable=EffectiveLayout.FirstOrDefault(static Item=>!Item.Camera.IsAvailable);
        if(Unavailable is { }UnavailableItem){
            this.SetStatus(UnavailableItem.Camera.ErrorMessage??UnavailableItem.Camera.DisplayName);
        }
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
                                      this.ReopenOnNewBuild();
                                      this.RenderSurfaceFrame();
                                   };
            CompositionTarget.Rendering+= this.RenderingHandler;

            // Subscribed once here (not inside Rebuild, which runs on every re-analysis) - it always
            // resizes whatever Renderer currently holds via the field, so re-subscribing per rebuild
            // would just accumulate redundant duplicate calls per resize.
            // これはサーフェスの実サイズを知る唯一の場所でもあるので、アスペクト比の元もここで更新する。
            this.ResizeHandler=(W,H)=>{
                                   try {
                                       this.SurfaceWidth=Math.Max(1,W);
                                       this.SurfaceHeight=Math.Max(1,H);
                                       this.PreviewClient?.Resize(
                                           W<=0||H<=0?0:W,
                                           W<=0||H<=0?0:H);
                                       this.ApplyCamera();
                                       this.RefreshCameraPreviews();
                                       this.RefreshOverlay();
                                   } catch(Exception Ex){
                                       this.HandleRenderFailure(Ex);
                                   }
                               };
            this.Surface.Resized+= this.ResizeHandler;
        }

        this.Rebuild();
    }

    /// <summary>
    /// 現在の<see cref="ViewState"/>をauthoring-owned frame stateへ反映する(ADR-0041 決定事項1、
    /// M0020 Phase 1)。値が変わらない場合はdirtyにならないため、毎フレーム呼んで構わない。
    /// </summary>
    private void RenderSurfaceFrame(){
        if(this.PreviewClient is null||!this.PreviewFrameDirty){
            return;
        }
        try {
            this.PreviewClient.UpdateFrame(this.CreateCurrentFrame());
            RenderPreviewHostResponse Response=
                this.PreviewClient.Render(captureForVerification:false);
            this.PreviewFrameDirty=false;
            this.UpdateCameraPreviewDiagnostics(Response);
            this.Surface?.SetVisible(true);
        } catch(Exception Ex){
            this.HandleRenderFailure(Ex);
        }
    }

    private void HandleRenderFailure(Exception Error){
        // RenderFrame/Resize failure invalidates any concurrent build before reporting status.
        // Otherwise IsBuildPending suppresses the visible WPF diagnostic and leaves a hidden
        // native child as a white HwndHost airspace region.
        this.BuildLifecycle.Invalidate();
        this.HasFatalRenderFailure=true;
        ActivityLog.LogError(nameof(SceneHwndHost),Error.ToString());
        bool RequiresFreshSurface=Error is RenderPreviewHostException HostError
            &&HostError.RequiresFreshSurfaceGeneration;
        this.DisposePreviewClient(ClearScene:false);
        if(RequiresFreshSurface&&!this.HostCrashRecoveryUsed
            &&this.SurfaceHost is { }Host){
            this.HostCrashRecoveryUsed=true;
            try {
                RenderSurfaceWindow OldSurface=Host.Surface;
                OldSurface.PointerInput-=this.OnPointerInput;
                OldSurface.WheelInput-=this.HandleWheel;
                if(this.ResizeHandler is not null){
                    OldSurface.Resized-=this.ResizeHandler;
                }
                RenderSurfaceWindow NewSurface=Host.RecreateSurface(InitiallyVisible:false);
                NewSurface.PointerInput+=this.OnPointerInput;
                NewSurface.WheelInput+=this.HandleWheel;
                if(this.ResizeHandler is not null){
                    NewSurface.Resized+=this.ResizeHandler;
                }
                this.OpenOrReplacePreviewScene();
                this.PreviewFrameDirty=true;
                this.HasFatalRenderFailure=false;
                return;
            } catch(Exception RecoveryError){
                ActivityLog.LogError(nameof(SceneHwndHost),RecoveryError.ToString());
                this.DisposePreviewClient(ClearScene:false);
            }
        }
        this.Surface?.SetVisible(false);
        this.SetStatus(VsixText.Get("SceneRenderFailed"));
    }

    private void ApplyCamera(){
        if(this.PreviewClient is null||this.ViewState is not { }State){
            return;
        }

        Matrix4x4 View=State.GetView();
        Matrix4x4 Projection=State.GetProjection(this.AspectRatio);
        Vector3 Position=State.GetEyePosition();
        if(!this.CurrentView.Equals(View)
            ||!this.CurrentProjection.Equals(Projection)
            ||!this.CurrentCameraPosition.Equals(Position)){
            this.CurrentView=View;
            this.CurrentProjection=Projection;
            this.CurrentCameraPosition=Position;
            this.PreviewFrameDirty=true;
        }
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

    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
    private void TickFlyIfNeeded(EventArgs Args){
        if(this.InputMachine is not { } Machine|| this.ViewState is not { } State||Machine.State!=SceneNavState.Flying){
            this.LastFlyTickTime=null; // Fly再突入時に古い時刻からの巨大なDeltaTimeが出ないようにする。
            return;
        }

        float DeltaSeconds=0f;
        if(Args is RenderingEventArgs RenderArgs){
            if(this.LastFlyTickTime is { } Previous){
                DeltaSeconds=(float)(RenderArgs.RenderingTime-Previous).TotalSeconds;
            }

            this.LastFlyTickTime=RenderArgs.RenderingTime;
        }

        bool Shift=(NativeMethods.GetKeyState(NativeMethods.VK_SHIFT)&0x8000)!=0;
        bool Ctrl=(NativeMethods.GetKeyState(NativeMethods.VK_CONTROL)&0x8000)!=0;

        // ピクセル完全一致中は回転をムーブイベントごとに即時確定済み(OnPointerInput参照)なので、
        // ここではWASDQEの移動だけを現在のYaw/Pitchで適用する。
        if(this.LookUsesPixelLock){
            State.TickFlyMovementOnly(Machine.FlyHeldKeys,DeltaSeconds,Shift,Ctrl);
            return;
        }

        State.TickFly(Machine.FlyHeldKeys, this.PendingLookDeltaX, this.PendingLookDeltaY,DeltaSeconds,Shift,Ctrl);
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

        if(this.CameraPreviewEnabled
            &&CameraPreviewHitTestPolicy.TryHit(this.CameraPreviewLayout,Event.X,Event.Y,out NodePath PreviewPath)){
            if(Event.Kind==PointerEventKind.Down&&Event.Button==PointerButton.Left){
                this.CameraPreviewSelectionRequested?.Invoke(PreviewPath);
            }

            return;
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

        // M0020追加: RMB LookでFlyingへ入った瞬間だけアンカーを捕捉する。
        if(Result.Capture==SceneCaptureAction.Begin){
            if(Result.State==SceneNavState.Flying){
                this.CaptureLookAnchor(Event.X,Event.Y); }
            if(Result.State==SceneNavState.Orbiting){
                this.OrbitPivot=this.SelectionCenter(); }
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
                if(this.OrbitPivot is { }Pivot){
                    this.ViewState?.ApplyOrbit(Result.DeltaX,Result.DeltaY,Pivot);
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
        this.LookUsesPixelLock= this.ViewState is { LookUsesPixelLock:true };
        if(!this.LookUsesPixelLock|| this.ViewState is not { } State){
            return;
        }

        (_, this.LookAnchorWorldDirection)=SceneViewCamera.ScreenPointToRay(
                                                                            State.GetView(),State.GetProjection(this.AspectRatio),X,Y, this.SurfaceWidth, this.SurfaceHeight);
    }

    /// <summary>LMB Down(Alt無し)のクリック選択用に、カーソル下のオブジェクトを拾う。</summary>
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
        if(this.PreviewClient is null|| this.ViewState is not { }State
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
        if(this.PreviewClient is null){
            return;
        }

        List<GizmoOverlayPrimitive>Primitives=[];
        IReadOnlyList<GizmoOverlayPrimitive>TransformPrimitives=Array.Empty<GizmoOverlayPrimitive>();
        IReadOnlyList<GizmoOverlayPrimitive>PropertyPrimitives=Array.Empty<GizmoOverlayPrimitive>();
        bool ShowsGizmos=SceneGizmoVisibilityPolicy.ShowsGizmos(this.InputMachine?.State??SceneNavState.Idle);
        if(ShowsGizmos&& this.CurrentMoveGizmo is { }Frame&& this.ViewState is { }GizmoState){
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
        if(ShowsGizmos&& this.CurrentPropertyGizmo is { }PropertyFrame){
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
            // M0037: Preview protocolはoverlay頂点予算超過を拒否する。HUDは表示専用の付加情報
            // なので、超過するくらいならギズモ本体を守るためHUDだけを落とす。
            if(ExistingVertices+HudVertices<=RenderPreviewHostProtocol.MaximumOverlayVertexCount){
                Primitives.AddRange(Hud);
            }
        }
        Primitives.AddRange(SceneCameraPreviewOverlay.Build(
            this.CurrentCameraPreviewPasses,this.CurrentView,this.CurrentProjection,
            this.SurfaceWidth,this.SurfaceHeight));

        if(this.GlobalSpatialOctreeEnabled&&this.CurrentSpatialDebugSnapshot is { }SpatialSnapshot&&this.ViewState is { }SpatialState){
            Matrix4x4 ViewProjection=SpatialState.GetView()*SpatialState.GetProjection(this.AspectRatio);
            this.CurrentSpatialDebugOverlay=SpatialDebugOverlay.Build(SpatialSnapshot,ViewProjection);
        } else {
            this.CurrentSpatialDebugOverlay=[];
        }
        if(this.PhysicsDebugEnabled
            &&(this.CurrentPhysicsDebugSnapshot is not null||this.CurrentPhysicsFractureBonds.Count>0)){
            this.CurrentPhysicsDebugOverlay=PhysicsDebugOverlay.Build(
                this.CurrentPhysicsDebugSnapshot,this.PhysicsDebugOptions,this.CurrentPhysicsFractureBonds);
        } else {
            this.CurrentPhysicsDebugOverlay=[];
        }
        Primitives.AddRange(this.CurrentSpatialDebugOverlay);
        Primitives.AddRange(this.CurrentPhysicsDebugOverlay);
        int TotalVertices=Primitives.Sum(static Primitive=>Primitive.Vertices.Count);
        if(TotalVertices>RenderPreviewHostProtocol.MaximumOverlayVertexCount){
            throw new ArgumentOutOfRangeException(
                nameof(Primitives),
                VsixText.Format(
                    "ScenePreviewOverlayVertexLimitExceeded",TotalVertices,
                    RenderPreviewHostProtocol.MaximumOverlayVertexCount));
        }
        // ApplyCamera refreshes the overlay on every UI tick; an unchanged overlay must not request a
        // frame, or the idle view re-renders at the display rate.
        if(GizmoOverlayPrimitive.SameContent(this.CurrentOverlayPrimitives,Primitives)){
            return;
        }
        this.CurrentOverlayPrimitives=Primitives;
        this.PreviewFrameDirty=true;
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
        this.SetStatus(VsixText.Format("ScenePropertyEditing",PrimaryNode.Node.DisplayName));
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
            this.SetStatus(VsixText.Get("SceneDuplicateUnsupported"));
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
                this.SetStatus(VsixText.Get("SceneDuplicatePreviewUnavailable"));
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
        this.SetStatus(VsixText.Format("SceneToolEditing",GetToolDisplayName(Family),PrimaryNode.Node.DisplayName));
        return true;
    }

    private bool TryStartDuplicatePreview(IReadOnlyList<DuplicateNodePlanItem>Plan){
        if(this.PreviewClient is null){ return false; }

        List<SceneRenderObject>RenderObjects=[];
        List<(NodePath SourcePath,NodePath PreviewPath,Matrix4x4 OriginalWorld)>Sources=[];
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
                Sources.Add((Object.NodeId,ResultPath,Object.World));
                break;
            }
        }

        if(RenderObjects.Count==0){
            return false;
        }

        this.CurrentPreviewRenderObjects=RenderObjects;
        this.DuplicateRenderPreviews=Sources.Select(Source=>new DuplicateRenderPreview(
            Source.SourcePath,Source.PreviewPath,Source.OriginalWorld)).ToArray();
        this.ActiveDuplicatePlan=Plan.ToArray();
        this.PreviewSelectedNodePaths=Plan.Select(static Item=>Item.ResultPath).ToArray();
        if(!this.TryReplacePreviewScene()){
            this.CurrentPreviewRenderObjects=[];
            this.DuplicateRenderPreviews=[];
            this.ActiveDuplicatePlan=[];
            this.PreviewSelectedNodePaths=[];
            return false;
        }
        return true;
    }

    private void RemoveDuplicatePreview(){
        if(this.DuplicateRenderPreviews.Count>0){
            this.CurrentPreviewRenderObjects=[];
            this.PreviewSelectedNodePaths=[];
            _=this.TryReplacePreviewScene();
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
                this.UpdatePreviewObjectTransform(DuplicatePreview.PreviewPath,World);
            }
        } else {
            foreach(SceneTransformPreview Preview in Previews){
                this.UpdateCurrentObjectTransform(Preview.NodePath,Preview.World);
            }
            foreach(SceneTransformPreview Descendant in this.PreviewDescendantOriginals){
                this.UpdateCurrentObjectTransform(
                    Descendant.NodePath,
                    Descendant.World*this.EditTransaction.CurrentWorldDelta);
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
            this.UpdatePreviewCamera(State);
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
                                                                             VsixText.Get("UndoEditSceneProperty"),Operations,out SceneEditCommitRequest?PropertyRequest);
            if(!PropertyCreated||PropertyRequest is null|| this.EditCommitRequested is null){
                this.CancelEditTransaction();
                this.SetStatus(VsixText.Get("ScenePropertyCommitDiscarded"));
                return;
            }

            this.EditCommitRequested.Invoke(PropertyRequest);
            this.SetStatus(VsixText.Get("ScenePropertyApplying"));
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
                                                            VsixText.Format("UndoDuplicateSceneSelection",GetToolDisplayName(Tool)),Members, this.ActiveDuplicatePlan.Select(static Item=>Item.ResultPath).ToArray(),out Request)
            : this.EditTransaction.TryCreateCommit(
                                                   VsixText.Format("UndoTransformSceneSelection",GetToolDisplayName(Tool)),Members,out Request);
        if(!Created
            ||Request is null|| this.EditCommitRequested is null){
            this.CancelEditTransaction();
            this.SetStatus(VsixText.Format("SceneToolCommitDiscarded",GetToolDisplayName(Tool)));
            return;
        }

        this.EditCommitRequested.Invoke(Request);
        this.SetStatus(VsixText.Format("SceneToolApplying",GetToolDisplayName(Tool)));
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
            this.RemoveDuplicatePreview();
        } else {
            foreach(SceneTransformPreview Original in Cancellation.RestoredTransforms){
                this.UpdateCurrentObjectTransform(Original.NodePath,Original.World);
            }
            foreach(SceneTransformPreview Descendant in this.PreviewDescendantOriginals){
                this.UpdateCurrentObjectTransform(Descendant.NodePath,Descendant.World);
            }
            // A runtime view kept the drag's last transforms; the unchanged document puts them back.
            if(this.PreviewIsRuntime){
                _=this.TryReplacePreviewScene();
            }
        }
        if(this.FollowViewApplied&& this.HasActiveFollowViewSnapshot&& this.ViewState is { }State){
            this.ActiveFollowViewSnapshot.Restore(State);
            this.UpdatePreviewCamera(State);
        }

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
        this.CurrentMoveGizmo=null;
        if(Cancellation.PendingSnapshot is { }Pending){
            this.SetTarget(Pending);
        } else {
            this.UpdateMoveGizmo();
        }
    }


    internal void CompleteEditCommit(bool Succeeded,string?ErrorMessage){
        if(this.EditTransaction.State!=EditTransactionState.Committing){
            return;
        }
        if(!Succeeded){
            this.CancelEditTransaction();
            this.SetStatus(ErrorMessage??VsixText.Get("SceneCommitFailed"));
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
            if(Pending is not null){
                this.SetTarget(Pending);
            } else {
                this.UpdateMoveGizmo();
            }

            this.SetStatus(VsixText.Get("ScenePropertyApplied"));
            return;
        }

        this.RemoveDuplicatePreview();
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

        this.SetStatus(VsixText.Get("SceneTransformApplied"));
    }

    /// <summary>
    /// <see cref="CompleteEditCommit"/>専用。
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
        this.PreviewFrameDirty=true;
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
        SceneActiveTool.Move=>VsixText.Get("SceneToolMove"),
        SceneActiveTool.Rotate=>VsixText.Get("SceneToolRotate"),
        SceneActiveTool.Scale=>VsixText.Get("SceneToolScale"),
        _=>VsixText.Get("SceneToolTransform"),
    };

    /// <summary>WPFの<c>LostKeyboardFocus</c>から呼ぶ。RMBは離されていないので状態遷移はしない。</summary>
    internal void ClearHeldFlyKeys()=> this.InputMachine?.HandleLostKeyboardFocus();

    internal bool CancelCurrentOperation(){
        if(this.InputMachine is not { }Machine){
            return false;
        }
        bool Canceled=Machine.State!=SceneNavState.Idle||this.EditTransaction.State==EditTransactionState.Dragging;
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
        return Canceled;
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

    /// <summary>
    /// The center of the selection, as Focus Selection frames it: the bounds of the selected objects' meshes,
    /// or the gizmo's pivot for a selection without one (an empty node, a light). Null without a selection.
    /// </summary>
    private Vector3?SelectionCenter(){
        if(this.SelectedNodePaths.Count==0){
            return null;
        }
        List<SceneRenderObject>Selected=[..this.CurrentRenderObjects.Where(Object=> this.SelectedNodePaths.Contains(Object.NodeId))];
        if(Selected.Count>0){
            return SceneCameraFraming.ComputeSelectionBounds(Selected).Center;
        }
        return this.CurrentMoveGizmo is { }Gizmo?Gizmo.Pivot:null;
    }

    /// <summary>(Re)builds <see cref="Renderer"/> from the pin 済み <see cref="SceneDocumentSnapshot"/>.</summary>
    private void Rebuild(){
        // Every rebuild supersedes the prior request, including paths that end before a worker is
        // started (analysis pending, invalid model, or a destroyed target).
        this.BuildLifecycle.Invalidate();
        if(this.Surface is null|| this.TargetKey is null|| this.TargetSnapshot is null|| this.TargetFilePath is null|| this.TargetClassName is null){
            return;
        }
        this.HasFatalRenderFailure=false;

        if(this.TargetModel is not { } Model){
            // Not (yet) found - e.g. analysis hasn't caught up with a just-opened tool window, or
            // the class was renamed/deleted out from under us. Leave the last good frame on screen.
            this.SetStatus(VsixText.Format("SceneAnalysisWaiting",this.TargetClassName));
            return;
        }

        if(Model.Root is null){
            this.HasFatalRenderFailure=true;
            this.DisposePreviewClient(ClearScene:true);
            Microsoft.CodeAnalysis.Diagnostic?Diagnostic=Model.Diagnostics.FirstOrDefault();
            string Detail=Diagnostic is null
                ?"Scene Root could not be analyzed."
                :$"{Diagnostic.Id}: {Diagnostic.GetMessage()}";
            ActivityLog.LogError(nameof(SceneHwndHost),$"{this.TargetClassName}: {Detail}");
            this.SetStatus(VsixText.Format("SceneAnalysisFailed",this.TargetClassName,
                Diagnostic?.Id??VsixText.Get("UnknownError")));
            return;
        }

        this.SetStatus(VsixText.Format("SceneLoading",this.TargetClassName));

        // 旧 .scene 実装の WarmMeshCache は、WPF の BuildWindowCore 内でJTF/Taskを同期wait
        // するデッドロックを避けるため、生ThreadへI/Oを隔離してからUI側でJoinしていた。
        // Scene DSL版ではさらに一歩進め、Primitive.Cli生成と後続のGLTF MeshLoaderを含む
        // Build全体をこの単一workerへ渡し、UIスレッドではJoin/Waitを一切行わない。完了後の
        // Preview Host session更新だけをDispatcherへ戻す。live解析が連続してもworkerはlatest-winsで
        // 未処理要求を置換するため、ブリッジプロセスが無制限に増えることもない。
        if(this.BuildWorker is not { } Worker){
            this.HasFatalRenderFailure=true;
            this.SetStatus(VsixText.Get("SceneRenderWorkerUnavailable"));
            return;
        }

        long BuildEpoch=this.BuildLifecycle.Begin();
        Worker.Request(new SceneRenderBuildRequest(
                                                    Model, this.TargetKey.Value, this.TargetSnapshot.AnalysisGeneration,
                                                    this.TargetFilePath, this.TargetClassName, this.Surface.Handle,BuildEpoch));
    }

    private SceneRenderBuildResult BuildRenderObjects(SceneRenderBuildRequest Request){
        CgeProjectContext Context=CgeProjectContext.Resolve(Request.FilePath)
            ??throw new InvalidOperationException(VsixText.Get("Common_GameProjectNotFound"));
        this.RepoRoot??=Context.WorkspaceRoot;
        IReadOnlyList<SceneRenderObject>RenderObjects=SceneModelRenderObjectBuilder.Build(
            Request.Model,
            EnumMemberName=>SceneModelRenderObjectBuilder.ResolveViaBridge(EnumMemberName,Context),
            AssetPath=>SceneModelRenderObjectBuilder.ResolveStaticMeshViaBridge(AssetPath,Context));
        return new SceneRenderBuildResult(Request,RenderObjects);
    }

    private void OnBuildCompleted(SceneRenderBuildRequest Request,SceneRenderBuildResult?Result,Exception?Error){
        // A result for a superseded target must be completely inert. In particular, it must not
        // clear a newer target's pending lease before the newer worker has returned.
        if(!this.BuildLifecycle.TryComplete(Request.BuildEpoch)){
            return;
        }

        if(this.Surface is null
            || this.Surface.Handle!=Request.SurfaceHandle
            || this.TargetKey is not { } Key
            ||!Key.Equals(Request.Key)
            || this.TargetSnapshot?.AnalysisGeneration!=Request.AnalysisGeneration){
            return;
        }

        if(Error is not null){
            this.HasFatalRenderFailure=true;
            ActivityLog.LogError(nameof(SceneHwndHost),Error.ToString());
            if(this.PreviewClient is null){
                this.Surface.SetVisible(false);
            }

            this.SetStatus(VsixText.Get("SceneRenderFailed"));
            return;
        }

        if(Result is null){
            return;
        }

        // 描画オブジェクト0件でもレンダラーは作る(グリッド/ギズモだけのフレームは正当、
        // M0020 Phase 0/Phase 3)。ここでサーフェスを隠すと前フレームの残像や背面ペインの露出を
        // 招くだけで、0件でも RenderFrame は clear + Present を行う。
        try {
            (this.SceneBoundsCenter,this.SceneBoundsRadius)=SceneCameraFraming.ComputeBounds(Result.RenderObjects);
            this.CurrentRenderObjects=Result.RenderObjects;
            this.CurrentPreviewRenderObjects=[];
            this.PreviewSelectedNodePaths=[];

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
            this.CurrentView=View;
            this.CurrentProjection=Projection;
            this.CurrentCameraPosition=this.ViewState?.GetEyePosition()
                ??GetCameraPosition(View);
            this.OpenOrReplacePreviewScene();
            this.HasFatalRenderFailure=false;
            this.RenderedAnalysisGeneration=Request.AnalysisGeneration;
            // 再解析でPreview Host sceneを置き換えても選択の見た目を保つ。複数選択(SelectedNodePaths)も
            // 一緒に渡さないと、primary以外のハイライトだけが再ビルドで消える。
            this.PreviewFrameDirty=true;
            this.RefreshCameraPreviews();
            this.UpdateMoveGizmo();
            if(!this.RuntimeOpenPending){
                this.SetStatus(this.RuntimeViewNote??VsixText.Format("SceneStatusDisplayed",Result.Request.ClassName));
            }
        } catch(Exception Ex){
            this.HasFatalRenderFailure=true;
            ActivityLog.LogError(nameof(SceneHwndHost),Ex.ToString());
            this.DisposePreviewClient(ClearScene:true);
            this.SetStatus(VsixText.Get("SceneRenderFailed"));
        }
    }

    private void DisposePreviewClient(bool ClearScene,bool DisposeInBackground=false){
        RenderPreviewHostClient?Client=this.PreviewClient;
        this.PreviewClient=null;
        // An open still in flight belongs to the view being disposed.
        this.RuntimeOpenGeneration++;
        this.RuntimeOpenPending=false;
        string?RuntimeTemp=this.RuntimeViewTempDirectory;
        this.RuntimeViewTempDirectory=null;
        this.PreviewIsRuntime=false;
        if(Client is not null){
            if(DisposeInBackground){
                _=ThreadPool.QueueUserWorkItem(_=>{
                    Client.Dispose();
                    DeleteEditorPreviewTempDirectory(RuntimeTemp);
                });
            }else{
                Client.Dispose();
                DeleteEditorPreviewTempDirectory(RuntimeTemp);
            }
        }else{
            DeleteEditorPreviewTempDirectory(RuntimeTemp);
        }
        this.RenderedAnalysisGeneration=-1;
        this.PreviewFrameDirty=false;
        if(ClearScene){
            this.CurrentRenderObjects=[];
            this.CurrentPreviewRenderObjects=[];
            this.CurrentOverlayPrimitives=[];
            this.CurrentCameraPreviewPasses=[];
            this.PreviewSelectedNodePaths=[];
        }
        this.Surface?.SetVisible(false);
    }

    private void OpenOrReplacePreviewScene(){
        RenderSurfaceWindow Surface=this.Surface
            ??throw new InvalidOperationException("Scene preview surface is unavailable.");
        RenderPreviewScene Scene=RenderPreviewSceneBuilder.FromScene(
            this.GetPreviewRoster(),this.CurrentView,this.CurrentProjection,
            this.CurrentCameraPosition,this.ViewState?.FovYRadians??SceneCameraFraming.DefaultFovYRadians);
        if(this.PreviewClient is not null){
            string SceneJson=this.PreviewIsRuntime?this.LiveSceneJson():string.Empty;
            this.ShowRuntimeViewState(this.PreviewClient.ReplaceScene(Scene,SceneJson));
            this.RuntimeViewSentSceneJson=this.PreviewIsRuntime?SceneJson:null;
            return;
        }
        if(this.RuntimeOpenPending){
            // The runtime view is opening off the UI thread; it catches up with this scene when it lands.
            return;
        }
        (string HostPath,string ShaderArtifactPath)=this.ResolvePreviewHost();
        // ADR-0070 B1: the game's own runtime when the game is built; the scene preview otherwise.
        this.RuntimeViewNote=null;
        if(this.TryPrepareRuntimeView() is { }Plan){
            this.BeginRuntimeViewOpen(Plan,Surface.Handle.ToInt64(),HostPath,ShaderArtifactPath,Scene);
            return;
        }
        this.OpenScenePreview();
    }

    private (string HostPath,string ShaderArtifactPath)ResolvePreviewHost(){
        this.RepoRoot??=CgeProjectContext.Resolve(this.TargetFilePath)?.WorkspaceRoot;
        if(this.RepoRoot is null){
            throw new InvalidOperationException(VsixText.Get("Common_GameProjectNotFound"));
        }
        return (RenderPreviewSceneBuilder.ResolveHostPath(this.RepoRoot),RenderPreviewSceneBuilder.ResolveShaderArtifactPath(this.RepoRoot));
    }

    /// <summary>The scene preview (the designer's render objects), when there is no runtime view of a built game.</summary>
    private void OpenScenePreview(){
        RenderSurfaceWindow Surface=this.Surface
            ??throw new InvalidOperationException("Scene preview surface is unavailable.");
        RenderPreviewScene Scene=RenderPreviewSceneBuilder.FromScene(
            this.GetPreviewRoster(),this.CurrentView,this.CurrentProjection,
            this.CurrentCameraPosition,this.ViewState?.FovYRadians??SceneCameraFraming.DefaultFovYRadians);
        (string HostPath,string ShaderArtifactPath)=this.ResolvePreviewHost();
        RenderPreviewHostClient Client=RenderPreviewHostClient.Start(HostPath);
        try {
            Client.Open(
                Surface.Handle.ToInt64(),this.SurfaceWidth,this.SurfaceHeight,
                ShaderArtifactPath,Scene);
            this.PreviewClient=Client;
        } catch {
            Client.Dispose();
            throw;
        }
    }

    private bool TryReplacePreviewScene(){
        try {
            this.OpenOrReplacePreviewScene();
            this.PreviewFrameDirty=true;
            return true;
        } catch(Exception Error){
            this.HandleRenderFailure(Error);
            return this.PreviewClient is not null&&!this.HasFatalRenderFailure;
        }
    }

    private RenderPreviewFrame CreateCurrentFrame()=>
        RenderPreviewSceneBuilder.CreateFrame(
            this.GetPreviewRoster(),this.CurrentView,this.CurrentProjection,
            this.CurrentCameraPosition,
            this.ViewState?.RenderMode??SceneViewRenderMode.Lit,
            this.PreviewSelectedNodePaths.Count>0
                ?this.PreviewSelectedNodePaths
                :this.SelectedNodePaths,
            this.CurrentOverlayPrimitives,this.CurrentCameraPreviewPasses,
            this.ViewState?.FovYRadians??SceneCameraFraming.DefaultFovYRadians,
            this.RuntimeViewDragOverrides());

    /// <summary>
    /// M0096: during a transform gizmo drag a runtime view gets the dragged roots' transforms with every
    /// frame; it ignores the designer's render objects, so the object would otherwise stand still until
    /// the commit. Property drags and duplicate previews are not transforms of existing nodes.
    /// </summary>
    private RenderPreviewTransformOverride[]?RuntimeViewDragOverrides()=>
        this.PreviewIsRuntime&&this.EditTransaction.State==EditTransactionState.Dragging
            &&this.ActivePropertyDrag is null&&this.ActiveDuplicatePlan.Count==0
            ?RenderPreviewSceneBuilder.DragOverrides(this.EditTransaction.CurrentTargets,this.EditTransaction.CurrentPreviews)
            :null;

    private IReadOnlyList<SceneRenderObject>GetPreviewRoster(){
        if(this.CurrentPreviewRenderObjects.Count==0){
            return this.CurrentRenderObjects;
        }
        return [..this.CurrentRenderObjects,..this.CurrentPreviewRenderObjects];
    }

    private void UpdateCameraPreviewDiagnostics(RenderPreviewHostResponse Response){
        if(Response.CameraPreviews.Length==0){ return; }
        RenderPreviewCameraDiagnostics First=Response.CameraPreviews[0];
        this.SetStatus(VsixText.Format(
            "SceneCameraDiagnostics",First.SceneObjectCount,
            First.FrustumCandidateCount,First.DrawCallCount,
            First.ViewProjectionHash.ToString(
                "X8",System.Globalization.CultureInfo.InvariantCulture)));
    }

    private bool UpdateCurrentObjectTransform(NodePath Path,in Matrix4x4 World){
        return this.UpdateObjectTransform(
            this.CurrentRenderObjects,Path,World,
            Updated=>this.CurrentRenderObjects=Updated);
    }

    private bool UpdatePreviewObjectTransform(NodePath Path,in Matrix4x4 World){
        return this.UpdateObjectTransform(
            this.CurrentPreviewRenderObjects,Path,World,
            Updated=>this.CurrentPreviewRenderObjects=Updated);
    }

    private bool UpdateObjectTransform(
        IReadOnlyList<SceneRenderObject>Objects,NodePath Path,in Matrix4x4 World,
        Action<IReadOnlyList<SceneRenderObject>>Commit){
        SceneRenderObject[]Updated=Objects.ToArray();
        for(int Index=0;Index<Updated.Length;Index++){
            SceneRenderObject Object=Updated[Index];
            if(!Object.NodeId.Equals(Path)){ continue; }
            Updated[Index]=new SceneRenderObject {
                Mesh=Object.Mesh,World=World,NodeId=Object.NodeId,
                IncludeInBounds=Object.IncludeInBounds,IsPickable=Object.IsPickable,
            };
            Commit(Updated);
            this.PreviewFrameDirty=true;
            return true;
        }
        return false;
    }

    private void UpdatePreviewCamera(SceneViewState State){
        this.CurrentView=State.GetView();
        this.CurrentProjection=State.GetProjection(this.AspectRatio);
        this.CurrentCameraPosition=State.GetEyePosition();
        this.PreviewFrameDirty=true;
    }

    private static Vector3 GetCameraPosition(in Matrix4x4 View){
        return Matrix4x4.Invert(View,out Matrix4x4 CameraWorld)
            ?new Vector3(CameraWorld.M41,CameraWorld.M42,CameraWorld.M43)
            :Vector3.Zero;
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
        IntPtr SurfaceHandle,
        long BuildEpoch);

    private sealed record SceneRenderBuildResult(
        SceneRenderBuildRequest Request,
        IReadOnlyList<SceneRenderObject>RenderObjects);
}
