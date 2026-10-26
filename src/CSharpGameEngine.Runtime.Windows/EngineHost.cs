using CSharpGameEngine.Rendering.Server;
using System.Numerics;
using CSharpGameEngine.Audio;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Audio.XAudio2;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Input;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Simulation;
using CSharpGameEngine.World;
using RenderCamera=CSharpGameEngine.Rendering.View.RenderCamera;
using EcsWorld=CSharpGameEngine.Entities.World;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// ADR-0070 B1：Scene Designerなど、別プロセスのビューのように、ホストが自身で作成せず描画するウィンドウ。
/// ホストはそのウィンドウのメッセージ処理も破棄も行わない。
/// </summary>
internal readonly record struct ExternalRenderSurface(nint Handle,int Width,int Height,GraphicsDeviceOptions DeviceOptions);

/// <summary>
/// ADR-0070 B1：Scene Designerのカメラ。ランタイムが適用する場所でシーンのアクティブカメラと置き換わるため、
/// ゲームのカメラと同じカメラ相対経路でビューを描画する。
/// </summary>
public readonly record struct EditorCameraPose(Transform Pose,float FovYRadians,float NearZ,float FarZ);

public readonly record struct AudioSourceSnapshot(
    Entity Entity,Vector3 ListenerRelativePosition,float Volume,
    AudioSpatialMix SpatialMix,bool Loop){
    public float EffectiveGain=>this.Volume*this.SpatialMix.Attenuation;
}

/// <summary>
/// 1つのゲームループが使う、プロセス内ランタイムリソースを所有する。ECS状態はこのコンテキストを借用する。
/// ネイティブウィンドウ、レンダラー、オーディオボイス、ホストリースはコンポーネントに保存しない。
/// </summary>
public sealed class EngineHost:IDisposable,IPhysicsHost {
    private readonly ReferenceFrameGraph FrameGraphStorage=new(ReferenceFrameId.Root);
    private readonly WorldOwner WorldStorage=new();
    private readonly List<AudioSourceSnapshot>AudioSourcesStorage=[];
    private AudioDeviceServer?AudioDevice;
    private bool Disposed;
    private FixedAffineTransform AudioListenerFrameLocalPose;
    private ReferenceFrameId AudioListenerFrame;
    private Entity AudioListenerEntity;
    private bool HasAudioListener;
    private readonly Dictionary<Entity,int[]>SkinnedMeshIndices=[];
    private readonly Dictionary<Entity,List<int>>RenderObjectsBySource=[];
    private (int MaterialIndex,LogicalScalar CellMeters)[]WorldGridMaterials=[];
    /// <summary>ADR-0078/M0105：レベル読み込み後、レンダラーのオブジェクトをEntitiesに同期させる。<see cref="LevelLoad"/>がレンダラーを構築するまではnull。</summary>
    internal RenderRoster?Roster { get;set;}
    private readonly RenderCameraPreview[]RadialObserverPreviews=new RenderCameraPreview[RenderCameraPreview.MaximumCount];
    private int RadialObserverPreviewCount;
    private bool RadialObserverPreviewsSubmitted;

    internal Win32Window?Window { get;private set; }
    internal ExternalRenderSurface?ExternalSurface { get;private set; }
    internal EditorCameraPose?EditorCamera { get;private set; }
    internal RenderServer?Renderer { get;private set; }
    internal InputServer Input { get;private set; }=new(new InputActionMap([]));
    internal RenderCamera RenderCamera { get;set; }
    internal FixedAffineTransform CameraFrameLocalOrigin { get;private set; }
    internal ReferenceFrameId CameraFrame { get;private set; }=ReferenceFrameId.Root;
    internal ReferenceFrameId PhysicsFrame=>this.Physics.Frame;
    internal bool HasCameraPose { get;private set; }
    public ReferenceFrameGraph ReferenceFrames=>this.FrameGraphStorage;
    public ref EcsWorld World=>ref this.WorldStorage.World;
    public FixedTickScheduler FixedTicks { get; }=new();
    public State CreateState()=>this.WorldStorage.CreateState();
    public WorldCellStreamingService?WorldStreaming { get;private set; }
    public ReferenceFrameSnapshot FrameSnapshot=>this.FrameGraphStorage.Current;
    public IReadOnlyList<AudioSourceSnapshot>AudioSources=>this.AudioSourcesStorage;
    public AudioMixer Audio=>this.EnsureAudioDevice().Mixer;
    private Audio?SoundStorage;
    /// <summary>ゲームコードが効果音を鳴らす入口(ADR-0077)。System の引数に <c>Audio</c> と書くと、これが渡される。</summary>
    public Audio Sound=>this.SoundStorage??=new Audio(this);
    private Hud?HudStorage;
    /// <summary>画面に出す情報の入口(ADR-0077)。System の引数に <c>Hud</c> と書くと、これが渡される。</summary>
    public Hud Hud=>this.HudStorage??=new Hud(this);
    internal void SetWindowTitle(string Title)=>this.Window?.SetTitle(Title);
    private ScreenUi?UiStorage;
    /// <summary>画面に文字と画像を出す入口(ADR-0078/M0104)。System の引数に <c>ScreenUi</c> と書くと、これが渡される。</summary>
    public ScreenUi Ui=>this.UiStorage??=new ScreenUi(this);
    /// <summary>このフレームに集まった画面 UI をレンダラーへ渡す。何も無ければ画面レイヤーは設定しない(そのフレームは UI を描かない)。</summary>
    internal void SubmitScreenUi(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.ScreenLayerFingerprint=0;
        if(this.UiStorage is not { }Ui||this.Renderer is not { }Surface){ return; }
        if(Ui.Build(Surface.SurfaceWidth,Surface.SurfaceHeight,out var Vertices,out var Batches)){
            Surface.SetScreenLayer(Vertices,Batches);
            if(this.Automation is not null){ this.ScreenLayerFingerprint=ScreenUi.Fingerprint(Vertices); }
        }
    }
    /// <summary>自動検証が読む、このフレームの画面レイヤーの頂点の指紋(UI が無いフレームは 0)。</summary>
    internal ulong ScreenLayerFingerprint { get;private set; }
    private Picking?PickingStorage;
    /// <summary>ポインターの下の物を求める入口(ADR-0077)。System の引数に <c>Picking</c> と書くと、これが渡される。</summary>
    public Picking Picking=>this.PickingStorage??=new Picking(this);
    /// <summary>
    /// このフレームのポインター。自動検証の <c>CGE_AUTOMATION_CURSOR</c> があればそれ、なければウィンドウ上の実際のカーソル。
    /// ウィンドウが無い、領域の外にあるときは <c>Inside</c> が false。
    /// </summary>
    public CSharpGameEngine.Input.Pointer Cursor{
        get{
            if(this.Automation?.ScriptedCursor is { }Scripted){ return new CSharpGameEngine.Input.Pointer(Scripted.X,Scripted.Y,true); }
            if(this.Window is not { }Surface||Surface.ClientWidth<=0||Surface.ClientHeight<=0||!Surface.TryGetCursorClient(out var X,out var Y)){ return default; }
            var Inside=X>=0&&Y>=0&&X<Surface.ClientWidth&&Y<Surface.ClientHeight;
            return new CSharpGameEngine.Input.Pointer(X/(float)Surface.ClientWidth,Y/(float)Surface.ClientHeight,Inside);
        }
    }
    /// <summary>
    /// カメラからポインター(クライアント領域の割合)を通る光線を物理のフレームで返す。カメラが無い、カメラが物理と別の
    /// 参照フレームにある、数値が収まらないときは false。
    /// </summary>
    internal bool TryPointerRay(float X,float Y,out PhysicsRay Ray){
        Ray=default;
        if(!this.HasCameraPose||this.CameraFrame!=this.Physics.Frame||!(this.RenderCamera.FovYRadians>0f)){ return false; }
        var Width=this.Window?.ClientWidth??0;
        var Height=this.Window?.ClientHeight??0;
        if(this.Window is null&&this.Renderer is { }Surface){
            Width=Surface.SurfaceWidth;
            Height=Surface.SurfaceHeight;
        }
        if(Width<=0||Height<=0){ return false; }
        return Picking.TryCreateRay(this.CameraFrameLocalOrigin.Logical,this.RenderCamera.FovYRadians,Width/(float)Height,X,Y,out Ray);
    }
    /// <summary>M0096 G：物理とキャラクターのシステムが<see cref="IPhysicsHost"/>経由でアクセスする物理ドメイン。</summary>
    public PhysicsDomainRegistry Physics { get; }
    public IReadOnlyPhysicsWorld PhysicsWorld=>this.Physics.Active.World;
    public PhysicsCommands PhysicsCommands=>this.Physics.Active.Commands;
    public PhysicsCachePlaybackState PhysicsCacheState=>this.Physics.Active.CacheState;

    public EngineHost(){
        this.Physics=new PhysicsDomainRegistry(Frame=>this.FrameSnapshot.Contains(Frame));
    }

    public bool TryLoadPhysicsCache(
        Stream Input,in SimulationCacheIdentity Identity,bool AllowRealtimeHandoff,out string Error){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        return this.Physics.Active.TryLoadCache(Input,Identity,AllowRealtimeHandoff,out Error);
    }

    public bool TryHandoffPhysicsCache(ulong NextTick){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        return this.Physics.Active.TryHandoffCache(NextTick);
    }

    /// <summary>ホスト所有のサイズ上限付き作業領域を使い、完了した固定ティックを1つストリーミングする。</summary>
    public void WritePhysicsCacheFrame(PhysicsCacheStreamWriter Writer,ulong CompletedTick){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.Physics.Active.WriteCacheFrame(Writer,CompletedTick);
    }

    public void OwnWorldStreaming(WorldCellStreamingService Streaming){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        ArgumentNullException.ThrowIfNull(Streaming);
        if(this.WorldStreaming is not null&&!ReferenceEquals(this.WorldStreaming,Streaming)){
            throw new InvalidOperationException("EngineHost already owns a world streaming service.");
        }
        this.WorldStreaming=Streaming;
    }

    public void SetPhysicsFrame(ReferenceFrameId Frame){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.Physics.SetFrame(Frame);
    }

    public IReadOnlyPhysicsWorld GetPhysicsWorld(ReferenceFrameId Frame)=>this.Physics.World(Frame);
    public bool TryGetPhysicsSyncFailure(Entity Entity,out PhysicsFaultType Failure)=>
        this.Physics.Active.TryGetSyncFailure(Entity,out Failure);
    internal PhysicsDomainRuntime ActivePhysicsDomain=>this.Physics.Active;
    internal AudioDeviceServer EnsureAudioDevice(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        return this.AudioDevice??=new AudioDeviceServer(AllowSilentFallback:true);
    }

    public void SetInput(InputServer Input){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.Input=Input??throw new ArgumentNullException(nameof(Input));
    }

    /// <summary>
    /// ホスト自身のウィンドウではなく、<paramref name="Handle"/>へ描画する。
    /// 最初のフレームより前に呼び出すこと。以後、レベルはこのサーフェスへ読み込まれ、入力は読み取られない。
    /// </summary>
    public void UseExternalSurface(nint Handle,int Width,int Height,GraphicsDeviceOptions DeviceOptions){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        ArgumentNullException.ThrowIfNull(DeviceOptions);
        if(Handle==0){ throw new ArgumentException("The surface handle is null.",nameof(Handle)); }
        if(this.Window is not null||this.Renderer is not null){
            throw new InvalidOperationException("The external surface must be set before the level loads.");
        }
        this.ExternalSurface=new ExternalRenderSurface(Handle,Width,Height,DeviceOptions);
    }

    public void ResizeExternalSurface(int Width,int Height){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if(this.ExternalSurface is not { }Surface){
            throw new InvalidOperationException("The host does not render into an external surface.");
        }
        this.ExternalSurface=Surface with { Width=Width,Height=Height };
        this.Renderer?.Resize(Width,Height);
    }

    public void SetEditorCamera(in EditorCameraPose Camera){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.EditorCamera=Camera;
    }

    public void ClearEditorCamera()=>this.EditorCamera=null;

    internal Win32Window EnsureWindow(in AppHost App){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        return this.Window??=new Win32Window(new Win32WindowOptions {
            Title=App.Title,Width=App.Width,Height=App.Height,Visible=this.Automation is null,
            // 自動実行では、スクリプトで指定されたキー（CGE_AUTOMATION_INPUT）だけで操作する。
            IgnoreDeviceInput=this.Automation is not null,
        });
    }

    internal void AttachRenderer(RenderServer Renderer,in RenderCamera Camera,
        in FixedAffineTransform CameraFrameLocalOrigin,ReferenceFrameId CameraFrame){
        this.OwnRenderer(Renderer);
        if(!this.FrameSnapshot.Contains(CameraFrame)){
            throw new InvalidOperationException($"The camera reference frame is not active: {CameraFrame}.");
        }
        this.RenderCamera=Camera;
        this.SetCameraPose(CameraFrameLocalOrigin,CameraFrame);
    }

    internal void OwnRenderer(RenderServer Renderer){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        ArgumentNullException.ThrowIfNull(Renderer);
        if(this.Renderer is not null&&!ReferenceEquals(this.Renderer,Renderer)){
            Renderer.Dispose();
            throw new InvalidOperationException("EngineHost already owns a renderer.");
        }
        // ADR-0078/M0111：アダプターの既定のスケーラビリティを使う（GPUではLumen、WARPではプローブボリューム）。CGE_SCALABILITY_GI／_REFLECTIONSで個別に指定可能。
        Renderer.Scalability=RenderScalabilityResolver.FromEnvironment(Renderer.IsWarp);
        Renderer.LumenVisualization=RenderScalabilityResolver.VisualizationFromEnvironment();
        this.Renderer=Renderer;
    }

    internal void SetCameraPose(in FixedAffineTransform CameraFrameLocalOrigin,ReferenceFrameId CameraFrame){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if(!this.FrameSnapshot.Contains(CameraFrame)){
            throw new InvalidOperationException($"The camera reference frame is not active: {CameraFrame}.");
        }
        this.CameraFrameLocalOrigin=CameraFrameLocalOrigin;
        this.CameraFrame=CameraFrame;
        this.HasCameraPose=true;
    }

    /// <summary>
    /// ワールド空間グリッドを持つマテリアルを記録する。レンダラーがカメラ相対空間で処理する場合も、
    /// 毎フレーム、線をワールド格子に沿わせられる。
    /// </summary>
    internal void RegisterWorldGridMaterials(IReadOnlyList<StageMaterialCreateInfo>Materials){
        this.WorldGridMaterials=[];
        this.AppendWorldGridMaterials(0,Materials);
    }

    /// <summary>レベル読み込み後にレンダラーへ追加されたマテリアルについても同様に記録する（M0105）。<paramref name="FirstIndex"/>は追加された最初のマテリアルのインデックス。</summary>
    internal void AppendWorldGridMaterials(int FirstIndex,IReadOnlyList<StageMaterialCreateInfo>Materials){
        List<(int,LogicalScalar)>Grids=[.. this.WorldGridMaterials];
        for(var Index=0;Index<Materials.Count;Index++){
            if(Materials[Index].WorldGrid is{ }Grid&&LogicalScalar.TryFromSingle(Grid.GridSizeMeters,out var CellMeters)&&CellMeters.Raw>0){
                Grids.Add((FirstIndex+Index,CellMeters));
            }
        }
        this.WorldGridMaterials=[.. Grids];
    }

    /// <summary>
    /// 1グリッドセルに対する位置の剰余を[0, cell]の範囲で表す。Q64.64で剰余を正確に求めてから一度だけfloatへ丸めるため、
    /// カメラがワールド原点からどれだけ離れていてもアンカーはfloatの精度を保つ。
    /// </summary>
    internal static float ReduceToGridCell(LogicalScalar Position,LogicalScalar CellMeters){
        var Remainder=Position.Raw%CellMeters.Raw;
        if(Remainder<0){
            Remainder+=CellMeters.Raw;
        }
        LogicalScalar.FromRaw(Remainder).TryToSingle(out var Reduced);
        return Reduced;
    }

    /// <summary>このフレーム用の観測者インセットをキューに入れる（M0099）。レンダラーの最大数を超えるプレビューは破棄する。</summary>
    internal void AddRadialObserverPreview(in RenderCameraPreview Preview){
        if(this.RadialObserverPreviewCount<this.RadialObserverPreviews.Length){
            this.RadialObserverPreviews[this.RadialObserverPreviewCount++]=Preview;
        }
    }

    /// <summary>アセット作成したEntityのメッシュがどのレンダラーオブジェクトになったかを記録し、エディターの選択に使う。</summary>
    internal void RegisterRenderObject(Entity Source,int ObjectIndex){
        if(!this.RenderObjectsBySource.TryGetValue(Source,out var Objects)){
            this.RenderObjectsBySource[Source]=Objects=[];
        }
        Objects.Add(ObjectIndex);
    }

    /// <summary><paramref name="Source"/>のレンダラーオブジェクト1つの描画を停止する（M0105）。Entityが破棄されたか、メッシュがなくなった場合に呼び出す。</summary>
    internal void ReleaseRenderObject(Entity Source,int ObjectIndex){
        if(this.Roster is{ }Roster){
            Roster.Release(Source,ObjectIndex,this);
        } else {
            this.Renderer?.RemoveObject(ObjectIndex);
            this.UnregisterRenderObject(Source,ObjectIndex);
        }
    }

    /// <summary><paramref name="Source"/>がどのレンダラーオブジェクトを所有していたか忘れる。オブジェクトの描画が停止された場合に使う（M0105）。</summary>
    internal void UnregisterRenderObject(Entity Source,int ObjectIndex){
        if(!this.RenderObjectsBySource.TryGetValue(Source,out var Objects)){ return; }
        Objects.Remove(ObjectIndex);
        if(Objects.Count==0){ this.RenderObjectsBySource.Remove(Source); }
    }

    /// <summary>ADR-0070 B1：Scene Designerで選択したEntityに対応するレンダラーオブジェクトをハイライトする。</summary>
    public void SetEditorSelection(IReadOnlyCollection<Entity>Selected){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        ArgumentNullException.ThrowIfNull(Selected);
        if(this.Renderer is not { }Renderer){ return; }
        foreach(var (Source,Objects) in this.RenderObjectsBySource){
            var Weight=Selected.Contains(Source)?1f:0f;
            foreach(var Object in Objects){ Renderer.SetObjectSelectionWeight(Object,Weight); }
        }
    }

    /// <summary>ADR-0070 B1：カメラ相対の描画空間でScene Designerのギズモを表す。</summary>
    public void SetEditorOverlay(ReadOnlySpan<CSharpGameEngine.Rendering.RenderOverlayVertex>Triangles,ReadOnlySpan<CSharpGameEngine.Rendering.RenderOverlayVertex>Lines){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.Renderer?.SetOverlay(Triangles,Lines);
    }

    /// <summary>スケルタルメッシュのEntityがスキニングするレンダラーメッシュを記録する（ADR-0066）。</summary>
    internal void RegisterSkinnedMeshes(Entity Entity,int[]MeshIndices)=>this.SkinnedMeshIndices[Entity]=MeshIndices;
    internal bool TryGetSkinnedMeshes(Entity Entity,out int[]MeshIndices)=>this.SkinnedMeshIndices.TryGetValue(Entity,out MeshIndices!);
    /// <summary>スケルタルメッシュのEntityがスキニングしていたレンダラーメッシュを忘れる（M0107）。Entityが消え、メッシュは登録一覧のプールへ戻る。</summary>
    internal void UnregisterSkinnedMeshes(Entity Entity)=>this.SkinnedMeshIndices.Remove(Entity);
    /// <summary><paramref name="Source"/>に属するレンダラーオブジェクトがまだあるかどうか。</summary>
    internal bool HasRenderObjects(Entity Source)=>this.RenderObjectsBySource.ContainsKey(Source);

    internal bool TryGetCameraPose(out FixedAffineTransform Origin,out ReferenceFrameId Frame){
        Origin=this.CameraFrameLocalOrigin;
        Frame=this.CameraFrame;
        return this.HasCameraPose;
    }

    /// <summary>このフレームにPresentation Systemが集めた環境ライティングComponent(ADR-0069)。</summary>
    internal EnvironmentCollection Environment { get; }=new();

    /// <summary>環境変数で要求された自動検証。未設定ならnull(<see cref="RuntimeAutomation"/>)。</summary>
    public RuntimeAutomation?Automation { get; }=RuntimeAutomation.FromEnvironment();

    /// <summary>
    /// 生成されたゲームループが各フレームに渡す経過時間。自動実行では1フレームにつき固定ティックの上限分だけ進める
    ///（60 Hzで8ティック）。そのため、フレーム番号に基づく入力と観測結果はアダプターの描画速度に依存しない。
    /// WARPは十分遅いため毎フレーム上限に達するが、GPUではそうならない。それ以外では実測時間を使う。
    /// </summary>
    public (long ElapsedCounter,long CounterFrequency)FrameTime(long MeasuredElapsedCounter,long CounterFrequency){
        var Time=this.Automation is null
            ?(MeasuredElapsedCounter,CounterFrequency)
            :RuntimeAutomation.FixedFrameTime(this.FixedTicks);
        this.CloudClock.Advance(Time.Item1,Time.Item2);
        return Time;
    }
    /// <summary>雲の移動に使うゲーム時間（ADR-0078/M0110）。各フレームの経過時間を使うため、自動実行時の雲はどのアダプターでも同じになる。</summary>
    private EnvironmentClock CloudClock;

    /// <summary>1フレームを表示する。自動検証が規定フレーム数に達したらfalseを返し、ループ終了を要求する。</summary>
    internal bool Present(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if((this.ExternalSurface is not null||this.Window is { IsMinimized:false })&&this.Renderer is { } Renderer){
            this.SubmitEnvironment(Renderer);
            Renderer.RenderFrame(this.Automation?.CapturesNextFrame??false);
            this.Automation?.RecordPresented(Renderer,this.AutomationCameraPosition(),this.Sound.FlushedRequests,this.AudioDevice?.OneShotsStarted??0,this.ScreenLayerFingerprint,this.AutomationFocusPosition);
        }
        return this.Automation is not { Completed:true };
    }

    /// <summary>
    /// アクティブカメラがぶら下がるEntity（ADR-0078/M0107。スプリングアームが追従するキャラクター）の位置を、カメラの基準座標系で表す。
    /// 親がいないカメラではnull。キャラクターより遅れて動くカメラはキャラクターの位置を表さなくなったため、自動実行レポートでは両方を記録する。
    /// </summary>
    internal Vector3? AutomationFocusPosition { get;set;}

    private Vector3? AutomationCameraPosition()=>
        this.HasCameraPose&&this.CameraFrameLocalOrigin.M41.TryToSingle(out var R)&&this.CameraFrameLocalOrigin.M42.TryToSingle(out var U)&&this.CameraFrameLocalOrigin.M43.TryToSingle(out var F)
            ?new Vector3(R,U,F):null;

    private void SubmitEnvironment(RenderServer Renderer){
        var Collected=this.Environment;
        if(this.HasCameraPose&&this.CameraFrameLocalOrigin.M41.TryToSingle(out var CameraR)&&this.CameraFrameLocalOrigin.M43.TryToSingle(out var CameraF)){
            Collected.CameraHorizontalMeters=new Vector2(CameraR,CameraF);
        }
        Collected.CloudSeconds=this.CloudClock.Seconds;
        // M0110：グローバル距離場のボクセルはワールドグリッドに配置する。カメラを描画空間の原点とする。
        if(Collected.DistanceField is { }Field&&this.HasCameraPose
            &&WorldVoxelGrid.TryLocate(this.CameraFrameLocalOrigin.M41,Field.VoxelSizeMeters,out var VoxelR,out var RemainderR)
            &&WorldVoxelGrid.TryLocate(this.CameraFrameLocalOrigin.M42,Field.VoxelSizeMeters,out var VoxelU,out var RemainderU)
            &&WorldVoxelGrid.TryLocate(this.CameraFrameLocalOrigin.M43,Field.VoxelSizeMeters,out var VoxelF,out var RemainderF)){
            Collected.DistanceFieldOriginVoxel=new LongVector3(VoxelR,VoxelU,VoxelF);
            Collected.DistanceFieldOriginRemainder=new Vector3(RemainderR,RemainderU,RemainderF);
        }
        var Settings=Collected.Build();
        if(!Settings.IsValid){
            Collected.InvalidReason="EnvironmentLightingSettings";
            Settings=EnvironmentLightingSettings.Legacy;
        }
        Renderer.SetEnvironmentLighting(Settings);
        if(this.HasCameraPose&&this.CameraFrameLocalOrigin.M42.TryToSingle(out var Altitude)){
            Renderer.SetCameraAltitude(Altitude);
            // カメラは描画空間の原点なので、描画空間の高さもここから始まる。
            Renderer.SetRenderOriginAltitude(Altitude);
        }
        if(this.HasCameraPose){
            foreach(var (MaterialIndex,CellMeters) in this.WorldGridMaterials){
                Renderer.SetWorldGridAnchor(MaterialIndex,new Vector3(
                    ReduceToGridCell(this.CameraFrameLocalOrigin.M41,CellMeters),
                    ReduceToGridCell(this.CameraFrameLocalOrigin.M42,CellMeters),
                    ReduceToGridCell(this.CameraFrameLocalOrigin.M43,CellMeters)));
            }
        }
        // M0099：このフレームにキューへ入れた観測者インセット。空リストなら前フレームの値を消去する。エディターが
        // カメラプレビューを所有するため、エディターのビューでは観測者インセットを使わない。
        if(this.EditorCamera is null&&(this.RadialObserverPreviewCount>0||this.RadialObserverPreviewsSubmitted)){
            Renderer.SetCameraPreviews(this.RadialObserverPreviews.AsSpan(0,this.RadialObserverPreviewCount));
            this.RadialObserverPreviewsSubmitted=this.RadialObserverPreviewCount>0;
        }
        this.RadialObserverPreviewCount=0;
        if(Settings.SkyLight is not null&&!Collected.AmbientSubmitted){
            // 平坦なアンビエント項はスカイライトの代用。撮影済みのスカイライトがあれば置き換える。
            Renderer.SetAmbientLight(Vector3.Zero);
        }
        Collected.Reset();
    }

    internal void BeginAudioFrame(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.AudioDevice?.ReapOneShots();
        this.AudioSourcesStorage.Clear();
        this.AudioListenerFrameLocalPose=default;
        this.AudioListenerFrame=default;
        this.AudioListenerEntity=default;
        this.HasAudioListener=false;
    }

    internal void SetAudioListener(Entity Entity,in FixedAffineTransform FrameLocalPose,ReferenceFrameId Frame){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if(this.HasAudioListener&&!this.AudioListenerEntity.Equals(Entity)){
            throw new InvalidOperationException("Runtime requires exactly one active AudioListener.");
        }
        if(!this.FrameSnapshot.Contains(Frame)){
            throw new InvalidOperationException($"The audio listener reference frame is not active: {Frame}.");
        }
        this.AudioListenerEntity=Entity;
        this.AudioListenerFrameLocalPose=FrameLocalPose;
        this.AudioListenerFrame=Frame;
        this.HasAudioListener=true;
    }

    /// <summary>
    /// リスナーから見たワールド位置の距離とパン（ADR-0077）。Entityの姿勢を解決できない場合、またはこのフレームにAudioListenerが
    /// 提出されなかった場合はfalse。その場合、呼び出し側は空間化せず音を再生する。
    /// </summary>
    internal bool TrySpatialize(Entity Source,out AudioSpatialMix Mix){
        Mix=default;
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if(!this.HasAudioListener){ return false; }
        if(RuntimeWorldTransform.TryResolveWorld(Source,this.World,out FixedAffineTransform Pose,out var Frame)!=FixedTransformResolveStatus.Success){ return false; }
        if(RuntimeWorldTransform.TryToCameraRelativeMatrix(Pose,Frame,this.AudioListenerFrameLocalPose,this.AudioListenerFrame,this.FrameSnapshot,out Matrix4x4 Relative)!=FixedTransformStatus.Success){ return false; }
        return AudioSpatializer.TryCalculate(Relative.Translation,AudioSpatialParameters.Default,out Mix);
    }

    internal bool TrySubmitAudioSource(Entity Entity,in FixedAffineTransform FrameLocalPose,
        ReferenceFrameId Frame,float Volume,float MinimumDistance,float MaximumDistance,bool Loop){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if(!this.HasAudioListener||!float.IsFinite(Volume)||Volume is <0f or >4f){ return false; }
        AudioSpatialParameters SpatialParameters;
        try { SpatialParameters=new AudioSpatialParameters(MinimumDistance,MaximumDistance); }
        catch(ArgumentOutOfRangeException){ return false; }
        FixedTransformStatus Status=RuntimeWorldTransform.TryToCameraRelativeMatrix(
            FrameLocalPose,Frame,this.AudioListenerFrameLocalPose,this.AudioListenerFrame,
            this.FrameSnapshot,out Matrix4x4 Relative);
        if(Status!=FixedTransformStatus.Success){ return false; }
        if(!AudioSpatializer.TryCalculate(Relative.Translation,SpatialParameters,out AudioSpatialMix Mix)){
            return false;
        }
        this.AudioSourcesStorage.Add(new AudioSourceSnapshot(
            Entity,Relative.Translation,Volume,Mix,Loop));
        return true;
    }

    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        try {
            this.WorldStreaming?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            this.AudioDevice?.Dispose();
        } finally {
            this.WorldStreaming=null;
            this.AudioDevice=null;
            try {
                this.Physics.Dispose();
            }
            finally {
                try { this.Renderer?.Dispose(); }
                finally {
                    this.Renderer=null;
                    try { this.Window?.Dispose(); }
                    finally {
                        this.Window=null;
                        this.ExternalSurface=null;
                        this.EditorCamera=null;
                        this.HasCameraPose=false;
                        this.HasAudioListener=false;
                        this.AudioSourcesStorage.Clear();
                        this.FixedTicks.Reset();
                        this.WorldStorage.Dispose();
                    }
                }
            }
        }
    }
}
