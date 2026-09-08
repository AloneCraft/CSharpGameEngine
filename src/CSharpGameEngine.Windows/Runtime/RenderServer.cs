using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.ShaderArtifacts;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// Converts declared <see cref="PrimitiveMesh"/> values into the StageMeshCreateInfo/
/// StageMaterialCreateInfo/StageObjectCreateInfo triples <see cref="StageRenderer"/>'s multi-object
/// Create overload needs, appended onto an existing (e.g. .glb-derived) roster via the
/// MeshIndexOffset/MaterialIndexOffset parameters. A pure function, not a stateful service,
/// matching this repo's universal "the object roster is fixed once at Create time" renderer
/// pattern (ADR-0040 Phase 6) - it has no ECS dependency, the caller owns all Entity/Transform
/// concerns.
/// </summary>
public sealed class RenderServer:IDisposable {
    private static readonly StableId RecoveryArtifactNamespace=
        StableId.Parse("ef11e972-e4bd-5127-82a4-7f52175f9d48");
    private static readonly Vector4 DefaultBaseColorFactor=new(0.6f,0.6f,0.6f,1f);
    private readonly nint Hwnd;
    private readonly GraphicsDeviceOptions DeviceOptions;
    private readonly string ShaderArtifactPath;
    private readonly StageMeshCreateInfo[]Meshes;
    private readonly StageMaterialCreateInfo[]Materials;
    private readonly StageObjectCreateInfo[]Objects;
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
        this.Meshes=Meshes;
        this.Materials=Materials;
        this.Objects=Objects;
        this.Renderer=Renderer;
        this.AppliedShaderPackage=Renderer.LastKnownGoodShaderArtifact;
        this.Lifecycle=new RenderDeviceLifecycle(ArtifactClosure);
        this.PendingFrame=new RenderFrameState(Objects.Length);
        this.CompletedFrame=new RenderFrameState(Objects.Length);
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
    public IReadOnlyList<D3D12DebugMessage>DebugMessages=>this.Backend.DebugMessages;
    public int ErrorOrWorseMessageCount=>this.Backend.ErrorOrWorseMessageCount;
    public bool ShaderArtifactLoaded=>this.Backend.ShaderArtifactLoaded;
    public string ShaderSemanticHash=>this.Backend.ShaderSemanticHash;
    public IReadOnlyList<string>ShaderArtifactDiagnostics=>this.Backend.ShaderArtifactDiagnostics;
    public uint FramesRendered=>this.Backend.FramesRendered;
    public ulong CompletedFence=>this.LastCompletedFence;
    public int ResidentMaterialTextureCount=>this.Backend.ResidentMaterialTextureCount;
    public PixelReadbackResult?LastCapturedFrame=>this.Backend.LastCapturedFrame;
    /// <summary>
    /// Canonical L4 declaration for the configured compatibility pipeline. The current D3D12
    /// StageRenderer remains the execution oracle, so tooling must present this as a declared plan
    /// rather than per-pass execution evidence.
    /// </summary>
    public RenderQualityPlan DeclaredQualityPlan=>this.DeclaredQualityPlanValue;
    public int SurfaceWidth=>this.Width;
    public int SurfaceHeight=>this.Height;

    internal void RemoveDeviceForTesting()=>this.Backend.RemoveDeviceForTesting();

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

    public void SetCamera(in RenderCamera Camera,float AspectRatioOverride=0f){
        this.Backend.SetCamera(Camera,AspectRatioOverride);
        this.PendingFrame.RecordCamera(Camera,AspectRatioOverride);
    }

    /// <summary>
    /// Submits a presentation-relative camera matrix for an isolated preview. Authoritative world
    /// coordinates must be made camera-relative before this boundary.
    /// </summary>
    public void SetViewProjection(in Matrix4x4 ViewProjection){
        this.Backend.SetViewProjection(ViewProjection);
        this.PendingFrame.RecordViewProjection(ViewProjection,Vector3.Zero);
    }

    public void SetViewProjection(
        in Matrix4x4 ViewProjection,in Vector3 CameraPosition){
        this.Backend.SetViewProjection(ViewProjection,CameraPosition);
        this.PendingFrame.RecordViewProjection(ViewProjection,CameraPosition);
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

    public void SetPresentationMode(RenderPresentationMode Mode){
        if(!Enum.IsDefined(Mode)){ throw new ArgumentOutOfRangeException(nameof(Mode)); }
        this.Backend.SetPresentationMode(Mode);
        this.PendingFrame.RecordPresentationMode(Mode);
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
        } catch(D3D12Exception Exception){
            this.HandleNativeFailure(Exception,CaptureForVerification:false);
        }
    }

    public void RenderFrame(bool CaptureForVerification=false){
        try {
            StageRenderer Renderer=this.Backend;
            if(this.PendingFrame.IsEmpty&&!this.CompletedFrame.IsEmpty){
                this.CompletedFrame.Replay(Renderer);
            }
            Renderer.RenderFrame(CaptureForVerification);
            _=this.ObserveCompletedFence(Renderer);
            this.SynchronizeArtifactClosure(Renderer);
            this.CommitPendingFrame();
        } catch(D3D12Exception Exception){
            this.HandleNativeFailure(Exception,CaptureForVerification);
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

    private void HandleNativeFailure(
        D3D12Exception Exception,bool CaptureForVerification){
        RenderRecoveryReason?Reason=Exception.HResult switch {
            unchecked((int)0x887A0005)=>RenderRecoveryReason.DeviceRemoved,
            unchecked((int)0x887A0006)=>RenderRecoveryReason.DeviceRemoved,
            unchecked((int)0x887A0007)=>RenderRecoveryReason.DeviceReset,
            _=>null,
        };
        if(Reason is { }RecoveryReason){
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
            this.AppliedShaderPackage=ShaderPackage;
            this.FenceBase=CompletedFence;
            RenderFrameState RecoveryFrame=
                !this.PendingFrame.IsEmpty?this.PendingFrame:this.CompletedFrame;
            bool CompletesPendingFrame=!this.PendingFrame.IsEmpty;
            if(!RecoveryFrame.IsEmpty){
                RecoveryFrame.Replay(Replacement);
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
        RenderFrameState PreviousCompleted=this.CompletedFrame;
        this.CompletedFrame=this.PendingFrame;
        this.PendingFrame=PreviousCompleted;
        this.PendingFrame.Reset();
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
        Result.Add(CreateArtifact("object-roster",Writer=>{
            Writer.Write(Objects.Count);
            foreach(StageObjectCreateInfo Object in Objects){
                Writer.Write(Object.MeshIndex);
                Writer.Write(Object.MaterialIndex);
            }
        }));
        return[.. Result];
    }

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
    /// Same PrimitiveType deduplicates to one shared StageMeshCreateInfo (mirroring how the .glb
    /// import path already separates Meshes from Objects), so N boxes cost one GPU mesh buffer.
    /// Every primitive shares one flat default material - per-primitive color/material is out of
    /// scope for Phase 6.
    /// </summary>
    public static (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,StageObjectCreateInfo[]Objects)BuildPrimitiveRoster(
        IReadOnlyList<PrimitiveType>Types,int MeshIndexOffset,int MaterialIndexOffset){
        List<PrimitiveType>DistinctTypes=[];
        Dictionary<PrimitiveType,int>MeshIndexByType=[];
        foreach(var Type in Types){
            if(!MeshIndexByType.ContainsKey(Type)){
                MeshIndexByType[Type]=DistinctTypes.Count;
                DistinctTypes.Add(Type);
            }
        }

        var Meshes=new StageMeshCreateInfo[DistinctTypes.Count];
        for(var Index=0;Index<DistinctTypes.Count;Index++){
            Meshes[Index]=ToStageMesh(CreateProceduralMesh(DistinctTypes[Index]));
        }

        StageMaterialCreateInfo[]Materials=[
            new StageMaterialCreateInfo { BaseColorFactor=DefaultBaseColorFactor,RoughnessFactor=1f,MetallicFactor=0f },
        ];

        var Objects=new StageObjectCreateInfo[Types.Count];
        for(var Index=0;Index<Types.Count;Index++){
            Objects[Index]=new StageObjectCreateInfo {
                MeshIndex=MeshIndexOffset+MeshIndexByType[Types[Index]],
                MaterialIndex=MaterialIndexOffset,
            };
        }

        return(Meshes,Materials,Objects);
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
            Tangents[Index]=StageRenderer.ArbitraryTangentFor(Normals[Index]);
        }
        return(
            [new StageMeshCreateInfo {
                Positions=Positions,Normals=Normals,Tangents=Tangents,Uvs=Uvs,
                Indices=Indices,
            }],
            [Material],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
    }

    /// <summary>Unit-size dimensions per shape (Transform.Scale=(1,1,1) fits roughly one unit), following Unity's GameObject.CreatePrimitive defaults (e.g. Cylinder/Cone/Capsule height=2, radius=0.5f).</summary>
    private static ProceduralMeshData CreateProceduralMesh(PrimitiveType Type)=>Type switch {
        PrimitiveType.Plane=>ProceduralMeshFactory.CreatePlane(1f,1f),
        PrimitiveType.Box=>ProceduralMeshFactory.CreateBox(Vector3.One),
        PrimitiveType.Sphere=>ProceduralMeshFactory.CreateSphere(0.5f),
        PrimitiveType.Cylinder=>ProceduralMeshFactory.CreateCylinder(0.5f,2f),
        PrimitiveType.Cone=>ProceduralMeshFactory.CreateCone(0.5f,2f),
        PrimitiveType.Torus=>ProceduralMeshFactory.CreateTorus(0.35f,0.15f),
        PrimitiveType.Disk=>ProceduralMeshFactory.CreateDisk(0.5f),
        PrimitiveType.Capsule=>ProceduralMeshFactory.CreateCapsule(0.5f,2f),
        _=>throw new ArgumentOutOfRangeException(nameof(Type),Type,"Unknown PrimitiveType."),
    };

    /// <summary>Synthesizes Uvs (unused - every primitive uses the flat default material, never a real texture) and Tangents via StageRenderer's own untextured-mesh adapter logic, so this stays byte-for-byte consistent with the legacy MeshCreateInfo path instead of a second, divergent implementation.</summary>
    private static StageMeshCreateInfo ToStageMesh(ProceduralMeshData Mesh){
        var VertexCount=Mesh.Positions.Length;
        var Uvs=new Vector2[VertexCount];
        var Tangents=new Vector4[VertexCount];
        for(var Index=0;Index<VertexCount;Index++){
            Tangents[Index]=StageRenderer.ArbitraryTangentFor(Mesh.Normals[Index]);
        }

        return new StageMeshCreateInfo { Positions=Mesh.Positions,Normals=Mesh.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Mesh.Indices };
    }
}
