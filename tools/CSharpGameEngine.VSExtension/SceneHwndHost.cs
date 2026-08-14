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
/// <see cref="SceneRenderer"/> is disposed and rebuilt on every re-analysis, so the camera lives here
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

    // M0020 Phase 1: ナビゲーション状態機械とカメラ/永続ツール状態(ADR-0041 決定事項1)。
    private SceneViewState?ViewState;
    private SceneInputStateMachine?InputMachine;
    private bool PendingCameraReset=true;
    private float PendingLookDeltaX;
    private float PendingLookDeltaY;
    private TimeSpan?LastFlyTickTime;

    private float AspectRatio=>SurfaceWidth/(float)SurfaceHeight;

    internal event Action<string>?StatusChanged;

    /// <summary>Pointerの<c>Down</c>で発火。WPF側は購読して<c>Keyboard.Focus</c>をここでだけ呼ぶ(ADR-0041 決定事項2)。</summary>
    internal event Action?ViewportActivated;

    /// <summary>
    /// LMBクリックでの選択確定(M0020 Phase 2)。ヒットNodePath(空間クリックならnull)とCtrl押下を運ぶ。
    /// <see cref="SceneEditorSessionService.Selection"/>への変換・発行はWPF側(<see cref="SceneHwndHost"/>は
    /// セッションを知らない)。
    /// </summary>
    internal event Action<NodePath?,bool>?ObjectPicked;

    public SceneHwndHost(){
        // Surfaceは描画先でありキーボード入力を持つコントロールではない。HwndHostの既定
        // TabInto経路を残すと、float/dockやマウスクリック後にVSのTab等がSurfaceへ入り、
        // WPFとWin32のフォーカスがずれてコードエディタの制御キーが効かなくなる。
        Focusable=false;
        KeyboardNavigation.SetIsTabStop(this,RenderSurfaceFocusPolicy.AllowTabInto);
    }

    protected override bool TabIntoCore(TraversalRequest Request)
        =>RenderSurfaceFocusPolicy.AllowTabInto;

    protected override HandleRef BuildWindowCore(HandleRef HwndParent){
        ThreadHelper.ThrowIfNotOnUIThread();

        // Rendererが最初のPresentを行えるまで子HWNDを隠す。NOREDIRECTIONBITMAPの空Surfaceを
        // 表示すると、ドッキング前に同じ領域にいたSolution Explorerが残像のように露出する。
        Surface=new RenderSurfaceWindow(HwndParent.Handle,800,600,InitiallyVisible:false);
        ViewState=SceneViewState.CreateFromSettings();
        InputMachine=new SceneInputStateMachine();
        Surface.PointerInput+=OnPointerInput;
        Surface.WheelInput+=HandleWheel;
        SynchronizationContext UiContext=SynchronizationContext.Current
            ??new DispatcherSynchronizationContext(this.Dispatcher);
#pragma warning disable VSTHRD001 // 生workerの完了を非同期Postするだけで、同期wait/message pumpは行わない。
        BuildWorker=new LatestOnlyThreadWorker<SceneRenderBuildRequest,SceneRenderBuildResult>(
            Work:BuildRenderObjects,
            Dispatch:Callback=>UiContext.Post(_=>Callback(),null),
            Completed:this.OnBuildCompleted);
#pragma warning restore VSTHRD001
        LoadIntoSurface();
        return new HandleRef(this,Surface.Handle);
    }

    protected override void DestroyWindowCore(HandleRef Hwnd){
        ThreadHelper.ThrowIfNotOnUIThread();

        BuildWorker?.Dispose();
        BuildWorker=null;
        DetachSubscriptions();
        DisposeRenderer();

        if(Surface is not null){
            Surface.PointerInput-=OnPointerInput;
            Surface.WheelInput-=HandleWheel;
        }

        // 保険の書き込み(通常はActiveTool等を変更した時点で即時書き込み済み)。
        InputMachine?.Reset();
        ViewState?.SaveToSettings();
        InputMachine=null;
        ViewState=null;

        Surface?.Dispose();
        Surface=null;
    }

    /// <summary>
    /// Session が pin した snapshot を描画対象にします。解析中の blocking error は Last Known Good
    /// projection を保持するので、ここでrendererを空へ戻しません。
    /// </summary>
    internal void SetTarget(SceneDocumentSnapshot Snapshot){
        // コンボボックスでのシーン切替はSceneHwndHostを再生成せず(EnsureHostが既存インスタンスを
        // 返すだけ)、このSetTargetの呼び直しだけが届く。TargetKeyが変わったことをここで検知し、
        // 次のOnBuildCompletedで新シーンへ再フレーミングする(ADR-0041、事前調査事実3)。
        if(this.TargetKey is not { } PreviousKey||!PreviousKey.Equals(Snapshot.Key)){
            PendingCameraReset=true;
        }

        this.TargetKey=Snapshot.Key;
        this.TargetSnapshot=Snapshot;
        SceneModel?Model=Snapshot.LastKnownGoodModel??Snapshot.CurrentModel;
        if(Model is not null){
            this.TargetFilePath=Model.FilePath;
            this.TargetClassName=Model.ClassName;
            this.TargetModel=Model;
        }

        if(Surface is not null){
            Rebuild();
        }
    }

    /// <summary>EditorNodeIdを現在snapshotのNodePathへ解決して既存Rendererへ渡します。</summary>
    internal void SetSelection(SceneDocumentSnapshot Snapshot,SceneSelection Selection){
        if(this.TargetKey is not { } Key||!Key.Equals(Snapshot.Key)){
            return;
        }

        this.SelectedNodePath=Selection.PrimaryNodeId is { } Id
            &&Snapshot.Projection?.TryGetNode(Id,out SceneProjectionNode Node)==true
            ?Node.Path
            :null;
        Renderer?.SetSelectedNode(this.SelectedNodePath);

        // FocusSelection(F)の境界球用(M0020 Phase 2)。ハイライトは単一(SetSelectedNode)のままだが、
        // 複数選択時のFはdesign doc §3「選択ノード群の境界球」どおり全選択オブジェクトを収める。
        List<NodePath>ResolvedPaths=[];
        foreach(EditorNodeId NodeId in Selection.NodeIds){
            if(Snapshot.Projection?.TryGetNode(NodeId,out SceneProjectionNode ResolvedNode)==true){
                ResolvedPaths.Add(ResolvedNode.Path);
            }
        }

        this.SelectedNodePaths=ResolvedPaths;
    }

    private void LoadIntoSurface(){
        if(Surface is null){
            return;
        }

        if(RenderingHandler is null){
            RenderingHandler=(_,Args)=>{
                TickFlyIfNeeded(Args);
                ApplyCamera();
                Renderer?.RenderFrame();
            };
            CompositionTarget.Rendering+=RenderingHandler;

            // Subscribed once here (not inside Rebuild, which runs on every re-analysis) - it always
            // resizes whatever Renderer currently holds via the field, so re-subscribing per rebuild
            // would just accumulate redundant duplicate calls per resize.
            // これはサーフェスの実サイズを知る唯一の場所でもあるので、アスペクト比の元もここで更新する。
            ResizeHandler=(W,H)=>{
                this.SurfaceWidth=Math.Max(1,W);
                this.SurfaceHeight=Math.Max(1,H);
                Renderer?.Resize(W,H);
                ApplyCamera();
            };
            Surface.Resized+=ResizeHandler;
        }

        Rebuild();
    }

    /// <summary>
    /// 現在の<see cref="ViewState"/>を<see cref="Renderer"/>へ注入する(ADR-0041 決定事項1、
    /// M0020 Phase 1)。値が変わらなければ<see cref="SceneRenderer.SetCamera"/>は何もしないので
    /// 毎フレーム呼んで構わない。
    /// </summary>
    private void ApplyCamera(){
        if(Renderer is not { } Active||ViewState is not { } State){
            return;
        }

        Active.SetCamera(State.GetView(),State.GetProjection(this.AspectRatio));
    }

    /// <summary>
    /// Fly(RMB+WASDQE)は押しっぱなしのキーでマウスの実移動が無いフレームも前進し続ける必要があるため、
    /// マウスのLook量は<see cref="OnPointerInput"/>が<see cref="PendingLookDeltaX"/>/
    /// <see cref="PendingLookDeltaY"/>へ蓄積するだけに留め、Renderingティックごとにここで1回だけ
    /// <see cref="SceneViewState.TickFly"/>へまとめて渡す。Orbit/PanはMoveイベントごとに即時適用で
    /// 十分(時間積分の要素が無いため、<see cref="OnPointerInput"/>参照)。
    /// </summary>
    private void TickFlyIfNeeded(EventArgs Args){
        if(InputMachine is not { } Machine||ViewState is not { } State||Machine.State!=SceneNavState.Flying){
            LastFlyTickTime=null; // Fly再突入時に古い時刻からの巨大なDeltaTimeが出ないようにする。
            return;
        }

        double DeltaSeconds=0;
        if(Args is RenderingEventArgs RenderArgs){
            if(LastFlyTickTime is { } Previous){
                DeltaSeconds=(RenderArgs.RenderingTime-Previous).TotalSeconds;
            }

            LastFlyTickTime=RenderArgs.RenderingTime;
        }

        bool Shift=(NativeMethods.GetKeyState(NativeMethods.VK_SHIFT)&0x8000)!=0;
        bool Ctrl=(NativeMethods.GetKeyState(NativeMethods.VK_CONTROL)&0x8000)!=0;
        State.TickFly(Machine.FlyHeldKeys,PendingLookDeltaX,PendingLookDeltaY,(float)DeltaSeconds,Shift,Ctrl);
        PendingLookDeltaX=0f;
        PendingLookDeltaY=0f;
    }

    /// <summary>
    /// <see cref="RenderSurfaceWindow.PointerInput"/>の購読先。キャプチャの実行
    /// (<see cref="RenderSurfaceWindow.SetPointerCapture"/>/
    /// <see cref="RenderSurfaceWindow.ReleasePointerCapture"/>)はここが担う —
    /// <see cref="SceneInputStateMachine"/>は純粋でWin32を知らないため、キャプチャすべきかどうかの
    /// 判断だけを<see cref="SceneCaptureAction"/>として返す。
    /// </summary>
    private void OnPointerInput(PointerEvent Event){
        if(InputMachine is not { } Machine){
            return;
        }

        if(Event.Kind==PointerEventKind.Down){
            ViewportActivated?.Invoke();
        }

        // ヒットテストはLMB Down・Alt無し(Orbit開始と競合しない)の時だけ行う(M0020 Phase 2)。
        // 状態機械自身はScenePicker/SceneRenderObjectを知らないため、結果だけを渡す。
        NodePath?HitNodePath=Event.Kind==PointerEventKind.Down&&Event.Button==PointerButton.Left&&!Event.Modifiers.Alt
            ?TryPickAt(Event.X,Event.Y)
            :null;

        SceneInputStepResult Result=Machine.Handle(Event,HitNodePath);
        switch(Result.Capture){
            case SceneCaptureAction.Begin:
                Surface?.SetPointerCapture();
                break;
            case SceneCaptureAction.End:
                Surface?.ReleasePointerCapture();
                break;
        }

        if(Result.SelectionConfirmed){
            ObjectPicked?.Invoke(Result.ConfirmedNodePath,Result.ConfirmedCtrl);
        }

        if(Result.DeltaX==0f&&Result.DeltaY==0f){
            return;
        }

        switch(Result.State){
            case SceneNavState.Flying:
                PendingLookDeltaX+=Result.DeltaX;
                PendingLookDeltaY+=Result.DeltaY;
                break;
            case SceneNavState.Orbiting:
                ViewState?.ApplyOrbit(Result.DeltaX,Result.DeltaY);
                break;
            case SceneNavState.Panning:
                ViewState?.ApplyPan(Result.DeltaX,Result.DeltaY);
                break;
        }
    }

    private NodePath?TryPickAt(int X,int Y){
        if(ViewState is not { } State||CurrentRenderObjects.Count==0){
            return null;
        }

        (Vector3 Origin,Vector3 Direction)=SceneViewCamera.ScreenPointToRay(
            State.GetView(),State.GetProjection(this.AspectRatio),X,Y,SurfaceWidth,SurfaceHeight);
        return ScenePicker.TryPick(CurrentRenderObjects,Origin,Direction,out NodePath Hit)?Hit:(NodePath?)null;
    }

    /// <summary>
    /// <see cref="RenderSurfaceWindow.WheelInput"/>の購読先。設計時はWPFの<c>PreviewMouseWheel</c>で
    /// 受ける計画だったが、実機検証で発火しないことが判明したため、他のマウス入力と同じく
    /// <see cref="RenderSurfaceWindow"/>のWndProcから受け取る(M0020 Phase 1訂正、ADR-0041)。
    /// Deltaは<c>WHEEL_DELTA</c>=120単位。Fly中(RMB押下中)はAdjustNavigationSpeed、それ以外は
    /// Dolly(Perspective)/Zoom(Orthographic)。
    /// </summary>
    private void HandleWheel(int Delta){
        if(InputMachine is not { } Machine||ViewState is not { } State){
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

    /// <summary>WPFの<c>KeyDown</c>から呼ぶ。Fly中(RMB押下中)以外は<see cref="SceneInputStateMachine"/>が無視する。</summary>
    internal void HandleKeyDown(NavigationKey Key)=>InputMachine?.HandleKeyDown(Key);

    internal void HandleKeyUp(NavigationKey Key)=>InputMachine?.HandleKeyUp(Key);

    /// <summary>WPFの<c>LostKeyboardFocus</c>から呼ぶ。RMBは離されていないので状態遷移はしない。</summary>
    internal void ClearHeldFlyKeys()=>InputMachine?.HandleLostKeyboardFocus();

    /// <summary>
    /// WPFのFキーから呼ぶ(M0020 Phase 2)。design doc §4.2「ツール切替キーはIdle時のみ有効」に
    /// 従いIdle以外では無視する。視線方向は変えず、選択ノード群の境界球へ再センタリングのみ行う
    /// (<see cref="SceneViewState.FocusOnBounds"/>)。
    /// </summary>
    internal void RequestFocusSelection(){
        if(InputMachine is not { } Machine||Machine.State!=SceneNavState.Idle){
            return;
        }

        if(ViewState is not { } State||SelectedNodePaths.Count==0){
            return;
        }

        List<SceneRenderObject>Selected=[..CurrentRenderObjects.Where(Object=>SelectedNodePaths.Contains(Object.NodeId))];
        if(Selected.Count==0){
            return;
        }

        (Vector3 Center,float Radius)=SceneCameraFraming.ComputeSelectionBounds(Selected);
        State.FocusOnBounds(Center,Radius);
    }

    /// <summary>(Re)builds <see cref="Renderer"/> from the pin 済み <see cref="SceneDocumentSnapshot"/>.</summary>
    private void Rebuild(){
        if(Surface is null||TargetKey is null||TargetSnapshot is null||TargetFilePath is null||TargetClassName is null){
            return;
        }

        if(TargetModel is not { } Model){
            // Not (yet) found - e.g. analysis hasn't caught up with a just-opened tool window, or
            // the class was renamed/deleted out from under us. Leave the last good frame on screen.
            SetStatus($"解析結果を待っています: {TargetClassName}");
            return;
        }

        if(Model.Root is null){
            DisposeRenderer();
            Microsoft.CodeAnalysis.Diagnostic?Diagnostic=Model.Diagnostics.FirstOrDefault();
            string Detail=Diagnostic is null
                ?"Rootを解析できませんでした。"
                :$"{Diagnostic.Id}: {Diagnostic.GetMessage()}";
            ActivityLog.LogError(nameof(SceneHwndHost),$"{TargetClassName}: {Detail}");
            SetStatus($"シーン解析エラー: {Detail}");
            return;
        }

        SetStatus($"読み込み中: {TargetClassName}");

        // 旧 .scene 実装の WarmMeshCache は、WPF の BuildWindowCore 内でJTF/Taskを同期wait
        // するデッドロックを避けるため、生ThreadへI/Oを隔離してからUI側でJoinしていた。
        // Scene DSL版ではさらに一歩進め、Primitive.Cli生成と後続のGLTF MeshLoaderを含む
        // Build全体をこの単一workerへ渡し、UIスレッドではJoin/Waitを一切行わない。完了後の
        // SceneRenderer生成だけをDispatcherへ戻す。live解析が連続してもworkerはlatest-winsで
        // 未処理要求を置換するため、ブリッジプロセスが無制限に増えることもない。
        BuildWorker?.Request(new SceneRenderBuildRequest(
            Model,
            TargetKey.Value,
            TargetSnapshot.AnalysisGeneration,
            TargetFilePath,
            TargetClassName,
            Surface.Handle));
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
        if(Surface is null
            ||Surface.Handle!=Request.SurfaceHandle
            ||TargetKey is not { } Key
            ||!Key.Equals(Request.Key)
            ||TargetSnapshot?.AnalysisGeneration!=Request.AnalysisGeneration){
            return;
        }

        if(Error is not null){
            ActivityLog.LogError(nameof(SceneHwndHost),Error.ToString());
            if(Renderer is null){
                Surface.SetVisible(false);
            }
            SetStatus($"表示に失敗しました: {Error.Message}");
            return;
        }

        if(Result is null){
            return;
        }

        // 描画オブジェクト0件でもレンダラーは作る(グリッド/ギズモだけのフレームは正当、
        // M0020 Phase 0/Phase 3)。ここでサーフェスを隠すと前フレームの残像や背面ペインの露出を
        // 招くだけで、0件でも RenderFrame は clear + Present を行う。
        try {
            DisposeRenderer();
            (this.SceneBoundsCenter,this.SceneBoundsRadius)=SceneCameraFraming.ComputeBounds(Result.RenderObjects);
            this.CurrentRenderObjects=Result.RenderObjects;

            Matrix4x4 View;
            Matrix4x4 Projection;
            if(ViewState is { } State){
                // Far平面はライブ編集での規模変化に追従させるため、リビルドのたびに無条件で更新する。
                State.LastKnownBoundsRadius=this.SceneBoundsRadius;
                if(PendingCameraReset){
                    // 初回ビルド成功時、またはコンボボックスでシーンが切り替わった直後
                    // (SetTargetがPendingCameraReset=trueを立てる、事前調査事実3)。
                    State.ResetToBounds(
                        this.SceneBoundsCenter,this.SceneBoundsRadius,
                        SceneCameraFraming.ViewDirection,SceneCameraFraming.DefaultFovYRadians);
                    PendingCameraReset=false;
                }

                View=State.GetView();
                Projection=State.GetProjection(this.AspectRatio);
            } else {
                (View,Projection)=SceneCameraFraming.FrameBounds(
                    this.SceneBoundsCenter,this.SceneBoundsRadius,SceneCameraFraming.DefaultFovYRadians,this.AspectRatio);
            }

            // 実サイズは Surface.Resized で既に届いているので、800x600 のハードコードは使わない。
            Renderer=new SceneRenderer(Surface.Handle,SurfaceWidth,SurfaceHeight,Result.RenderObjects,View,Projection);
            Renderer.SetSelectedNode(this.SelectedNodePath);
            Surface.SetVisible(true);
            SetStatus($"表示中: {Result.Request.ClassName}");
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(SceneHwndHost),Ex.ToString());
            DisposeRenderer();
            SetStatus($"表示に失敗しました: {Ex.Message}");
        }
    }

    private void DisposeRenderer(){
        Renderer?.Dispose();
        Renderer=null;
        Surface?.SetVisible(false);
    }

    private void DetachSubscriptions(){
        if(RenderingHandler is not null){
            CompositionTarget.Rendering-=RenderingHandler;
            RenderingHandler=null;
        }

        if(ResizeHandler is not null&&Surface is not null){
            Surface.Resized-=ResizeHandler;
            ResizeHandler=null;
        }
    }

    private void SetStatus(string Status)=>StatusChanged?.Invoke(Status);

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
}
