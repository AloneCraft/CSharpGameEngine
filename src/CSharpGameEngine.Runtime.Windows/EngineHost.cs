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
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;
using EcsWorld=CSharpGameEngine.Entities.World;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// ADR-0070 B1: a window the host renders into instead of creating its own, such as the Scene
/// Designer's view in another process. The host never pumps or destroys it.
/// </summary>
internal readonly record struct ExternalRenderSurface(nint Handle,int Width,int Height,GraphicsDeviceOptions DeviceOptions);

/// <summary>
/// ADR-0070 B1: the Scene Designer's camera. It replaces the scene's active camera where the runtime
/// applies it, so the view goes through the same camera-relative path as the game's camera.
/// </summary>
public readonly record struct EditorCameraPose(Transform Pose,float FovYRadians,float NearZ,float FarZ);

public readonly record struct AudioSourceSnapshot(
    Entity Entity,Vector3 ListenerRelativePosition,float Volume,
    AudioSpatialMix SpatialMix,bool Loop){
    public float EffectiveGain=>this.Volume*this.SpatialMix.Attenuation;
}

/// <summary>
/// Owns process-local runtime resources for one game loop. ECS state borrows this context; no
/// native window, renderer, audio voice, or host lease is stored in a component.
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
    /// <summary>M0096 G: the physics domains, which the physics and character systems reach through <see cref="IPhysicsHost"/>.</summary>
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

    /// <summary>Streams one completed fixed tick through host-owned bounded scratch storage.</summary>
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
    /// Renders into <paramref name="Handle"/> instead of a window of the host's own. Call before the
    /// first frame; the level then loads into this surface and input is not read.
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
            Title=App.Title,Width=App.Width,Height=App.Height,Visible=true,
            // An automation run is driven only by its scripted keys (CGE_AUTOMATION_INPUT).
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
    /// Remembers the materials with a world-space grid, so every frame can anchor their lines to the world lattice
    /// although the renderer works camera-relative.
    /// </summary>
    internal void RegisterWorldGridMaterials(IReadOnlyList<StageMaterialCreateInfo>Materials){
        List<(int,LogicalScalar)>Grids=[];
        for(var Index=0;Index<Materials.Count;Index++){
            if(Materials[Index].WorldGrid is{ }Grid&&LogicalScalar.TryFromSingle(Grid.GridSizeMeters,out var CellMeters)&&CellMeters.Raw>0){
                Grids.Add((Index,CellMeters));
            }
        }
        this.WorldGridMaterials=[.. Grids];
    }

    /// <summary>
    /// The position modulo one grid cell, in [0, cell]. The remainder is exact in Q64.64 and is rounded to float
    /// once, so the anchor keeps full float precision however far the camera is from the world origin.
    /// </summary>
    internal static float ReduceToGridCell(LogicalScalar Position,LogicalScalar CellMeters){
        var Remainder=Position.Raw%CellMeters.Raw;
        if(Remainder<0){
            Remainder+=CellMeters.Raw;
        }
        LogicalScalar.FromRaw(Remainder).TryToSingle(out var Reduced);
        return Reduced;
    }

    /// <summary>Queues an observer inset (M0099) for this frame; previews beyond the renderer's maximum are dropped.</summary>
    internal void AddRadialObserverPreview(in RenderCameraPreview Preview){
        if(this.RadialObserverPreviewCount<this.RadialObserverPreviews.Length){
            this.RadialObserverPreviews[this.RadialObserverPreviewCount++]=Preview;
        }
    }

    /// <summary>Records which renderer object an authored entity's mesh became, for the editor's selection.</summary>
    internal void RegisterRenderObject(Entity Source,int ObjectIndex){
        if(!this.RenderObjectsBySource.TryGetValue(Source,out var Objects)){
            this.RenderObjectsBySource[Source]=Objects=[];
        }
        Objects.Add(ObjectIndex);
    }

    /// <summary>ADR-0070 B1: highlights the renderer objects of the Scene Designer's selected entities.</summary>
    public void SetEditorSelection(IReadOnlyCollection<Entity>Selected){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        ArgumentNullException.ThrowIfNull(Selected);
        if(this.Renderer is not { }Renderer){ return; }
        foreach(var (Source,Objects) in this.RenderObjectsBySource){
            var Weight=Selected.Contains(Source)?1f:0f;
            foreach(var Object in Objects){ Renderer.SetObjectSelectionWeight(Object,Weight); }
        }
    }

    /// <summary>ADR-0070 B1: the Scene Designer's gizmos, in camera-relative render space.</summary>
    public void SetEditorOverlay(ReadOnlySpan<CSharpGameEngine.Rendering.RenderOverlayVertex>Triangles,ReadOnlySpan<CSharpGameEngine.Rendering.RenderOverlayVertex>Lines){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.Renderer?.SetOverlay(Triangles,Lines);
    }

    /// <summary>Records which renderer meshes a SkeletalMesh entity skins (ADR-0066).</summary>
    internal void RegisterSkinnedMeshes(Entity Entity,int[]MeshIndices)=>this.SkinnedMeshIndices[Entity]=MeshIndices;
    internal bool TryGetSkinnedMeshes(Entity Entity,out int[]MeshIndices)=>this.SkinnedMeshIndices.TryGetValue(Entity,out MeshIndices!);

    internal bool TryGetCameraPose(out FixedAffineTransform Origin,out ReferenceFrameId Frame){
        Origin=this.CameraFrameLocalOrigin;
        Frame=this.CameraFrame;
        return this.HasCameraPose;
    }

    /// <summary>このフレームにPresentation Systemが集めた環境ライティングComponent(ADR-0069)。</summary>
    internal EnvironmentCollection Environment { get; }=new();

    /// <summary>環境変数で要求された自動検証。未設定ならnull(<see cref="RuntimeAutomation"/>)。</summary>
    public RuntimeAutomation?Automation { get; }=RuntimeAutomation.FromEnvironment();

    /// <summary>1フレームを表示する。自動検証が規定フレーム数に達したらfalseを返し、ループ終了を要求する。</summary>
    internal bool Present(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if((this.ExternalSurface is not null||this.Window is { IsMinimized:false })&&this.Renderer is { } Renderer){
            this.SubmitEnvironment(Renderer);
            Renderer.RenderFrame(this.Automation?.CapturesNextFrame??false);
            this.Automation?.RecordPresented(Renderer,this.AutomationCameraPosition());
        }
        return this.Automation is not { Completed:true };
    }

    private Vector3? AutomationCameraPosition()=>
        this.HasCameraPose&&this.CameraFrameLocalOrigin.M41.TryToSingle(out var R)&&this.CameraFrameLocalOrigin.M42.TryToSingle(out var U)&&this.CameraFrameLocalOrigin.M43.TryToSingle(out var F)
            ?new Vector3(R,U,F):null;

    private void SubmitEnvironment(RenderServer Renderer){
        var Collected=this.Environment;
        var Settings=Collected.Build();
        if(!Settings.IsValid){
            Collected.InvalidReason="EnvironmentLightingSettings";
            Settings=EnvironmentLightingSettings.Legacy;
        }
        Renderer.SetEnvironmentLighting(Settings);
        if(this.HasCameraPose&&this.CameraFrameLocalOrigin.M42.TryToSingle(out var Altitude)){
            Renderer.SetCameraAltitude(Altitude);
            // The camera is the render-space origin, so it is also where render-space heights start.
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
        // M0099: the observer insets queued this frame; an empty list clears the previous frame's. The editor owns
        // the camera previews, so its view never gets observer insets.
        if(this.EditorCamera is null&&(this.RadialObserverPreviewCount>0||this.RadialObserverPreviewsSubmitted)){
            Renderer.SetCameraPreviews(this.RadialObserverPreviews.AsSpan(0,this.RadialObserverPreviewCount));
            this.RadialObserverPreviewsSubmitted=this.RadialObserverPreviewCount>0;
        }
        this.RadialObserverPreviewCount=0;
        if(Settings.SkyLight is not null&&!Collected.AmbientSubmitted){
            // The flat ambient term is a stand-in for sky light; a captured sky light replaces it.
            Renderer.SetAmbientLight(Vector3.Zero);
        }
        Collected.Reset();
    }

    internal void BeginAudioFrame(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
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
