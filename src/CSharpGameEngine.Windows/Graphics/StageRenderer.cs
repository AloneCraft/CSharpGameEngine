using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using CSharpGameEngine.ShaderArtifacts;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D_PRIMITIVE_TOPOLOGY;
using static TerraFX.Interop.DirectX.D3D_ROOT_SIGNATURE_VERSION;
using static TerraFX.Interop.DirectX.D3D12_COMMAND_LIST_TYPE;
using static TerraFX.Interop.DirectX.D3D12_COMPARISON_FUNC;
using static TerraFX.Interop.DirectX.D3D12_CULL_MODE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_RANGE_TYPE;
using static TerraFX.Interop.DirectX.D3D12_FENCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_FILTER;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_INPUT_CLASSIFICATION;
using static TerraFX.Interop.DirectX.D3D12_PRIMITIVE_TOPOLOGY_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_ROOT_PARAMETER_TYPE;
using static TerraFX.Interop.DirectX.D3D12_ROOT_SIGNATURE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_SHADER_VISIBILITY;
using static TerraFX.Interop.DirectX.D3D12_SRV_DIMENSION;
using static TerraFX.Interop.DirectX.D3D12_STATIC_BORDER_COLOR;
using static TerraFX.Interop.DirectX.D3D12_TEXTURE_ADDRESS_MODE;
using static TerraFX.Interop.DirectX.D3D12_TEXTURE_COPY_TYPE;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXGI;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.DirectX.DXGI_SWAP_EFFECT;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>One procedural or imported mesh plus its solid base color, for the legacy (pre-ADR-0018) untextured API. Kept only as an input shape for <see cref="StageRenderer.Create(nint, int, int, GraphicsDeviceOptions, string, IReadOnlyList{MeshCreateInfo})"/>, which adapts it into one <see cref="StageMeshCreateInfo"/>+<see cref="StageMaterialCreateInfo"/>+<see cref="StageObjectCreateInfo"/> triple per entry.</summary>
public readonly struct MeshCreateInfo {
    public required IReadOnlyList<Vector3>Positions { get; init;}
    public required IReadOnlyList<Vector3>Normals { get; init;}
    public required IReadOnlyList<uint>Indices { get; init;}
    public required Vector4 BaseColorFactor { get; init;}
}

/// <summary>
/// Renders a fixed set of independently-transformed, textured PBR-lite meshes (ADR-0018 extends
/// the untextured ADR-0016 vertical slice) with a movable camera, up to four data-driven
/// directional lights + ambient term, and a simplified exponential height fog. Deliberately evolved in place
/// rather than duplicated into a third renderer class (see ADR-0018): unlike the
/// ModelRenderer/StageRenderer split in ADR-0004, the old untextured contract here is strictly
/// subsumed by the new one (a 1x1 fallback texture per slot reproduces the old solid-color look
/// exactly), so a third copy of the swap chain/RTV/DSV/fence/Resize plumbing would add no value.
/// </summary>
public sealed unsafe class StageRenderer:IDisposable {
    public const int FrameCount=2;
    public const DXGI_FORMAT BackBufferFormat=DXGI_FORMAT_R8G8B8A8_UNORM;
    public const DXGI_FORMAT DepthBufferFormat=DXGI_FORMAT_D32_FLOAT;
    private const DXGI_FORMAT HdrColorFormat=DXGI_FORMAT_R16G16B16A16_FLOAT;
    private const DXGI_FORMAT MaterialTextureFormat=DXGI_FORMAT_R8G8B8A8_UNORM;
    private const float GltfImportEmissiveDefaultNits=100f;
    private const DXGI_FORMAT ShadowAtlasResourceFormat=DXGI_FORMAT_R32_TYPELESS;
    private const DXGI_FORMAT ShadowAtlasDsvFormat=DXGI_FORMAT_D32_FLOAT;
    private const DXGI_FORMAT ShadowAtlasSrvFormat=DXGI_FORMAT_R32_FLOAT;
    public const int ShadowAtlasSizePixels=PhysicalShadowAtlasPlan.AtlasSizePixels;
    public const int ShadowAtlasTileSizePixels=PhysicalShadowAtlasPlan.TileSizePixels;
    public const int DirectionalShadowAtlasTileIndex=0;

    /// <summary>D3D12 constant buffer views must start at a 256-byte aligned GPU address.</summary>
    private const uint ConstantAlignment=512;

    /// <summary>Maximum additive directional lights accepted per frame. Must match Stage shader ABI.</summary>
    public const int MaxDirectionalLights=4;

    /// <summary>Maximum local lights accepted per frame across point and spot submissions.</summary>
    public const int MaxLocalLights=1024;

    /// <summary>Legacy public cap retained for source compatibility. Point and spot now share the 1024-light StructuredBuffer.</summary>
    public const int MaxPointLights=MaxLocalLights;

    /// <summary>Legacy public cap retained for source compatibility. Point and spot now share the 1024-light StructuredBuffer.</summary>
    public const int MaxSpotLights=MaxLocalLights;

    /// <summary>Forward+ screen-space tile edge length in physical pixels.</summary>
    public const int ForwardPlusTileSizePixels=16;

    /// <summary>Maximum local-light candidates evaluated by one Forward+ tile.</summary>
    public const int MaxLightsPerForwardPlusTile=64;

    /// <summary>Six 90-degree projections for the one physical Point caster selected by the atlas plan.</summary>
    public const int MaxPointShadowProjections=
        PhysicalShadowAtlasPlan.PointLightBudget*PhysicalShadowAtlasPlan.PointTilesPerLight;

    /// <summary>Maximum projected spot-light casters selected by the physical atlas plan.</summary>
    public const int MaxSpotShadowProjections=PhysicalShadowAtlasPlan.SpotTileBudget;

    /// <summary>Maximum projected Area/Emissive casters sharing the physical atlas budget.</summary>
    public const int MaxAreaAndEmissiveShadowProjections=
        PhysicalShadowAtlasPlan.AreaAndEmissiveTileBudget;

    private const uint DirectionalLightStride=32; // 2 float4 - see Stage shader ABI's DirectionalLightData
    private const uint LocalLightStride=96; // 6 float4 - see Stage shader ABI's LocalLightData
    private const uint PointShadowProjectionStride=80; // float4x4 + float4 - see Stage shader ABI
    private const uint SpotShadowProjectionStride=80; // float4x4 + float4 - see Stage shader ABI
    private const uint AreaShadowMetadataStride=16; // one float4 - see Stage shader ABI
    private const uint AreaShadowProjectionStride=80; // float4x4 + float4 - see Stage shader ABI
    private const int PointShadowProjectionConstantSlotOffset=1+MaxSpotShadowProjections;
    private const int AreaShadowProjectionConstantSlotOffset=
        PointShadowProjectionConstantSlotOffset+MaxPointShadowProjections;
    private const int ShadowAtlasProjectionConstantSlotCount=
        AreaShadowProjectionConstantSlotOffset+MaxAreaAndEmissiveShadowProjections;
    private const uint ForwardPlusTileRecordUIntCount=1u+(uint)MaxLightsPerForwardPlusTile;
    private const uint ForwardPlusTileRecordStride=sizeof(uint)*ForwardPlusTileRecordUIntCount;

    // FrameCB (b0) byte layout - see Stage shader ABI. Local lights and Forward+ tile lists live in
    // root-SRV StructuredBuffers (t5/t6); only their per-frame counts/grid remain here.
    private const uint FrameViewProjectionOffset=0;   // float4x4, 64 bytes
    private const uint FrameCameraPositionOffset=64;  // float4 (xyz used)
    private const uint FrameDirectionalLightsOffset=80; // MaxDirectionalLights * DirectionalLightStride bytes
    private const uint FrameAmbientColorOffset=FrameDirectionalLightsOffset+(MaxDirectionalLights*DirectionalLightStride);
    private const uint FrameFogParamsOffset=FrameAmbientColorOffset+16; // float4: x=density, y=heightFalloff
    private const uint FrameFogColorOffset=FrameFogParamsOffset+16;     // float4 (xyz used)
    private const uint FrameExposureOffset=FrameFogColorOffset+16;      // float4: x=2^-EV100
    private const uint FrameLightCountsOffset=FrameExposureOffset+16;   // int4: x=local, y=shadow directional index, z=directional count, w=spot projections
    private const uint FrameForwardPlusGridOffset=FrameLightCountsOffset+16; // uint4: tile count X/Y, tile edge, tile cap
    private const uint FrameShadowViewProjectionOffset=FrameForwardPlusGridOffset+16;
    private const uint FrameDirectionalShadowAtlasUvScaleOffset=FrameShadowViewProjectionOffset+64;
    private const uint FrameConstantsSize=FrameDirectionalShadowAtlasUvScaleOffset+16;

    /// <summary>
    /// FrameConstantsBuffer専用のフレームスロット幅。FrameConstantsSize以上、かつD3D12のroot CBV
    /// アライメント要件(256バイト)の倍数でなければならない。汎用のConstantAlignmentを流用せず
    /// FrameConstantsSizeから計算するのは、FrameCBを将来また拡張したとき(ShadowViewProjection追加時に
    /// スロット幅の更新を忘れ、フレームスロット間でデータが衝突・範囲外書き込みを起こしていた実際の
    /// 回帰の再発を防ぐため)にスロット幅が自動的に追従するようにするため。
    /// </summary>
    private const uint FrameConstantsStride=((FrameConstantsSize+255u)/256u)*256u;

    // ObjectCB (b1) byte layout - see Stage shader ABI.
    private const uint ObjectWorldOffset=0;            // float4x4, 64 bytes
    private const uint ObjectBaseColorFactorOffset=64; // float4
    private const uint ObjectMaterialParamsOffset=80;  // float4: x=roughness, y=metallic
    private const uint ObjectEmissiveFactorOffset=96;  // float4: xyz = emissive HDR factor

    // HDR clear values are the inverse ACES values for the public LDR ClearColor. This keeps the
    // empty-scene background stable across the new post-process pass while geometry still benefits
    // from HDR lighting and bloom.
    private static readonly Vector4 ClearColorVector=new(0.23238164f,0.4095592f,1.2785315f,1.0f);
    public static readonly PixelColor ClearColor=new(89,140,217,255);

    private static readonly Vector3 DefaultLightDirectionTowardLight=Vector3.Normalize(new Vector3(0.4f,0.7f,0.5f));
    private static readonly ShadowAtlasTile DirectionalShadowAtlasTile=
        ShadowAtlasTile.FromIndex(DirectionalShadowAtlasTileIndex);
    private static readonly Vector3 DefaultLightColor=new(1.0f,1.0f,1.0f);
    private static readonly Vector3 DefaultAmbientColor=new(0.12f,0.12f,0.15f);

    private struct Vertex {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Tangent;
        public Vector2 Uv;
    }

    private enum LocalLightType {
        Point,
        Spot,
        Area,
    }

    /// <summary>Deferred directional submission used to rank direct lighting independently of shadow priority.</summary>
    private readonly struct DirectionalLightSubmission(
        Vector3 DirectionTowardLight,Vector3 ColorTimesIntensity,bool CastsShadows,
        int ShadowPriority,float ShadowBiasMeters,int StableSubmissionOrder) {
        public Vector3 DirectionTowardLight { get; }=DirectionTowardLight;
        public Vector3 ColorTimesIntensity { get; }=ColorTimesIntensity;
        public bool CastsShadows { get; }=CastsShadows;
        public int ShadowPriority { get; }=ShadowPriority;
        public float ShadowBiasMeters { get; }=ShadowBiasMeters;
        public int StableSubmissionOrder { get; }=StableSubmissionOrder;
    }

    private readonly struct RankedDirectionalLightSubmission(DirectionalLightSubmission Submission,float ScreenContribution) {
        public DirectionalLightSubmission Submission { get; }=Submission;
        public float ScreenContribution { get; }=ScreenContribution;
    }

    /// <summary>CPU-side deferred local-light submission. Ranking is finalized once the camera for the frame is known.</summary>
    private readonly struct LocalLightSubmission(
        LocalLightType Type,Vector3 Position,float Range,Vector3 Direction,float InnerConeCos,
        Vector3 ColorTimesIntensity,float OuterConeCos,int SelectionPriority,int StableSubmissionOrder,
        bool CastsShadows,float ShadowBiasMeters,
        Vector3 Right,Vector3 Up,float HalfWidth,float HalfHeight,PhysicalLightKind ShadowKind) {
        public LocalLightSubmission(
            LocalLightType Type,Vector3 Position,float Range,Vector3 Direction,float InnerConeCos,
            Vector3 ColorTimesIntensity,float OuterConeCos,int SelectionPriority,int StableSubmissionOrder,
            bool CastsShadows,float ShadowBiasMeters)
            :this(Type,Position,Range,Direction,InnerConeCos,ColorTimesIntensity,OuterConeCos,
                SelectionPriority,StableSubmissionOrder,CastsShadows,ShadowBiasMeters,
                Vector3.Zero,Vector3.Zero,0f,0f,
                Type==LocalLightType.Point?PhysicalLightKind.Point:PhysicalLightKind.Spot) {
            if(Type==LocalLightType.Area)throw new ArgumentOutOfRangeException(nameof(Type));
        }
        public LocalLightType Type { get; }=Type;
        public Vector3 Position { get; }=Position;
        public float Range { get; }=Range;
        public Vector3 Direction { get; }=Direction;
        public float InnerConeCos { get; }=InnerConeCos;
        public Vector3 ColorTimesIntensity { get; }=ColorTimesIntensity;
        public float OuterConeCos { get; }=OuterConeCos;
        public int SelectionPriority { get; }=SelectionPriority;
        public int StableSubmissionOrder { get; }=StableSubmissionOrder;
        public bool CastsShadows { get; }=CastsShadows;
        public float ShadowBiasMeters { get; }=ShadowBiasMeters;
        public Vector3 Right { get; }=Right;
        public Vector3 Up { get; }=Up;
        public float HalfWidth { get; }=HalfWidth;
        public float HalfHeight { get; }=HalfHeight;
        public PhysicalLightKind ShadowKind { get; }=ShadowKind;
    }

    private readonly struct RankedLocalLightSubmission(LocalLightSubmission Submission,float ScreenContribution) {
        public LocalLightSubmission Submission { get; }=Submission;
        public float ScreenContribution { get; }=ScreenContribution;
    }

    /// <summary>Matches Stage shader ABI's 6-float4 LocalLightData exactly.</summary>
    private struct LocalLightGpuData {
        public Vector4 PositionAndRange;
        public Vector4 DirectionAndInnerCos;
        public Vector4 ColorAndOuterCos;
        public Vector4 TypeAndPadding;
        public Vector4 RightAndHalfWidth;
        public Vector4 UpAndHalfHeight;
    }

    /// <summary>Matches Stage shader ABI's matrix + PhysicalShadowAtlasPlan UV rectangle record for a Point face.</summary>
    private struct PointShadowProjectionGpuData {
        public Matrix4x4 ViewProjection;
        public Vector4 UvScaleOffset;
    }

    /// <summary>Matches Stage shader ABI's matrix + PhysicalShadowAtlasPlan UV rectangle record.</summary>
    private struct SpotShadowProjectionGpuData {
        public Matrix4x4 ViewProjection;
        public Vector4 UvScaleOffset;
    }

    /// <summary>Matches Stage shader ABI's Area/Emissive PCSS source radius and clip-distance record at t9.</summary>
    private struct AreaShadowMetadataGpuData {
        public Vector4 SourceRadiusAndPadding;
    }

    /// <summary>Matches Stage shader ABI's independent Area/Emissive center-projection record at t10.</summary>
    private struct AreaShadowProjectionGpuData {
        public Matrix4x4 ViewProjection;
        public Vector4 UvScaleOffset;
    }

    /// <summary>One atlas projection render target and its shadow-only constant-buffer slot.</summary>
    private readonly struct ShadowAtlasProjection(ShadowAtlasTile Tile,int ShadowConstantsSlot) {
        public ShadowAtlasTile Tile { get; }=Tile;
        public int ShadowConstantsSlot { get; }=ShadowConstantsSlot;
    }

    private GraphicsDevice?Device;
    private IDXGISwapChain3*SwapChain;
    private ID3D12DescriptorHeap*RtvHeap;
    private uint RtvDescriptorSize;
    private readonly ID3D12Resource*[]BackBuffers=new ID3D12Resource*[FrameCount];
    private ID3D12DescriptorHeap*DsvHeap;
    private uint DsvDescriptorSize;
    private ID3D12Resource*DepthBuffer;
    private ID3D12Resource*HdrColorBuffer;
    private ID3D12DescriptorHeap*HdrSrvHeap;
    private ID3D12Resource*ShadowAtlas;
    private readonly ID3D12CommandAllocator*[]CommandAllocators=new ID3D12CommandAllocator*[FrameCount];
    private ID3D12GraphicsCommandList*CommandList;
    private ID3D12Fence*Fence;
    private readonly ulong[]FenceValues=new ulong[FrameCount];
    private HANDLE FenceEvent;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private ID3D12RootSignature*ShadowRootSignature;
    private ID3D12PipelineState*ShadowPipelineState;
    private ID3D12RootSignature*PostProcessRootSignature;
    private ID3D12PipelineState*PostProcessPipelineState;
    private byte[]ShadowVertexShaderBytecode=[];
    private byte[]FullscreenVertexShaderBytecode=[];
    private byte[]PostProcessPixelShaderBytecode=[];

    // Per-mesh (indexed by meshIndex).
    private int MeshCount;
    private ID3D12Resource*[]VertexBuffers=[];
    private D3D12_VERTEX_BUFFER_VIEW[]VertexBufferViews=[];
    private ID3D12Resource*[]IndexBuffers=[];
    private D3D12_INDEX_BUFFER_VIEW[]IndexBufferViews=[];
    private uint[]IndexCounts=[];

    // Per-material (indexed by materialIndex). Four material textures plus one shared shadow SRV.
    private int MaterialCount;
    private ID3D12Resource*[]MaterialTextures=[];
    private ID3D12DescriptorHeap*SrvHeap;
    private uint SrvDescriptorSize;
    private Vector4[]MaterialBaseColorFactors=[];
    private float[]MaterialRoughnessFactors=[];
    private float[]MaterialMetallicFactors=[];
    private Vector3[]MaterialEmissiveNits=[];

    // Per-object (indexed by objectIndex).
    private int ObjectCount;
    private int[]ObjectMeshIndices=[];
    private int[]ObjectMaterialIndices=[];
    private Matrix4x4[]ObjectTransforms=[];

    private ID3D12Resource*FrameConstantsBuffer;
    private byte*FrameConstantsMapped;
    private ID3D12Resource*LocalLightsBuffer;
    private byte*LocalLightsMapped;
    private ID3D12Resource*ForwardPlusTileListBuffer;
    private byte*ForwardPlusTileListMapped;
    private uint ForwardPlusTileFrameSize;
    private int ForwardPlusTileCountX;
    private int ForwardPlusTileCountY;
    private ID3D12Resource*ObjectConstantsBuffer;
    private byte*ObjectConstantsMapped;
    private ID3D12Resource*ShadowConstantsBuffer;
    private byte*ShadowConstantsMapped;
    private ID3D12Resource*SpotShadowProjectionBuffer;
    private byte*SpotShadowProjectionMapped;
    private ID3D12Resource*PointShadowProjectionBuffer;
    private byte*PointShadowProjectionMapped;
    private ID3D12Resource*AreaShadowMetadataBuffer;
    private byte*AreaShadowMetadataMapped;
    private ID3D12Resource*AreaShadowProjectionBuffer;
    private byte*AreaShadowProjectionMapped;

    // AddDirectionalLightTowardLight/point/spot lights are additive per frame. The legacy
    // SetDirectionalLight singleton is rewritten into every swap-chain frame slot until it is
    // replaced, matching the original setter's behaviour; additive counts reset so removed
    // owning entities do not linger.
    private int DirectionalLightCount=1;
    private int DirectionalShadowIndex;
    private bool DirectionalLightsExplicitThisFrame;
    private bool DirectionalLightsPersistAcrossFrames;
    private readonly List<PhysicalLightCandidate>DirectionalShadowCandidates=[];
    private PhysicalLightCandidate?SelectedDirectionalShadowAtlasCandidate;
    private DirectionalLightSubmission?PersistentDirectionalLightSubmission;
    private readonly List<DirectionalLightSubmission>DirectionalLightSubmissions=[];
    private readonly List<LocalLightSubmission>LocalLightSubmissions=[];
    private readonly List<LocalLightSubmission>SelectedLocalLightSubmissions=[];
    private readonly List<ShadowAtlasProjection>PointShadowAtlasProjections=[];
    private readonly List<ShadowAtlasProjection>SpotShadowAtlasProjections=[];
    private readonly List<ShadowAtlasProjection>AreaShadowAtlasProjections=[];
    private readonly Matrix4x4[]FrameViewProjections=new Matrix4x4[FrameCount];
    private int LocalLightCount;
    private int PointShadowProjectionCount;
    private int SpotShadowProjectionCount;
    private int AreaShadowProjectionCount;
    private int DroppedDirectionalLightsThisFrameValue;
    private int DroppedLocalLightsThisFrameValue;
    private int DroppedTileLightReferencesThisFrameValue;

    /// <summary>Visible diagnostic counter for Directional submissions over the four-light cap.</summary>
    public int DroppedDirectionalLightsThisFrame=>this.DroppedDirectionalLightsThisFrameValue;

    /// <summary>Directional overflow count captured when the prior frame was submitted.</summary>
    public int DroppedDirectionalLightsLastFrame { get;private set; }

    /// <summary>Visible diagnostic counter; direct lighting for already selected lights continues.</summary>
    public int DroppedLocalLightsThisFrame=>this.DroppedLocalLightsThisFrameValue;

    /// <summary>Overflow count captured when the prior frame was submitted.</summary>
    public int DroppedLocalLightsLastFrame { get;private set; }

    /// <summary>Visible count of candidate references rejected by the 64-light per-tile cap during the current frame.</summary>
    public int DroppedTileLightReferencesThisFrame=>this.DroppedTileLightReferencesThisFrameValue;

    /// <summary>Tile-list overflow count captured when the prior frame was submitted.</summary>
    public int DroppedTileLightReferencesLastFrame { get;private set; }

    private ID3D12Resource*ReadbackBuffer;
    private ulong ReadbackBufferSize;
    private D3D12_PLACED_SUBRESOURCE_FOOTPRINT ReadbackFootprint;

    private uint FrameIndex;
    private int Width;
    private int Height;
    private bool SizeIsZero;
    private bool Disposed;
    private string ShaderArtifactPath=string.Empty;
    private ShaderArtifactMonitor?ShaderArtifactMonitor;

    private D3D12_VIEWPORT Viewport;
    private RECT ScissorRect;

    public string AdapterDescription=> this.Device?.AdapterDescription??string.Empty;

    public bool IsDisposed=>this.Disposed;

    public bool IsWarp=> this.Device?.IsWarp??false;

    public bool DebugLayerEnabled=> this.Device?.DebugLayerEnabled??false;

    public bool GpuBasedValidationEnabled=> this.Device?.GpuBasedValidationEnabled??false;

    public IReadOnlyList<D3D12DebugMessage>DebugMessages=> this.Device?.CollectedMessages??[];

    public int ErrorOrWorseMessageCount=> this.Device?.ErrorOrWorseMessageCount??0;

    public static bool ShaderHotReloadEnabled=>ShaderArtifactMonitor.IsEnabled;

    public bool ShaderArtifactLoaded { get;private set;}

    public string ShaderSemanticHash { get;private set;}=string.Empty;

    public IReadOnlyList<string>ShaderArtifactDiagnostics { get;private set;}=[];

    public uint FramesRendered { get;private set;}

    public PixelReadbackResult?LastCapturedFrame { get;private set;}

    public static StageRenderer Create(
        nint Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,string ShaderArtifactPath,
        IReadOnlyList<StageMeshCreateInfo>Meshes,IReadOnlyList<StageMaterialCreateInfo>Materials,IReadOnlyList<StageObjectCreateInfo>Objects){
        ArgumentNullException.ThrowIfNull(ShaderArtifactPath);
        ArgumentNullException.ThrowIfNull(Meshes);
        ArgumentNullException.ThrowIfNull(Materials);
        ArgumentNullException.ThrowIfNull(Objects);
        if(Width<=0||Height<=0){
            throw new ArgumentOutOfRangeException(nameof(Width),"Initial width/height must both be positive.");
        }

        if(Meshes.Count==0){
            throw new ArgumentException("At least one mesh is required.",nameof(Meshes));
        }
        if(Materials.Count==0){
            throw new ArgumentException("At least one material is required.",nameof(Materials));
        }
        if(Objects.Count==0){
            throw new ArgumentException("At least one object is required.",nameof(Objects));
        }

        foreach(StageMeshCreateInfo Mesh in Meshes){
            if(Mesh.Positions.Count==0||Mesh.Positions.Count!=Mesh.Normals.Count
                ||Mesh.Positions.Count!=Mesh.Tangents.Count||Mesh.Positions.Count!=Mesh.Uvs.Count
                ||Mesh.Indices.Count==0){
                throw new ArgumentException("Each mesh must have at least one vertex with matching Positions/Normals/Tangents/Uvs counts, and at least one index.",nameof(Meshes));
            }
        }

        foreach(StageObjectCreateInfo Obj in Objects){
            if(Obj.MeshIndex<0||Obj.MeshIndex>=Meshes.Count){
                throw new ArgumentOutOfRangeException(nameof(Objects), $"Object references out-of-range MeshIndex {Obj.MeshIndex}.");
            }
            if(Obj.MaterialIndex<0||Obj.MaterialIndex>=Materials.Count){
                throw new ArgumentOutOfRangeException(nameof(Objects), $"Object references out-of-range MaterialIndex {Obj.MaterialIndex}.");
            }
        }

        StageRenderer Renderer=new(){
            Width=Width,
            Height=Height,
        };

        var Completed=false;
        try {
            Renderer.Initialize((HWND)Hwnd,Width,Height,DeviceOptions,ShaderArtifactPath,Meshes,Materials,Objects);
            Completed=true;
            return Renderer;
        } finally {
            if(!Completed){
                Renderer.Dispose();
            }
        }
    }

    /// <summary>Legacy adapter (pre-ADR-0018): one untextured solid-color mesh per object, exactly reproducing the old 1:1 mesh/material/object indexing so existing callers (and <c>StageGpuIntegrationTests</c>) keep working unchanged. Synthesized UVs/tangents never matter visually here since every material falls back to its 1x1 default textures.</summary>
    public static StageRenderer Create(
        nint Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,string ShaderArtifactPath,
        IReadOnlyList<MeshCreateInfo>Meshes){
        ArgumentNullException.ThrowIfNull(Meshes);

        var StageMeshes=new StageMeshCreateInfo[Meshes.Count];
        var StageMaterials=new StageMaterialCreateInfo[Meshes.Count];
        var StageObjects=new StageObjectCreateInfo[Meshes.Count];

        for(var I=0;I<Meshes.Count;I++){
            MeshCreateInfo Mesh=Meshes[I];
            var VertexCount=Mesh.Positions.Count;
            var Uvs=new Vector2[VertexCount];
            var Tangents=new Vector4[VertexCount];
            for(var V=0;V<VertexCount;V++){
                Tangents[V]=ArbitraryTangentFor(Mesh.Normals[V]);
            }

            StageMeshes[I]=new StageMeshCreateInfo { Positions=Mesh.Positions,Normals=Mesh.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Mesh.Indices };
            StageMaterials[I]=new StageMaterialCreateInfo { BaseColorFactor=Mesh.BaseColorFactor,RoughnessFactor=1f,MetallicFactor=0f };
            StageObjects[I]=new StageObjectCreateInfo { MeshIndex=I,MaterialIndex=I };
        }

        return Create(Hwnd,Width,Height,DeviceOptions,ShaderArtifactPath,StageMeshes,StageMaterials,StageObjects);
    }

    /// <summary>An arbitrary unit tangent perpendicular to <paramref name="Normal"/> - correct for TBN orthonormality even though it isn't aligned to any real UV direction, which is fine when the bound normal texture is always the flat 1x1 fallback (see <see cref="MaterialTextureFormat"/> fallback images). Internal (not private) so <see cref="CSharpGameEngine.Runtime.Windows.RenderServer"/> can reuse the same untextured-mesh UV/tangent synthesis instead of duplicating it.</summary>
    internal static Vector4 ArbitraryTangentFor(Vector3 Normal){
        Vector3 Reference=MathF.Abs(Normal.Y)<0.99f?Vector3.UnitY:Vector3.UnitX;
        Vector3 Tangent=Vector3.Normalize(Vector3.Cross(Reference,Normal));
        return new Vector4(Tangent,1f);
    }

    private StageRenderer(){
    }

    private void Initialize(HWND Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,string ShaderArtifactPath,
        IReadOnlyList<StageMeshCreateInfo>Meshes,IReadOnlyList<StageMaterialCreateInfo>Materials,IReadOnlyList<StageObjectCreateInfo>Objects){
        this.Device=GraphicsDevice.Create(DeviceOptions);

        this.CreateSwapChain(Hwnd,Width,Height);
        this.CreateRtvHeap();
        this.CreateRenderTargetViews();
        this.CreateDsvHeap();
        this.CreateDepthBuffer(Width,Height);
        this.CreateHdrColorBuffer(Width,Height);
        this.CreateShadowAtlas();

        for(var I=0;I<FrameCount;I++){
            ID3D12CommandAllocator*Allocator;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,__uuidof<ID3D12CommandAllocator>(),(void**)&Allocator),
                "ID3D12Device::CreateCommandAllocator","creating a frame command allocator");
            this.CommandAllocators[I]=Allocator;
        }

        ID3D12GraphicsCommandList*CommandList;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT, this.CommandAllocators[0],null,__uuidof<ID3D12GraphicsCommandList>(),(void**)&CommandList),
            "ID3D12Device::CreateCommandList","creating the graphics command list");
        this.CommandList=CommandList;
        D3D12Diagnostics.EnsureSuccess(CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing the initial command list state");

        ID3D12Fence*Fence;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateFence(0,D3D12_FENCE_FLAG_NONE,__uuidof<ID3D12Fence>(),(void**)&Fence),
            "ID3D12Device::CreateFence","creating the frame fence");
        this.Fence=Fence;

        this.FenceEvent=CreateEventW(lpEventAttributes:null,bManualReset:FALSE,bInitialState:FALSE,lpName:null);
        if(this.FenceEvent==HANDLE.NULL){
            throw new InvalidOperationException("CreateEventW failed while creating the frame fence event.");
        }

        this.FrameIndex= this.SwapChain->GetCurrentBackBufferIndex();
        this.FenceValues[this.FrameIndex]=1;

        this.ShaderArtifactPath=Path.GetFullPath(ShaderArtifactPath);
        this.LoadShaderArtifact(this.ShaderArtifactPath);
        this.CreateRootSignature();
        this.CreateShadowRootSignature();
        this.CreatePostProcessRootSignature();
        this.CreatePipelineState();
        this.CreateShadowPipelineState();
        this.CreatePostProcessPipelineState();
        this.ShaderArtifactMonitor=new ShaderArtifactMonitor(this.ShaderArtifactPath);
        this.CreateMeshBuffers(Meshes);
        this.CreateMaterialResources(Materials);
        this.CreateObjectMappings(Objects);
        this.CreateConstantBuffers();
        this.CreateForwardPlusTileBuffer(Width,Height);
        this.CreateShadowConstantsBuffer();
        this.UpdateViewportAndScissor(Width,Height);
        this.InitializeConstantsToDefaults();
    }

    private void CreateSwapChain(HWND Hwnd,int Width,int Height){
        DXGI_SWAP_CHAIN_DESC1 Desc=new(){
            BufferCount=FrameCount,
            Width=(uint)Width,
            Height=(uint)Height,
            Format=BackBufferFormat,
            BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT,
            SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD,
            SampleDesc=new DXGI_SAMPLE_DESC(1,0),
        };

        IDXGISwapChain1*SwapChain1;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Factory->CreateSwapChainForHwnd(
                                                                                 (IUnknown*)this.Device.CommandQueue,
                                                                                 Hwnd,
                                                                                 &Desc,
                                                                                 pFullscreenDesc:null,
                                                                                 pRestrictToOutput:null,
                                                                                 &SwapChain1),
            "IDXGIFactory4::CreateSwapChainForHwnd","creating the swap chain");

        IDXGISwapChain3*SwapChain3;
        HRESULT Hr=SwapChain1->QueryInterface(__uuidof<IDXGISwapChain3>(),(void**)&SwapChain3);
        _=SwapChain1->Release();
        D3D12Diagnostics.EnsureSuccess(Hr,"IDXGISwapChain1::QueryInterface(IDXGISwapChain3)","upgrading the swap chain interface");

        this.SwapChain=SwapChain3;
    }

    private void CreateRtvHeap(){
        D3D12_DESCRIPTOR_HEAP_DESC Desc=new(){
            NumDescriptors=FrameCount+1,
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_RTV,
        };

        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateDescriptorHeap(&Desc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating the RTV descriptor heap");

        this.RtvHeap=Heap;
        this.RtvDescriptorSize= this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    }

    private void CreateRenderTargetViews(){
        D3D12_CPU_DESCRIPTOR_HANDLE RtvBase= this.RtvHeap->GetCPUDescriptorHandleForHeapStart();

        for(uint I=0;I<FrameCount;I++){
            ID3D12Resource*BackBuffer;
            D3D12Diagnostics.EnsureSuccess(this.SwapChain->GetBuffer(I,__uuidof<ID3D12Resource>(),(void**)&BackBuffer),
                "IDXGISwapChain3::GetBuffer","retrieving a back buffer", $"buffer[{I}]");

            D3D12_CPU_DESCRIPTOR_HANDLE RtvHandle=new(RtvBase,(int)I, this.RtvDescriptorSize);
            this.Device!.Device->CreateRenderTargetView(BackBuffer,pDesc:null,RtvHandle);

            this.BackBuffers[I]=BackBuffer;
        }
    }

    private void CreateDsvHeap(){
        D3D12_DESCRIPTOR_HEAP_DESC Desc=new(){
            NumDescriptors=2,
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_DSV,
        };

        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateDescriptorHeap(&Desc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating the DSV descriptor heap");

        this.DsvHeap=Heap;
        this.DsvDescriptorSize= this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_DSV);
    }

    private void CreateDepthBuffer(int Width,int Height){
        D3D12_RESOURCE_DESC ResourceDesc=D3D12_RESOURCE_DESC.Tex2D(DepthBufferFormat,(uint)Width,(uint)Height,mipLevels:1);
        ResourceDesc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_DEFAULT);
        D3D12_CLEAR_VALUE ClearValue=new(DepthBufferFormat,1.0f,0);

        ID3D12Resource*DepthBuffer;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&ResourceDesc,D3D12_RESOURCE_STATE_DEPTH_WRITE,&ClearValue,__uuidof<ID3D12Resource>(),(void**)&DepthBuffer),
            "ID3D12Device::CreateCommittedResource","creating the depth buffer");

        D3D12_DEPTH_STENCIL_VIEW_DESC DsvDesc=new(){
            Format=DepthBufferFormat,
            ViewDimension=D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D,
        };
        this.Device.Device->CreateDepthStencilView(DepthBuffer,&DsvDesc, this.DsvHeap->GetCPUDescriptorHandleForHeapStart());

        this.DepthBuffer=DepthBuffer;
    }

    private void CreateHdrColorBuffer(int Width,int Height){
        D3D12_RESOURCE_DESC ResourceDesc=D3D12_RESOURCE_DESC.Tex2D(HdrColorFormat,(uint)Width,(uint)Height,mipLevels:1);
        ResourceDesc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_DEFAULT);
        D3D12_CLEAR_VALUE ClearValue=new(HdrColorFormat,0f,0);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&ResourceDesc,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,&ClearValue,__uuidof<ID3D12Resource>(),(void**)&Resource),
            nameof(this.CreateHdrColorBuffer),nameof(this.CreateHdrColorBuffer));

        D3D12_CPU_DESCRIPTOR_HANDLE RtvHandle=new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),FrameCount, this.RtvDescriptorSize);
        this.Device.Device->CreateRenderTargetView(Resource,pDesc:null,RtvHandle);
        this.HdrColorBuffer=Resource;

        if(this.HdrSrvHeap==null){
            D3D12_DESCRIPTOR_HEAP_DESC SrvHeapDesc=new(){ Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,NumDescriptors=1,Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE };
            ID3D12DescriptorHeap*SrvHeap;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateDescriptorHeap(&SrvHeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&SrvHeap),
                nameof(this.CreateHdrColorBuffer),nameof(this.CreateHdrColorBuffer));
            this.HdrSrvHeap=SrvHeap;
        }

        D3D12_SHADER_RESOURCE_VIEW_DESC SrvDesc=new(){
            Format=HdrColorFormat,
            ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        SrvDesc.Texture2D.MipLevels=1;
        this.Device.Device->CreateShaderResourceView(Resource,&SrvDesc, this.HdrSrvHeap->GetCPUDescriptorHandleForHeapStart());
    }

    private void CreateShadowAtlas(){
        D3D12_RESOURCE_DESC ResourceDesc=D3D12_RESOURCE_DESC.Tex2D(ShadowAtlasResourceFormat,ShadowAtlasSizePixels,ShadowAtlasSizePixels,mipLevels:1);
        ResourceDesc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_DEFAULT);
        D3D12_CLEAR_VALUE ClearValue=new(ShadowAtlasDsvFormat,1.0f,0);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&ResourceDesc,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,&ClearValue,__uuidof<ID3D12Resource>(),(void**)&Resource),
            nameof(this.CreateShadowAtlas),nameof(this.CreateShadowAtlas));

        D3D12_DEPTH_STENCIL_VIEW_DESC DsvDesc=new(){ Format=ShadowAtlasDsvFormat,ViewDimension=D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D };
        D3D12_CPU_DESCRIPTOR_HANDLE ShadowDsv=new(this.DsvHeap->GetCPUDescriptorHandleForHeapStart(),1, this.DsvDescriptorSize);
        this.Device.Device->CreateDepthStencilView(Resource,&DsvDesc,ShadowDsv);
        this.ShadowAtlas=Resource;
    }

private void LoadShaderArtifact(string ShaderArtifactPath){
        if(!ShaderArtifactRuntime.TryLoad(ShaderArtifactPath,ShaderArtifactTargetKind.Stage,out var Package,out var Error)||Package is null){
            this.ShaderArtifactLoaded=false;
            this.ShaderArtifactDiagnostics=[Error];
            throw new InvalidOperationException($"Shader artifact load failed for '{ShaderArtifactPath}':{Environment.NewLine}{Error}");
        }
        this.ApplyShaderArtifact(Package);
        this.ShaderArtifactLoaded=true;
        this.ShaderSemanticHash=Package.Document.SemanticHash;
        this.ShaderArtifactDiagnostics=[];
    }
    private void ApplyShaderArtifact(ShaderArtifactPackage Package){
        this.VertexShaderBytecode=Package.GetBytecode("VSMain");
        this.PixelShaderBytecode=Package.GetBytecode("PSMain");
        this.FullscreenVertexShaderBytecode=Package.GetBytecode("FullscreenVS");
        this.PostProcessPixelShaderBytecode=Package.GetBytecode("PostProcessPS");
        this.ShadowVertexShaderBytecode=Package.GetBytecode("VSShadowMain");
    }
    private void TryReloadShaderArtifact(){
        if(this.ShaderArtifactMonitor?.ConsumeReloadRequest()!=true){
            return;
        }
        if(!ShaderArtifactRuntime.TryLoad(this.ShaderArtifactPath,ShaderArtifactTargetKind.Stage,out var Package,out var Error)||Package is null){
            this.ShaderArtifactDiagnostics=[Error];
            return;
        }
        var OldVertexShader=this.VertexShaderBytecode;
        var OldPixelShader=this.PixelShaderBytecode;
        var OldFullscreenVertexShader=this.FullscreenVertexShaderBytecode;
        var OldPostProcessPixelShader=this.PostProcessPixelShaderBytecode;
        var OldShadowVertexShader=this.ShadowVertexShaderBytecode;
        var OldPipelineState=this.PipelineState;
        var OldShadowPipelineState=this.ShadowPipelineState;
        var OldPostProcessPipelineState=this.PostProcessPipelineState;
        try{
            this.ApplyShaderArtifact(Package);
            this.CreatePipelineState();
            this.CreateShadowPipelineState();
            this.CreatePostProcessPipelineState();
            this.WaitForGpuIdle();
            if(OldPipelineState!=null){ _=OldPipelineState->Release(); }
            if(OldShadowPipelineState!=null){ _=OldShadowPipelineState->Release(); }
            if(OldPostProcessPipelineState!=null){ _=OldPostProcessPipelineState->Release(); }
            this.ShaderArtifactLoaded=true;
            this.ShaderSemanticHash=Package.Document.SemanticHash;
            this.ShaderArtifactDiagnostics=[];
        }catch(Exception Exception){
            this.VertexShaderBytecode=OldVertexShader;
            this.PixelShaderBytecode=OldPixelShader;
            this.FullscreenVertexShaderBytecode=OldFullscreenVertexShader;
            this.PostProcessPixelShaderBytecode=OldPostProcessPixelShader;
            this.ShadowVertexShaderBytecode=OldShadowVertexShader;
            if(this.PipelineState!=OldPipelineState){ _=this.PipelineState->Release();this.PipelineState=OldPipelineState; }
            if(this.ShadowPipelineState!=OldShadowPipelineState){ _=this.ShadowPipelineState->Release();this.ShadowPipelineState=OldShadowPipelineState; }
            if(this.PostProcessPipelineState!=OldPostProcessPipelineState){ _=this.PostProcessPipelineState->Release();this.PostProcessPipelineState=OldPostProcessPipelineState; }
            this.ShaderArtifactDiagnostics=[Exception.Message];
        }
    }
    private byte[]VertexShaderBytecode=[];
    private byte[]PixelShaderBytecode=[];

    private void CreateRootSignature(){
        D3D12_ROOT_PARAMETER FrameParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL,
        };
        FrameParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0,RegisterSpace=0 };

        D3D12_ROOT_PARAMETER ObjectParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL,
        };
        ObjectParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1,RegisterSpace=0 };

        D3D12_DESCRIPTOR_RANGE SrvRange=new(){
            RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,
            NumDescriptors=5,
            BaseShaderRegister=0,
        };

        D3D12_ROOT_PARAMETER TextureParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };

        D3D12_STATIC_SAMPLER_DESC StaticSampler=new(){
            Filter=D3D12_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU=D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            AddressV=D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            AddressW=D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            ComparisonFunc=D3D12_COMPARISON_FUNC_NEVER,
            BorderColor=D3D12_STATIC_BORDER_COLOR_OPAQUE_WHITE,
            MaxLOD=float.MaxValue,
            ShaderRegister=0,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };

        D3D12_STATIC_SAMPLER_DESC ShadowSampler=new(){
            Filter=D3D12_FILTER_COMPARISON_MIN_MAG_LINEAR_MIP_POINT,
            AddressU=D3D12_TEXTURE_ADDRESS_MODE_BORDER,
            AddressV=D3D12_TEXTURE_ADDRESS_MODE_BORDER,
            AddressW=D3D12_TEXTURE_ADDRESS_MODE_BORDER,
            ComparisonFunc=D3D12_COMPARISON_FUNC_LESS_EQUAL,
            BorderColor=D3D12_STATIC_BORDER_COLOR_OPAQUE_WHITE,
            MaxLOD=float.MaxValue,
            ShaderRegister=1,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };

        D3D12_ROOT_PARAMETER LocalLightsParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };
        LocalLightsParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=5,RegisterSpace=0 };

        D3D12_ROOT_PARAMETER ForwardPlusTileListParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };
        ForwardPlusTileListParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=6,RegisterSpace=0 };

        D3D12_ROOT_PARAMETER SpotShadowProjectionsParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };
        SpotShadowProjectionsParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=7,RegisterSpace=0 };

        D3D12_ROOT_PARAMETER PointShadowProjectionsParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };
        PointShadowProjectionsParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=8,RegisterSpace=0 };

        D3D12_ROOT_PARAMETER AreaShadowMetadataParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };
        AreaShadowMetadataParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=9,RegisterSpace=0 };

        D3D12_ROOT_PARAMETER AreaShadowProjectionsParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };
        AreaShadowProjectionsParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=10,RegisterSpace=0 };

        D3D12_ROOT_PARAMETER*RootParams=stackalloc D3D12_ROOT_PARAMETER[9];
        RootParams[0]=FrameParam;
        RootParams[1]=ObjectParam;
        RootParams[2]=TextureParam;
        RootParams[2].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&SrvRange };
        RootParams[3]=LocalLightsParam;
        RootParams[4]=ForwardPlusTileListParam;
        RootParams[5]=SpotShadowProjectionsParam;
        RootParams[6]=PointShadowProjectionsParam;
        RootParams[7]=AreaShadowMetadataParam;
        RootParams[8]=AreaShadowProjectionsParam;
        D3D12_STATIC_SAMPLER_DESC*StaticSamplers=stackalloc D3D12_STATIC_SAMPLER_DESC[2];
        StaticSamplers[0]=StaticSampler;
        StaticSamplers[1]=ShadowSampler;

        D3D12_ROOT_SIGNATURE_DESC RootSignatureDesc=new(){
            NumParameters=9,
            pParameters=RootParams,
            NumStaticSamplers=2,
            pStaticSamplers=StaticSamplers,
            Flags=D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT,
        };

        ID3DBlob*SignatureBlob=null;
        ID3DBlob*ErrorBlob=null;
        try {
            HRESULT Hr=D3D12SerializeRootSignature(&RootSignatureDesc,D3D_ROOT_SIGNATURE_VERSION_1,&SignatureBlob,&ErrorBlob);
            if(Hr.FAILED){
                var Error=ErrorBlob!=null
                    ?new string((sbyte*)ErrorBlob->GetBufferPointer())
                    :"(no error blob)";
                throw new D3D12Exception("D3D12SerializeRootSignature",Hr.Value,"serializing the stage root signature",Error);
            }

            ID3D12RootSignature*RootSignature;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateRootSignature(0,SignatureBlob->GetBufferPointer(),SignatureBlob->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&RootSignature),
                "ID3D12Device::CreateRootSignature","creating the root signature");
            this.RootSignature=RootSignature;
        } finally {
            if(SignatureBlob!=null){
                _=SignatureBlob->Release();
            }

            if(ErrorBlob!=null){
                _=ErrorBlob->Release();
            }
        }
    }

    private void CreateShadowRootSignature(){
        D3D12_ROOT_PARAMETER FrameParam=new(){ ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,ShaderVisibility=D3D12_SHADER_VISIBILITY_VERTEX };
        FrameParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0,RegisterSpace=0 };
        D3D12_ROOT_PARAMETER ObjectParam=new(){ ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,ShaderVisibility=D3D12_SHADER_VISIBILITY_VERTEX };
        ObjectParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1,RegisterSpace=0 };
        D3D12_ROOT_PARAMETER*RootParams=stackalloc D3D12_ROOT_PARAMETER[2];
        RootParams[0]=FrameParam;
        RootParams[1]=ObjectParam;
        D3D12_ROOT_SIGNATURE_DESC RootSignatureDesc=new(){ NumParameters=2,pParameters=RootParams,Flags=D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT };
        ID3DBlob*SignatureBlob=null;
        ID3DBlob*ErrorBlob=null;
        try {
            D3D12Diagnostics.EnsureSuccess(D3D12SerializeRootSignature(&RootSignatureDesc,D3D_ROOT_SIGNATURE_VERSION_1,&SignatureBlob,&ErrorBlob),nameof(this.CreateShadowRootSignature),nameof(this.CreateShadowRootSignature));
            ID3D12RootSignature*Signature;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateRootSignature(0,SignatureBlob->GetBufferPointer(),SignatureBlob->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Signature),nameof(this.CreateShadowRootSignature),nameof(this.CreateShadowRootSignature));
            this.ShadowRootSignature=Signature;
        } finally {
            if(SignatureBlob!=null){ _=SignatureBlob->Release(); }
            if(ErrorBlob!=null){ _=ErrorBlob->Release(); }
        }
    }

    private void CreatePostProcessRootSignature(){
        D3D12_DESCRIPTOR_RANGE SceneRange=new(){
            RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,
            NumDescriptors=1,
            BaseShaderRegister=0,
        };
        D3D12_ROOT_PARAMETER SceneParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };
        SceneParam.DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&SceneRange };

        D3D12_STATIC_SAMPLER_DESC Sampler=new(){
            Filter=D3D12_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU=D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            AddressV=D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            AddressW=D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            ComparisonFunc=D3D12_COMPARISON_FUNC_NEVER,
            BorderColor=D3D12_STATIC_BORDER_COLOR_OPAQUE_WHITE,
            MaxLOD=float.MaxValue,
            ShaderRegister=0,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };

        D3D12_ROOT_PARAMETER*RootParams=stackalloc D3D12_ROOT_PARAMETER[1];
        RootParams[0]=SceneParam;
        D3D12_ROOT_SIGNATURE_DESC RootSignatureDesc=new(){
            NumParameters=1,
            pParameters=RootParams,
            NumStaticSamplers=1,
            pStaticSamplers=&Sampler,
        };

        ID3DBlob*SignatureBlob=null;
        ID3DBlob*ErrorBlob=null;
        try {
            D3D12Diagnostics.EnsureSuccess(
                D3D12SerializeRootSignature(&RootSignatureDesc,D3D_ROOT_SIGNATURE_VERSION_1,&SignatureBlob,&ErrorBlob),
                nameof(this.CreatePostProcessRootSignature),nameof(this.CreatePostProcessRootSignature));
            ID3D12RootSignature*Signature;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateRootSignature(0,SignatureBlob->GetBufferPointer(),SignatureBlob->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Signature),
                nameof(this.CreatePostProcessRootSignature),nameof(this.CreatePostProcessRootSignature));
            this.PostProcessRootSignature=Signature;
        } finally {
            if(SignatureBlob!=null){ _=SignatureBlob->Release(); }
            if(ErrorBlob!=null){ _=ErrorBlob->Release(); }
        }
    }

    private void CreatePipelineState(){
        fixed(byte*PPosition="POSITION\0"u8)
        fixed(byte*PNormal="NORMAL\0"u8)
        fixed(byte*PTangent="TANGENT\0"u8)
        fixed(byte*PTexCoord="TEXCOORD\0"u8)
        fixed(byte*PVertexShader= this.VertexShaderBytecode)
        fixed(byte*PPixelShader= this.PixelShaderBytecode){
            D3D12_INPUT_ELEMENT_DESC*InputElements=stackalloc D3D12_INPUT_ELEMENT_DESC[4]{
                new D3D12_INPUT_ELEMENT_DESC {
                    SemanticName=(sbyte*)PPosition,
                    Format=DXGI_FORMAT_R32G32B32_FLOAT,
                    InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
                new D3D12_INPUT_ELEMENT_DESC {
                    SemanticName=(sbyte*)PNormal,
                    Format=DXGI_FORMAT_R32G32B32_FLOAT,
                    AlignedByteOffset=12,
                    InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
                new D3D12_INPUT_ELEMENT_DESC {
                    SemanticName=(sbyte*)PTangent,
                    Format=DXGI_FORMAT_R32G32B32A32_FLOAT,
                    AlignedByteOffset=24,
                    InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
                new D3D12_INPUT_ELEMENT_DESC {
                    SemanticName=(sbyte*)PTexCoord,
                    Format=DXGI_FORMAT_R32G32_FLOAT,
                    AlignedByteOffset=40,
                    InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
            };

            D3D12_GRAPHICS_PIPELINE_STATE_DESC PsoDesc=new(){
                InputLayout=new D3D12_INPUT_LAYOUT_DESC {
                    pInputElementDescs=InputElements,
                    NumElements=4,
                },
                pRootSignature= this.RootSignature,
                VS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PVertexShader,BytecodeLength=(nuint)this.VertexShaderBytecode.Length },
                PS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PPixelShader,BytecodeLength=(nuint)this.PixelShaderBytecode.Length },
                RasterizerState=D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState=D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState=D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask=uint.MaxValue,
                PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                NumRenderTargets=1,
                DSVFormat=DepthBufferFormat,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            // Real backface culling is deferred, same rationale as ModelRenderer (ADR-0004).
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            PsoDesc.RTVFormats[0]=HdrColorFormat;

            ID3D12PipelineState*PipelineState;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateGraphicsPipelineState(&PsoDesc,__uuidof<ID3D12PipelineState>(),(void**)&PipelineState),
                "ID3D12Device::CreateGraphicsPipelineState","creating the stage pipeline state");
            this.PipelineState=PipelineState;
        }
    }

    private void CreateShadowPipelineState(){
        fixed(byte*PPosition="POSITION\0"u8)
        fixed(byte*PNormal="NORMAL\0"u8)
        fixed(byte*PTangent="TANGENT\0"u8)
        fixed(byte*PTexCoord="TEXCOORD\0"u8)
        fixed(byte*PVertexShader= this.ShadowVertexShaderBytecode){
            D3D12_INPUT_ELEMENT_DESC*InputElements=stackalloc D3D12_INPUT_ELEMENT_DESC[4]{
                new D3D12_INPUT_ELEMENT_DESC { SemanticName=(sbyte*)PPosition,Format=DXGI_FORMAT_R32G32B32_FLOAT,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA },
                new D3D12_INPUT_ELEMENT_DESC { SemanticName=(sbyte*)PNormal,Format=DXGI_FORMAT_R32G32B32_FLOAT,AlignedByteOffset=12,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA },
                new D3D12_INPUT_ELEMENT_DESC { SemanticName=(sbyte*)PTangent,Format=DXGI_FORMAT_R32G32B32A32_FLOAT,AlignedByteOffset=24,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA },
                new D3D12_INPUT_ELEMENT_DESC { SemanticName=(sbyte*)PTexCoord,Format=DXGI_FORMAT_R32G32_FLOAT,AlignedByteOffset=40,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA },
            };
            D3D12_GRAPHICS_PIPELINE_STATE_DESC PsoDesc=new(){
                InputLayout=new D3D12_INPUT_LAYOUT_DESC { pInputElementDescs=InputElements,NumElements=4 },
                pRootSignature= this.ShadowRootSignature,
                VS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PVertexShader,BytecodeLength=(nuint)this.ShadowVertexShaderBytecode.Length },
                RasterizerState=D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState=D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState=D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask=uint.MaxValue,
                PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                NumRenderTargets=0,
                DSVFormat=ShadowAtlasDsvFormat,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            ID3D12PipelineState*Pipeline;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateGraphicsPipelineState(&PsoDesc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline),nameof(this.CreateShadowPipelineState),nameof(this.CreateShadowPipelineState));
            this.ShadowPipelineState=Pipeline;
        }
    }

    private void CreatePostProcessPipelineState(){
        fixed(byte*PVertexShader= this.FullscreenVertexShaderBytecode)
        fixed(byte*PPixelShader= this.PostProcessPixelShaderBytecode){
            D3D12_GRAPHICS_PIPELINE_STATE_DESC PsoDesc=new(){
                pRootSignature= this.PostProcessRootSignature,
                VS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PVertexShader,BytecodeLength=(nuint)this.FullscreenVertexShaderBytecode.Length },
                PS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PPixelShader,BytecodeLength=(nuint)this.PostProcessPixelShaderBytecode.Length },
                RasterizerState=D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState=D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState=D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask=uint.MaxValue,
                PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                NumRenderTargets=1,
                RTVFormats=default,
                DSVFormat=DXGI_FORMAT_UNKNOWN,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            PsoDesc.DepthStencilState.DepthEnable=FALSE;
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            PsoDesc.RTVFormats[0]=BackBufferFormat;

            ID3D12PipelineState*Pipeline;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateGraphicsPipelineState(&PsoDesc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline),
                nameof(this.CreatePostProcessPipelineState),nameof(this.CreatePostProcessPipelineState));
            this.PostProcessPipelineState=Pipeline;
        }
    }

    private void CreateMeshBuffers(IReadOnlyList<StageMeshCreateInfo>Meshes){
        this.MeshCount=Meshes.Count;
        this.VertexBuffers=new ID3D12Resource*[this.MeshCount];
        this.VertexBufferViews=new D3D12_VERTEX_BUFFER_VIEW[this.MeshCount];
        this.IndexBuffers=new ID3D12Resource*[this.MeshCount];
        this.IndexBufferViews=new D3D12_INDEX_BUFFER_VIEW[this.MeshCount];
        this.IndexCounts=new uint[this.MeshCount];

        for(var I=0;I< this.MeshCount;I++){
            StageMeshCreateInfo Mesh=Meshes[I];
            this.CreateVertexBuffer(I,Mesh.Positions,Mesh.Normals,Mesh.Tangents,Mesh.Uvs);
            this.CreateIndexBuffer(I,Mesh.Indices);
        }
    }

    private void CreateVertexBuffer(int MeshIndex,IReadOnlyList<Vector3>Positions,IReadOnlyList<Vector3>Normals,IReadOnlyList<Vector4>Tangents,IReadOnlyList<Vector2>Uvs){
        var VertexCount=Positions.Count;
        Vertex[]Vertices=new Vertex[VertexCount];
        for(var I=0;I<VertexCount;I++){
            Vertices[I]=new Vertex { Position=Positions[I],Normal=Normals[I],Tangent=Tangents[I],Uv=Uvs[I]};
        }

        var BufferSize=(uint)(sizeof(Vertex)*VertexCount);

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(BufferSize);

        ID3D12Resource*VertexBuffer;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&VertexBuffer),
            "ID3D12Device::CreateCommittedResource","creating a stage vertex buffer (upload heap)");

        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(VertexBuffer->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping a stage vertex buffer");
        fixed(Vertex*PVertices=Vertices){
            Unsafe.CopyBlock(Mapped,PVertices,BufferSize);
        }
        VertexBuffer->Unmap(0,null);

        this.VertexBuffers[MeshIndex]=VertexBuffer;
        this.VertexBufferViews[MeshIndex]=new D3D12_VERTEX_BUFFER_VIEW {
                                                                           BufferLocation=VertexBuffer->GetGPUVirtualAddress(),
                                                                           StrideInBytes=(uint)sizeof(Vertex),
                                                                           SizeInBytes=BufferSize,
                                                                       };
    }

    private void CreateIndexBuffer(int MeshIndex,IReadOnlyList<uint>Indices){
        var IndexCount=Indices.Count;
        var IndexArray=Indices as uint[]??[.. Indices];
        var BufferSize=(uint)(sizeof(uint)*IndexCount);

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(BufferSize);

        ID3D12Resource*IndexBuffer;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&IndexBuffer),
            "ID3D12Device::CreateCommittedResource","creating a stage index buffer (upload heap)");

        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(IndexBuffer->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping a stage index buffer");
        fixed(uint*PIndices=IndexArray){
            Unsafe.CopyBlock(Mapped,PIndices,BufferSize);
        }
        IndexBuffer->Unmap(0,null);

        this.IndexBuffers[MeshIndex]=IndexBuffer;
        this.IndexCounts[MeshIndex]=(uint)IndexCount;
        this.IndexBufferViews[MeshIndex]=new D3D12_INDEX_BUFFER_VIEW {
                                                                         BufferLocation=IndexBuffer->GetGPUVirtualAddress(),
                                                                         SizeInBytes=BufferSize,
                                                                         Format=DXGI_FORMAT_R32_UINT,
                                                                     };
    }

    private void CreateMaterialResources(IReadOnlyList<StageMaterialCreateInfo>Materials){
        this.MaterialCount=Materials.Count;
        this.MaterialTextures=new ID3D12Resource*[this.MaterialCount*4];
        this.MaterialBaseColorFactors=new Vector4[this.MaterialCount];
        this.MaterialRoughnessFactors=new float[this.MaterialCount];
        this.MaterialMetallicFactors=new float[this.MaterialCount];
        this.MaterialEmissiveNits=new Vector3[this.MaterialCount];

        for(var I=0;I< this.MaterialCount;I++){
            StageMaterialCreateInfo Material=Materials[I];
            this.MaterialBaseColorFactors[I]=Material.BaseColorFactor;
            this.MaterialRoughnessFactors[I]=Material.RoughnessFactor;
            this.MaterialMetallicFactors[I]=Material.MetallicFactor;
            this.MaterialEmissiveNits[I]=Material.EmissiveNits!=Vector3.Zero
                ?Material.EmissiveNits
                :Material.EmissiveFactor*GltfImportEmissiveDefaultNits;

            this.MaterialTextures[(I*4)+0]= this.CreateTextureResource(Material.BaseColorTextureBytes,WhiteFallback());
            this.MaterialTextures[(I*4)+1]= this.CreateTextureResource(Material.NormalTextureBytes,FlatNormalFallback());
            this.MaterialTextures[(I*4)+2]= this.CreateTextureResource(Material.MetallicRoughnessTextureBytes,WhiteFallback());
            this.MaterialTextures[(I*4)+3]= this.CreateTextureResource(Material.EmissiveTextureBytes,WhiteFallback());
        }

        D3D12_DESCRIPTOR_HEAP_DESC SrvHeapDesc=new(){
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
            NumDescriptors=(uint)(this.MaterialCount*5),
            Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
        };
        ID3D12DescriptorHeap*SrvHeap;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateDescriptorHeap(&SrvHeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&SrvHeap),
            "ID3D12Device::CreateDescriptorHeap","creating the material SRV descriptor heap");
        this.SrvHeap=SrvHeap;
        this.SrvDescriptorSize= this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

        D3D12_CPU_DESCRIPTOR_HANDLE SrvHeapStart=SrvHeap->GetCPUDescriptorHandleForHeapStart();
        D3D12_SHADER_RESOURCE_VIEW_DESC SrvDesc=new(){
            Format=MaterialTextureFormat,
            ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        SrvDesc.Texture2D.MipLevels=1;

        for(var I=0;I< this.MaterialTextures.Length;I++){
            D3D12_CPU_DESCRIPTOR_HANDLE Handle=new(SrvHeapStart,(I/4)*5+(I%4), this.SrvDescriptorSize);
            this.Device.Device->CreateShaderResourceView(this.MaterialTextures[I],&SrvDesc,Handle);
        }

        D3D12_SHADER_RESOURCE_VIEW_DESC ShadowSrvDesc=new(){
            Format=ShadowAtlasSrvFormat,
            ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        ShadowSrvDesc.Texture2D.MipLevels=1;
        for(var I=0;I< this.MaterialCount;I++){
            D3D12_CPU_DESCRIPTOR_HANDLE Handle=new(SrvHeapStart,(I*5)+4, this.SrvDescriptorSize);
            this.Device.Device->CreateShaderResourceView(this.ShadowAtlas,&ShadowSrvDesc,Handle);
        }
    }

    private static DecodedImage WhiteFallback()=>new(1,1,[255,255,255,255]);

    /// <summary>Flat tangent-space normal (0,0,1) encoded as RGB(128,128,255), so an unmapped surface's TBN-transformed normal is exactly its own vertex normal regardless of the (possibly arbitrary) tangent.</summary>
    private static DecodedImage FlatNormalFallback()=>new(1,1,[128,128,255,255]);

    /// <summary>Decodes (or falls back to a procedural default, mirroring <see cref="ModelRenderer"/>'s "every mesh gets a real SRV" rationale) and uploads one material texture slot.</summary>
    private ID3D12Resource*CreateTextureResource(byte[]?EncodedBytes,DecodedImage Fallback){
        DecodedImage Image=DecodeOrFallback(EncodedBytes,Fallback);

        D3D12_RESOURCE_DESC TextureDesc=D3D12_RESOURCE_DESC.Tex2D(MaterialTextureFormat,(uint)Image.Width,(uint)Image.Height,mipLevels:1);

        D3D12_HEAP_PROPERTIES DefaultHeapProps=new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource*Texture;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&DefaultHeapProps,D3D12_HEAP_FLAG_NONE,&TextureDesc,D3D12_RESOURCE_STATE_COPY_DEST,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&Texture),
            "ID3D12Device::CreateCommittedResource","creating a material texture");

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT Footprint;
        ulong TotalBytes;
        this.Device.Device->GetCopyableFootprints(&TextureDesc,0,1,0,&Footprint,null,null,&TotalBytes);

        D3D12_HEAP_PROPERTIES UploadHeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC StagingDesc=D3D12_RESOURCE_DESC.Buffer(TotalBytes);

        ID3D12Resource*Staging;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&UploadHeapProps,D3D12_HEAP_FLAG_NONE,&StagingDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&Staging),
            "ID3D12Device::CreateCommittedResource","creating a material texture upload staging buffer");

        try {
            // GetCopyableFootprints' RowPitch is 256-byte aligned; the decoded image is tightly
            // packed (stride = Width*4). These differ whenever Width*4 isn't already a multiple of
            // 256, so this must be a per-row copy, not one bulk CopyBlock (same as ModelRenderer).
            D3D12_RANGE NoRead=new();
            byte*Mapped;
            D3D12Diagnostics.EnsureSuccess(Staging->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping a material texture upload staging buffer");

            var SrcRowPitch=(uint)Image.Width*4;
            var DstRowPitch=Footprint.Footprint.RowPitch;
            fixed(byte*PSrc=Image.Pixels){
                for(uint Y=0;Y<Image.Height;Y++){
                    Unsafe.CopyBlock(Mapped+(Y*DstRowPitch),PSrc+(Y*SrcRowPitch),SrcRowPitch);
                }
            }
            Staging->Unmap(0,null);

            D3D12Diagnostics.EnsureSuccess(this.CommandAllocators[0]->Reset(),"ID3D12CommandAllocator::Reset","resetting the allocator for a material texture upload");
            D3D12Diagnostics.EnsureSuccess(this.CommandList->Reset(this.CommandAllocators[0],null),"ID3D12GraphicsCommandList::Reset","resetting the command list for a material texture upload");

            D3D12_TEXTURE_COPY_LOCATION Dst=new(){ pResource=Texture,Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            Dst.SubresourceIndex=0;
            D3D12_TEXTURE_COPY_LOCATION Src=new(){ pResource=Staging,Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            Src.PlacedFootprint=Footprint;
            this.CommandList->CopyTextureRegion(&Dst,0,0,0,&Src,null);

            D3D12_RESOURCE_BARRIER ToShaderResource=D3D12_RESOURCE_BARRIER.InitTransition(Texture,D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
            this.CommandList->ResourceBarrier(1,&ToShaderResource);

            D3D12Diagnostics.EnsureSuccess(this.CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing a material texture upload command list");

            ID3D12CommandList*CommandListBase=(ID3D12CommandList*)this.CommandList;
            this.Device.CommandQueue->ExecuteCommandLists(1,&CommandListBase);

            this.WaitForGpuIdle();
        } finally {
            _=Staging->Release();
        }

        return Texture;
    }

    private static DecodedImage DecodeOrFallback(byte[]?EncodedBytes,DecodedImage Fallback){
        if(EncodedBytes is null||EncodedBytes.Length==0){
            return Fallback;
        }

        WicDecodeResult DecodeResult=WicImageDecoder.Decode(EncodedBytes);
        return DecodeResult.Success?DecodeResult.Image!:Fallback;
    }

    private void CreateObjectMappings(IReadOnlyList<StageObjectCreateInfo>Objects){
        this.ObjectCount=Objects.Count;
        this.ObjectMeshIndices=new int[this.ObjectCount];
        this.ObjectMaterialIndices=new int[this.ObjectCount];
        this.ObjectTransforms=new Matrix4x4[this.ObjectCount];

        for(var I=0;I< this.ObjectCount;I++){
            this.ObjectMeshIndices[I]=Objects[I].MeshIndex;
            this.ObjectMaterialIndices[I]=Objects[I].MaterialIndex;
            this.ObjectTransforms[I]=Matrix4x4.Identity;
        }
    }

    private void CreateConstantBuffers(){
        var FrameBufferSize=FrameConstantsStride*FrameCount;
        this.CreateUploadBuffer(FrameBufferSize,out this.FrameConstantsBuffer,out this.FrameConstantsMapped,"creating the frame constant buffer");

        var LocalLightBufferSize=LocalLightStride*(uint)MaxLocalLights*FrameCount;
        this.CreateUploadBuffer(LocalLightBufferSize,out this.LocalLightsBuffer,out this.LocalLightsMapped,"creating the Forward+ local-light StructuredBuffer");

        var SpotShadowProjectionBufferSize=SpotShadowProjectionStride*(uint)MaxSpotShadowProjections*FrameCount;
        this.CreateUploadBuffer(SpotShadowProjectionBufferSize,out this.SpotShadowProjectionBuffer,
            out this.SpotShadowProjectionMapped,"creating the spot shadow-projection StructuredBuffer");

        var PointShadowProjectionBufferSize=PointShadowProjectionStride*(uint)MaxPointShadowProjections*FrameCount;
        this.CreateUploadBuffer(PointShadowProjectionBufferSize,out this.PointShadowProjectionBuffer,
            out this.PointShadowProjectionMapped,"creating the point shadow-projection StructuredBuffer");

        var AreaShadowMetadataBufferSize=AreaShadowMetadataStride*(uint)MaxAreaAndEmissiveShadowProjections*FrameCount;
        this.CreateUploadBuffer(AreaShadowMetadataBufferSize,out this.AreaShadowMetadataBuffer,
            out this.AreaShadowMetadataMapped,"creating the area shadow-metadata StructuredBuffer");

        var AreaShadowProjectionBufferSize=AreaShadowProjectionStride*(uint)MaxAreaAndEmissiveShadowProjections*FrameCount;
        this.CreateUploadBuffer(AreaShadowProjectionBufferSize,out this.AreaShadowProjectionBuffer,
            out this.AreaShadowProjectionMapped,"creating the area shadow-projection StructuredBuffer");

        var ObjectBufferSize=ConstantAlignment*(uint)this.ObjectCount*FrameCount;
        this.CreateUploadBuffer(ObjectBufferSize,out this.ObjectConstantsBuffer,out this.ObjectConstantsMapped,"creating the object constant buffer");
    }

    private void CreateForwardPlusTileBuffer(int Width,int Height){
        this.ForwardPlusTileCountX=(Width+ForwardPlusTileSizePixels-1)/ForwardPlusTileSizePixels;
        this.ForwardPlusTileCountY=(Height+ForwardPlusTileSizePixels-1)/ForwardPlusTileSizePixels;
        uint TileCount=checked((uint)(this.ForwardPlusTileCountX*this.ForwardPlusTileCountY));
        this.ForwardPlusTileFrameSize=checked(TileCount*ForwardPlusTileRecordStride);
        uint BufferSize=checked(this.ForwardPlusTileFrameSize*FrameCount);
        this.CreateUploadBuffer(BufferSize,out this.ForwardPlusTileListBuffer,out this.ForwardPlusTileListMapped,"creating the Forward+ tile-list StructuredBuffer");
        for(uint Frame=0;Frame<FrameCount;Frame++){
            this.WriteForwardPlusGrid(Frame);
            this.ClearForwardPlusTileList(Frame);
        }
    }

    private void ReleaseForwardPlusTileBuffer(){
        if(this.ForwardPlusTileListBuffer!=null){
            this.ForwardPlusTileListBuffer->Unmap(0,null);
            this.ForwardPlusTileListMapped=null;
            _=this.ForwardPlusTileListBuffer->Release();
            this.ForwardPlusTileListBuffer=null;
            this.ForwardPlusTileFrameSize=0;
            this.ForwardPlusTileCountX=0;
            this.ForwardPlusTileCountY=0;
        }
    }

    private void CreateShadowConstantsBuffer(){
        var ShadowBufferSize=ConstantAlignment*FrameCount*(uint)ShadowAtlasProjectionConstantSlotCount;
        this.CreateUploadBuffer(ShadowBufferSize,out this.ShadowConstantsBuffer,out this.ShadowConstantsMapped,nameof(this.CreateShadowConstantsBuffer));
    }

    private void CreateUploadBuffer(uint Size,out ID3D12Resource*Buffer,out byte*Mapped,string Context){
        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(Size);

        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource",Context);

        D3D12_RANGE NoRead=new();
        byte*Ptr;
        D3D12Diagnostics.EnsureSuccess(Resource->Map(0,&NoRead,(void**)&Ptr),"ID3D12Resource::Map",Context);

        Buffer=Resource;
        Mapped=Ptr;
    }

    /// <summary>
    /// Writes an identity world matrix (with each object's material factors), a default camera,
    /// and default light/ambient/fog values into every frame-in-flight slot, so the very first
    /// Present reads fully-initialized data even if the caller's first SetCamera/SetObjectTransform/
    /// SetDirectionalLight/etc. call happens after this constructor returns.
    /// </summary>
    private void InitializeConstantsToDefaults(){
        Camera DefaultCamera=Camera.CreateDefault(Vector3.Zero);

        for(uint Frame=0;Frame<FrameCount;Frame++){
            this.WriteViewProjection(Frame,DefaultCamera.GetView()*DefaultCamera.GetProjection((float)this.Width/ this.Height));
            this.WriteCameraPosition(Frame,DefaultCamera.GetEyePosition());
            this.WriteDirectionalLight(Frame,0,DefaultLightDirectionTowardLight,DefaultLightColor,0.0015f);
            this.WriteAmbientLight(Frame,DefaultAmbientColor);
            this.WriteFog(Frame,FogParams.None);
            this.WriteExposure(Frame,DefaultCamera.ExposureEv100);
            this.WriteShadowViewProjection(Frame,BuildShadowViewProjection(DefaultLightDirectionTowardLight));
            this.WriteLightCounts(Frame); // D3D12 does not guarantee zero-initialized upload-heap memory; without this the very first frame could loop over garbage PointLights/SpotLights entries.
            this.WriteAreaShadowDefaults(Frame);

            for(var ObjectIndex=0;ObjectIndex< this.ObjectCount;ObjectIndex++){
                this.WriteObjectConstants(Frame,ObjectIndex);
            }
        }
    }

    /// <summary>
    /// Sets the camera used for the frame currently being built. Must be called every frame,
    /// before <see cref="RenderFrame"/> - the constant buffer is double-buffered per frame-in-flight
    /// slot (<see cref="FrameCount"/>) and only the current slot is written here.
    /// </summary>
    public void SetCamera(in Camera Camera,float AspectRatioOverride=0f){
        this.ThrowIfDisposed();
        var AspectRatio=AspectRatioOverride>0f?AspectRatioOverride:(float)this.Width/ this.Height;
        this.WriteViewProjection(this.FrameIndex,Camera.GetView()*Camera.GetProjection(AspectRatio));
        this.WriteCameraPosition(this.FrameIndex,Camera.GetEyePosition());
        this.WriteExposure(this.FrameIndex,Camera.ExposureEv100);
    }

    /// <summary>
    /// Backward-compatible singleton setter. It replaces this frame's additive Directional list
    /// with exactly one light. <paramref name="DirectionTowardLight"/> points from the scene
    /// toward the light, not the direction light travels.
    /// </summary>
    public void SetDirectionalLight(
        Vector3 DirectionTowardLight,Vector3 Color,float Intensity,
        bool CastsShadows=true,float ShadowBiasMeters=0.0015f,int ShadowPriority=0){
        this.ThrowIfDisposed();
        if(!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f){
            throw new ArgumentOutOfRangeException(nameof(ShadowBiasMeters));
        }
        this.BeginDirectionalLightSubmission();
        var Submission=new DirectionalLightSubmission(
            DirectionTowardLight,Color*Intensity,CastsShadows,ShadowPriority,ShadowBiasMeters,
            StableSubmissionOrder:0);
        this.AddDirectionalLightCore(
            DirectionTowardLight,Color,Intensity,CastsShadows,ShadowPriority,ShadowBiasMeters);
        // Keep the value in the deferred list too: a later Add in this same frame must retain
        // the setter's light instead of silently replacing it.
        this.DirectionalLightSubmissions.Add(Submission);
        this.PersistentDirectionalLightSubmission=Submission;
        this.DirectionalLightsPersistAcrossFrames=true;
    }

    /// <summary>
    /// Adds a legacy surface-to-light Directional input for the current frame. Direct-light
    /// overflow keeps the four largest screen contributions, then stable submission order.
    /// The one-map shadow pass independently follows <paramref name="ShadowPriority"/>.
    /// </summary>
    public void AddDirectionalLightTowardLight(
        Vector3 DirectionTowardLight,Vector3 Color,float Intensity,
        bool CastsShadows=true,int ShadowPriority=0,float ShadowBiasMeters=0.0015f){
        this.ThrowIfDisposed();
        if(!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f){
            throw new ArgumentOutOfRangeException(nameof(ShadowBiasMeters));
        }
        if(!this.DirectionalLightsExplicitThisFrame){
            this.BeginDirectionalLightSubmission();
        }
        this.DirectionalLightsPersistAcrossFrames=false;
        this.DirectionalLightSubmissions.Add(new DirectionalLightSubmission(
            DirectionTowardLight,Color*Intensity,CastsShadows,ShadowPriority,ShadowBiasMeters,
            this.DirectionalLightSubmissions.Count));
    }

    private void BeginDirectionalLightSubmission(){
        this.DirectionalLightsExplicitThisFrame=true;
        this.DirectionalLightsPersistAcrossFrames=false;
        this.DirectionalLightCount=0;
        this.DirectionalShadowIndex=-1;
        this.SelectedDirectionalShadowAtlasCandidate=null;
        this.DirectionalShadowCandidates.Clear();
        this.PersistentDirectionalLightSubmission=null;
        this.DirectionalLightSubmissions.Clear();
    }

    private void AddDirectionalLightCore(
        Vector3 DirectionTowardLight,Vector3 Color,float Intensity,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        if(this.DirectionalLightCount>=MaxDirectionalLights){
            this.DroppedDirectionalLightsThisFrameValue++;
            return;
        }

        int Index=this.DirectionalLightCount;
        this.WriteDirectionalLight(
            this.FrameIndex,Index,DirectionTowardLight,Color*Intensity,ShadowBiasMeters);
        this.DirectionalLightCount++;
        this.DirectionalShadowCandidates.Add(new PhysicalLightCandidate(
            Index,PhysicalLightKind.Directional,ShadowPriority,ScreenContribution:0f,CastsShadows));
        int SelectedShadowIndex=PhysicalLightBudget.SelectDirectionalShadowIndex(
            this.DirectionalShadowCandidates);
        this.SelectedDirectionalShadowAtlasCandidate=SelectedShadowIndex>=0
            ?this.DirectionalShadowCandidates[SelectedShadowIndex] :null;
        if(SelectedShadowIndex!=this.DirectionalShadowIndex){
            this.DirectionalShadowIndex=SelectedShadowIndex;
            if(SelectedShadowIndex>=0){
                this.WriteShadowViewProjection(
                    this.FrameIndex,BuildShadowViewProjection(DirectionTowardLight));
            }
        }
        this.WriteLightCounts(this.FrameIndex);
    }

    /// <summary>
    /// Finalizes additive Directional submissions after the frame's camera is known. Direct
    /// lighting intentionally ignores shadow priority: it is ranked by contribution and then
    /// stable submission order. Shadow priority is applied only to the selected direct lights.
    /// </summary>
    private void PrepareDirectionalLights(uint FrameIndex){
        if(this.DirectionalLightsPersistAcrossFrames){
            if(this.PersistentDirectionalLightSubmission is not DirectionalLightSubmission Submission){
                this.DirectionalLightCount=0;
                this.DirectionalShadowIndex=-1;
                this.SelectedDirectionalShadowAtlasCandidate=null;
                this.DirectionalShadowCandidates.Clear();
                this.WriteLightCounts(FrameIndex);
                return;
            }

            this.DirectionalLightCount=1;
            this.DirectionalShadowIndex=-1;
            this.SelectedDirectionalShadowAtlasCandidate=null;
            this.DirectionalShadowCandidates.Clear();
            this.WriteDirectionalLight(
                FrameIndex,0,Submission.DirectionTowardLight,Submission.ColorTimesIntensity,
                Submission.ShadowBiasMeters);
            if(Submission.CastsShadows){
                var Candidate=new PhysicalLightCandidate(
                    Submission.StableSubmissionOrder,PhysicalLightKind.Directional,
                    Submission.ShadowPriority,CalculateDirectionalScreenContribution(Submission),
                    CastsShadows:true);
                this.DirectionalShadowCandidates.Add(Candidate);
                this.DirectionalShadowIndex=0;
                this.SelectedDirectionalShadowAtlasCandidate=Candidate;
                this.WriteShadowViewProjection(
                    FrameIndex,BuildShadowViewProjection(Submission.DirectionTowardLight));
            }
            this.WriteLightCounts(FrameIndex);
            return;
        }

        if(!this.DirectionalLightsExplicitThisFrame){
            return;
        }

        List<RankedDirectionalLightSubmission> Ranked=new(this.DirectionalLightSubmissions.Count);
        foreach(DirectionalLightSubmission Submission in this.DirectionalLightSubmissions){
            Ranked.Add(new RankedDirectionalLightSubmission(
                Submission,CalculateDirectionalScreenContribution(Submission)));
        }
        Ranked.Sort(static (Left,Right)=>{
            int Contribution=Right.ScreenContribution.CompareTo(Left.ScreenContribution);
            return Contribution!=0
                ?Contribution
                :Left.Submission.StableSubmissionOrder.CompareTo(Right.Submission.StableSubmissionOrder);
        });

        this.DirectionalLightCount=Math.Min(Ranked.Count,MaxDirectionalLights);
        this.DroppedDirectionalLightsThisFrameValue=Ranked.Count-this.DirectionalLightCount;
        this.DirectionalShadowIndex=-1;
        this.SelectedDirectionalShadowAtlasCandidate=null;
        this.DirectionalShadowCandidates.Clear();
        for(int Index=0;Index<this.DirectionalLightCount;Index++){
            DirectionalLightSubmission Submission=Ranked[Index].Submission;
            this.WriteDirectionalLight(
                FrameIndex,Index,Submission.DirectionTowardLight,Submission.ColorTimesIntensity,
                Submission.ShadowBiasMeters);
            this.DirectionalShadowCandidates.Add(new PhysicalLightCandidate(
                Submission.StableSubmissionOrder,PhysicalLightKind.Directional,
                Submission.ShadowPriority,Ranked[Index].ScreenContribution,Submission.CastsShadows));
        }

        int SelectedStableOrder=PhysicalLightBudget.SelectDirectionalShadowIndex(
            this.DirectionalShadowCandidates);
        if(SelectedStableOrder>=0){
            for(int Index=0;Index<this.DirectionalShadowCandidates.Count;Index++){
                if(this.DirectionalShadowCandidates[Index].StableSubmissionOrder!=SelectedStableOrder){
                    continue;
                }
                this.DirectionalShadowIndex=Index;
                this.SelectedDirectionalShadowAtlasCandidate=this.DirectionalShadowCandidates[Index];
                this.WriteShadowViewProjection(
                    FrameIndex,BuildShadowViewProjection(Ranked[Index].Submission.DirectionTowardLight));
                break;
            }
        }
        this.WriteLightCounts(FrameIndex);
    }

    private static float CalculateDirectionalScreenContribution(in DirectionalLightSubmission Submission){
        float Contribution=MathF.Max(Submission.ColorTimesIntensity.X,
            MathF.Max(Submission.ColorTimesIntensity.Y,Submission.ColorTimesIntensity.Z));
        if(!float.IsFinite(Contribution)){
            return float.MaxValue;
        }
        return MathF.Max(0f,Contribution);
    }

    /// <summary>Sets the flat ambient term (a stand-in for sky/indirect lighting) for the frame currently being built.</summary>
    public void SetAmbientLight(Vector3 Color){
        this.ThrowIfDisposed();
        this.WriteAmbientLight(this.FrameIndex,Color);
    }

    /// <summary>Sets the simplified exponential height fog for the frame currently being built. Pass <see cref="FogParams.None"/> to disable it (zero density).</summary>
    public void SetFog(FogParams Fog){
        this.ThrowIfDisposed();
        this.WriteFog(this.FrameIndex,Fog);
    }

    /// <summary>
    /// Adds one point light (radiates equally in all directions, attenuated by distance with a
    /// smooth falloff that reaches zero at <paramref name="Range"/>) for the frame currently being
    /// built. It is source-compatible with the former fixed point-light cbuffer API; submission is
    /// deferred until <see cref="RenderFrame"/>, where all point/spot lights share the 1024-entry
    /// StructuredBuffer and deterministic Forward+ ranking.
    /// </summary>
    public void AddPointLight(Vector3 Position,Vector3 Color,float Intensity,float Range){
        this.ThrowIfDisposed();
        this.AddPointLight(Position,Color,Intensity,Range,SelectionPriority:0);
    }

    internal void AddPointLight(Vector3 Position,Vector3 Color,float Intensity,float Range,int SelectionPriority){
        this.AddPointLight(
            Position,Color,Intensity,Range,SelectionPriority,CastsShadows:false,ShadowBiasMeters:0f);
    }

    internal void AddPointLight(
        Vector3 Position,Vector3 Color,float Intensity,float Range,int SelectionPriority,
        bool CastsShadows,float ShadowBiasMeters){
        if(!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f){
            throw new ArgumentOutOfRangeException(nameof(ShadowBiasMeters));
        }
        this.LocalLightSubmissions.Add(new LocalLightSubmission(
            LocalLightType.Point,Position,Range,Vector3.Zero,0f,Color*Intensity,0f,
            SelectionPriority,this.LocalLightSubmissions.Count,CastsShadows,ShadowBiasMeters));
    }

    /// <summary>
    /// Adds one spot light (a point light additionally narrowed to a cone) for the frame currently
    /// being built. <paramref name="Direction"/> is the direction the cone points toward (need not
    /// be normalized) and the brightness falls off smoothly between
    /// <paramref name="InnerConeAngleRadians"/> (full brightness) and
    /// <paramref name="OuterConeAngleRadians"/> (zero). It shares the same deferred StructuredBuffer
    /// and 1024-entry global selection as <see cref="AddPointLight(Vector3,Vector3,float,float)"/>.
    /// </summary>
    public void AddSpotLight(Vector3 Position,Vector3 Direction,Vector3 Color,float Intensity,float Range,float InnerConeAngleRadians,float OuterConeAngleRadians){
        this.ThrowIfDisposed();
        this.AddSpotLight(Position,Direction,Color,Intensity,Range,InnerConeAngleRadians,OuterConeAngleRadians,SelectionPriority:0);
    }

    internal void AddSpotLight(
        Vector3 Position,Vector3 Direction,Vector3 Color,float Intensity,float Range,
        float InnerConeAngleRadians,float OuterConeAngleRadians,int SelectionPriority){
        this.AddSpotLight(Position,Direction,Color,Intensity,Range,InnerConeAngleRadians,
            OuterConeAngleRadians,SelectionPriority,CastsShadows:false,ShadowBiasMeters:0f);
    }

    internal void AddSpotLight(
        Vector3 Position,Vector3 Direction,Vector3 Color,float Intensity,float Range,
        float InnerConeAngleRadians,float OuterConeAngleRadians,int SelectionPriority,
        bool CastsShadows,float ShadowBiasMeters){
        if(!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f){
            throw new ArgumentOutOfRangeException(nameof(ShadowBiasMeters));
        }
        this.LocalLightSubmissions.Add(new LocalLightSubmission(
            LocalLightType.Spot,Position,Range,Vector3.Normalize(Direction),MathF.Cos(InnerConeAngleRadians),
            Color*Intensity,MathF.Cos(OuterConeAngleRadians),SelectionPriority,
            this.LocalLightSubmissions.Count,CastsShadows,ShadowBiasMeters));
    }
    /// <summary>
    /// Queues one one-sided rectangle for the current frame. Its shadow settings are retained on
    /// the submission but are intentionally not allocated until the projected Atlas path is
    /// connected; direct lighting remains active in that interim state.
    /// </summary>
    internal void AddAreaLight(
        Vector3 Position,Vector3 Direction,Vector3 Right,Vector3 Up,Vector3 LinearRgb,
        float LuminanceNits,float WidthMeters,float HeightMeters,float RangeMeters,
        int SelectionPriority,bool CastsShadows,float ShadowBiasMeters,PhysicalLightKind ShadowKind){
        if(ShadowKind!=PhysicalLightKind.Area&&ShadowKind!=PhysicalLightKind.Emissive){
            throw new ArgumentOutOfRangeException(nameof(ShadowKind));
        }
        if(!IsFinite(Position)||!IsFinite(LinearRgb)
            ||!float.IsFinite(LuminanceNits)||LuminanceNits<0f
            ||!float.IsFinite(WidthMeters)||WidthMeters<=0f
            ||!float.IsFinite(HeightMeters)||HeightMeters<=0f
            ||!float.IsFinite(RangeMeters)||RangeMeters<=0f
            ||!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f
            ||!TryNormalize(Direction,out Vector3 UnitDirection)
            ||!TryNormalize(Right,out Vector3 UnitRight)
            ||!TryNormalize(Up,out Vector3 UnitUp)
            ||MathF.Abs(Vector3.Dot(UnitDirection,UnitRight))>1e-3f
            ||MathF.Abs(Vector3.Dot(UnitDirection,UnitUp))>1e-3f
            ||MathF.Abs(Vector3.Dot(UnitRight,UnitUp))>1e-3f
            ||Vector3.Dot(Vector3.Cross(UnitRight,UnitUp),UnitDirection)<0.999f){
            throw new ArgumentException(
                "Area lights require finite positive dimensions/range and a right-handed orthonormal basis.");
        }
        this.LocalLightSubmissions.Add(new LocalLightSubmission(
            LocalLightType.Area,Position,RangeMeters,UnitDirection,0f,LinearRgb*LuminanceNits,0f,
            SelectionPriority,this.LocalLightSubmissions.Count,CastsShadows,ShadowBiasMeters,
            UnitRight,UnitUp,WidthMeters*0.5f,HeightMeters*0.5f,ShadowKind));
    }


    /// <summary>Sets the world transform for one object for the frame currently being built (see <see cref="SetCamera"/> for the per-frame-slot contract).</summary>
    public void SetObjectTransform(int ObjectIndex,Matrix4x4 World){
        this.ThrowIfDisposed();
        if(ObjectIndex<0||ObjectIndex>= this.ObjectCount){
            throw new ArgumentOutOfRangeException(nameof(ObjectIndex));
        }

        this.ObjectTransforms[ObjectIndex]=World;
        this.WriteObjectConstants(this.FrameIndex,ObjectIndex);
    }

    private void ThrowIfDisposed(){
        if(this.Disposed){
            throw new ObjectDisposedException(nameof(StageRenderer));
        }
    }

    private byte*FrameSlot(uint FrameIndex)=> this.FrameConstantsMapped+(FrameIndex*FrameConstantsStride);

    private void WriteViewProjection(uint FrameIndex,Matrix4x4 ViewProjection){
        this.FrameViewProjections[FrameIndex]=ViewProjection;
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameViewProjectionOffset,&ViewProjection,(uint)sizeof(Matrix4x4));
    }

    private void WriteCameraPosition(uint FrameIndex,Vector3 CameraPosition){
        Vector4 Packed=new(CameraPosition,0f);
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameCameraPositionOffset,&Packed,(uint)sizeof(Vector4));
    }

    /// <summary>Writes Stage shader ABI's DirectionalLightData[index] (direction+bias, color*intensity).</summary>
    private void WriteDirectionalLight(
        uint FrameIndex,int Index,Vector3 DirectionTowardLight,Vector3 ColorTimesIntensity,
        float ShadowBiasMeters){
        var Destination=this.FrameSlot(FrameIndex)+FrameDirectionalLightsOffset+((uint)Index*DirectionalLightStride);
        Vector4 Direction=new(Vector3.Normalize(DirectionTowardLight),ShadowBiasMeters);
        Vector4 Color=new(ColorTimesIntensity,0f);
        Unsafe.CopyBlock(Destination,&Direction,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Destination+16,&Color,(uint)sizeof(Vector4));
    }

    private void WriteAmbientLight(uint FrameIndex,Vector3 Color){
        Vector4 Packed=new(Color,0f);
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameAmbientColorOffset,&Packed,(uint)sizeof(Vector4));
    }

    private void WriteFog(uint FrameIndex,FogParams Fog){
        Vector4 FogParams0=new(Fog.Density,Fog.HeightFalloff,0f,0f);
        Vector4 FogColor=new(Fog.Color,0f);
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameFogParamsOffset,&FogParams0,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameFogColorOffset,&FogColor,(uint)sizeof(Vector4));
    }

    private void WriteExposure(uint FrameIndex,float ExposureEv100){
        if(!float.IsFinite(ExposureEv100)){
            throw new ArgumentOutOfRangeException(nameof(ExposureEv100),"Camera exposure must be finite.");
        }
        // EV100 is one stop per integer: +1 halves the HDR signal before ACES.
        Vector4 Packed=new(PhysicalLightUnits.Ev100ToExposureScale(ExposureEv100),ExposureEv100,0f,0f);
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameExposureOffset,&Packed,(uint)sizeof(Vector4));
    }

    private static Matrix4x4 BuildShadowViewProjection(Vector3 DirectionTowardLight){
        Vector3 Direction=Vector3.Normalize(DirectionTowardLight);
        Matrix4x4 View=Matrix4x4.CreateLookAt(-(Direction*35f),Vector3.Zero,Vector3.UnitY);
        return View*Matrix4x4.CreateOrthographic(40f,40f,0.1f,80f);
    }

    private void WriteShadowViewProjection(uint FrameIndex,Matrix4x4 ShadowViewProjection){
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameShadowViewProjectionOffset,&ShadowViewProjection,(uint)sizeof(Matrix4x4));
        Unsafe.CopyBlock(this.ShadowConstantsSlot(FrameIndex,0),&ShadowViewProjection,(uint)sizeof(Matrix4x4));
        this.WriteDirectionalShadowAtlasUvScaleOffset(FrameIndex);
    }

    private byte*ShadowConstantsSlot(uint FrameIndex,int Slot){
        if((uint)Slot>=(uint)ShadowAtlasProjectionConstantSlotCount){
            throw new ArgumentOutOfRangeException(nameof(Slot));
        }
        uint LinearSlot=(FrameIndex*(uint)ShadowAtlasProjectionConstantSlotCount)+(uint)Slot;
        return this.ShadowConstantsMapped+(LinearSlot*ConstantAlignment);
    }

    /// <summary>Writes the PhysicalShadowAtlasPlan tile-0 rectangle used by the directional shadow pass.</summary>
    private void WriteDirectionalShadowAtlasUvScaleOffset(uint FrameIndex){
        Vector4 UvScaleOffset=DirectionalShadowAtlasTile.UvScaleOffset;
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameDirectionalShadowAtlasUvScaleOffset,
            &UvScaleOffset,(uint)sizeof(Vector4));
    }

    private byte*LocalLightsFrameSlot(uint FrameIndex)=>this.LocalLightsMapped+(FrameIndex*(LocalLightStride*(uint)MaxLocalLights));

    private byte*SpotShadowProjectionFrameSlot(uint FrameIndex)=>this.SpotShadowProjectionMapped+(FrameIndex*(SpotShadowProjectionStride*(uint)MaxSpotShadowProjections));

    private byte*PointShadowProjectionFrameSlot(uint FrameIndex)=>this.PointShadowProjectionMapped+(FrameIndex*(PointShadowProjectionStride*(uint)MaxPointShadowProjections));

    private byte*AreaShadowMetadataFrameSlot(uint FrameIndex)=>this.AreaShadowMetadataMapped
        +(FrameIndex*(AreaShadowMetadataStride*(uint)MaxAreaAndEmissiveShadowProjections));

    private byte*AreaShadowProjectionFrameSlot(uint FrameIndex)=>this.AreaShadowProjectionMapped
        +(FrameIndex*(AreaShadowProjectionStride*(uint)MaxAreaAndEmissiveShadowProjections));

    private byte*ForwardPlusTileFrameSlot(uint FrameIndex)=>this.ForwardPlusTileListMapped+(FrameIndex*this.ForwardPlusTileFrameSize);

    private void WriteForwardPlusGrid(uint FrameIndex){
        var Grid=stackalloc uint[4]{
            (uint)this.ForwardPlusTileCountX,(uint)this.ForwardPlusTileCountY,
            ForwardPlusTileSizePixels,MaxLightsPerForwardPlusTile,
        };
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameForwardPlusGridOffset,Grid,16);
    }

    private void ClearForwardPlusTileList(uint FrameIndex){
        if(this.ForwardPlusTileFrameSize!=0){
            Unsafe.InitBlock(this.ForwardPlusTileFrameSlot(FrameIndex),0,this.ForwardPlusTileFrameSize);
        }
    }

    private void PrepareForwardPlusLocalLights(uint FrameIndex){
        this.ClearForwardPlusTileList(FrameIndex);
        this.SelectedLocalLightSubmissions.Clear();
        this.SpotShadowProjectionCount=0;
        this.PointShadowProjectionCount=0;
        this.AreaShadowProjectionCount=0;
        Matrix4x4 ViewProjection=this.FrameViewProjections[FrameIndex];
        List<RankedLocalLightSubmission> Ranked=new(this.LocalLightSubmissions.Count);
        foreach(LocalLightSubmission Submission in this.LocalLightSubmissions){
            Ranked.Add(new RankedLocalLightSubmission(
                Submission,CalculateScreenContribution(Submission,ViewProjection)));
        }

        Ranked.Sort(static (Left,Right)=>{
            int Priority=Right.Submission.SelectionPriority.CompareTo(Left.Submission.SelectionPriority);
            if(Priority!=0){
                return Priority;
            }
            int Contribution=Right.ScreenContribution.CompareTo(Left.ScreenContribution);
            return Contribution!=0
                ?Contribution
                :Left.Submission.StableSubmissionOrder.CompareTo(Right.Submission.StableSubmissionOrder);
        });

        this.LocalLightCount=Math.Min(Ranked.Count,MaxLocalLights);
        this.DroppedLocalLightsThisFrameValue=Ranked.Count-this.LocalLightCount;
        this.DroppedTileLightReferencesThisFrameValue=0;
        for(int LocalIndex=0;LocalIndex<this.LocalLightCount;LocalIndex++){
            LocalLightSubmission Submission=Ranked[LocalIndex].Submission;
            this.WriteLocalLight(FrameIndex,LocalIndex,Submission);
            this.SelectedLocalLightSubmissions.Add(Submission);
            if(this.TryGetForwardPlusTileBounds(Submission,ViewProjection,
                out int MinimumTileX,out int MaximumTileX,out int MinimumTileY,out int MaximumTileY)){
                for(int TileY=MinimumTileY;TileY<=MaximumTileY;TileY++){
                    for(int TileX=MinimumTileX;TileX<=MaximumTileX;TileX++){
                        this.AddForwardPlusTileCandidate(FrameIndex,TileX,TileY,LocalIndex);
                    }
                }
            }
        }
        this.WriteLightCounts(FrameIndex);
    }

    /// <summary>
    /// Uses PhysicalShadowAtlasPlan as the only allocation authority. Selected Point casters
    /// receive their plan-owned six face tiles; selected Spot and Area/Emissive casters each
    /// receive one center-projection tile through the same single plan selection.
    /// </summary>
    private void PrepareLocalShadowAtlasProjections(uint FrameIndex){
        this.PointShadowAtlasProjections.Clear();
        this.SpotShadowAtlasProjections.Clear();
        this.AreaShadowAtlasProjections.Clear();
        this.PointShadowProjectionCount=0;
        this.SpotShadowProjectionCount=0;
        this.AreaShadowProjectionCount=0;
        List<PhysicalLightCandidate>Casters=[];
        Dictionary<int,int>LocalIndexByStableOrder=[];

        if(this.DirectionalShadowIndex>=0){
            // The atlas has one directional tile regardless of the original direct-list stable
            // order. Normalize it to zero so a selected submission beyond the first four cannot
            // collide with the local-light namespace used below.
            PhysicalLightCandidate Directional=this.SelectedDirectionalShadowAtlasCandidate
                ??new PhysicalLightCandidate(0,PhysicalLightKind.Directional,0,0f,CastsShadows:true);
            Casters.Add(new PhysicalLightCandidate(
                0,PhysicalLightKind.Directional,Directional.ShadowPriority,
                Directional.ScreenContribution,CastsShadows:true));
        }

        Matrix4x4 ViewProjection=this.FrameViewProjections[FrameIndex];
        for(int LocalIndex=0;LocalIndex<this.SelectedLocalLightSubmissions.Count;LocalIndex++){
            LocalLightSubmission Submission=this.SelectedLocalLightSubmissions[LocalIndex];
            bool IsValidCaster=Submission.Type switch {
                LocalLightType.Point=>TryValidatePointShadowProjection(Submission),
                LocalLightType.Spot=>TryBuildSpotShadowViewProjection(Submission,out _),
                LocalLightType.Area=>TryBuildAreaShadowViewProjection(Submission,out _),
                _=>false,
            };
            if(!Submission.CastsShadows||!IsValidCaster){
                continue;
            }

            int StableOrder=MaxDirectionalLights+Submission.StableSubmissionOrder;
            PhysicalLightKind Kind=Submission.ShadowKind;
            Casters.Add(new PhysicalLightCandidate(
                StableOrder,Kind,Submission.SelectionPriority,
                CalculateScreenContribution(Submission,ViewProjection),CastsShadows:true));
            LocalIndexByStableOrder.Add(StableOrder,LocalIndex);
        }

        if(Casters.Count==0){
            this.WriteLightCounts(FrameIndex);
            return;
        }

        PhysicalShadowAtlasPlan Plan=PhysicalShadowAtlasPlan.Select(Casters);
        Span<Matrix4x4> PointFaceViewProjections=stackalloc Matrix4x4[MaxPointShadowProjections];
        foreach(ShadowAtlasAllocation Allocation in Plan.Allocations){
            if(!LocalIndexByStableOrder.TryGetValue(
                Allocation.Candidate.StableSubmissionOrder,out int LocalIndex)){
                continue;
            }

            LocalLightSubmission Submission=this.SelectedLocalLightSubmissions[LocalIndex];
            if(Allocation.Candidate.Kind==PhysicalLightKind.Point){
                if(this.PointShadowProjectionCount!=0
                    ||Allocation.TileCount!=MaxPointShadowProjections){
                    continue;
                }

                if(!TryBuildPointShadowViewProjections(Submission,PointFaceViewProjections)){
                    continue;
                }

                int ProjectionBaseIndex=this.PointShadowProjectionCount;
                for(int FaceIndex=0;FaceIndex<MaxPointShadowProjections;FaceIndex++){
                    ShadowAtlasTile PointTile=Allocation.GetTile(FaceIndex);
                    this.WritePointShadowProjection(
                        FrameIndex,ProjectionBaseIndex+FaceIndex,PointFaceViewProjections[FaceIndex],PointTile);
                    this.PointShadowAtlasProjections.Add(new ShadowAtlasProjection(
                        PointTile,PointShadowProjectionConstantSlotOffset+ProjectionBaseIndex+FaceIndex));
                }
                this.WritePointShadowMetadata(FrameIndex,LocalIndex,Submission,ProjectionBaseIndex);
                this.PointShadowProjectionCount+=MaxPointShadowProjections;
                continue;
            }

            if(Allocation.Candidate.Kind==PhysicalLightKind.Spot){
                if(this.SpotShadowProjectionCount>=MaxSpotShadowProjections
                    ||!TryBuildSpotShadowViewProjection(Submission,out Matrix4x4 ShadowViewProjection)){
                    continue;
                }

                int ProjectionIndex=this.SpotShadowProjectionCount;
                ShadowAtlasTile Tile=Allocation.GetTile(0);
                this.WriteSpotShadowProjection(FrameIndex,ProjectionIndex,ShadowViewProjection,Tile);
                this.WriteSpotShadowMetadata(FrameIndex,LocalIndex,Submission,ProjectionIndex);
                this.SpotShadowAtlasProjections.Add(new ShadowAtlasProjection(
                    Tile,ProjectionIndex+1)); // slot zero belongs to the directional projection.
                this.SpotShadowProjectionCount++;
                continue;
            }

            if((Allocation.Candidate.Kind!=PhysicalLightKind.Area
                &&Allocation.Candidate.Kind!=PhysicalLightKind.Emissive)
                ||this.AreaShadowProjectionCount>=MaxAreaAndEmissiveShadowProjections
                ||!TryBuildAreaShadowViewProjection(Submission,out Matrix4x4 AreaShadowViewProjection)){
                continue;
            }

            int AreaProjectionIndex=this.AreaShadowProjectionCount;
            if(Allocation.TileCount!=1){
                continue;
            }
            ShadowAtlasTile AreaTile=Allocation.GetTile(0);
            this.WriteAreaShadowProjection(
                FrameIndex,AreaProjectionIndex,AreaShadowViewProjection,AreaTile);
            this.WriteAreaShadowMetadata(FrameIndex,LocalIndex,Submission,AreaProjectionIndex);
            this.AreaShadowAtlasProjections.Add(new ShadowAtlasProjection(
                AreaTile,AreaShadowProjectionConstantSlotOffset+AreaProjectionIndex));
            this.AreaShadowProjectionCount++;
        }

        this.WriteLightCounts(FrameIndex);
    }

    private static float CalculateScreenContribution(in LocalLightSubmission Submission,in Matrix4x4 ViewProjection){
        float Luminance=MathF.Max(Submission.ColorTimesIntensity.X,
            MathF.Max(Submission.ColorTimesIntensity.Y,Submission.ColorTimesIntensity.Z));
        if(!float.IsFinite(Luminance)||!float.IsFinite(Submission.Range)){
            return float.MaxValue;
        }
        Luminance=MathF.Max(0f,Luminance);
        Vector4 Clip=Vector4.Transform(new Vector4(Submission.Position,1f),ViewProjection);
        float Depth=MathF.Abs(Clip.W);
        if(!float.IsFinite(Depth)||Depth<1e-3f){
            return MathF.Min(float.MaxValue,Luminance*Submission.Range*Submission.Range);
        }
        float Contribution=Luminance*Submission.Range*Submission.Range/MathF.Max(Depth*Depth,1e-4f);
        return float.IsFinite(Contribution)?Contribution:float.MaxValue;
    }

    private bool TryGetForwardPlusTileBounds(
        in LocalLightSubmission Submission,in Matrix4x4 ViewProjection,
        out int MinimumTileX,out int MaximumTileX,out int MinimumTileY,out int MaximumTileY){
        if(this.ForwardPlusTileCountX<=0||this.ForwardPlusTileCountY<=0){
            MinimumTileX=MaximumTileX=MinimumTileY=MaximumTileY=0;
            return false;
        }
        if(!IsFinite(Submission.Position)||!float.IsFinite(Submission.Range)||Submission.Range<=0f){
            MinimumTileX=0;
            MaximumTileX=this.ForwardPlusTileCountX-1;
            MinimumTileY=0;
            MaximumTileY=this.ForwardPlusTileCountY-1;
            return true;
        }

        float MinimumNdcX=float.MaxValue;
        float MaximumNdcX=float.MinValue;
        float MinimumNdcY=float.MaxValue;
        float MaximumNdcY=float.MinValue;
        for(int X=-1;X<=1;X+=2){
            for(int Y=-1;Y<=1;Y+=2){
                for(int Z=-1;Z<=1;Z+=2){
                    Vector3 Corner=Submission.Position+(new Vector3(X,Y,Z)*Submission.Range);
                    Vector4 Clip=Vector4.Transform(new Vector4(Corner,1f),ViewProjection);
                    if(!float.IsFinite(Clip.X)||!float.IsFinite(Clip.Y)||!float.IsFinite(Clip.W)||Clip.W<=1e-4f){
                        MinimumTileX=0;
                        MaximumTileX=this.ForwardPlusTileCountX-1;
                        MinimumTileY=0;
                        MaximumTileY=this.ForwardPlusTileCountY-1;
                        return true;
                    }
                    float NdcX=Clip.X/Clip.W;
                    float NdcY=Clip.Y/Clip.W;
                    MinimumNdcX=MathF.Min(MinimumNdcX,NdcX);
                    MaximumNdcX=MathF.Max(MaximumNdcX,NdcX);
                    MinimumNdcY=MathF.Min(MinimumNdcY,NdcY);
                    MaximumNdcY=MathF.Max(MaximumNdcY,NdcY);
                }
            }
        }

        if(MaximumNdcX< -1f||MinimumNdcX>1f||MaximumNdcY< -1f||MinimumNdcY>1f){
            MinimumTileX=MaximumTileX=MinimumTileY=MaximumTileY=0;
            return false;
        }
        float MinimumPixelX=(MinimumNdcX*0.5f+0.5f)*this.Width;
        float MaximumPixelX=(MaximumNdcX*0.5f+0.5f)*this.Width;
        float MinimumPixelY=(-MaximumNdcY*0.5f+0.5f)*this.Height;
        float MaximumPixelY=(-MinimumNdcY*0.5f+0.5f)*this.Height;
        MinimumTileX=Math.Clamp((int)MathF.Floor(MinimumPixelX/ForwardPlusTileSizePixels),0,this.ForwardPlusTileCountX-1);
        MaximumTileX=Math.Clamp((int)MathF.Floor(MaximumPixelX/ForwardPlusTileSizePixels),0,this.ForwardPlusTileCountX-1);
        MinimumTileY=Math.Clamp((int)MathF.Floor(MinimumPixelY/ForwardPlusTileSizePixels),0,this.ForwardPlusTileCountY-1);
        MaximumTileY=Math.Clamp((int)MathF.Floor(MaximumPixelY/ForwardPlusTileSizePixels),0,this.ForwardPlusTileCountY-1);
        return true;
    }

    private static bool IsFinite(Vector3 Value)=>float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z);

    private static bool TryNormalize(Vector3 Value,out Vector3 Normalized){
        Normalized=Vector3.Zero;
        if(!IsFinite(Value)){
            return false;
        }
        float LengthSquared=Value.LengthSquared();
        if(!float.IsFinite(LengthSquared)||LengthSquared<=1e-12f){
            return false;
        }
        Normalized=Value/MathF.Sqrt(LengthSquared);
        return IsFinite(Normalized);
    }

    private void WriteLocalLight(uint FrameIndex,int Index,in LocalLightSubmission Submission){
        LocalLightGpuData Data=new(){
            PositionAndRange=new Vector4(Submission.Position,Submission.Range),
            DirectionAndInnerCos=new Vector4(Submission.Direction,Submission.InnerConeCos),
            ColorAndOuterCos=new Vector4(Submission.ColorTimesIntensity,Submission.OuterConeCos),
            TypeAndPadding=new Vector4((float)Submission.Type,-1f,0f,0f),
            RightAndHalfWidth=new Vector4(Submission.Right,Submission.HalfWidth),
            UpAndHalfHeight=new Vector4(Submission.Up,Submission.HalfHeight),
        };
        byte*Destination=this.LocalLightsFrameSlot(FrameIndex)+((uint)Index*LocalLightStride);
        Unsafe.CopyBlock(Destination,&Data,LocalLightStride);
    }

    private void WritePointShadowMetadata(
        uint FrameIndex,int LocalLightIndex,in LocalLightSubmission Submission,int ProjectionBaseIndex){
        if(Submission.Type!=LocalLightType.Point){
            throw new ArgumentException("Only point lights may own six-face atlas shadows.",nameof(Submission));
        }
        Vector4 Metadata=new((float)Submission.Type,ProjectionBaseIndex,Submission.ShadowBiasMeters,0f);
        byte*Destination=this.LocalLightsFrameSlot(FrameIndex)+((uint)LocalLightIndex*LocalLightStride)+48;
        Unsafe.CopyBlock(Destination,&Metadata,(uint)sizeof(Vector4));
    }

    private void WriteSpotShadowMetadata(
        uint FrameIndex,int LocalLightIndex,in LocalLightSubmission Submission,int ProjectionIndex){
        if(Submission.Type!=LocalLightType.Spot){
            throw new ArgumentException("Only spot lights may own projected atlas shadows.",nameof(Submission));
        }
        Vector4 Metadata=new((float)Submission.Type,ProjectionIndex,Submission.ShadowBiasMeters,0f);
        byte*Destination=this.LocalLightsFrameSlot(FrameIndex)+((uint)LocalLightIndex*LocalLightStride)+48;
        Unsafe.CopyBlock(Destination,&Metadata,(uint)sizeof(Vector4));
    }

    /// <summary>
    /// Writes the source extent and selected center-projection index for one true rectangular
    /// Area/Emissive light. The metadata record is indexed by projection index; the local-light
    /// record carries that same index for the later pixel-shadow lookup.
    /// </summary>
    private void WriteAreaShadowMetadata(
        uint FrameIndex,int LocalLightIndex,in LocalLightSubmission Submission,int ProjectionIndex){
        if(Submission.Type!=LocalLightType.Area){
            throw new ArgumentException("Only Area/Emissive lights may own area atlas shadows.",nameof(Submission));
        }
        if((uint)ProjectionIndex>=(uint)MaxAreaAndEmissiveShadowProjections){
            throw new ArgumentOutOfRangeException(nameof(ProjectionIndex));
        }

        AreaShadowMetadataGpuData Data=new(){
            SourceRadiusAndPadding=new Vector4(
                MathF.Sqrt((Submission.HalfWidth*Submission.HalfWidth)
                    +(Submission.HalfHeight*Submission.HalfHeight)),
                Math.Clamp(Submission.Range*0.005f,0.01f,0.1f),
                MathF.Max(Submission.Range,
                    Math.Clamp(Submission.Range*0.005f,0.01f,0.1f)+0.01f),0f),
        };
        byte*MetadataDestination=this.AreaShadowMetadataFrameSlot(FrameIndex)
            +((uint)ProjectionIndex*AreaShadowMetadataStride);
        Unsafe.CopyBlock(MetadataDestination,&Data,AreaShadowMetadataStride);

        Vector4 Metadata=new((float)Submission.Type,ProjectionIndex,Submission.ShadowBiasMeters,0f);
        byte*LocalLightDestination=this.LocalLightsFrameSlot(FrameIndex)
            +((uint)LocalLightIndex*LocalLightStride)+48;
        Unsafe.CopyBlock(LocalLightDestination,&Metadata,(uint)sizeof(Vector4));
    }

    private void WriteAreaShadowProjection(
        uint FrameIndex,int ProjectionIndex,Matrix4x4 ViewProjection,in ShadowAtlasTile Tile){
        if((uint)ProjectionIndex>=(uint)MaxAreaAndEmissiveShadowProjections){
            throw new ArgumentOutOfRangeException(nameof(ProjectionIndex));
        }
        AreaShadowProjectionGpuData Data=new(){
            ViewProjection=ViewProjection,
            UvScaleOffset=Tile.UvScaleOffset,
        };
        byte*Destination=this.AreaShadowProjectionFrameSlot(FrameIndex)
            +((uint)ProjectionIndex*AreaShadowProjectionStride);
        Unsafe.CopyBlock(Destination,&Data,AreaShadowProjectionStride);
        Unsafe.CopyBlock(this.ShadowConstantsSlot(
            FrameIndex,AreaShadowProjectionConstantSlotOffset+ProjectionIndex),
            &ViewProjection,(uint)sizeof(Matrix4x4));
    }

    /// <summary>Initializes the independently-bound t9/t10 records before any Area light is submitted.</summary>
    private void WriteAreaShadowDefaults(uint FrameIndex){
        AreaShadowMetadataGpuData Metadata=new(){ SourceRadiusAndPadding=Vector4.Zero };
        AreaShadowProjectionGpuData Projection=new(){
            ViewProjection=Matrix4x4.Identity,
            UvScaleOffset=Vector4.Zero,
        };
        for(int ProjectionIndex=0;ProjectionIndex<MaxAreaAndEmissiveShadowProjections;ProjectionIndex++){
            byte*MetadataDestination=this.AreaShadowMetadataFrameSlot(FrameIndex)
                +((uint)ProjectionIndex*AreaShadowMetadataStride);
            Unsafe.CopyBlock(MetadataDestination,&Metadata,AreaShadowMetadataStride);
            byte*ProjectionDestination=this.AreaShadowProjectionFrameSlot(FrameIndex)
                +((uint)ProjectionIndex*AreaShadowProjectionStride);
            Unsafe.CopyBlock(ProjectionDestination,&Projection,AreaShadowProjectionStride);
            Matrix4x4 ViewProjection=Projection.ViewProjection;
            Unsafe.CopyBlock(this.ShadowConstantsSlot(
                FrameIndex,AreaShadowProjectionConstantSlotOffset+ProjectionIndex),
                &ViewProjection,(uint)sizeof(Matrix4x4));
        }
    }

    private void WritePointShadowProjection(
        uint FrameIndex,int ProjectionIndex,Matrix4x4 ViewProjection,in ShadowAtlasTile Tile){
        if((uint)ProjectionIndex>=(uint)MaxPointShadowProjections){
            throw new ArgumentOutOfRangeException(nameof(ProjectionIndex));
        }
        PointShadowProjectionGpuData Data=new(){
            ViewProjection=ViewProjection,
            UvScaleOffset=Tile.UvScaleOffset,
        };
        byte*Destination=this.PointShadowProjectionFrameSlot(FrameIndex)
            +((uint)ProjectionIndex*PointShadowProjectionStride);
        Unsafe.CopyBlock(Destination,&Data,PointShadowProjectionStride);
        Unsafe.CopyBlock(this.ShadowConstantsSlot(
            FrameIndex,PointShadowProjectionConstantSlotOffset+ProjectionIndex),
            &ViewProjection,(uint)sizeof(Matrix4x4));
    }

    private void WriteSpotShadowProjection(
        uint FrameIndex,int ProjectionIndex,Matrix4x4 ViewProjection,in ShadowAtlasTile Tile){
        if((uint)ProjectionIndex>=(uint)MaxSpotShadowProjections){
            throw new ArgumentOutOfRangeException(nameof(ProjectionIndex));
        }
        SpotShadowProjectionGpuData Data=new(){
            ViewProjection=ViewProjection,
            UvScaleOffset=Tile.UvScaleOffset,
        };
        byte*Destination=this.SpotShadowProjectionFrameSlot(FrameIndex)
            +((uint)ProjectionIndex*SpotShadowProjectionStride);
        Unsafe.CopyBlock(Destination,&Data,SpotShadowProjectionStride);
        Unsafe.CopyBlock(this.ShadowConstantsSlot(FrameIndex,ProjectionIndex+1),
            &ViewProjection,(uint)sizeof(Matrix4x4));
    }

    private static bool TryBuildSpotShadowViewProjection(
        in LocalLightSubmission Submission,out Matrix4x4 ShadowViewProjection){
        ShadowViewProjection=Matrix4x4.Identity;
        if(!IsFinite(Submission.Position)||!IsFinite(Submission.Direction)
            ||!float.IsFinite(Submission.Range)||Submission.Range<=0f
            ||!float.IsFinite(Submission.OuterConeCos)){
            return false;
        }

        float DirectionLengthSquared=Submission.Direction.LengthSquared();
        if(DirectionLengthSquared<1e-8f||!float.IsFinite(DirectionLengthSquared)){
            return false;
        }

        Vector3 Direction=Vector3.Normalize(Submission.Direction);
        Vector3 Up=MathF.Abs(Vector3.Dot(Direction,Vector3.UnitY))>0.99f
            ?Vector3.UnitX:Vector3.UnitY;
        float OuterHalfAngleRadians=MathF.Acos(Math.Clamp(Submission.OuterConeCos,-1f,1f));
        float FieldOfView=Math.Clamp(OuterHalfAngleRadians*2f,0.01f,MathF.PI-0.01f);
        float NearZ=Math.Clamp(Submission.Range*0.005f,0.01f,0.1f);
        float FarZ=MathF.Max(Submission.Range,NearZ+0.01f);
        Matrix4x4 View=Matrix4x4.CreateLookAt(
            Submission.Position,Submission.Position+Direction,Up);
        ShadowViewProjection=View*Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView,1f,NearZ,FarZ);
        return true;
    }

    /// <summary>
    /// Builds the safe center projection used by an Area/Emissive rectangular source's atlas
    /// tile. The source itself remains a rectangle for direct lighting; this is intentionally a
    /// conservative, one-tile perspective view centered on the source normal for the later PCSS
    /// lookup path.
    /// </summary>
    private static bool TryBuildAreaShadowViewProjection(
        in LocalLightSubmission Submission,out Matrix4x4 ShadowViewProjection){
        ShadowViewProjection=Matrix4x4.Identity;
        if(Submission.Type!=LocalLightType.Area
            ||!IsFinite(Submission.Position)||!IsFinite(Submission.Direction)||!IsFinite(Submission.Up)
            ||!float.IsFinite(Submission.Range)||Submission.Range<=0f
            ||!float.IsFinite(Submission.HalfWidth)||Submission.HalfWidth<=0f
            ||!float.IsFinite(Submission.HalfHeight)||Submission.HalfHeight<=0f){
            return false;
        }
        if(!TryNormalize(Submission.Direction,out Vector3 Direction)
            ||!TryNormalize(Submission.Up,out Vector3 Up)){
            return false;
        }
        float DirectionUpDot=MathF.Abs(Vector3.Dot(Direction,Up));
        if(!float.IsFinite(DirectionUpDot)||DirectionUpDot>=0.999f){
            return false;
        }

        float NearZ=Math.Clamp(Submission.Range*0.005f,0.01f,0.1f);
        float FarZ=MathF.Max(Submission.Range,NearZ+0.01f);
        Matrix4x4 View=Matrix4x4.CreateLookAt(
            Submission.Position,Submission.Position+Direction,Up);
        ShadowViewProjection=View*Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI/2f,1f,NearZ,FarZ);
        return true;
    }

    private static bool TryValidatePointShadowProjection(in LocalLightSubmission Submission)=>
        IsFinite(Submission.Position)&&float.IsFinite(Submission.Range)&&Submission.Range>0f;

    /// <summary>
    /// Builds the six row-vector 90-degree point-light views in the atlas face order
    /// +X, -X, +Y, -Y, +Z, -Z. HLSL's face selector uses this exact ordering.
    /// </summary>
    private static bool TryBuildPointShadowViewProjections(
        in LocalLightSubmission Submission,Span<Matrix4x4> FaceViewProjections){
        if(FaceViewProjections.Length<MaxPointShadowProjections
            ||!TryValidatePointShadowProjection(Submission)){
            return false;
        }

        float NearZ=Math.Clamp(Submission.Range*0.005f,0.01f,0.1f);
        float FarZ=MathF.Max(Submission.Range,NearZ+0.01f);
        Matrix4x4 Projection=Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI/2f,1f,NearZ,FarZ);
        for(int FaceIndex=0;FaceIndex<MaxPointShadowProjections;FaceIndex++){
            GetPointShadowFaceBasis(FaceIndex,out Vector3 Direction,out Vector3 Up);
            Matrix4x4 View=Matrix4x4.CreateLookAt(
                Submission.Position,Submission.Position+Direction,Up);
            FaceViewProjections[FaceIndex]=View*Projection;
        }
        return true;
    }

    private static void GetPointShadowFaceBasis(
        int FaceIndex,out Vector3 Direction,out Vector3 Up){
        switch(FaceIndex){
            case 0:
                Direction=Vector3.UnitX;
                Up=Vector3.UnitY;
                return;
            case 1:
                Direction=-Vector3.UnitX;
                Up=Vector3.UnitY;
                return;
            case 2:
                Direction=Vector3.UnitY;
                Up=-Vector3.UnitZ;
                return;
            case 3:
                Direction=-Vector3.UnitY;
                Up=Vector3.UnitZ;
                return;
            case 4:
                Direction=Vector3.UnitZ;
                Up=Vector3.UnitY;
                return;
            case 5:
                Direction=-Vector3.UnitZ;
                Up=Vector3.UnitY;
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(FaceIndex));
        }
    }

    private void AddForwardPlusTileCandidate(uint FrameIndex,int TileX,int TileY,int LocalLightIndex){
        uint TileIndex=(uint)((TileY*this.ForwardPlusTileCountX)+TileX);
        uint*Record=(uint*)(this.ForwardPlusTileFrameSlot(FrameIndex)+(TileIndex*ForwardPlusTileRecordStride));
        uint Count=Record[0];
        if(Count>=(uint)MaxLightsPerForwardPlusTile){
            this.DroppedTileLightReferencesThisFrameValue++;
            return;
        }
        Record[Count+1]=(uint)LocalLightIndex;
        Record[0]=Count+1;
    }

    /// <summary>Writes FrameCB counts: local StructuredBuffer count, directional shadow index and directional count.</summary>
    private void WriteLightCounts(uint FrameIndex){
        var Counts=stackalloc int[4]{ this.LocalLightCount,this.DirectionalShadowIndex,this.DirectionalLightCount,this.SpotShadowProjectionCount };
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameLightCountsOffset,Counts,16);
    }

    private void WriteObjectConstants(uint FrameIndex,int ObjectIndex){
        var Slot=((ulong)FrameIndex*(ulong)this.ObjectCount)+(ulong)ObjectIndex;
        var Destination= this.ObjectConstantsMapped+(Slot*ConstantAlignment);

        Matrix4x4 World= this.ObjectTransforms[ObjectIndex];
        Unsafe.CopyBlock(Destination+ObjectWorldOffset,&World,(uint)sizeof(Matrix4x4));

        var MaterialIndex= this.ObjectMaterialIndices[ObjectIndex];
        Vector4 BaseColorFactor= this.MaterialBaseColorFactors[MaterialIndex];
        Unsafe.CopyBlock(Destination+ObjectBaseColorFactorOffset,&BaseColorFactor,(uint)sizeof(Vector4));

        Vector4 MaterialParams=new(this.MaterialRoughnessFactors[MaterialIndex], this.MaterialMetallicFactors[MaterialIndex],0f,0f);
        Unsafe.CopyBlock(Destination+ObjectMaterialParamsOffset,&MaterialParams,(uint)sizeof(Vector4));

        Vector4 EmissiveNits=new(this.MaterialEmissiveNits[MaterialIndex],1f);
        Unsafe.CopyBlock(Destination+ObjectEmissiveFactorOffset,&EmissiveNits,(uint)sizeof(Vector4));
    }

    private ulong GetFrameConstantsAddress(uint FrameIndex){
        return this.FrameConstantsBuffer->GetGPUVirtualAddress()+(FrameIndex*FrameConstantsStride);
    }

    private ulong GetLocalLightsAddress(uint FrameIndex)=>
        this.LocalLightsBuffer->GetGPUVirtualAddress()+(FrameIndex*(LocalLightStride*(uint)MaxLocalLights));

    private ulong GetSpotShadowProjectionAddress(uint FrameIndex)=>
        this.SpotShadowProjectionBuffer->GetGPUVirtualAddress()+(FrameIndex*(SpotShadowProjectionStride*(uint)MaxSpotShadowProjections));

    private ulong GetPointShadowProjectionAddress(uint FrameIndex)=>
        this.PointShadowProjectionBuffer->GetGPUVirtualAddress()+(FrameIndex*(PointShadowProjectionStride*(uint)MaxPointShadowProjections));

    private ulong GetAreaShadowMetadataAddress(uint FrameIndex)=>
        this.AreaShadowMetadataBuffer->GetGPUVirtualAddress()
        +(FrameIndex*(AreaShadowMetadataStride*(uint)MaxAreaAndEmissiveShadowProjections));

    private ulong GetAreaShadowProjectionAddress(uint FrameIndex)=>
        this.AreaShadowProjectionBuffer->GetGPUVirtualAddress()
        +(FrameIndex*(AreaShadowProjectionStride*(uint)MaxAreaAndEmissiveShadowProjections));

    private ulong GetForwardPlusTileListAddress(uint FrameIndex)=>
        this.ForwardPlusTileListBuffer->GetGPUVirtualAddress()+(FrameIndex*this.ForwardPlusTileFrameSize);

    private ulong GetObjectConstantsAddress(uint FrameIndex,int ObjectIndex){
        var Slot=((ulong)FrameIndex*(ulong)this.ObjectCount)+(ulong)ObjectIndex;
        return this.ObjectConstantsBuffer->GetGPUVirtualAddress()+(Slot*ConstantAlignment);
    }

    private ulong GetShadowConstantsAddress(uint FrameIndex,int Slot){
        if((uint)Slot>=(uint)ShadowAtlasProjectionConstantSlotCount){
            throw new ArgumentOutOfRangeException(nameof(Slot));
        }
        ulong LinearSlot=((ulong)FrameIndex*(uint)ShadowAtlasProjectionConstantSlotCount)+(uint)Slot;
        return this.ShadowConstantsBuffer->GetGPUVirtualAddress()+(LinearSlot*ConstantAlignment);
    }

    private D3D12_GPU_DESCRIPTOR_HANDLE GetMaterialDescriptorTable(int MaterialIndex){
        return new D3D12_GPU_DESCRIPTOR_HANDLE(this.SrvHeap->GetGPUDescriptorHandleForHeapStart(),MaterialIndex*5, this.SrvDescriptorSize);
    }

    private void UpdateViewportAndScissor(int Width,int Height){
        this.Viewport=new D3D12_VIEWPORT {
                                             TopLeftX=0,
                                             TopLeftY=0,
                                             Width=Width,
                                             Height=Height,
                                             MinDepth=0.0f,
                                             MaxDepth=1.0f,
                                         };

        this.ScissorRect=new RECT { left=0,top=0,right=Width,bottom=Height };
        this.Width=Width;
        this.Height=Height;
    }

    public void Resize(int Width,int Height){
        if(Width<=0||Height<=0){
            this.SizeIsZero=true;
            return;
        }

        this.SizeIsZero=false;

        if(this.Width==Width&&this.Height==Height){
            return;
        }

        this.WaitForGpuIdle();

        // See ModelRenderer.Resize / ADR-0003 §8: ResizeBuffers restarts the swap chain's
        // back-buffer index cycle, which would otherwise desynchronize the per-slot fence values.
        var SyncedFenceValue= this.FenceValues[this.FrameIndex];
        for(var I=0;I<FrameCount;I++){
            this.FenceValues[I]=SyncedFenceValue;
        }

        for(var I=0;I<FrameCount;I++){
            if(this.BackBuffers[I]!=null){
                _= this.BackBuffers[I]->Release();
                this.BackBuffers[I]=null;
            }
        }

        this.ReleaseReadbackBuffer();
        this.ReleaseForwardPlusTileBuffer();

        if(this.DepthBuffer!=null){
            _= this.DepthBuffer->Release();
            this.DepthBuffer=null;
        }

        if(this.HdrColorBuffer!=null){
            _= this.HdrColorBuffer->Release();
            this.HdrColorBuffer=null;
        }

        D3D12Diagnostics.EnsureSuccess(this.SwapChain->ResizeBuffers(FrameCount,(uint)Width,(uint)Height,BackBufferFormat,0),
            "IDXGISwapChain3::ResizeBuffers","resizing the swap chain");

        this.CreateRenderTargetViews();
        this.CreateDepthBuffer(Width,Height);
        this.CreateHdrColorBuffer(Width,Height);
        this.CreateForwardPlusTileBuffer(Width,Height);
        this.FrameIndex= this.SwapChain->GetCurrentBackBufferIndex();
        this.UpdateViewportAndScissor(Width,Height);
    }

    /// <summary>Renders the selected directional and Spot projection tiles in one Atlas transition pair.</summary>
    private void RenderShadowAtlasProjections(){
        if(this.DirectionalShadowIndex<0&&this.PointShadowAtlasProjections.Count==0
            &&this.SpotShadowAtlasProjections.Count==0
            &&this.AreaShadowAtlasProjections.Count==0){
            return;
        }
        D3D12_RESOURCE_BARRIER ToDepth=D3D12_RESOURCE_BARRIER.InitTransition(this.ShadowAtlas,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_DEPTH_WRITE);
        this.CommandList->ResourceBarrier(1,&ToDepth);
        D3D12_CPU_DESCRIPTOR_HANDLE ShadowDsv=new(this.DsvHeap->GetCPUDescriptorHandleForHeapStart(),1, this.DsvDescriptorSize);
        this.CommandList->OMSetRenderTargets(0,null,FALSE,&ShadowDsv);
        this.CommandList->SetGraphicsRootSignature(this.ShadowRootSignature);
        this.CommandList->SetPipelineState(this.ShadowPipelineState);
        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        if(this.DirectionalShadowIndex>=0){
            this.RenderShadowAtlasProjection(new ShadowAtlasProjection(DirectionalShadowAtlasTile,0),ShadowDsv);
        }
        foreach(ShadowAtlasProjection Projection in this.PointShadowAtlasProjections){
            this.RenderShadowAtlasProjection(Projection,ShadowDsv);
        }
        foreach(ShadowAtlasProjection Projection in this.SpotShadowAtlasProjections){
            this.RenderShadowAtlasProjection(Projection,ShadowDsv);
        }
        foreach(ShadowAtlasProjection Projection in this.AreaShadowAtlasProjections){
            this.RenderShadowAtlasProjection(Projection,ShadowDsv);
        }
        D3D12_RESOURCE_BARRIER ToShader=D3D12_RESOURCE_BARRIER.InitTransition(this.ShadowAtlas,D3D12_RESOURCE_STATE_DEPTH_WRITE,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        this.CommandList->ResourceBarrier(1,&ToShader);
    }

    private void RenderShadowAtlasProjection(
        in ShadowAtlasProjection Projection,D3D12_CPU_DESCRIPTOR_HANDLE ShadowDsv){
        ShadowAtlasTile Tile=Projection.Tile;
        RECT ShadowScissor=new(){ left=Tile.X,top=Tile.Y,right=Tile.X+Tile.Width,bottom=Tile.Y+Tile.Height };
        this.CommandList->ClearDepthStencilView(ShadowDsv,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,1.0f,0,1,&ShadowScissor);
        this.CommandList->SetGraphicsRootConstantBufferView(0,
            this.GetShadowConstantsAddress(this.FrameIndex,Projection.ShadowConstantsSlot));
        D3D12_VIEWPORT ShadowViewport=new(){
            TopLeftX=Tile.X,TopLeftY=Tile.Y,Width=Tile.Width,Height=Tile.Height,
            MinDepth=0f,MaxDepth=1f,
        };
        this.CommandList->RSSetViewports(1,&ShadowViewport);
        this.CommandList->RSSetScissorRects(1,&ShadowScissor);
        for(var I=0;I< this.ObjectCount;I++){
            var MeshIndex= this.ObjectMeshIndices[I];
            this.CommandList->SetGraphicsRootConstantBufferView(1, this.GetObjectConstantsAddress(this.FrameIndex,I));
            fixed(D3D12_VERTEX_BUFFER_VIEW*PVertexBufferView=&this.VertexBufferViews[MeshIndex])
            fixed(D3D12_INDEX_BUFFER_VIEW*PIndexBufferView=&this.IndexBufferViews[MeshIndex]){
                this.CommandList->IASetVertexBuffers(0,1,PVertexBufferView);
                this.CommandList->IASetIndexBuffer(PIndexBufferView);
                this.CommandList->DrawIndexedInstanced(this.IndexCounts[MeshIndex],1,0,0,0);
            }
        }
    }

    public void RenderFrame(bool CaptureForVerification=false){
        this.ThrowIfDisposed();
        this.TryReloadShaderArtifact();
        if(this.SizeIsZero){
            return;
        }

        ID3D12CommandAllocator*Allocator= this.CommandAllocators[this.FrameIndex];
        D3D12Diagnostics.EnsureSuccess(Allocator->Reset(),"ID3D12CommandAllocator::Reset","resetting the frame command allocator");
        D3D12Diagnostics.EnsureSuccess(this.CommandList->Reset(Allocator, this.PipelineState),"ID3D12GraphicsCommandList::Reset","resetting the graphics command list");
        this.PrepareDirectionalLights(this.FrameIndex);
        this.PrepareForwardPlusLocalLights(this.FrameIndex);

        this.PrepareLocalShadowAtlasProjections(this.FrameIndex);
        this.RenderShadowAtlasProjections();

        ID3D12Resource*BackBuffer= this.BackBuffers[this.FrameIndex];

        D3D12_RESOURCE_BARRIER ToHdrRenderTarget=D3D12_RESOURCE_BARRIER.InitTransition(this.HdrColorBuffer,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
        this.CommandList->ResourceBarrier(1,&ToHdrRenderTarget);

        D3D12_CPU_DESCRIPTOR_HANDLE HdrRtvHandle=new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),FrameCount, this.RtvDescriptorSize);
        D3D12_CPU_DESCRIPTOR_HANDLE DsvHandle= this.DsvHeap->GetCPUDescriptorHandleForHeapStart();
        this.CommandList->OMSetRenderTargets(1,&HdrRtvHandle,FALSE,&DsvHandle);

        Vector4 ClearColor=ClearColorVector;
        this.CommandList->ClearRenderTargetView(HdrRtvHandle,(float*)&ClearColor,0,null);
        this.CommandList->ClearDepthStencilView(DsvHandle,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,1.0f,0,0,null);

        this.CommandList->SetGraphicsRootSignature(this.RootSignature);
        this.CommandList->SetPipelineState(this.PipelineState);

        ID3D12DescriptorHeap*SrvHeap=this.SrvHeap;
        this.CommandList->SetDescriptorHeaps(1,&SrvHeap);
        this.CommandList->SetGraphicsRootConstantBufferView(0, this.GetFrameConstantsAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(3,this.GetLocalLightsAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(4,this.GetForwardPlusTileListAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(5,this.GetSpotShadowProjectionAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(6,this.GetPointShadowProjectionAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(7,this.GetAreaShadowMetadataAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(8,this.GetAreaShadowProjectionAddress(this.FrameIndex));

        fixed(D3D12_VIEWPORT*PViewport=&this.Viewport)
        fixed(RECT*PScissorRect=&this.ScissorRect){
            this.CommandList->RSSetViewports(1,PViewport);
            this.CommandList->RSSetScissorRects(1,PScissorRect);
        }

        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

        for(var I=0;I< this.ObjectCount;I++){
            var MeshIndex= this.ObjectMeshIndices[I];
            this.CommandList->SetGraphicsRootConstantBufferView(1, this.GetObjectConstantsAddress(this.FrameIndex,I));
            this.CommandList->SetGraphicsRootDescriptorTable(2, this.GetMaterialDescriptorTable(this.ObjectMaterialIndices[I]));

            fixed(D3D12_VERTEX_BUFFER_VIEW*PVertexBufferView=&this.VertexBufferViews[MeshIndex])
            fixed(D3D12_INDEX_BUFFER_VIEW*PIndexBufferView=&this.IndexBufferViews[MeshIndex]){
                this.CommandList->IASetVertexBuffers(0,1,PVertexBufferView);
                this.CommandList->IASetIndexBuffer(PIndexBufferView);
                this.CommandList->DrawIndexedInstanced(this.IndexCounts[MeshIndex],1,0,0,0);
            }
        }

        D3D12_RESOURCE_BARRIER ToHdrShaderResource=D3D12_RESOURCE_BARRIER.InitTransition(this.HdrColorBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        this.CommandList->ResourceBarrier(1,&ToHdrShaderResource);

        D3D12_RESOURCE_BARRIER ToBackBufferRenderTarget=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_PRESENT,D3D12_RESOURCE_STATE_RENDER_TARGET);
        this.CommandList->ResourceBarrier(1,&ToBackBufferRenderTarget);
        D3D12_CPU_DESCRIPTOR_HANDLE RtvHandle=new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),(int)this.FrameIndex, this.RtvDescriptorSize);
        this.CommandList->OMSetRenderTargets(1,&RtvHandle,FALSE,null);
        this.CommandList->SetGraphicsRootSignature(this.PostProcessRootSignature);
        this.CommandList->SetPipelineState(this.PostProcessPipelineState);
        ID3D12DescriptorHeap*PostSrvHeap= this.HdrSrvHeap;
        this.CommandList->SetDescriptorHeaps(1,&PostSrvHeap);
        this.CommandList->SetGraphicsRootDescriptorTable(0, this.HdrSrvHeap->GetGPUDescriptorHandleForHeapStart());
        fixed(D3D12_VIEWPORT*PViewport=&this.Viewport)
        fixed(RECT*PScissorRect=&this.ScissorRect){
            this.CommandList->RSSetViewports(1,PViewport);
            this.CommandList->RSSetScissorRects(1,PScissorRect);
        }

        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        this.CommandList->DrawInstanced(3,1,0,0);

        if(CaptureForVerification){
            this.EnsureReadbackBuffer();

            D3D12_RESOURCE_BARRIER ToCopySource=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_COPY_SOURCE);
            this.CommandList->ResourceBarrier(1,&ToCopySource);

            D3D12_TEXTURE_COPY_LOCATION Dst=new(){ pResource= this.ReadbackBuffer,Type=D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            Dst.PlacedFootprint= this.ReadbackFootprint;
            D3D12_TEXTURE_COPY_LOCATION Src=new(){ pResource=BackBuffer,Type=D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            Src.SubresourceIndex=0;
            this.CommandList->CopyTextureRegion(&Dst,0,0,0,&Src,null);

            D3D12_RESOURCE_BARRIER ToPresent=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_COPY_SOURCE,D3D12_RESOURCE_STATE_PRESENT);
            this.CommandList->ResourceBarrier(1,&ToPresent);
        } else {
            D3D12_RESOURCE_BARRIER ToPresent=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PRESENT);
            this.CommandList->ResourceBarrier(1,&ToPresent);
        }

        D3D12Diagnostics.EnsureSuccess(this.CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing the frame command list");

        ID3D12CommandList*CommandListBase=(ID3D12CommandList*)this.CommandList;
        this.Device!.CommandQueue->ExecuteCommandLists(1,&CommandListBase);

        D3D12Diagnostics.EnsureSuccess(this.SwapChain->Present(1,0),"IDXGISwapChain3::Present","presenting the frame");

        this.MoveToNextFrame();
        this.Device.PollDebugMessages();
        this.FramesRendered++;

        // All additive light counts (and the data they gate) have already been submitted to the
        // GPU above; reset so the next frame starts from zero and only reflects lights that call
        // their Add method again this frame (see the fields' doc comment).
        if(!this.DirectionalLightsPersistAcrossFrames){
            this.DirectionalLightCount=0;
            this.DirectionalShadowIndex=-1;
            this.SelectedDirectionalShadowAtlasCandidate=null;
        }
        this.DirectionalLightsExplicitThisFrame=false;
        this.DirectionalShadowCandidates.Clear();
        this.DirectionalLightSubmissions.Clear();
        this.LocalLightSubmissions.Clear();
        this.SelectedLocalLightSubmissions.Clear();
        this.PointShadowAtlasProjections.Clear();
        this.SpotShadowAtlasProjections.Clear();
        this.AreaShadowAtlasProjections.Clear();
        this.LocalLightCount=0;
        this.PointShadowProjectionCount=0;
        this.SpotShadowProjectionCount=0;
        this.AreaShadowProjectionCount=0;
        this.DroppedDirectionalLightsLastFrame=this.DroppedDirectionalLightsThisFrameValue;
        this.DroppedDirectionalLightsThisFrameValue=0;
        this.DroppedLocalLightsLastFrame=this.DroppedLocalLightsThisFrameValue;
        this.DroppedLocalLightsThisFrameValue=0;
        this.DroppedTileLightReferencesLastFrame=this.DroppedTileLightReferencesThisFrameValue;
        this.DroppedTileLightReferencesThisFrameValue=0;

        if(CaptureForVerification){
            this.WaitForGpuIdle();
            this.LastCapturedFrame= this.ReadBackCapturedFrame();
        }
    }

    private void EnsureReadbackBuffer(){
        if(this.ReadbackBuffer!=null){
            return;
        }

        D3D12_RESOURCE_DESC TextureDesc=D3D12_RESOURCE_DESC.Tex2D(BackBufferFormat,(uint)this.Width,(uint)this.Height,mipLevels:1);

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT Footprint;
        ulong TotalBytes;
        this.Device!.Device->GetCopyableFootprints(&TextureDesc,0,1,0,&Footprint,null,null,&TotalBytes);

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_READBACK);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(TotalBytes);

        ID3D12Resource*ReadbackBuffer;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_COPY_DEST,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&ReadbackBuffer),
            "ID3D12Device::CreateCommittedResource","creating the pixel-verification readback buffer");

        this.ReadbackBuffer=ReadbackBuffer;
        this.ReadbackBufferSize=TotalBytes;
        this.ReadbackFootprint=Footprint;
    }

    private void ReleaseReadbackBuffer(){
        if(this.ReadbackBuffer!=null){
            _= this.ReadbackBuffer->Release();
            this.ReadbackBuffer=null;
            this.ReadbackBufferSize=0;
        }
    }

    private PixelReadbackResult ReadBackCapturedFrame(){
        D3D12_RANGE ReadRange=new(){ Begin=0,End=(nuint)this.ReadbackBufferSize };
        void*Mapped;
        D3D12Diagnostics.EnsureSuccess(this.ReadbackBuffer->Map(0,&ReadRange,&Mapped),"ID3D12Resource::Map","mapping the readback buffer");

        var Data=new byte[this.ReadbackBufferSize];
        fixed(byte*PData=Data){
            Unsafe.CopyBlock(PData,Mapped,(uint)this.ReadbackBufferSize);
        }

        D3D12_RANGE WrittenRange=new();
        this.ReadbackBuffer->Unmap(0,&WrittenRange);

        return new PixelReadbackResult {
            Width= this.Width,
            Height= this.Height,
            RowPitch= this.ReadbackFootprint.Footprint.RowPitch,
            Format=BackBufferFormat,
            Pixels=Data,
        };
    }

    private void MoveToNextFrame(){
        var CurrentFenceValue= this.FenceValues[this.FrameIndex];
        D3D12Diagnostics.EnsureSuccess(this.Device!.CommandQueue->Signal(this.Fence,CurrentFenceValue),"ID3D12CommandQueue::Signal","signaling the frame fence");

        this.FrameIndex= this.SwapChain->GetCurrentBackBufferIndex();

        if(this.Fence->GetCompletedValue()< this.FenceValues[this.FrameIndex]){
            D3D12Diagnostics.EnsureSuccess(this.Fence->SetEventOnCompletion(this.FenceValues[this.FrameIndex], this.FenceEvent),"ID3D12Fence::SetEventOnCompletion","waiting for the next frame slot");
            _=WaitForSingleObjectEx(this.FenceEvent,INFINITE,FALSE);
        }

        this.FenceValues[this.FrameIndex]=CurrentFenceValue+1;
    }

    private void WaitForGpuIdle(){
        if(this.Device==null|| this.Fence==null){
            return;
        }

        D3D12Diagnostics.EnsureSuccess(this.Device.CommandQueue->Signal(this.Fence, this.FenceValues[this.FrameIndex]),"ID3D12CommandQueue::Signal","signaling the GPU-idle fence");
        D3D12Diagnostics.EnsureSuccess(this.Fence->SetEventOnCompletion(this.FenceValues[this.FrameIndex], this.FenceEvent),"ID3D12Fence::SetEventOnCompletion","waiting for the GPU to go idle");
        _=WaitForSingleObjectEx(this.FenceEvent,INFINITE,FALSE);
        this.FenceValues[this.FrameIndex]++;
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;
        this.ShaderArtifactMonitor?.Dispose();
        this.ShaderArtifactMonitor=null;

        this.WaitForGpuIdle();

        this.ReleaseReadbackBuffer();
        this.ReleaseForwardPlusTileBuffer();

        if(this.FrameConstantsBuffer!=null){
            this.FrameConstantsBuffer->Unmap(0,null);
            this.FrameConstantsMapped=null;
            _= this.FrameConstantsBuffer->Release();
            this.FrameConstantsBuffer=null;
        }

        if(this.LocalLightsBuffer!=null){
            this.LocalLightsBuffer->Unmap(0,null);
            this.LocalLightsMapped=null;
            _=this.LocalLightsBuffer->Release();
            this.LocalLightsBuffer=null;
        }

        if(this.SpotShadowProjectionBuffer!=null){
            this.SpotShadowProjectionBuffer->Unmap(0,null);
            this.SpotShadowProjectionMapped=null;
            _=this.SpotShadowProjectionBuffer->Release();
            this.SpotShadowProjectionBuffer=null;
        }

        if(this.PointShadowProjectionBuffer!=null){
            this.PointShadowProjectionBuffer->Unmap(0,null);
            this.PointShadowProjectionMapped=null;
            _=this.PointShadowProjectionBuffer->Release();
            this.PointShadowProjectionBuffer=null;
        }

        if(this.AreaShadowMetadataBuffer!=null){
            this.AreaShadowMetadataBuffer->Unmap(0,null);
            this.AreaShadowMetadataMapped=null;
            _=this.AreaShadowMetadataBuffer->Release();
            this.AreaShadowMetadataBuffer=null;
        }

        if(this.AreaShadowProjectionBuffer!=null){
            this.AreaShadowProjectionBuffer->Unmap(0,null);
            this.AreaShadowProjectionMapped=null;
            _=this.AreaShadowProjectionBuffer->Release();
            this.AreaShadowProjectionBuffer=null;
        }

        if(this.ObjectConstantsBuffer!=null){
            this.ObjectConstantsBuffer->Unmap(0,null);
            this.ObjectConstantsMapped=null;
            _= this.ObjectConstantsBuffer->Release();
            this.ObjectConstantsBuffer=null;
        }

        if(this.ShadowConstantsBuffer!=null){
            this.ShadowConstantsBuffer->Unmap(0,null);
            this.ShadowConstantsMapped=null;
            _= this.ShadowConstantsBuffer->Release();
            this.ShadowConstantsBuffer=null;
        }

        if(this.HdrColorBuffer!=null){
            _= this.HdrColorBuffer->Release();
            this.HdrColorBuffer=null;
        }

        if(this.HdrSrvHeap!=null){
            _= this.HdrSrvHeap->Release();
            this.HdrSrvHeap=null;
        }

        if(this.SrvHeap!=null){
            _= this.SrvHeap->Release();
            this.SrvHeap=null;
        }

        for(var I=0;I< this.MaterialTextures.Length;I++){
            if(this.MaterialTextures[I]!=null){
                _= this.MaterialTextures[I]->Release();
                this.MaterialTextures[I]=null;
            }
        }

        for(var I=0;I< this.IndexBuffers.Length;I++){
            if(this.IndexBuffers[I]!=null){
                _= this.IndexBuffers[I]->Release();
                this.IndexBuffers[I]=null;
            }
        }

        for(var I=0;I< this.VertexBuffers.Length;I++){
            if(this.VertexBuffers[I]!=null){
                _= this.VertexBuffers[I]->Release();
                this.VertexBuffers[I]=null;
            }
        }

        if(this.PipelineState!=null){
            _= this.PipelineState->Release();
            this.PipelineState=null;
        }

        if(this.ShadowPipelineState!=null){
            _= this.ShadowPipelineState->Release();
            this.ShadowPipelineState=null;
        }

        if(this.PostProcessPipelineState!=null){
            _= this.PostProcessPipelineState->Release();
            this.PostProcessPipelineState=null;
        }

        if(this.RootSignature!=null){
            _= this.RootSignature->Release();
            this.RootSignature=null;
        }

        if(this.ShadowRootSignature!=null){
            _= this.ShadowRootSignature->Release();
            this.ShadowRootSignature=null;
        }

        if(this.PostProcessRootSignature!=null){
            _= this.PostProcessRootSignature->Release();
            this.PostProcessRootSignature=null;
        }

        if(this.FenceEvent!=HANDLE.NULL){
            _=CloseHandle(this.FenceEvent);
            this.FenceEvent=HANDLE.NULL;
        }

        if(this.Fence!=null){
            _= this.Fence->Release();
            this.Fence=null;
        }

        if(this.CommandList!=null){
            _= this.CommandList->Release();
            this.CommandList=null;
        }

        for(var I=0;I<FrameCount;I++){
            if(this.CommandAllocators[I]!=null){
                _= this.CommandAllocators[I]->Release();
                this.CommandAllocators[I]=null;
            }
        }

        if(this.DepthBuffer!=null){
            _= this.DepthBuffer->Release();
            this.DepthBuffer=null;
        }

        if(this.ShadowAtlas!=null){
            _= this.ShadowAtlas->Release();
            this.ShadowAtlas=null;
        }

        if(this.DsvHeap!=null){
            _= this.DsvHeap->Release();
            this.DsvHeap=null;
        }

        for(var I=0;I<FrameCount;I++){
            if(this.BackBuffers[I]!=null){
                _= this.BackBuffers[I]->Release();
                this.BackBuffers[I]=null;
            }
        }

        if(this.RtvHeap!=null){
            _= this.RtvHeap->Release();
            this.RtvHeap=null;
        }

        if(this.SwapChain!=null){
            _= this.SwapChain->Release();
            this.SwapChain=null;
        }

        this.Device?.Dispose();
        this.Device=null;
    }
}
