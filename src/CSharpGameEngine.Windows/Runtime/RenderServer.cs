using CSharpGameEngine.Rendering.Backend;
using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.ShaderArtifacts;
using RenderCamera=CSharpGameEngine.Rendering.View.RenderCamera;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>Owns submitted frame state, roster artifacts and device-generation recovery.</summary>
public sealed class RenderServer:IDisposable {
    private static readonly StableId RecoveryArtifactNamespace=
        StableId.Parse("ef11e972-e4bd-5127-82a4-7f52175f9d48");
    private readonly nint Hwnd;
    private readonly GraphicsDeviceOptions DeviceOptions;
    private readonly string ShaderArtifactPath;
    // The roster the renderer is rebuilt from after a device loss. It starts as the one given to Create and grows with AddMesh,
    // AddMaterial and AddObject; a removed object keeps its slot, marked Removed, so every index stays what the caller holds (M0105).
    private readonly List<StageMeshCreateInfo>Meshes;
    private readonly List<StageMaterialCreateInfo>Materials;
    private readonly List<StageObjectCreateInfo>Objects;
    private bool ObjectRosterArtifactStale;
    private readonly RenderDeviceLifecycle Lifecycle;
    private RenderFrameState PendingFrame;
    private RenderFrameState CompletedFrame;
    private RenderQualityPlan DeclaredQualityPlanValue;
    private ShaderArtifactPackage AppliedShaderPackage;
    private StageRenderer?Renderer;
    private ulong FenceBase;
    private ulong LastCompletedFence;
    private int Width;
    private int Height;

    private RenderServer(
        nint Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,
        string ShaderArtifactPath,StageMeshCreateInfo[]Meshes,
        StageMaterialCreateInfo[]Materials,StageObjectCreateInfo[]Objects,
        StageRenderer Renderer,AssetArtifactReference[]ArtifactClosure){
        this.Hwnd=Hwnd;
        this.Width=Width;
        this.Height=Height;
        this.DeviceOptions=DeviceOptions;
        this.ShaderArtifactPath=ShaderArtifactPath;
        this.Meshes=[.. Meshes];
        this.Materials=[.. Materials];
        this.Objects=[.. Objects];
        this.Renderer=Renderer;
        this.AppliedShaderPackage=Renderer.LastKnownGoodShaderArtifact;
        this.Lifecycle=new RenderDeviceLifecycle(ArtifactClosure);
        this.PendingFrame=new RenderFrameState(Objects.Length+StageRenderer.DynamicObjectHeadroom);
        this.CompletedFrame=new RenderFrameState(Objects.Length+StageRenderer.DynamicObjectHeadroom);
        this.DeclaredQualityPlanValue=RenderQualityPlan.Build(
            checked((uint)Width),checked((uint)Height),
            RenderQualityProfile.Compatibility);
    }

    public RenderDeviceSnapshot DeviceSnapshot=>this.Lifecycle.Snapshot;
    public static PixelColor ClearColor=>StageRenderer.ClearColor;
    public static bool ShaderHotReloadEnabled=>StageRenderer.ShaderHotReloadEnabled;
    public string AdapterDescription=>this.Backend.AdapterDescription;
    public bool IsWarp=>this.Backend.IsWarp;
    public bool DebugLayerEnabled=>this.Backend.DebugLayerEnabled;
    public bool GpuBasedValidationEnabled=>this.Backend.GpuBasedValidationEnabled;
    public IReadOnlyList<RenderDeviceMessage>DebugMessages=>this.Backend.DebugMessages;
    public int ErrorOrWorseMessageCount=>this.Backend.ErrorOrWorseMessageCount;
    public bool ShaderArtifactLoaded=>this.Backend.ShaderArtifactLoaded;
    public string ShaderSemanticHash=>this.Backend.ShaderSemanticHash;
    public IReadOnlyList<string>ShaderArtifactDiagnostics=>this.Backend.ShaderArtifactDiagnostics;
    public uint FramesRendered=>this.Backend.FramesRendered;
    public ulong CompletedFence=>this.LastCompletedFence;
    public int ResidentMaterialTextureCount=>this.Backend.ResidentMaterialTextureCount;
    public int ResidentScreenTextureCount=>this.Backend.ResidentScreenTextureCount;
    public PixelReadbackResult?LastCapturedFrame=>this.Backend.LastCapturedFrame;
    /// <summary>
    /// Canonical L4 declaration for the configured compatibility pipeline. The current D3D12
    /// StageRenderer remains the execution oracle, so tooling must present this as a declared plan
    /// rather than per-pass execution evidence.
    /// </summary>
    public RenderQualityPlan DeclaredQualityPlan=>this.DeclaredQualityPlanValue;
    public int SurfaceWidth=>this.Width;
    public int SurfaceHeight=>this.Height;
    /// <summary>The meshes the renderer holds, created and added ones alike. A mesh index is valid for the life of the server.</summary>
    public int MeshCount=>this.Meshes.Count;
    /// <summary>The materials the renderer holds, created and added ones alike.</summary>
    public int MaterialCount=>this.Materials.Count;
    /// <summary>How many more materials <see cref="AddMaterial"/> accepts.</summary>
    public int FreeMaterialSlots=>this.Backend.FreeMaterialSlots;
    /// <summary>The objects drawn now (not counting removed ones).</summary>
    public int ActiveObjectCount=>this.Backend.ActiveObjectCount;
    /// <summary>Meshes with a distance field, and objects in the global distance field at the last frame (ADR-0078/M0110).</summary>
    public int DistanceFieldMeshCount=>this.Backend.DistanceFieldMeshCount;
    public int DistanceFieldObjectCount=>this.Backend.DistanceFieldObjectCount;
    /// <summary>
    /// The adapter-level quality of the dynamic lighting (ADR-0078/M0111): Lumen's GI and reflections run at High and Epic. The default is the
    /// adapter's (<see cref="RenderScalability.DefaultFor"/>); a value set here survives device recovery.
    /// </summary>
    public RenderScalability Scalability{
        get=>this.ScalabilityValue??this.Backend.Scalability;
        set{
            this.ScalabilityValue=value;
            this.Backend.Scalability=value;
        }
    }
    private RenderScalability?ScalabilityValue;
    /// <summary>Debugging (Unreal's r.Lumen.Visualize, M0111): while Lumen runs, the frame shows this instead of the lit scene. Survives device recovery.</summary>
    public LumenVisualization LumenVisualization{
        get=>this.LumenVisualizationValue;
        set{
            this.LumenVisualizationValue=value;
            this.Backend.LumenVisualization=value;
        }
    }
    private LumenVisualization LumenVisualizationValue;
    /// <summary>Whether the last frame lit the scene with the Lumen-like GI (M0111) rather than the probe volume and the sky light.</summary>
    public bool LumenActive=>this.Backend.LumenActiveLastFrame;

    internal void RemoveDeviceForTesting()=>this.Backend.RemoveDeviceForTesting();
    internal float[]ReadDepthForTesting()=>this.Backend.ReadDepthForTesting();

    private StageRenderer Backend{
        get{
            this.Lifecycle.EnsureCanSubmit();
            return this.Renderer
                ??throw new InvalidOperationException("Ready Render Server has no device generation.");
        }
    }

    public static RenderServer Create(
        nint Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,
        string ShaderArtifactPath,IReadOnlyList<StageMeshCreateInfo>Meshes,
        IReadOnlyList<StageMaterialCreateInfo>Materials,
        IReadOnlyList<StageObjectCreateInfo>Objects){
        ArgumentNullException.ThrowIfNull(DeviceOptions);
        ArgumentNullException.ThrowIfNull(ShaderArtifactPath);
        ArgumentNullException.ThrowIfNull(Materials);
        ArgumentNullException.ThrowIfNull(Objects);
        StageMeshCreateInfo[]OwnedMeshes=CloneMeshes(Meshes);
        StageMaterialCreateInfo[]OwnedMaterials=[.. Materials];
        StageObjectCreateInfo[]OwnedObjects=[.. Objects];
        GraphicsDeviceOptions OwnedOptions=new(){
            UseWarp=DeviceOptions.UseWarp,
            EnableDebugLayer=DeviceOptions.EnableDebugLayer,
            EnableGpuBasedValidation=DeviceOptions.EnableGpuBasedValidation,
        };
        string FullShaderArtifactPath=Path.GetFullPath(ShaderArtifactPath);
        StageRenderer Renderer=StageRenderer.Create(
            Hwnd,Width,Height,OwnedOptions,FullShaderArtifactPath,
            OwnedMeshes,OwnedMaterials,OwnedObjects);
        try {
            AssetArtifactReference[]Closure=BuildArtifactClosure(
                Renderer.LastKnownGoodShaderArtifact,
                OwnedMeshes,OwnedMaterials,OwnedObjects);
            return new RenderServer(
                Hwnd,Width,Height,OwnedOptions,FullShaderArtifactPath,
                OwnedMeshes,OwnedMaterials,OwnedObjects,Renderer,Closure);
        } catch {
            Renderer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Adds one mesh to the renderer (M0105) and returns its index. The mesh joins the roster a device loss rebuilds from.
    /// A skinned mesh joins the skinning pass (M0107); the renderer waits for the GPU once to grow it.
    /// </summary>
    public int AddMesh(StageMeshCreateInfo Mesh){
        var Owned=CloneMeshes([Mesh])[0];
        var Index=this.Backend.AddMesh(Owned);
        if(Index!=this.Meshes.Count){
            throw new InvalidOperationException("The renderer and the Render Server disagree on the mesh count.");
        }
        var Closure=new List<AssetArtifactReference>(this.Lifecycle.RequiredArtifacts);
        Closure.Insert(1+Index,CreateArtifact($"mesh/{Index}",Writer=>WriteMesh(Writer,Owned)));
        this.Meshes.Add(Owned);
        this.Lifecycle.ReplaceRequiredArtifacts(Closure);
        return Index;
    }

    /// <summary>
    /// Adds one material (M0105) and returns its index, or -1 when <see cref="FreeMaterialSlots"/> is 0. Uploading its textures waits
    /// for the GPU once.
    /// </summary>
    public int AddMaterial(StageMaterialCreateInfo Material){
        var Index=this.Backend.AddMaterial(Material);
        if(Index<0){ return -1; }
        if(Index!=this.Materials.Count){
            throw new InvalidOperationException("The renderer and the Render Server disagree on the material count.");
        }
        var Closure=new List<AssetArtifactReference>(this.Lifecycle.RequiredArtifacts);
        Closure.Insert(1+this.Meshes.Count+Index,CreateArtifact($"material/{Index}",Writer=>WriteMaterial(Writer,Material)));
        this.Materials.Add(Material);
        this.Lifecycle.ReplaceRequiredArtifacts(Closure);
        return Index;
    }

    /// <summary>
    /// Starts drawing one more object (M0105) and returns its index, or -1 when the renderer's headroom
    /// (<see cref="StageRenderer.DynamicObjectHeadroom"/>) is used up. A removed object's index is handed out again. The object
    /// stays at the origin until <see cref="SetObjectTransform"/> places it.
    /// </summary>
    public int AddObject(int MeshIndex,int MaterialIndex){
        var Index=this.Backend.AddObject(MeshIndex,MaterialIndex);
        if(Index<0){ return -1; }
        var Info=new StageObjectCreateInfo { MeshIndex=MeshIndex,MaterialIndex=MaterialIndex };
        if(Index==this.Objects.Count){ this.Objects.Add(Info); } else { this.Objects[Index]=Info; }
        this.ObjectRosterArtifactStale=true;
        return Index;
    }

    /// <summary>Stops drawing one object (M0105). Its index is handed out again by <see cref="AddObject"/>.</summary>
    public void RemoveObject(int ObjectIndex){
        this.Backend.RemoveObject(ObjectIndex);
        var Info=this.Objects[ObjectIndex];
        if(!Info.Removed){
            this.Objects[ObjectIndex]=new StageObjectCreateInfo { MeshIndex=Info.MeshIndex,MaterialIndex=Info.MaterialIndex,Removed=true };
            this.ObjectRosterArtifactStale=true;
        }
    }

    /// <summary>Submits a validated main view for this frame and retains its value for recovery.</summary>
    public void SetView(in RenderView View)=>this.SetViewSource(RenderViewSource.Fixed(View));
    /// <summary>Resolves and submits physical view input while retaining its value for resize/recovery.</summary>
    public void SetViewSource(in RenderViewSource Source){
        var View=Source.Resolve(this.Width,this.Height);
        this.Backend.SetView(View);
        this.PendingFrame.RecordViewSource(Source);
    }

    public void SetCamera(in RenderCamera Camera,float AspectRatioOverride=0f){
        this.SetViewSource(CSharpGameEngine.Rendering.View.RenderViewPolicy.FromCameraSource(Camera,AspectRatioOverride));
    }

    /// <summary>
    /// Submits a presentation-relative orthographic matrix with linear depth. Perspective callers
    /// must supply physical projection parameters through the overload below.
    /// </summary>
    public void SetViewProjection(in Matrix4x4 ViewProjection){
        this.SetViewProjection(ViewProjection,Vector3.Zero);
    }

    public void SetViewProjection(
        in Matrix4x4 ViewProjection,in Vector3 CameraPosition){
        this.SetView(CSharpGameEngine.Rendering.View.RenderViewPolicy.FromOrthographic(ViewProjection,CameraPosition));
    }

    /// <summary>
    /// M0100: an isolated preview's perspective matrix drawn with radial depth; <paramref name="Radial"/> carries the
    /// camera's field of view, clip range and transition length. Device recovery replays it.
    /// </summary>
    public void SetViewProjection(
        in Matrix4x4 ViewProjection,in Vector3 CameraPosition,in RadialViewParameters Radial){
        this.SetViewSource(CSharpGameEngine.Rendering.View.RenderViewPolicy.FromPerspectiveSource(ViewProjection,CameraPosition,Radial));
    }

    public void SetDirectionalLight(in DirectionalLightSubmission Value){
        Value.Validate();
        this.Backend.SetLight(Value);
        this.PendingFrame.RecordSetDirectional(Value);
    }
    public void AddDirectionalLight(in DirectionalLightSubmission Value){
        Value.Validate();
        this.Backend.Submit(Value);
        this.PendingFrame.RecordAddDirectional(Value);
    }
    public void SetAmbientLight(Vector3 Color){
        this.Backend.SetAmbientLight(Color);
        this.PendingFrame.RecordAmbient(Color);
    }
    public void SetFog(FogParams Fog){
        this.Backend.SetFog(Fog);
        this.PendingFrame.RecordFog(Fog);
    }
    /// <summary>Scene environment (ADR-0069): physical sky, sky light, physical fog, exposure and tone mapper.</summary>
    public void SetEnvironmentLighting(in EnvironmentLightingSettings Settings){
        this.Backend.SetEnvironmentLighting(Settings);
        this.PendingFrame.RecordEnvironment(Settings);
    }
    /// <summary>Absolute camera height above the world origin in metres for the atmosphere and height fog.</summary>
    public void SetCameraAltitude(float Meters){
        this.Backend.SetCameraAltitude(Meters);
        this.PendingFrame.RecordCameraAltitude(Meters);
    }
    /// <summary>Height above the world origin of the render-space origin, for the simplified height fog (zero in world coordinates).</summary>
    public void SetRenderOriginAltitude(float Meters){
        this.Backend.SetRenderOriginAltitude(Meters);
        this.PendingFrame.RecordRenderOriginAltitude(Meters);
    }
    /// <summary>The render-space origin reduced modulo a grid material's cell size, so the world-space grid stays on the world lattice.</summary>
    public void SetWorldGridAnchor(int MaterialIndex,Vector3 Anchor){
        this.Backend.SetWorldGridAnchor(MaterialIndex,Anchor);
        this.PendingFrame.RecordWorldGridAnchor(MaterialIndex,Anchor);
    }

    public void AddPointLight(in PointLightSubmission Value){
        Value.Validate();
        this.Backend.Submit(Value);
        this.PendingFrame.RecordPoint(Value);
    }
    public void AddSpotLight(in SpotLightSubmission Value){
        Value.Validate();
        this.Backend.Submit(Value);
        this.PendingFrame.RecordSpot(Value);
    }
    public void AddRectAreaLight(in RectAreaLightSubmission Value){
        Value.Validate();
        this.Backend.Submit(Value);
        this.PendingFrame.RecordArea(Value);
    }
    public void SubmitDirectLighting(
        IReadOnlyList<EmissivePatch>Patches,float RangeMeters,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        this.Backend.SubmitDirectLighting(
            Patches,RangeMeters,CastsShadows,ShadowPriority,ShadowBiasMeters);
        this.PendingFrame.RecordEmissive(
            Patches,RangeMeters,CastsShadows,ShadowPriority,ShadowBiasMeters);
    }

    public void SetObjectTransform(int ObjectIndex,Matrix4x4 World){
        this.Backend.SetObjectTransform(ObjectIndex,World);
        this.PendingFrame.RecordObjectTransform(ObjectIndex,World);
    }

    /// <summary>Sets a skinned mesh's palette (ADR-0066); recorded so device recovery replays the current pose.</summary>
    public void SetSkinPalette(int MeshIndex,ReadOnlySpan<Matrix4x4>Palette){
        this.Backend.SetSkinPalette(MeshIndex,Palette);
        this.PendingFrame.RecordSkinPalette(MeshIndex,Palette);
    }

    public void SetPresentationMode(RenderPresentationMode Mode){
        if(!Enum.IsDefined(Mode)){ throw new ArgumentOutOfRangeException(nameof(Mode)); }
        this.Backend.SetPresentationMode(Mode);
        this.PendingFrame.RecordPresentationMode(Mode);
    }

    /// <summary>
    /// Chooses where one object is drawn (M0107): out of the camera's view (UE's OwnerNoSee) and/or out of the shadow maps (CastShadow off). Recorded with
    /// the frame, so a game that sets it every frame keeps it through a device recovery.
    /// </summary>
    public void SetObjectVisibility(int ObjectIndex,bool VisibleInView,bool CastsShadow){
        this.Backend.SetObjectVisibility(ObjectIndex,VisibleInView,CastsShadow);
        this.PendingFrame.RecordObjectVisibility(ObjectIndex,VisibleInView,CastsShadow);
    }

    /// <summary>
    /// Multiplies one object's material emission by <paramref name="Scale"/> (ADR-0078/M0109: a material scalar the game changes while it runs, as the
    /// Vehicle's brake lights). 1 is the material's own emission. Recorded with the frame, so it survives a device recovery.
    /// </summary>
    public void SetObjectEmissiveScale(int ObjectIndex,float Scale){
        this.Backend.SetObjectEmissiveScale(ObjectIndex,Scale);
        this.PendingFrame.RecordObjectEmissiveScale(ObjectIndex,Scale);
    }

    public void SetObjectSelectionWeight(int ObjectIndex,float Weight){
        if(!float.IsFinite(Weight)||Weight<0f||Weight>1f){
            throw new ArgumentOutOfRangeException(
                nameof(Weight),"Selection weight must be finite and between zero and one.");
        }
        this.Backend.SetObjectSelectionWeight(ObjectIndex,Weight);
        this.PendingFrame.RecordObjectSelectionWeight(ObjectIndex,Weight);
    }

    public void SetOverlay(
        ReadOnlySpan<RenderOverlayVertex>Triangles,
        ReadOnlySpan<RenderOverlayVertex>Lines){
        this.Backend.SetOverlay(Triangles,Lines);
        this.PendingFrame.RecordOverlay(Triangles,Lines);
    }

    /// <summary>The game's UI over the finished frame (ADR-0078/M0104). Set it every frame; a frame without it draws none.</summary>
    public void SetScreenLayer(ReadOnlySpan<RenderScreenVertex>Vertices,ReadOnlySpan<RenderScreenBatch>Batches){
        this.Backend.SetScreenLayer(Vertices,Batches);
        this.PendingFrame.RecordScreenLayer(Vertices,Batches);
    }

    public void SetCameraPreviews(ReadOnlySpan<RenderCameraPreview>Previews){
        this.Backend.SetCameraPreviews(Previews);
        this.PendingFrame.RecordCameraPreviews(Previews);
    }

    public RenderCameraPreviewDiagnostics[]GetCameraPreviewDiagnostics()=>
        this.Backend.GetCameraPreviewDiagnostics();

    public void Resize(int Width,int Height){
        ObjectDisposedException.ThrowIf(
            this.DeviceSnapshot.State==RenderDeviceState.Disposed,this);
        if(Width<=0||Height<=0){
            if(this.DeviceSnapshot.State==RenderDeviceState.Ready){
                StageRenderer Renderer=this.Backend;
                Renderer.Resize(Width,Height);
                this.Lifecycle.Suspend(this.ObserveCompletedFence(Renderer));
            }
            return;
        }

        RenderQualityPlan ReplacementPlan=RenderQualityPlan.Build(
            checked((uint)Width),checked((uint)Height),
            this.DeclaredQualityPlanValue.Profile);
        this.Width=Width;
        this.Height=Height;
        this.DeclaredQualityPlanValue=ReplacementPlan;
        if(this.DeviceSnapshot.State==RenderDeviceState.Suspended){
            this.Recover(RenderRecoveryReason.Resume);
            return;
        }

        try {
            this.Backend.Resize(Width,Height);
        } catch(RenderBackendFailureException Exception){
            this.HandleBackendFailure(Exception,CaptureForVerification:false);
        }
    }

    public void RenderFrame(bool CaptureForVerification=false){
        try {
            StageRenderer Renderer=this.Backend;
            if(this.PendingFrame.IsEmpty&&!this.CompletedFrame.IsEmpty){
                this.CompletedFrame.Replay(Renderer,this.Width,this.Height);
            }
            Renderer.RenderFrame(CaptureForVerification);
            _=this.ObserveCompletedFence(Renderer);
            this.RefreshObjectRosterArtifact();
            this.SynchronizeArtifactClosure(Renderer);
            this.CommitPendingFrame();
        } catch(RenderBackendFailureException Exception){
            this.HandleBackendFailure(Exception,CaptureForVerification);
        }
    }

    public void Dispose(){
        if(this.DeviceSnapshot.State==RenderDeviceState.Disposed){ return; }
        try {
            this.Renderer?.Dispose();
        } finally {
            this.Renderer=null;
            this.Lifecycle.Dispose();
        }
    }

    private void HandleBackendFailure(
        RenderBackendFailureException Exception,bool CaptureForVerification){
        if(Exception.RecoveryReason is { }RecoveryReason){
            this.Recover(RecoveryReason,CaptureForVerification);
            return;
        }

        this.Lifecycle.Fault(new RenderDeviceFailure(
            RenderDeviceFailureKind.NativeFailure,Exception.HResult,Exception.Message));
        this.Lifecycle.EnsureCanSubmit();
    }

    private void Recover(
        RenderRecoveryReason Reason,bool CaptureForVerification=false){
        StageRenderer Prior=this.Renderer
            ??throw new InvalidOperationException("No render-device generation is available.");
        this.SynchronizeArtifactClosure(Prior);
        ShaderArtifactPackage ShaderPackage=Prior.LastKnownGoodShaderArtifact;
        ulong CompletedFence=this.ObserveCompletedFence(Prior);
        this.Lifecycle.BeginRecovery(Reason,CompletedFence);
        this.Renderer=null;

        try {
            Prior.Dispose();
            StageRenderer Replacement=StageRenderer.CreateFromLastKnownGood(
                this.Hwnd,this.Width,this.Height,this.DeviceOptions,
                this.ShaderArtifactPath,ShaderPackage,
                this.Meshes,this.Materials,this.Objects);
            this.Renderer=Replacement;
            if(this.ScalabilityValue is{ }Scalability){
                Replacement.Scalability=Scalability;
            }
            Replacement.LumenVisualization=this.LumenVisualizationValue;
            this.AppliedShaderPackage=ShaderPackage;
            this.FenceBase=CompletedFence;
            RenderFrameState RecoveryFrame=
                !this.PendingFrame.IsEmpty?this.PendingFrame:this.CompletedFrame;
            bool CompletesPendingFrame=!this.PendingFrame.IsEmpty;
            if(!RecoveryFrame.IsEmpty){
                RecoveryFrame.Replay(Replacement,this.Width,this.Height);
                Replacement.RenderFrame(CaptureForVerification);
            }
            this.Lifecycle.CompleteRecovery(
                this.Lifecycle.RequiredArtifacts,
                this.ObserveCompletedFence(Replacement));
            if(CompletesPendingFrame){ this.CommitPendingFrame(); }
        } catch(Exception Exception){
            this.Renderer?.Dispose();
            this.Renderer=null;
            this.Lifecycle.Fault(new RenderDeviceFailure(
                RenderDeviceFailureKind.ArtifactRebuildFailed,
                Exception.HResult,Exception.Message));
            this.Lifecycle.EnsureCanSubmit();
            throw;
        }
    }

    private ulong ObserveCompletedFence(StageRenderer Renderer){
        ulong NativeCompletedFence=Renderer.CompletedFence;
        if(NativeCompletedFence==ulong.MaxValue){
            return this.LastCompletedFence;
        }
        ulong CompletedFence=checked(this.FenceBase+NativeCompletedFence);
        if(CompletedFence>this.LastCompletedFence){
            this.LastCompletedFence=CompletedFence;
        }
        return this.LastCompletedFence;
    }

    private void CommitPendingFrame(){
        if(this.PendingFrame.IsEmpty){ return; }
        this.PendingFrame.InheritPersistentSettings(this.CompletedFrame);
        RenderFrameState PreviousCompleted=this.CompletedFrame;
        this.CompletedFrame=this.PendingFrame;
        this.PendingFrame=PreviousCompleted;
        this.PendingFrame.Reset();
    }

    /// <summary>Publishes the object roster's new identity once per frame, not once per added or removed object.</summary>
    private void RefreshObjectRosterArtifact(){
        if(!this.ObjectRosterArtifactStale){ return; }
        this.ObjectRosterArtifactStale=false;
        var Closure=new List<AssetArtifactReference>(this.Lifecycle.RequiredArtifacts);
        Closure[^1]=CreateObjectRosterArtifact(this.Objects);
        this.Lifecycle.ReplaceRequiredArtifacts(Closure);
    }

    private void SynchronizeArtifactClosure(StageRenderer Renderer){
        ShaderArtifactPackage Package=Renderer.LastKnownGoodShaderArtifact;
        if(ReferenceEquals(Package,this.AppliedShaderPackage)){ return; }
        AssetArtifactReference[]Closure=[.. this.Lifecycle.RequiredArtifacts];
        Closure[0]=CreateShaderArtifact(Package);
        this.Lifecycle.ReplaceRequiredArtifacts(Closure);
        this.AppliedShaderPackage=Package;
    }

    private static StageMeshCreateInfo[]CloneMeshes(
        IReadOnlyList<StageMeshCreateInfo>Meshes){
        ArgumentNullException.ThrowIfNull(Meshes);
        StageMeshCreateInfo[]Result=new StageMeshCreateInfo[Meshes.Count];
        for(int Index=0;Index<Meshes.Count;Index++){
            StageMeshCreateInfo Mesh=Meshes[Index];
            Result[Index]=new StageMeshCreateInfo {
                Positions=[.. Mesh.Positions],
                Normals=[.. Mesh.Normals],
                Tangents=[.. Mesh.Tangents],
                Uvs=[.. Mesh.Uvs],
                Indices=[.. Mesh.Indices],
                Skin=Mesh.Skin is{ }Skin?new StageSkinCreateInfo { JointCount=Skin.JointCount,JointIndices=[.. Skin.JointIndices],Weights=[.. Skin.Weights] }:null,
                // ADR-0078/M0110: a distance field is immutable, so the copy shares it (a device recovery rebuilds the global distance field from it).
                DistanceField=Mesh.DistanceField,
            };
        }
        return Result;
    }

    private static AssetArtifactReference[]BuildArtifactClosure(
        ShaderArtifactPackage ShaderPackage,
        IReadOnlyList<StageMeshCreateInfo>Meshes,
        IReadOnlyList<StageMaterialCreateInfo>Materials,
        IReadOnlyList<StageObjectCreateInfo>Objects){
        List<AssetArtifactReference>Result=[
            CreateShaderArtifact(ShaderPackage),
        ];
        for(int Index=0;Index<Meshes.Count;Index++){
            StageMeshCreateInfo Mesh=Meshes[Index];
            Result.Add(CreateArtifact($"mesh/{Index}",Writer=>WriteMesh(Writer,Mesh)));
        }
        for(int Index=0;Index<Materials.Count;Index++){
            StageMaterialCreateInfo Material=Materials[Index];
            Result.Add(CreateArtifact(
                $"material/{Index}",Writer=>WriteMaterial(Writer,Material)));
        }
        Result.Add(CreateObjectRosterArtifact(Objects));
        return[.. Result];
    }

    private static AssetArtifactReference CreateObjectRosterArtifact(IReadOnlyList<StageObjectCreateInfo>Objects)=>
        CreateArtifact("object-roster",Writer=>{
            Writer.Write(Objects.Count);
            foreach(StageObjectCreateInfo Object in Objects){
                Writer.Write(Object.MeshIndex);
                Writer.Write(Object.MaterialIndex);
                Writer.Write(Object.Removed);
            }
        });

    private static AssetArtifactReference CreateShaderArtifact(
        ShaderArtifactPackage ShaderPackage)=>CreateArtifact("shader/stage",Writer=>{
            using MemoryStream Serialized=new();
            ShaderArtifactSerializer.Write(
                Serialized,ShaderPackage.Document,out string Error);
            if(!string.IsNullOrEmpty(Error)){
                throw new InvalidDataException(Error);
            }
            Writer.Write(Serialized.ToArray());
        });

    private static AssetArtifactReference CreateArtifact(
        string Name,Action<BinaryWriter>Write){
        using MemoryStream Bytes=new();
        using(BinaryWriter Writer=new(Bytes,System.Text.Encoding.UTF8,leaveOpen:true)){
            Write(Writer);
        }
        AssetId AssetId=new(StableId.FromName(RecoveryArtifactNamespace,Name));
        return new AssetArtifactReference(
            new AssetKey(AssetId,null),Hash256.Compute(Bytes.ToArray()));
    }

    private static void WriteMesh(BinaryWriter Writer,in StageMeshCreateInfo Mesh){
        Writer.Write(Mesh.Positions.Count);
        foreach(Vector3 Value in Mesh.Positions){
            Writer.Write(Value.X); Writer.Write(Value.Y); Writer.Write(Value.Z);
        }
        foreach(Vector3 Value in Mesh.Normals){
            Writer.Write(Value.X); Writer.Write(Value.Y); Writer.Write(Value.Z);
        }
        foreach(Vector4 Value in Mesh.Tangents){
            Writer.Write(Value.X); Writer.Write(Value.Y);
            Writer.Write(Value.Z); Writer.Write(Value.W);
        }
        foreach(Vector2 Value in Mesh.Uvs){
            Writer.Write(Value.X); Writer.Write(Value.Y);
        }
        Writer.Write(Mesh.Indices.Count);
        foreach(uint Value in Mesh.Indices){ Writer.Write(Value); }
        Writer.Write(Mesh.Skin?.JointCount??0);
        if(Mesh.Skin is{ }Skin){
            foreach(ushort Value in Skin.JointIndices){ Writer.Write(Value); }
            foreach(float Value in Skin.Weights){ Writer.Write(Value); }
        }
        // M0110: the distance field is part of what the renderer was given; a mesh without one keeps the identity it had before.
        if(Mesh.DistanceField is{ }Field){
            Writer.Write(Field.MeshHash.ToByteArray());
            Writer.Write(Field.ResolutionX); Writer.Write(Field.ResolutionY); Writer.Write(Field.ResolutionZ);
            Writer.Write(Field.BoundsMin.X); Writer.Write(Field.BoundsMin.Y); Writer.Write(Field.BoundsMin.Z);
            Writer.Write(Field.VoxelSize);
            foreach(Half Value in Field.Distances){ Writer.Write(Value); }
        }
    }

    private static void WriteMaterial(
        BinaryWriter Writer,in StageMaterialCreateInfo Material){
        Writer.Write(Material.BaseColorFactor.X);
        Writer.Write(Material.BaseColorFactor.Y);
        Writer.Write(Material.BaseColorFactor.Z);
        Writer.Write(Material.BaseColorFactor.W);
        Writer.Write(Material.RoughnessFactor);
        Writer.Write(Material.MetallicFactor);
        Writer.Write(Material.EmissiveFactor.X);
        Writer.Write(Material.EmissiveFactor.Y);
        Writer.Write(Material.EmissiveFactor.Z);
        Writer.Write(Material.EmissiveNits.X);
        Writer.Write(Material.EmissiveNits.Y);
        Writer.Write(Material.EmissiveNits.Z);
        Writer.Write(Material.EmissiveTextureLuminanceScale);
        WriteTexture(Writer,Material.BaseColorTexture);
        WriteTexture(Writer,Material.NormalTexture);
        WriteTexture(Writer,Material.EmissiveTexture);
        WriteTexture(Writer,Material.MetallicRoughnessTexture);
        Writer.Write(Material.WorldGrid is not null);
        if(Material.WorldGrid is{ }Grid){
            Writer.Write(Grid.GridSizeMeters);
            Writer.Write(Grid.SubGridDivisions);
            Writer.Write(Grid.WireHalfWidth);
            Writer.Write(Grid.GridColor.X); Writer.Write(Grid.GridColor.Y); Writer.Write(Grid.GridColor.Z);
            Writer.Write(Grid.SubGridColor.X); Writer.Write(Grid.SubGridColor.Y); Writer.Write(Grid.SubGridColor.Z);
        }
    }

    private static void WriteTexture(
        BinaryWriter Writer,RenderTextureResource?Texture){
        Writer.Write(Texture is not null);
        if(Texture is null){ return; }
        Writer.Write(Texture.Width);
        Writer.Write(Texture.Height);
        Writer.Write(Texture.ContentHash.ToString());
    }

    /// <summary>
    /// Builds the canonical immutable roster for one imported or procedural mesh. Callers do not
    /// create a renderer-specific object: geometry, material, and draw identity enter the same
    /// Render Server boundary used by authored worlds and isolated previews.
    /// </summary>
    public static (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,StageObjectCreateInfo[]Objects)BuildSingleMeshRoster(
        IReadOnlyList<Vector3>Positions,IReadOnlyList<Vector3>Normals,
        IReadOnlyList<Vector2>Uvs,IReadOnlyList<uint>Indices,
        in StageMaterialCreateInfo Material){
        ArgumentNullException.ThrowIfNull(Positions);
        ArgumentNullException.ThrowIfNull(Normals);
        ArgumentNullException.ThrowIfNull(Uvs);
        ArgumentNullException.ThrowIfNull(Indices);
        if(Positions.Count==0||Positions.Count!=Normals.Count
            ||Positions.Count!=Uvs.Count||Indices.Count==0){
            throw new ArgumentException(
                "Mesh must have positions, normals, and UVs for every vertex and at least one index.",
                nameof(Positions));
        }

        var Tangents=new Vector4[Positions.Count];
        for(int Index=0;Index<Tangents.Length;Index++){
            Tangents[Index]=StageMeshGeometry.ArbitraryTangentFor(Normals[Index]);
        }
        return(
            [new StageMeshCreateInfo {
                Positions=Positions,Normals=Normals,Tangents=Tangents,Uvs=Uvs,
                Indices=Indices,
            }],
            [Material],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
    }


}
