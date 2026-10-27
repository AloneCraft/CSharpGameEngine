using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// アセットエディターの 3D ビューポート。分離された Render Server プレビュー（ADR-0019/0020）に <see cref="AssetViewportCamera"/> のオービット／パン／ドリーと、
/// プレビューレベルテンプレート（<see cref="PreviewLevelTemplate"/>、M0127）による照明・背景・環境・床を加えたもの。
/// ホストがクラッシュした場合は、WPF が安定したコンテナー HWND を保ったまま DXGI 子サーフェスを作り直して一度だけ再生する。
/// 描画面は Win32 フォーカスを持たない（ADR-0041、<see cref="RenderSurfaceFocusPolicy"/>）。
/// テンプレートの切り替えは対象モデルを読み直さない: ライトと背景は毎フレームのワイヤ（<see cref="RenderPreviewLighting"/>）で差し替え、
/// 床の形が変わるときだけシーンを作り直す。カメラは維持し、初期値へ戻すのは <see cref="FrameMesh"/>（フレーム操作）だけ。
/// </summary>
internal sealed class AssetViewportHost(string ModelPath,PreviewLevelTemplate InitialTemplate):HwndHost {
    /// <summary>入力がなくても描画を更新する間隔（フレーム数）。スワップチェーンの表示を保つ。</summary>
    private const int KeepAliveFrames=30;
    private string ModelPath=ModelPath;
    private string?RepoRoot;
    private RenderSurfaceHostWindow?SurfaceHost;
    private RenderSurfaceWindow?SubscribedSurface;
    private RenderPreviewHostClient?PreviewClient;
    private EventHandler?RenderingHandler;
    private Action<int,int>?ResizeHandler;
    private readonly AssetViewportResizeQueue ResizeQueue=new();
    private readonly AssetViewportRecovery Recovery=new();
    private AuthoringOperationSnapshot?CurrentOperation;
    private SimpleMesh?Mesh;
    private PreviewBounds Bounds;
    private PreviewSceneComposition?Composition;
    private RenderPreviewLighting?Lighting;
    private int EnvironmentRevision;
    private bool CameraInitialized;
    private int FramesSinceRender;
    private bool Dirty=true;
    private int SurfaceWidth=800;
    private int SurfaceHeight=600;
    private RenderPreviewPresentationMode Mode=RenderPreviewPresentationMode.Lit;
    private RenderPreviewAssetSource?Skeletal;
    private string?StaticAssetPath;
    internal bool UsesHostGltf=>this.StaticAssetPath is not null||this.Skeletal is not null;
    private RenderPreviewAnimation?AnimationValue;
    private bool ShowSkeletonValue;
    private int SelectedJointValue=-1;
    internal AssetViewportCamera Camera { get;}=new();
    /// <summary>いま適用しているテンプレート。</summary>
    internal PreviewLevelTemplate Template { get;private set;}=InitialTemplate;
    internal event Action<AuthoringOperationSnapshot>?OperationChanged;
    /// <summary>ホストがスケルタルアセットを読み込んだ（ジョイントとクリップの要約。ADR-0090）。再読み込み・復旧でも上がる。</summary>
    internal event Action<RenderPreviewAssetSummary>?AssetSummaryLoaded;
    /// <summary>
    /// ホストがテンプレートの環境（大気・露出など）を適用できなかったため、標準の Default へ戻した。引数は 拒否されたテンプレート・代わりに適用したテンプレート・理由。
    /// ビューポートを黒いまま止めないための安全な縮退で、呼び出し側はドロップダウンと警告表示を合わせる。
    /// </summary>
    internal event Action<PreviewLevelTemplate,PreviewLevelTemplate,string>?TemplateRejected;
    /// <summary>メッシュを読み込めた（再読み込み・復旧を含む）。統計は Details 欄に表示する。</summary>
    internal event Action<AssetMeshStatistics>?MeshLoaded;
    /// <summary>ビューポートが押された。シェルが WPF のキーボードフォーカスを取るために使う（F キー等）。</summary>
    internal event Action?ViewportPressed;
    /// <summary>スケルタル glTF をホストに読ませる指定。null なら静的なメッシュ。変更するとモデルを読み込み直す（カメラは初期位置へ戻る）。</summary>
    internal RenderPreviewAssetSource?SkeletalSource {
        get=>this.Skeletal;
        set{
            if(Equals(this.Skeletal?.MeshPath,value?.MeshPath)&&Equals(this.Skeletal?.AnimationPath,value?.AnimationPath)){ return; }
            this.Skeletal=value;
            this.AnimationValue=value is null?null:new RenderPreviewAnimation { ClipIndex=-1 };
            this.CameraInitialized=false;
            if(this.Surface is not null){
                this.TearDownRenderer();
                this.LoadIntoSurface(IsRecovery:false);
            }
        }
    }
    /// <summary>true の間、<see cref="SkeletalSource"/> が決まるまでモデルを読み込まない（スキンを持たないアニメーションだけのファイルが、プレビューするメッシュの選択を待つとき）。</summary>
    internal bool AwaitSkeletalSource { get;set;}
    /// <summary>形状・境界を読むファイル。スケルタルではプレビューするメッシュ、そうでなければ開いているファイル。</summary>
    private string GeometryPath=>this.Skeletal?.MeshPath??this.ModelPath;
    /// <summary>アニメーション姿勢（クリップ番号・時刻・ループ）。スケルタルのときだけ有効。ClipIndex が -1 ならバインド姿勢。</summary>
    internal void SetAnimation(int ClipIndex,float TimeSeconds,bool Loop){
        if(this.Skeletal is null){ return; }
        this.AnimationValue=new RenderPreviewAnimation { ClipIndex=ClipIndex,TimeSeconds=Math.Max(0f,TimeSeconds),Loop=Loop };
        this.Dirty=true;
    }
    internal bool ShowSkeleton {
        get=>this.ShowSkeletonValue;
        set{
            if(this.ShowSkeletonValue==value){ return; }
            this.ShowSkeletonValue=value;
            this.Dirty=true;
        }
    }
    /// <summary>強調するジョイント。-1 なら強調しない。</summary>
    internal int SelectedJoint {
        get=>this.SelectedJointValue;
        set{
            if(this.SelectedJointValue==value){ return; }
            this.SelectedJointValue=value;
            this.Dirty=true;
        }
    }
    internal RenderPreviewPresentationMode PresentationMode {
        get=>this.Mode;
        set{
            if(this.Mode==value){ return; }
            this.Mode=value;
            this.Dirty=true;
        }
    }
    private RenderSurfaceWindow?Surface=>this.SurfaceHost?.Surface;
    protected override HandleRef BuildWindowCore(HandleRef HwndParent){
        this.SurfaceHost=new RenderSurfaceHostWindow(HwndParent.Handle,this.SurfaceWidth,this.SurfaceHeight);
        this.LoadIntoSurface(IsRecovery:false);
        return new HandleRef(this,this.SurfaceHost.Handle);
    }
    protected override void DestroyWindowCore(HandleRef Hwnd){
        this.TearDownRenderer();
        this.SurfaceHost?.Dispose();
        this.SurfaceHost=null;
    }
    /// <summary>別のファイルへ再読み込みする（VS が名前変更・再読み込みしたドキュメントに DocData を再利用する場合）。</summary>
    internal void SetModelPath(string ModelPath){
        this.Skeletal=SkeletalAssetSourcePolicy.Retarget(this.Skeletal,this.ModelPath,ModelPath);
        this.ModelPath=ModelPath;
        this.CameraInitialized=false;
        if(this.Surface is not null){
            this.TearDownRenderer();
            this.LoadIntoSurface(IsRecovery:false);
        }
    }
    /// <summary>
    /// 明示的なリセット（フレーム操作・F キー）。メッシュ全体が収まるように、テンプレートが定義するカメラの初期値（方向・視野角・投影方式）へ戻す。
    /// 通常のテンプレート切り替えはユーザーのカメラ操作を維持する。
    /// </summary>
    internal void FrameMesh(){
        if(this.Mesh is not null||this.StaticAssetPath is not null||this.Skeletal is not null){
            this.Camera.Frame(this.Bounds.Center,this.Bounds.Radius,this.Aspect,this.Template.Camera);
            this.CameraInitialized=true;
            this.Dirty=true;
        }
    }
    /// <summary>
    /// テンプレートを切り替える。対象モデルは読み直さず、カメラも維持する。ライトと背景は次のフレームで差し替わり、
    /// 床の形（大きさ・色・グリッド）が変わる場合だけシーンを作り直す。メッシュ読み込み前に呼ぶと、読み込み後に適用する。
    /// </summary>
    internal void SetTemplate(PreviewLevelTemplate Template){
        this.Template=Template;
        if(this.PreviewClient is null){
            if(this.Surface is not null){ this.LoadIntoSurface(IsRecovery:false); }
            return;
        }
        if(this.Mesh is null&&this.StaticAssetPath is null&&this.Skeletal is null){ return; }
        try {
            var Next=this.Compose();
            this.Lighting=PreviewLevelWire.CreateLighting(Template,this.Bounds,++this.EnvironmentRevision);
            if(this.Composition is null||this.Composition.FloorSignature!=Next.FloorSignature){
                this.PreviewClient.ReplaceScene(Next.Scene);
            }
            this.Composition=Next;
            this.Dirty=true;
        } catch(RenderPreviewHostException Error){
            this.HandleHostFailure(Error);
        }
    }
    private float Aspect=>this.SurfaceWidth/(float)Math.Max(1,this.SurfaceHeight);
    private PreviewSceneComposition Compose(){
        this.Camera.SetFarReach(PreviewSceneComposer.FarReachRadii(this.Template));
        var Next=PreviewSceneComposer.Compose(this.Mesh,this.Bounds,this.Template,this.SurfaceWidth,this.SurfaceHeight,this.Skeletal,this.StaticAssetPath);
        // ReplaceScene／Open が最初に Present するフレームで、固定の既定カメラが一瞬映らないように、いまのカメラを入れておく。
        Next.Scene.Camera=RenderPreviewSceneBuilder.CreateCamera(this.Camera.View,this.Camera.Projection(this.Aspect),this.Camera.Eye,this.Camera.FovYRadians);
        return Next;
    }
    private bool LoadIntoSurface(bool IsRecovery){
        var Surface=this.Surface;
        if(Surface is null||(this.AwaitSkeletalSource&&this.Skeletal is null)){ return false; }
        if(!IsRecovery){ this.Recovery.Reset(); }
        var RequestId=Guid.NewGuid();
        this.PublishOperation(AuthoringOperationSnapshot.Start(RequestId,AuthoringHostOperation.Import,"Import",VsixText.Get("AssetPreview_Loading")));
        try {
            this.RepoRoot??=CgeProjectContext.Resolve(this.ModelPath)?.WorkspaceRoot;
            if(this.RepoRoot is null){ throw new InvalidOperationException(VsixText.Get("Common_GameProjectNotFound")); }
            var GltfInfo=this.Skeletal is null?GltfSkeletalProbe.Probe(this.GeometryPath):null;
            this.StaticAssetPath=GltfInfo is{ SkinCount:0,IsAnimationOnly:false }?this.GeometryPath:null;
            var HostAsset=this.StaticAssetPath is not null||this.Skeletal is not null;
            var Loaded=HostAsset?null:MeshLoader.Load(this.GeometryPath,this.RepoRoot);
            this.PublishOperation(this.CurrentOperation!.ApplyProgress(new AuthoringHostProgress { RequestId=RequestId,Sequence=1,Fraction=0.6f,Phase="Preview" },VsixText.Get("AssetPreview_PreparingPreview")));
            var HostPath=RenderPreviewSceneBuilder.ResolveHostPath(this.RepoRoot);
            var ShaderArtifactPath=RenderPreviewSceneBuilder.ResolveShaderArtifactPath(this.RepoRoot);
            this.SurfaceWidth=this.SurfaceHost?.Width??this.SurfaceWidth;
            this.SurfaceHeight=this.SurfaceHost?.Height??this.SurfaceHeight;
            this.Mesh=Loaded;
            var Statistics=Loaded is null?null:AssetMeshStatistics.From(Loaded);
            this.Bounds=Loaded is null?new PreviewBounds(new System.Numerics.Vector3(-0.5f),new System.Numerics.Vector3(0.5f),System.Numerics.Vector3.Zero,1f):PreviewBounds.From(Loaded,Statistics!);
            var NeedsInitialFrame=!this.CameraInitialized;
            if(!this.CameraInitialized){
                this.Camera.Frame(this.Bounds.Center,this.Bounds.Radius,this.Aspect,this.Template.Camera);
                if(!HostAsset){ this.CameraInitialized=true; }
            }
            this.Composition=this.Compose();
            this.Lighting=PreviewLevelWire.CreateLighting(this.Template,this.Bounds,++this.EnvironmentRevision);
            this.PreviewClient=RenderPreviewHostClient.Start(HostPath);
            var Opened=this.PreviewClient.Open(Surface.Handle.ToInt64(),this.SurfaceWidth,this.SurfaceHeight,ShaderArtifactPath,this.Composition.Scene);
            if(HostAsset){
                var HostBounds=this.Skeletal is not null?Opened.AssetSummary?.RestBounds:Opened.StaticBounds;
                if(!PreviewBounds.TryFrom(HostBounds,out var ActualBounds)){ throw new InvalidOperationException("Asset bounds are missing or invalid."); }
                this.Bounds=ActualBounds;
                Statistics=AssetMeshStatistics.From(HostBounds!,ActualBounds);
                if(NeedsInitialFrame){ this.Camera.Frame(this.Bounds.Center,this.Bounds.Radius,this.Aspect,this.Template.Camera);this.CameraInitialized=true; }
                var ActualComposition=this.Compose();
                if(this.Composition.FloorSignature!=ActualComposition.FloorSignature){ this.PreviewClient.ReplaceScene(ActualComposition.Scene); }
                this.Composition=ActualComposition;
                this.Lighting=PreviewLevelWire.CreateLighting(this.Template,this.Bounds,++this.EnvironmentRevision);
            }
            // Openだけではテンプレートの環境は未適用。最初のフレームが成功してからReady／縮退成功を通知する。
            this.PreviewClient.UpdateFrame(this.CreateFrame());
            this.PreviewClient.Render(captureForVerification:false);
            this.PublishOperation(this.CurrentOperation!.Complete(new AuthoringHostResult {
                RequestId=RequestId,State=AuthoringHostTerminalState.Succeeded,ExitCode=0,Output=VsixText.Get("AssetPreview_Ready"),
                Progress=new AuthoringHostProgress { RequestId=RequestId,Sequence=2,Fraction=1f,Phase="Completed" },
            }));
            this.ResizeHandler=(W,H)=>{
                if(W!=0||H!=0){
                    this.SurfaceWidth=Math.Max(1,W);
                    this.SurfaceHeight=Math.Max(1,H);
                }
                this.Dirty=true;
                this.ResizeQueue.Request(W,H);
            };
            Surface.Resized+=this.ResizeHandler;
            Surface.PointerInput+=this.OnPointerInput;
            Surface.WheelInput+=this.OnWheelInput;
            this.SubscribedSurface=Surface;
            this.RenderingHandler=(_,_)=>this.OnRendering();
            CompositionTarget.Rendering+=this.RenderingHandler;
            this.Dirty=true;
            this.MeshLoaded?.Invoke(Statistics!);
            if(Opened.AssetSummary is{ }Summary){ this.AssetSummaryLoaded?.Invoke(Summary); }
            return true;
        } catch(Exception Error){
            ActivityLog.LogError(nameof(AssetViewportHost),Error.ToString());
            var Message=VsixText.Format("AssetPreview_LoadFailed",Error.Message);
            var Operation=this.CurrentOperation??AuthoringOperationSnapshot.Start(RequestId,AuthoringHostOperation.Import,"Import",Message);
            this.PublishOperation(Error is AuthoringHostOperationException HostFailure?Operation.Fail(Message,HostFailure.Result.Diagnostics):Operation.Fail("ASSET_IMPORT_FAILED",Message));
            this.TearDownRenderer();
            if(!IsRecovery&&Error is RenderPreviewHostException PreviewError){ return this.HandleHostFailure(PreviewError); }
            return false;
        }
    }
    private void OnRendering(){
        var Client=this.PreviewClient;
        if(Client is null){ return; }
        if(!this.Dirty&&++this.FramesSinceRender<KeepAliveFrames){ return; }
        try {
            this.ResizeQueue.Apply((W,H)=>{ Client.Resize(W,H); });
            Client.UpdateFrame(this.CreateFrame());
            Client.Render(captureForVerification:false);
            this.Dirty=false;
            this.FramesSinceRender=0;
        } catch(RenderPreviewHostException Error){
            this.HandleHostFailure(Error);
        }
    }
    private RenderPreviewFrame CreateFrame(){
        var Aspect=this.Aspect;
        return new RenderPreviewFrame {
            Camera=RenderPreviewSceneBuilder.CreateCamera(this.Camera.View,this.Camera.Projection(Aspect),this.Camera.Eye,this.Camera.FovYRadians),
            PresentationMode=this.Mode,
            Lighting=this.Lighting,
            Animation=this.AnimationValue,
            ShowSkeleton=this.ShowSkeletonValue,
            SelectedJoint=this.SelectedJointValue,
            Objects=this.Composition!.Objects,
        };
    }
    private void OnPointerInput(PointerEvent Event){
        if(Event.Kind==PointerEventKind.Down){ this.ViewportPressed?.Invoke(); }
        switch(this.Camera.Handle(Event)){
            case AssetViewportNavigationResult.CaptureStarted:
                this.Surface?.SetPointerCapture();
                break;
            case AssetViewportNavigationResult.CaptureEnded:
                this.Surface?.ReleasePointerCapture();
                break;
            case AssetViewportNavigationResult.CameraChanged:
                this.Dirty=true;
                break;
        }
    }
    private void OnWheelInput(int Delta){
        var Notches=Delta/120;
        if(Notches==0){ return; }
        this.Camera.Dolly(Notches);
        this.Dirty=true;
    }
    private bool HandleHostFailure(RenderPreviewHostException Error){
        ActivityLog.LogError(nameof(AssetViewportHost),Error.ToString());
        this.TearDownRenderer();
        if(this.SurfaceHost is{ }Host){
            var Rejected=this.Template;
            var Safe=PreviewLevelBuiltIns.Default();
            try {
                if(this.Recovery.TryRecover(Error.RequiresFreshSurfaceGeneration,
                    string.Equals(this.Template.Id,PreviewLevelBuiltIns.DefaultId,StringComparison.OrdinalIgnoreCase),
                    ReloadFresh:()=>{
                        _=Host.RecreateSurface(InitiallyVisible:true);
                        return this.LoadIntoSurface(IsRecovery:true);
                    },
                    LoadDefault:()=>{
                        this.Template=Safe;
                        _=Host.RecreateSurface(InitiallyVisible:true);
                        return this.LoadIntoSurface(IsRecovery:true);
                    },
                    DefaultApplied:()=>this.TemplateRejected?.Invoke(Rejected,Safe,Error.Message))){ return true; }
            } catch(Exception RecoveryError){
                ActivityLog.LogError(nameof(AssetViewportHost),RecoveryError.ToString());
            }
        }
        if(this.CurrentOperation is{ State:not AuthoringOperationUiState.Failed }Operation){
            this.PublishOperation(Operation.Fail("ASSET_PREVIEW_HOST_FAILED",Error.Message));
        }
        this.Surface?.SetVisible(false);
        return false;
    }
    private void PublishOperation(AuthoringOperationSnapshot Operation){
        this.CurrentOperation=Operation;
        this.OperationChanged?.Invoke(Operation);
    }
    private void TearDownRenderer(){
        this.ResizeQueue.Clear();
        if(this.RenderingHandler is not null){
            CompositionTarget.Rendering-=this.RenderingHandler;
            this.RenderingHandler=null;
        }
        if(this.SubscribedSurface is{ }Subscribed){
            if(this.ResizeHandler is not null){ Subscribed.Resized-=this.ResizeHandler; }
            Subscribed.PointerInput-=this.OnPointerInput;
            Subscribed.WheelInput-=this.OnWheelInput;
            Subscribed.ReleasePointerCapture();
        }
        this.ResizeHandler=null;
        this.SubscribedSurface=null;
        this.PreviewClient?.Dispose();
        this.PreviewClient=null;
    }
}
