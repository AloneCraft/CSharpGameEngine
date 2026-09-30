using System.Numerics;
using CSharpGameEngine.Audio;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Audio.XAudio2;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Input;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Simulation;
using CSharpGameEngine.World;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;
using EcsWorld=CSharpGameEngine.Entities.World;

namespace CSharpGameEngine.Runtime.Windows;

public readonly record struct AudioSourceSnapshot(
    Entity Entity,Vector3 ListenerRelativePosition,float Volume,
    AudioSpatialMix SpatialMix,bool Loop){
    public float EffectiveGain=>this.Volume*this.SpatialMix.Attenuation;
}

/// <summary>
/// Owns process-local runtime resources for one game loop. ECS state borrows this context; no
/// native window, renderer, audio voice, or host lease is stored in a component.
/// </summary>
public sealed class EngineHost:IDisposable {
    private readonly ReferenceFrameGraph FrameGraphStorage=new(ReferenceFrameId.Root);
    private readonly WorldOwner WorldStorage=new();
    private readonly List<AudioSourceSnapshot>AudioSourcesStorage=[];
    private readonly Dictionary<ReferenceFrameId,PhysicsDomainRuntime>PhysicsDomains=[];
    private AudioDeviceServer?AudioDevice;
    private bool Disposed;
    private FixedAffineTransform AudioListenerFrameLocalPose;
    private ReferenceFrameId AudioListenerFrame;
    private Entity AudioListenerEntity;
    private bool HasAudioListener;

    internal Win32Window?Window { get;private set; }
    internal RenderServer?Renderer { get;private set; }
    internal InputServer Input { get;private set; }=new(new InputActionMap([]));
    internal RenderCamera RenderCamera { get;set; }
    internal FixedAffineTransform CameraFrameLocalOrigin { get;private set; }
    internal ReferenceFrameId CameraFrame { get;private set; }=ReferenceFrameId.Root;
    internal ReferenceFrameId PhysicsFrame { get;private set; }=ReferenceFrameId.Root;
    internal bool HasCameraPose { get;private set; }
    public ReferenceFrameGraph ReferenceFrames=>this.FrameGraphStorage;
    public ref EcsWorld World=>ref this.WorldStorage.World;
    public FixedTickScheduler FixedTicks { get; }=new();
    public State CreateState()=>this.WorldStorage.CreateState();
    public WorldCellStreamingService?WorldStreaming { get;private set; }
    public ReferenceFrameSnapshot FrameSnapshot=>this.FrameGraphStorage.Current;
    public IReadOnlyList<AudioSourceSnapshot>AudioSources=>this.AudioSourcesStorage;
    public AudioMixer Audio=>this.EnsureAudioDevice().Mixer;
    public IReadOnlyPhysicsWorld PhysicsWorld=>this.GetPhysicsDomain(this.PhysicsFrame).World;
    public PhysicsCommands PhysicsCommands=>this.GetPhysicsDomain(this.PhysicsFrame).Commands;
    public PhysicsCachePlaybackState PhysicsCacheState=>this.GetPhysicsDomain(this.PhysicsFrame).CacheState;

    public PlatformDebugSnapshot CapturePlatformDebugSnapshot(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        PlatformDebugAudioSnapshot AudioSnapshot=this.AudioDevice is null
            ?PlatformDebugSnapshotCapture.UnavailableAudio(
                "The runtime audio device has not been initialized.")
            :this.AudioDevice.CaptureDebugSnapshot();
        return PlatformDebugSnapshotCapture.Capture(
            this.Input.CaptureDebugSnapshot(),AudioSnapshot);
    }

    public bool TryLoadPhysicsCache(
        Stream Input,in SimulationCacheIdentity Identity,bool AllowRealtimeHandoff,out string Error){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        return this.GetPhysicsDomain(this.PhysicsFrame).TryLoadCache(Input,Identity,AllowRealtimeHandoff,out Error);
    }

    public bool TryHandoffPhysicsCache(ulong NextTick){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        return this.GetPhysicsDomain(this.PhysicsFrame).TryHandoffCache(NextTick);
    }

    /// <summary>Streams one completed fixed tick through host-owned bounded scratch storage.</summary>
    public void WritePhysicsCacheFrame(PhysicsCacheStreamWriter Writer,ulong CompletedTick){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.GetPhysicsDomain(this.PhysicsFrame).WriteCacheFrame(Writer,CompletedTick);
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
        if(!this.FrameSnapshot.Contains(Frame)){
            throw new ArgumentException($"The physics reference frame is not active: {Frame}.",nameof(Frame));
        }
        this.PhysicsFrame=Frame;
        this.GetPhysicsDomain(Frame);
    }

    public IReadOnlyPhysicsWorld GetPhysicsWorld(ReferenceFrameId Frame)=>this.GetPhysicsDomain(Frame).World;
    public bool TryGetPhysicsSyncFailure(Entity Entity,out PhysicsFaultType Failure)=>
        this.GetPhysicsDomain(this.PhysicsFrame).TryGetSyncFailure(Entity,out Failure);
    internal PhysicsDomainRuntime ActivePhysicsDomain=>this.GetPhysicsDomain(this.PhysicsFrame);
    internal AudioDeviceServer EnsureAudioDevice(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        return this.AudioDevice??=new AudioDeviceServer(AllowSilentFallback:true);
    }

    public void SetInput(InputServer Input){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.Input=Input??throw new ArgumentNullException(nameof(Input));
    }

    internal Win32Window EnsureWindow(in AppHost App){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        return this.Window??=new Win32Window(new Win32WindowOptions {
            Title=App.Title,Width=App.Width,Height=App.Height,Visible=true,
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
        if(this.Window is { IsMinimized:false }&&this.Renderer is { } Renderer){
            this.SubmitEnvironment(Renderer);
            Renderer.RenderFrame(this.Automation?.CapturesNextFrame??false);
            this.Automation?.RecordPresented(Renderer);
        }
        return this.Automation is not { Completed:true };
    }

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
        }
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

    private PhysicsDomainRuntime GetPhysicsDomain(ReferenceFrameId Frame){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if(!this.FrameSnapshot.Contains(Frame)){
            throw new ArgumentException($"The physics reference frame is not active: {Frame}.",nameof(Frame));
        }
        if(!this.PhysicsDomains.TryGetValue(Frame,out PhysicsDomainRuntime? Domain)){
            Domain=new PhysicsDomainRuntime(Frame);
            this.PhysicsDomains.Add(Frame,Domain);
        }
        return Domain;
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
                foreach(PhysicsDomainRuntime Domain in this.PhysicsDomains.Values){ Domain.Dispose(); }
            }
            finally {
                this.PhysicsDomains.Clear();
                try { this.Renderer?.Dispose(); }
                finally {
                    this.Renderer=null;
                    try { this.Window?.Dispose(); }
                    finally {
                        this.Window=null;
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
