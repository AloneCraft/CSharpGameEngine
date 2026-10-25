using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Rendering.Pipeline;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Rendering;
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
/// Renders textured, transformed meshes through the native logarithmic Stage pipeline, with
/// physical lighting, shadows, environment effects and presentation. One owner manages the
/// swap chain, frame resources, hot reload and resize for imported and procedural geometry.
/// </summary>
public sealed unsafe partial class StageRenderer:IDisposable {
    public const int FrameCount=2;
    private const int CameraPassCount=1+RenderCameraPreview.MaximumCount;
    public const DXGI_FORMAT BackBufferFormat=DXGI_FORMAT_R8G8B8A8_UNORM;
    public const DXGI_FORMAT DepthBufferFormat=DXGI_FORMAT_D32_FLOAT;
    private const DXGI_FORMAT HdrColorFormat=DXGI_FORMAT_R16G16B16A16_FLOAT;
    private const DXGI_FORMAT MaterialTextureFormat=DXGI_FORMAT_R8G8B8A8_UNORM;
    /// <summary>
    /// Material textures are typeless so one resident texture can be read through the view its slot
    /// requires: glTF base colour and emissive are sRGB-encoded, normal and metallic-roughness are linear.
    /// </summary>
    private const DXGI_FORMAT MaterialTextureResourceFormat=DXGI_FORMAT_R8G8B8A8_TYPELESS;
    private const DXGI_FORMAT MaterialColorTextureViewFormat=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    private const float GltfImportEmissiveDefaultNits=100f;
    /// <summary>The material sampler's anisotropy (M0110; Unreal's r.MaxAnisotropy default).</summary>
    internal const uint MaterialMaxAnisotropy=8;
    private const DXGI_FORMAT ShadowAtlasResourceFormat=DXGI_FORMAT_R32_TYPELESS;
    private const DXGI_FORMAT ShadowAtlasDsvFormat=DXGI_FORMAT_D32_FLOAT;
    private const DXGI_FORMAT ShadowAtlasSrvFormat=DXGI_FORMAT_R32_FLOAT;
    public const int ShadowAtlasSizePixels=PhysicalShadowAtlasPlan.AtlasSizePixels;
    public const int ShadowAtlasTileSizePixels=PhysicalShadowAtlasPlan.TileSizePixels;
    public const int DirectionalShadowAtlasTileIndex=PhysicalShadowAtlasPlan.DirectionalTileIndex;

    /// <summary>D3D12 constant buffer views must start at a 256-byte aligned GPU address.</summary>
    private const uint ConstantAlignment=512;

    /// <summary>Maximum additive directional lights accepted per frame. Must match Stage shader ABI.</summary>
    public const int MaxDirectionalLights=4;

    /// <summary>Maximum local lights accepted per frame across point and spot submissions.</summary>
    public const int MaxLocalLights=1024;
    public const int MaxOverlayVertices=32_768;
    /// <summary>Line vertices of the centre-camera marker drawn over one observer preview (M0099): a frustum of 8 lines.</summary>
    private const int ObserverMarkerVertexCount=16;
    /// <summary>Overlay vertices per frame slot: the caller's overlay, then one marker per camera preview.</summary>
    private const int OverlayFrameVertexCapacity=MaxOverlayVertices+ObserverMarkerVertexCount*RenderCameraPreview.MaximumCount;

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
    /// <summary>Cascade 0 keeps slot 0; cascades 1-3 follow every local-light projection slot.</summary>
    private const int CascadeShadowConstantSlotOffset=
        AreaShadowProjectionConstantSlotOffset+MaxAreaAndEmissiveShadowProjections;
    private const int ShadowAtlasProjectionConstantSlotCount=
        CascadeShadowConstantSlotOffset+DirectionalShadowCascades.Count-1;
    private const uint ForwardPlusTileRecordUIntCount=1u+(uint)MaxLightsPerForwardPlusTile;
    private const uint ForwardPlusTileRecordStride=sizeof(uint)*ForwardPlusTileRecordUIntCount;

    // FrameCB (b0) byte layout - see Stage shader ABI. Local lights and Forward+ tile lists live in
    // root-SRV StructuredBuffers (t5/t6); only their per-frame counts/grid remain here.
    private const uint FrameViewProjectionOffset=0;   // float4x4, 64 bytes
    private const uint FrameCameraPositionOffset=64;  // float4 (xyz used)
    private const uint FrameDirectionalLightsOffset=80; // MaxDirectionalLights * DirectionalLightStride bytes
    private const uint FrameAmbientColorOffset=FrameDirectionalLightsOffset+(MaxDirectionalLights*DirectionalLightStride);
    private const uint FrameFogParamsOffset=FrameAmbientColorOffset+16; // float4: x=density, y=heightFalloff, z=render origin altitude
    private const uint FrameFogColorOffset=FrameFogParamsOffset+16;     // float4 (xyz used)
    private const uint FrameExposureOffset=FrameFogColorOffset+16;      // float4: x=2^-EV100
    private const uint FrameLightCountsOffset=FrameExposureOffset+16;   // int4: x=local, y=shadow directional index, z=directional count, w=spot projections
    private const uint FrameForwardPlusGridOffset=FrameLightCountsOffset+16; // uint4: tile count X/Y, tile edge, tile cap
    private const uint FrameInverseViewProjectionOffset=FrameForwardPlusGridOffset+16;
    private const uint FrameViewportSizeOffset=FrameInverseViewProjectionOffset+64;
    private const uint FrameSkyLightParamsOffset=FrameViewportSizeOffset+16; // float4: x=sky light, y=physical fog, z=camera altitude m
    private const uint FramePhysicalFogParamsOffset=FrameSkyLightParamsOffset+16; // float4: x=ray origin terms, y=falloff per metre, z=minimum transmittance, w=sky contribution
    private const uint FramePhysicalFogColorOffset=FramePhysicalFogParamsOffset+16; // float4: rgb inscattering luminance
    private const uint FrameCascadeViewProjectionOffset=FramePhysicalFogColorOffset+16; // 4 x float4x4 directional cascades
    private const uint FrameCascadeSpheresOffset=FrameCascadeViewProjectionOffset+(64*DirectionalShadowCascades.Count); // float4: xyz centre, w radius
    private const uint FrameCascadeUvScaleOffsetsOffset=FrameCascadeSpheresOffset+(16*DirectionalShadowCascades.Count); // float4 atlas quadrant per cascade
    private const uint FrameCascadeDepthRangesOffset=FrameCascadeUvScaleOffsetsOffset+(16*DirectionalShadowCascades.Count); // float4: projection depth range metres
    private const uint FrameGiGridOriginOffset=FrameCascadeDepthRangesOffset+16; // float4: xyz render-space position of probe (0,0,0)
    private const uint FrameGiGridSpacingOffset=FrameGiGridOriginOffset+16; // float4: xyz metres between probes, w intensity
    private const uint FrameGiGridCountsOffset=FrameGiGridSpacingOffset+16; // uint4: xyz probe counts, w probes per atlas row (0 = GI off)
    private const uint FrameGiAtlasSizeOffset=FrameGiGridCountsOffset+16; // float4: irradiance atlas texels, distance atlas texels
    private const uint FrameAmbientOcclusionOffset=FrameGiAtlasSizeOffset+16; // float4: x enabled, y intensity, zw = 1 / viewport size
    private const uint FrameDepthEncoding0Offset=FrameAmbientOcclusionOffset+16; // float4: x encoding (RenderDepthEncoding), y radial transition m, z 1/G(rMax), w far discard depth m
    private const uint FrameDepthEncoding1Offset=FrameDepthEncoding0Offset+16; // float4: xyz observer centre (render space)
    private const uint FrameLumenParamsOffset=FrameDepthEncoding1Offset+16; // float4: x Lumen diffuse (t16), y Lumen reflections (t17) (M0111)
    private const uint FrameConstantsSize=FrameLumenParamsOffset+16;

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
    private const uint ObjectMaterialParamsOffset=80;  // float4: x=roughness, y=metallic, z=presentation mode
    private const uint ObjectEmissiveFactorOffset=96;  // float4: xyz=emissive HDR, w=selection weight
    private const uint ObjectWorldGridParamsOffset=112; // float4: x=grid size (m, 0 = no grid), y=sub grid divisions, z=wire half width
    private const uint ObjectWorldGridColorOffset=128;  // float4: xyz=main grid line colour
    private const uint ObjectWorldSubGridColorOffset=144; // float4: xyz=sub grid line colour
    private const uint ObjectWorldGridAnchorOffset=160; // float4: xyz=render origin modulo the grid size (zero in world coordinates)
    private const uint ObjectConservativeRasterDepthOffset=176; // float4: xy=(a, b) of the object's conservative radial raster depth a - b/w (M0100)

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

    private struct OverlayVertexGpu {
        public Vector3 Position;
        public Vector4 Color;
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
    private ID3D12Resource*SceneDistanceBuffer;
    private ID3D12DescriptorHeap*HdrSrvHeap;
    private ID3D12Resource*ShadowAtlas;
    private readonly ID3D12CommandAllocator*[]CommandAllocators=new ID3D12CommandAllocator*[FrameCount];
    private ID3D12GraphicsCommandList*CommandList;
    private ID3D12Fence*Fence;
    private readonly ulong[]FenceValues=new ulong[FrameCount];
    private HANDLE FenceEvent;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private ID3D12PipelineState*PresentationPipelineState;
    private ID3D12PipelineState*WireframePipelineState;
    private ID3D12PipelineState*CameraPreviewPipelineState;
    private ID3D12PipelineState*CameraPreviewWireframePipelineState;
    private ID3D12PipelineState*OverlayTrianglePipelineState;
    private ID3D12PipelineState*OverlayLinePipelineState;
    private ID3D12PipelineState*ObserverPipelineState;
    private ID3D12PipelineState*ObserverWireframePipelineState;
    private ID3D12RootSignature*ShadowRootSignature;
    private ID3D12PipelineState*ShadowPipelineState;
    private ID3D12RootSignature*PostProcessRootSignature;
    private StageAtmosphere?Atmosphere;
    private StageGlobalIllumination?GlobalIllumination;
    private StageAmbientOcclusion?AmbientOcclusion;
    private StageAutoExposure?AutoExposureStage;
    private StageSkinning?Skinning;
    private StageGlobalDistanceField?GlobalDistanceField;
    private bool AutoExposureInitialized;
    private readonly Stopwatch AutoExposureClock=Stopwatch.StartNew();
    private TimeSpan LastAutoExposureTime;
    private bool GlobalIlluminationDescriptorsLive;
    private int AmbientOcclusionDescriptorGeneration;
    private EnvironmentLightingSettings EnvironmentSettings=EnvironmentLightingSettings.Legacy;
    private AtmosphereFrame CurrentAtmosphereFrame;
    private float CameraAltitudeMeters;
    private float RenderOriginAltitudeMeters;
    private float CurrentCameraEv100;
    private Vector3 CurrentAmbientColor=DefaultAmbientColor;
    private FogParams CurrentFog=FogParams.None;
    private Matrix4x4 CurrentViewProjection=Matrix4x4.Identity;
    private Vector3 AtmosphereSunDirection=Vector3.UnitY;
    private Vector3 CurrentCameraPosition;
    private RenderViewDepth CurrentViewDepth=RenderViewDepth.ForOrthographic(Matrix4x4.Identity);
    /// <summary>The camera's rotation with the radial projection's finite auxiliary planes (radial mode only).</summary>
    private Matrix4x4 CurrentAuxiliaryViewProjection=Matrix4x4.Identity;
    /// <summary>The radial main pass's view-projection: the camera's, with the conservative depth row (radial mode only).</summary>
    /// <summary>The camera basis and field of view of the last <see cref="SetCamera"/>, for observer markers.</summary>
    private bool HasCameraBasis;
    /// <summary>Test-only GPU timestamps around the main pass's draws (performance evidence); null unless enabled.</summary>
    private ID3D12QueryHeap*MainPassTimestampHeap;
    private ID3D12Resource*MainPassTimestampReadback;
    private ulong MainPassTimestampFrequency;
    private uint LastTimedFrameIndex=uint.MaxValue;
    private Vector3 CurrentCameraForward;
    private Vector3 CurrentCameraUp;
    private Vector3 CurrentCameraRight;
    private float CurrentCameraTanHalfFovY;
    private float CurrentCameraAspectRatio;
    private Vector3 AtmosphereSunIlluminance;
    private ID3D12PipelineState*PostProcessPipelineState;
    private byte[]ShadowVertexShaderBytecode=[];
    private byte[]FullscreenVertexShaderBytecode=[];
    private byte[]PostProcessPixelShaderBytecode=[];
    private byte[]PreviewPixelShaderBytecode=[];
    private byte[]PreviewCleanPixelShaderBytecode=[];
    private byte[]OverlayVertexShaderBytecode=[];
    private byte[]OverlayPixelShaderBytecode=[];
    private byte[]ObserverVertexShaderBytecode=[];

    // Per-mesh (indexed by meshIndex).
    private int MeshCount;
    private ID3D12Resource*[]VertexBuffers=[];
    private D3D12_VERTEX_BUFFER_VIEW[]VertexBufferViews=[];
    private ID3D12Resource*[]IndexBuffers=[];
    private D3D12_INDEX_BUFFER_VIEW[]IndexBufferViews=[];
    private uint[]IndexCounts=[];

    // Per-material (indexed by materialIndex). Four material textures plus one shared shadow SRV.
    private int MaterialCount;
    /// <summary>The materials the descriptor heap has room for: the created ones plus <see cref="DynamicMaterialHeadroom"/> that <see cref="AddMaterial"/> can fill later (M0105).</summary>
    private int MaterialCapacity;
    private ID3D12Resource*[]MaterialTextures=[];
    private readonly List<nint>OwnedMaterialTextures=[];
    /// <summary>Material textures by content and colour space (M0110: an sRGB and a linear use of the same texels average their mips differently).</summary>
    private readonly Dictionary<(Hash256 Hash,RenderTextureColorSpace ColorSpace),nint>ResidentTextures=[];
    private ID3D12DescriptorHeap*SrvHeap;
    private uint SrvDescriptorSize;
    private Vector4[]MaterialBaseColorFactors=[];
    private float[]MaterialRoughnessFactors=[];
    private float[]MaterialMetallicFactors=[];
    private Vector3[]MaterialEmissiveNits=[];
    private StageWorldGrid?[]MaterialWorldGrids=[];
    private Vector3[]MaterialWorldGridAnchors=[];

    // Per-object (indexed by objectIndex). ObjectCount is the number of slots handed out so far (a removed object leaves its slot
    // free for the next AddObject); ObjectCapacity is how many the constant buffer has room for (M0105).
    private int ObjectCount;
    private int ObjectCapacity;
    private bool[]ObjectActive=[];
    /// <summary>Objects the camera does not see (UE's OwnerNoSee): the main pass, the GI probes and the camera previews skip them; shadows still use them (M0107).</summary>
    private bool[]ObjectHiddenFromView=[];
    /// <summary>Objects that cast no shadow (UE's CastShadow off): the shadow passes skip them (M0107).</summary>
    private bool[]ObjectNoShadow=[];
    /// <summary>Each object's factor on its material's emission (ADR-0078/M0109: a material scalar a game changes at run time, as brake lights). 1 by default.</summary>
    private float[]ObjectEmissiveScales=[];
    private readonly List<int>FreeObjectSlots=[];
    private int[]ObjectMeshIndices=[];
    /// <summary>Each mesh's bounds centre, where an object's conservative radial raster depth touches (M0100).</summary>
    private Vector3[]MeshBoundsCentres=[];
    private Vector3[]MeshBoundsMinimums=[];
    private Vector3[]MeshBoundsMaximums=[];
    private int[]ObjectMaterialIndices=[];
    private Matrix4x4[]ObjectTransforms=[];
    private float[]ObjectSelectionWeights=[];
    private RenderPresentationMode PresentationMode;
    private int SelectedObjectCount;
    private readonly RenderCameraPreview[]RequestedCameraPreviews=
        new RenderCameraPreview[RenderCameraPreview.MaximumCount];
    private readonly RenderCameraPreview[]EffectiveCameraPreviews=
        new RenderCameraPreview[RenderCameraPreview.MaximumCount];
    private readonly RenderCameraPreviewDiagnostics[]CameraPreviewDiagnostics=
        new RenderCameraPreviewDiagnostics[RenderCameraPreview.MaximumCount];
    private int RequestedCameraPreviewCount;
    private int CameraPreviewCount;
    private bool CameraPreviewDiagnosticsDirty;

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
    private ID3D12Resource*OverlayVertexBuffer;
    private byte*OverlayVertexMapped;
    private int OverlayTriangleVertexCount;
    private int OverlayLineVertexCount;
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
    // owning entities do not linger. Nothing is lit before a light is submitted: starting at one
    // (the default light written by InitializeConstantsToDefaults) lit only the first frame, and
    // the end-of-frame reset then dropped it, so an unlit scene changed after one present.
    private int DirectionalLightCount;
    private int DirectionalShadowIndex=-1;
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
    private ShaderArtifactPackage?LastKnownGoodShaderPackage;

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
    public int ResidentMaterialTextureCount=>this.OwnedMaterialTextures.Count;
    /// <summary>Objects <see cref="AddObject"/> can add beyond the ones the renderer was created with (M0105).</summary>
    public const int DynamicObjectHeadroom=4096;
    /// <summary>Materials <see cref="AddMaterial"/> can add beyond the ones the renderer was created with (M0105).</summary>
    public const int DynamicMaterialHeadroom=256;
    /// <summary>How many more materials <see cref="AddMaterial"/> accepts.</summary>
    public int FreeMaterialSlots=>this.MaterialCapacity-this.MaterialCount;
    /// <summary>How many objects are drawn now.</summary>
    public int ActiveObjectCount=>this.ObjectCount-this.FreeObjectSlots.Count;
    /// <summary>Meshes with a distance field (ADR-0078/M0110).</summary>
    public int DistanceFieldMeshCount=>this.GlobalDistanceField?.MeshesWithFields??0;
    /// <summary>Objects in the global distance field at the last frame (0 without one).</summary>
    public int DistanceFieldObjectCount=>this.GlobalDistanceField?.LastFieldObjects??0;
    internal ulong CompletedFence=>this.Fence==null?0:this.Fence->GetCompletedValue();

    public PixelReadbackResult?LastCapturedFrame { get;private set;}

    internal ShaderArtifactPackage LastKnownGoodShaderArtifact=>
        this.LastKnownGoodShaderPackage
        ??throw new InvalidOperationException("The renderer has no validated shader artifact.");

    internal AtmosphereReadback ReadAtmosphereForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.Atmosphere!.ReadbackForTesting();
    }

    /// <summary>Test-only read of the cloud panorama (M0110); waits for the GPU.</summary>
    internal (Vector4[]Texels,int Width,int Height) ReadCloudPanoramaForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.Atmosphere!.ReadCloudPanoramaForTesting();
    }
    /// <summary>Test-only read of the GPU auto exposure state (EV100, scale, settled, valid); waits for the GPU.</summary>
    internal Vector4 ReadAutoExposureStateForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.AutoExposureStage!.ReadStateForTesting();
    }

    /// <summary>Fixed adaptation step for deterministic tests; null measures wall-clock time between frames.</summary>
    internal float?AutoExposureDeltaSecondsForTesting { get;set; }

    internal (float[]Values,int Width,int Height) ReadAmbientOcclusionForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return (this.AmbientOcclusion!.ReadbackForTesting(),this.AmbientOcclusion.Width,this.AmbientOcclusion.Height);
    }

    internal GlobalIlluminationReadback ReadGlobalIlluminationForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.GlobalIllumination!.ReadbackForTesting();
    }

    internal void RemoveDeviceForTesting(){
        this.ThrowIfDisposed();
        this.Device!.RemoveDeviceForTesting();
    }

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

    /// <summary>
    /// Recreates one native device generation from the shader package already validated and
    /// successfully applied by the prior generation. The path is retained only for subsequent
    /// Debug hot reload; recovery does not reread it.
    /// </summary>
    internal static StageRenderer CreateFromLastKnownGood(
        nint Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,string ShaderArtifactPath,
        ShaderArtifactPackage ShaderPackage,
        IReadOnlyList<StageMeshCreateInfo>Meshes,
        IReadOnlyList<StageMaterialCreateInfo>Materials,
        IReadOnlyList<StageObjectCreateInfo>Objects){
        ArgumentNullException.ThrowIfNull(DeviceOptions);
        ArgumentNullException.ThrowIfNull(ShaderArtifactPath);
        ArgumentNullException.ThrowIfNull(ShaderPackage);
        ArgumentNullException.ThrowIfNull(Meshes);
        ArgumentNullException.ThrowIfNull(Materials);
        ArgumentNullException.ThrowIfNull(Objects);
        if(Width<=0||Height<=0){
            throw new ArgumentOutOfRangeException(nameof(Width),
                "Recovery width/height must both be positive.");
        }

        StageRenderer Renderer=new(){ Width=Width,Height=Height };
        var Completed=false;
        try {
            Renderer.Initialize(
                (HWND)Hwnd,Width,Height,DeviceOptions,ShaderArtifactPath,
                Meshes,Materials,Objects,ShaderPackage);
            Completed=true;
            return Renderer;
        } finally {
            if(!Completed){ Renderer.Dispose(); }
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
        IReadOnlyList<StageMeshCreateInfo>Meshes,IReadOnlyList<StageMaterialCreateInfo>Materials,
        IReadOnlyList<StageObjectCreateInfo>Objects,
        ShaderArtifactPackage?ValidatedShaderPackage=null){
        this.Device=GraphicsDevice.Create(DeviceOptions);

        this.CreateSwapChain(Hwnd,Width,Height);
        this.CreateRtvHeap();
        this.CreateRenderTargetViews();
        this.CreateDsvHeap();
        this.CreateDepthBuffer(Width,Height);
        this.CreateHdrColorBuffer(Width,Height);
        this.CreateSceneDistanceBuffer(Width,Height);
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
        this.Atmosphere=new StageAtmosphere(this.Device,this.ShaderArtifactPath,FrameCount);
        this.GlobalIllumination=new StageGlobalIllumination(this.Device,this.ShaderArtifactPath,FrameCount,FrameConstantsStride);
        this.AmbientOcclusion=new StageAmbientOcclusion(this.Device,this.ShaderArtifactPath,FrameCount);
        this.AutoExposureStage=new StageAutoExposure(this.Device,this.ShaderArtifactPath,FrameCount);
        this.Atmosphere.ExposureStateAddress=this.AutoExposureStage.StateAddress;
        this.Skinning=new StageSkinning(this.Device,this.ShaderArtifactPath,FrameCount);
        this.GlobalDistanceField=new StageGlobalDistanceField(this.Device,this.ShaderArtifactPath,FrameCount);
        this.Lumen=new StageLumen(this.Device,this.ShaderArtifactPath,FrameCount,FrameConstantsStride,ConstantAlignment);
        this.Scalability=RenderScalability.DefaultFor(this.Device.IsWarp);
        if(ValidatedShaderPackage is null){
            this.LoadShaderArtifact(this.ShaderArtifactPath);
        } else {
            this.ApplyShaderArtifact(ValidatedShaderPackage);
            this.CommitShaderArtifact(ValidatedShaderPackage);
        }
        this.CreateRootSignature();
        this.CreateShadowRootSignature();
        this.CreatePostProcessRootSignature();
        this.CreateScreenRootSignature();
        this.CreatePipelineState();
        this.CreateShadowPipelineState();
        this.CreatePostProcessPipelineState();
        this.CreateScreenPipelineState();
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
            NumDescriptors=FrameCount+2,
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
        // Typeless for depth readback; scene effects consume the separate physical-distance target.
        D3D12_RESOURCE_DESC ResourceDesc=D3D12_RESOURCE_DESC.Tex2D(DXGI_FORMAT_R32_TYPELESS,(uint)Width,(uint)Height,mipLevels:1);
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

    private void CreateSceneDistanceBuffer(int Width,int Height){
        var Desc=D3D12_RESOURCE_DESC.Tex2D(DXGI_FORMAT_R32_FLOAT,(uint)Width,(uint)Height,mipLevels:1);
        Desc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
        D3D12_CLEAR_VALUE Clear=new(){ Format=DXGI_FORMAT_R32_FLOAT };
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_RENDER_TARGET,&Clear,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "CreateCommittedResource","creating the physical scene distance target");
        this.SceneDistanceBuffer=Resource;
        D3D12_CPU_DESCRIPTOR_HANDLE Rtv=new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),FrameCount+1,this.RtvDescriptorSize);
        this.Device.Device->CreateRenderTargetView(Resource,null,Rtv);
    }

    private void CreateHdrColorBuffer(int Width,int Height){
        D3D12_RESOURCE_DESC ResourceDesc=D3D12_RESOURCE_DESC.Tex2D(HdrColorFormat,(uint)Width,(uint)Height,mipLevels:1);
        ResourceDesc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET|D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
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
        this.CommitShaderArtifact(Package);
    }
    private void ApplyShaderArtifact(ShaderArtifactPackage Package){
        this.VertexShaderBytecode=Package.GetBytecode("VSMain");
        this.PixelShaderBytecode=Package.GetBytecode("PSMain");
        this.PreviewPixelShaderBytecode=Package.GetBytecode("PreviewPSMain");
        this.PreviewCleanPixelShaderBytecode=Package.GetBytecode("PreviewCleanPSMain");
        this.OverlayVertexShaderBytecode=Package.GetBytecode("OverlayVSMain");
        this.OverlayPixelShaderBytecode=Package.GetBytecode("OverlayPSMain");
        this.FullscreenVertexShaderBytecode=Package.GetBytecode("FullscreenVS");
        this.PostProcessPixelShaderBytecode=Package.GetBytecode("PostProcessPS");
        this.ApplyScreenShaders(Package);
        this.ShadowVertexShaderBytecode=Package.GetBytecode("VSShadowMain");
        this.ObserverVertexShaderBytecode=Package.GetBytecode("ObserverVSMain");
        this.LumenSurfacePixelShaderBytecode=Package.GetBytecode("SurfacePrepassPSMain");
        this.LumenCardPixelShaderBytecode=Package.GetBytecode("LumenCardPSMain");
    }
    private void CommitShaderArtifact(ShaderArtifactPackage Package){
        this.LastKnownGoodShaderPackage=Package;
        this.ShaderArtifactLoaded=true;
        this.ShaderSemanticHash=Package.Document.SemanticHash;
        this.ShaderArtifactDiagnostics=[];
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
        var OldPreviewPixelShader=this.PreviewPixelShaderBytecode;
        var OldPreviewCleanPixelShader=this.PreviewCleanPixelShaderBytecode;
        var OldOverlayVertexShader=this.OverlayVertexShaderBytecode;
        var OldOverlayPixelShader=this.OverlayPixelShaderBytecode;
        var OldFullscreenVertexShader=this.FullscreenVertexShaderBytecode;
        var OldPostProcessPixelShader=this.PostProcessPixelShaderBytecode;
        var OldShadowVertexShader=this.ShadowVertexShaderBytecode;
        var OldObserverVertexShader=this.ObserverVertexShaderBytecode;
        var OldObserverPipelineState=this.ObserverPipelineState;
        var OldObserverWireframePipelineState=this.ObserverWireframePipelineState;
        var OldPipelineState=this.PipelineState;
        var OldPresentationPipelineState=this.PresentationPipelineState;
        var OldWireframePipelineState=this.WireframePipelineState;
        var OldCameraPreviewPipelineState=this.CameraPreviewPipelineState;
        var OldCameraPreviewWireframePipelineState=
            this.CameraPreviewWireframePipelineState;
        var OldOverlayTrianglePipelineState=this.OverlayTrianglePipelineState;
        var OldOverlayLinePipelineState=this.OverlayLinePipelineState;
        var OldShadowPipelineState=this.ShadowPipelineState;
        var OldPostProcessPipelineState=this.PostProcessPipelineState;
        var OldScreenVertexShader=this.ScreenVertexShaderBytecode;
        var OldScreenPixelShader=this.ScreenPixelShaderBytecode;
        var OldScreenPipelineState=this.ScreenPipelineState;
        var OldLumenSurfacePixelShader=this.LumenSurfacePixelShaderBytecode;
        var OldLumenCardPixelShader=this.LumenCardPixelShaderBytecode;
        var OldLumenSurfacePipelineState=this.LumenSurfacePipelineState;
        var OldLumenCardPipelineState=this.LumenCardPipelineState;
        try{
            this.ApplyShaderArtifact(Package);
            this.CreatePipelineState();
            this.CreateShadowPipelineState();
            this.CreatePostProcessPipelineState();
            this.CreateScreenPipelineState();
            this.WaitForGpuIdle();
            if(OldObserverPipelineState!=null){ _=OldObserverPipelineState->Release(); }
            if(OldObserverWireframePipelineState!=null){ _=OldObserverWireframePipelineState->Release(); }
            if(OldPipelineState!=null){ _=OldPipelineState->Release(); }
            if(OldPresentationPipelineState!=null){ _=OldPresentationPipelineState->Release(); }
            if(OldWireframePipelineState!=null){ _=OldWireframePipelineState->Release(); }
            if(OldCameraPreviewPipelineState!=null){ _=OldCameraPreviewPipelineState->Release(); }
            if(OldCameraPreviewWireframePipelineState!=null){ _=OldCameraPreviewWireframePipelineState->Release(); }
            if(OldOverlayTrianglePipelineState!=null){ _=OldOverlayTrianglePipelineState->Release(); }
            if(OldOverlayLinePipelineState!=null){ _=OldOverlayLinePipelineState->Release(); }
            if(OldShadowPipelineState!=null){ _=OldShadowPipelineState->Release(); }
            if(OldPostProcessPipelineState!=null){ _=OldPostProcessPipelineState->Release(); }
            if(OldScreenPipelineState!=null){ _=OldScreenPipelineState->Release(); }
            if(OldLumenSurfacePipelineState!=null){ _=OldLumenSurfacePipelineState->Release(); }
            if(OldLumenCardPipelineState!=null){ _=OldLumenCardPipelineState->Release(); }
            this.CommitShaderArtifact(Package);
        }catch(Exception Exception){
            this.VertexShaderBytecode=OldVertexShader;
            this.PixelShaderBytecode=OldPixelShader;
            this.PreviewPixelShaderBytecode=OldPreviewPixelShader;
            this.PreviewCleanPixelShaderBytecode=OldPreviewCleanPixelShader;
            this.OverlayVertexShaderBytecode=OldOverlayVertexShader;
            this.OverlayPixelShaderBytecode=OldOverlayPixelShader;
            this.FullscreenVertexShaderBytecode=OldFullscreenVertexShader;
            this.PostProcessPixelShaderBytecode=OldPostProcessPixelShader;
            this.ShadowVertexShaderBytecode=OldShadowVertexShader;
            this.ObserverVertexShaderBytecode=OldObserverVertexShader;
            this.ScreenVertexShaderBytecode=OldScreenVertexShader;
            this.ScreenPixelShaderBytecode=OldScreenPixelShader;
            this.LumenSurfacePixelShaderBytecode=OldLumenSurfacePixelShader;
            this.LumenCardPixelShaderBytecode=OldLumenCardPixelShader;
            if(this.LumenSurfacePipelineState!=OldLumenSurfacePipelineState){ _=this.LumenSurfacePipelineState->Release();this.LumenSurfacePipelineState=OldLumenSurfacePipelineState; }
            if(this.LumenCardPipelineState!=OldLumenCardPipelineState){ _=this.LumenCardPipelineState->Release();this.LumenCardPipelineState=OldLumenCardPipelineState; }
            if(this.ScreenPipelineState!=OldScreenPipelineState){ _=this.ScreenPipelineState->Release();this.ScreenPipelineState=OldScreenPipelineState; }
            if(this.ObserverPipelineState!=OldObserverPipelineState){ _=this.ObserverPipelineState->Release();this.ObserverPipelineState=OldObserverPipelineState; }
            if(this.ObserverWireframePipelineState!=OldObserverWireframePipelineState){ _=this.ObserverWireframePipelineState->Release();this.ObserverWireframePipelineState=OldObserverWireframePipelineState; }
            if(this.PipelineState!=OldPipelineState){ _=this.PipelineState->Release();this.PipelineState=OldPipelineState; }
            if(this.PresentationPipelineState!=OldPresentationPipelineState){ _=this.PresentationPipelineState->Release();this.PresentationPipelineState=OldPresentationPipelineState; }
            if(this.WireframePipelineState!=OldWireframePipelineState){ _=this.WireframePipelineState->Release();this.WireframePipelineState=OldWireframePipelineState; }
            if(this.CameraPreviewPipelineState!=OldCameraPreviewPipelineState){ _=this.CameraPreviewPipelineState->Release();this.CameraPreviewPipelineState=OldCameraPreviewPipelineState; }
            if(this.CameraPreviewWireframePipelineState!=OldCameraPreviewWireframePipelineState){ _=this.CameraPreviewWireframePipelineState->Release();this.CameraPreviewWireframePipelineState=OldCameraPreviewWireframePipelineState; }
            if(this.OverlayTrianglePipelineState!=OldOverlayTrianglePipelineState){ _=this.OverlayTrianglePipelineState->Release();this.OverlayTrianglePipelineState=OldOverlayTrianglePipelineState; }
            if(this.OverlayLinePipelineState!=OldOverlayLinePipelineState){ _=this.OverlayLinePipelineState->Release();this.OverlayLinePipelineState=OldOverlayLinePipelineState; }
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

        // M0110: material textures have mips; anisotropic filtering (Unreal's default r.MaxAnisotropy 8) keeps grazing surfaces sharp.
        D3D12_STATIC_SAMPLER_DESC StaticSampler=new(){
            Filter=D3D12_FILTER_ANISOTROPIC,
            MaxAnisotropy=MaterialMaxAnisotropy,
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

        D3D12_ROOT_PARAMETER SkyIrradianceParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };
        SkyIrradianceParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=11,RegisterSpace=0 };

        D3D12_DESCRIPTOR_RANGE GlobalIlluminationRange=new(){
            RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,
            NumDescriptors=EnvironmentDescriptorCount,
            BaseShaderRegister=12,
        };

        D3D12_ROOT_PARAMETER GlobalIlluminationParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };
        GlobalIlluminationParam.DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&GlobalIlluminationRange };

        D3D12_ROOT_PARAMETER*RootParams=stackalloc D3D12_ROOT_PARAMETER[11];
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
        RootParams[9]=SkyIrradianceParam;
        RootParams[10]=GlobalIlluminationParam;
        D3D12_STATIC_SAMPLER_DESC*StaticSamplers=stackalloc D3D12_STATIC_SAMPLER_DESC[2];
        StaticSamplers[0]=StaticSampler;
        StaticSamplers[1]=ShadowSampler;

        D3D12_ROOT_SIGNATURE_DESC RootSignatureDesc=new(){
            NumParameters=11,
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
        this.PostProcessRootSignature=this.CreateSrvAndConstantsRootSignature(D3D12_ROOT_SIGNATURE_FLAG_NONE,nameof(this.CreatePostProcessRootSignature));
    }

    /// <summary>
    /// The root signature of the passes that read one texture and four constants: 0 = pixel SRV table t0, 1 = four 32-bit constants b0, and a
    /// linear clamp sampler s0. The post-process pass uses it as is; the screen layer adds the input-assembler flag.
    /// </summary>
    private ID3D12RootSignature*CreateSrvAndConstantsRootSignature(D3D12_ROOT_SIGNATURE_FLAGS Flags,string Context){
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

        D3D12_ROOT_PARAMETER PostProcessParams=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };
        PostProcessParams.Constants=new D3D12_ROOT_CONSTANTS { ShaderRegister=0,RegisterSpace=0,Num32BitValues=4 };

        D3D12_ROOT_PARAMETER*RootParams=stackalloc D3D12_ROOT_PARAMETER[2];
        RootParams[0]=SceneParam;
        RootParams[1]=PostProcessParams;
        D3D12_ROOT_SIGNATURE_DESC RootSignatureDesc=new(){
            NumParameters=2,
            pParameters=RootParams,
            NumStaticSamplers=1,
            pStaticSamplers=&Sampler,
            Flags=Flags,
        };

        ID3DBlob*SignatureBlob=null;
        ID3DBlob*ErrorBlob=null;
        try {
            D3D12Diagnostics.EnsureSuccess(
                D3D12SerializeRootSignature(&RootSignatureDesc,D3D_ROOT_SIGNATURE_VERSION_1,&SignatureBlob,&ErrorBlob),
                Context,Context);
            ID3D12RootSignature*Signature;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateRootSignature(0,SignatureBlob->GetBufferPointer(),SignatureBlob->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Signature),
                Context,Context);
            return Signature;
        } finally {
            if(SignatureBlob!=null){ _=SignatureBlob->Release(); }
            if(ErrorBlob!=null){ _=ErrorBlob->Release(); }
        }
    }

    private void CreatePipelineState(){
        ID3D12PipelineState*Shipping=null;
        ID3D12PipelineState*Presentation=null;
        ID3D12PipelineState*Wireframe=null;
        ID3D12PipelineState*Observer=null;
        ID3D12PipelineState*ObserverWireframe=null;
        ID3D12PipelineState*CameraPreview=null;
        ID3D12PipelineState*CameraPreviewWireframe=null;
        ID3D12PipelineState*OverlayTriangles=null;
        ID3D12PipelineState*OverlayLines=null;
        ID3D12PipelineState*LumenSurface=null;
        ID3D12PipelineState*LumenCard=null;
        try {
            LumenSurface=this.CreateMeshPipelineState(this.LumenSurfacePixelShaderBytecode,D3D12_FILL_MODE.D3D12_FILL_MODE_SOLID,
                RenderTargets:[StageLumen.SurfaceNormalFormat,StageLumen.SurfaceDistanceFormat]);
            LumenCard=this.CreateMeshPipelineState(this.LumenCardPixelShaderBytecode,D3D12_FILL_MODE.D3D12_FILL_MODE_SOLID,
                RenderTargets:[StageLumen.CardAlbedoFormat,StageLumen.CardNormalFormat,StageLumen.CardEmissiveFormat]);
            Shipping=this.CreateMeshPipelineState(
                this.PixelShaderBytecode,D3D12_FILL_MODE.D3D12_FILL_MODE_SOLID);
            Presentation=this.CreateMeshPipelineState(
                this.PreviewPixelShaderBytecode,D3D12_FILL_MODE.D3D12_FILL_MODE_SOLID);
            Wireframe=this.CreateMeshPipelineState(
                this.PreviewPixelShaderBytecode,D3D12_FILL_MODE.D3D12_FILL_MODE_WIREFRAME);
            CameraPreview=this.CreateMeshPipelineState(
                this.PreviewCleanPixelShaderBytecode,D3D12_FILL_MODE.D3D12_FILL_MODE_SOLID,WriteSceneDistance:false);
            CameraPreviewWireframe=this.CreateMeshPipelineState(
                this.PreviewCleanPixelShaderBytecode,D3D12_FILL_MODE.D3D12_FILL_MODE_WIREFRAME,WriteSceneDistance:false);
            OverlayTriangles=this.CreateOverlayPipelineState(
                D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE);
            OverlayLines=this.CreateOverlayPipelineState(
                D3D12_PRIMITIVE_TOPOLOGY_TYPE_LINE);
            Observer=this.CreateMeshPipelineState(this.PreviewCleanPixelShaderBytecode,D3D12_FILL_MODE.D3D12_FILL_MODE_SOLID,this.ObserverVertexShaderBytecode,WriteSceneDistance:false);
            ObserverWireframe=this.CreateMeshPipelineState(this.PreviewCleanPixelShaderBytecode,D3D12_FILL_MODE.D3D12_FILL_MODE_WIREFRAME,this.ObserverVertexShaderBytecode,WriteSceneDistance:false);
            this.ObserverPipelineState=Observer;
            this.ObserverWireframePipelineState=ObserverWireframe;
            this.PipelineState=Shipping;
            this.PresentationPipelineState=Presentation;
            this.WireframePipelineState=Wireframe;
            this.CameraPreviewPipelineState=CameraPreview;
            this.CameraPreviewWireframePipelineState=CameraPreviewWireframe;
            this.OverlayTrianglePipelineState=OverlayTriangles;
            this.OverlayLinePipelineState=OverlayLines;
            this.LumenSurfacePipelineState=LumenSurface;
            this.LumenCardPipelineState=LumenCard;
        } catch {
            if(Observer!=null){ _=Observer->Release(); }
            if(ObserverWireframe!=null){ _=ObserverWireframe->Release(); }
            if(LumenSurface!=null){ _=LumenSurface->Release(); }
            if(LumenCard!=null){ _=LumenCard->Release(); }
            if(Shipping!=null){ _=Shipping->Release(); }
            if(Presentation!=null){ _=Presentation->Release(); }
            if(Wireframe!=null){ _=Wireframe->Release(); }
            if(CameraPreview!=null){ _=CameraPreview->Release(); }
            if(CameraPreviewWireframe!=null){ _=CameraPreviewWireframe->Release(); }
            if(OverlayTriangles!=null){ _=OverlayTriangles->Release(); }
            if(OverlayLines!=null){ _=OverlayLines->Release(); }
            throw;
        }
    }

    private ID3D12PipelineState*CreateMeshPipelineState(
        byte[]PixelShaderBytecode,D3D12_FILL_MODE FillMode,byte[]?VertexShaderBytecode=null,bool DepthClip=false,DXGI_FORMAT[]?RenderTargets=null,bool WriteSceneDistance=true){
        var VertexShader=VertexShaderBytecode??this.VertexShaderBytecode;
        RenderTargets??=[HdrColorFormat,DXGI_FORMAT_R32_FLOAT];
        fixed(byte*PPosition="POSITION\0"u8)
        fixed(byte*PNormal="NORMAL\0"u8)
        fixed(byte*PTangent="TANGENT\0"u8)
        fixed(byte*PTexCoord="TEXCOORD\0"u8)
        fixed(byte*PVertexShader=VertexShader)
        fixed(byte*PPixelShader=PixelShaderBytecode){
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
                VS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PVertexShader,BytecodeLength=(nuint)VertexShader.Length },
                PS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PPixelShader,BytecodeLength=(nuint)PixelShaderBytecode.Length },
                RasterizerState=D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState=D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState=D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask=uint.MaxValue,
                PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                NumRenderTargets=(uint)(RenderTargets?.Length??1),
                DSVFormat=DepthBufferFormat,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            if(!WriteSceneDistance){
                PsoDesc.BlendState.IndependentBlendEnable=TRUE;
                PsoDesc.BlendState.RenderTarget[1].RenderTargetWriteMask=0;
            }
            // Real backface culling is deferred, the imported-mesh winding policy (ADR-0004).
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            PsoDesc.RasterizerState.FillMode=FillMode;
            PsoDesc.RasterizerState.DepthClipEnable=DepthClip?TRUE:FALSE;
            PsoDesc.RTVFormats[0]=HdrColorFormat;
            // M0111: the Lumen surface prepass and card captures write several targets.
            for(var Target=0;Target<(RenderTargets?.Length??0);Target++){
                PsoDesc.RTVFormats[Target]=RenderTargets![Target];
            }

            ID3D12PipelineState*PipelineState;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateGraphicsPipelineState(&PsoDesc,__uuidof<ID3D12PipelineState>(),(void**)&PipelineState),
                "ID3D12Device::CreateGraphicsPipelineState","creating the stage pipeline state");
            return PipelineState;
        }
    }

    private ID3D12PipelineState*CreateOverlayPipelineState(
        D3D12_PRIMITIVE_TOPOLOGY_TYPE TopologyType){
        byte*PPositionSemantic=stackalloc byte[]{
            (byte)'P',(byte)'O',(byte)'S',(byte)'I',
            (byte)'T',(byte)'I',(byte)'O',(byte)'N',0,
        };
        byte*PColorSemantic=stackalloc byte[]{
            (byte)'C',(byte)'O',(byte)'L',(byte)'O',(byte)'R',0,
        };
        fixed(byte*PPosition="POSITION\\0"u8)
        fixed(byte*PColor="COLOR\\0"u8)
        fixed(byte*PVertexShader=this.OverlayVertexShaderBytecode)
        fixed(byte*PPixelShader=this.OverlayPixelShaderBytecode){
            D3D12_INPUT_ELEMENT_DESC*InputElements=
                stackalloc D3D12_INPUT_ELEMENT_DESC[2]{
                    new D3D12_INPUT_ELEMENT_DESC {
                        SemanticName=(sbyte*)PPositionSemantic,
                        Format=DXGI_FORMAT_R32G32B32_FLOAT,
                        InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                    },
                    new D3D12_INPUT_ELEMENT_DESC {
                        SemanticName=(sbyte*)PColorSemantic,
                        Format=DXGI_FORMAT_R32G32B32A32_FLOAT,
                        AlignedByteOffset=12,
                        InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                    },
                };
            D3D12_GRAPHICS_PIPELINE_STATE_DESC PsoDesc=new(){
                InputLayout=new D3D12_INPUT_LAYOUT_DESC {
                    pInputElementDescs=InputElements,
                    NumElements=2,
                },
                pRootSignature=this.RootSignature,
                VS=new D3D12_SHADER_BYTECODE {
                    pShaderBytecode=PVertexShader,
                    BytecodeLength=(nuint)this.OverlayVertexShaderBytecode.Length,
                },
                PS=new D3D12_SHADER_BYTECODE {
                    pShaderBytecode=PPixelShader,
                    BytecodeLength=(nuint)this.OverlayPixelShaderBytecode.Length,
                },
                RasterizerState=D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState=D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState=D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask=uint.MaxValue,
                PrimitiveTopologyType=TopologyType,
                NumRenderTargets=1,
                DSVFormat=DepthBufferFormat,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            PsoDesc.DepthStencilState.DepthEnable=FALSE;
            PsoDesc.DepthStencilState.StencilEnable=FALSE;
            PsoDesc.BlendState.RenderTarget[0]=
                new D3D12_RENDER_TARGET_BLEND_DESC {
                    BlendEnable=TRUE,
                    LogicOpEnable=FALSE,
                    SrcBlend=D3D12_BLEND.D3D12_BLEND_SRC_ALPHA,
                    DestBlend=D3D12_BLEND.D3D12_BLEND_INV_SRC_ALPHA,
                    BlendOp=D3D12_BLEND_OP.D3D12_BLEND_OP_ADD,
                    SrcBlendAlpha=D3D12_BLEND.D3D12_BLEND_ONE,
                    DestBlendAlpha=D3D12_BLEND.D3D12_BLEND_INV_SRC_ALPHA,
                    BlendOpAlpha=D3D12_BLEND_OP.D3D12_BLEND_OP_ADD,
                    LogicOp=D3D12_LOGIC_OP.D3D12_LOGIC_OP_NOOP,
                    RenderTargetWriteMask=(byte)
                        D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_ALL,
                };
            PsoDesc.RTVFormats[0]=BackBufferFormat;
            ID3D12PipelineState*PipelineState;
            HRESULT Result=this.Device!.Device->CreateGraphicsPipelineState(
                &PsoDesc,__uuidof<ID3D12PipelineState>(),
                (void**)&PipelineState);
            if(Result.FAILED){ this.Device.PollDebugMessages(); }
            string DebugContext=Result.FAILED
                ?string.Join(Environment.NewLine,this.Device.CollectedMessages)
                :string.Empty;
            D3D12Diagnostics.EnsureSuccess(
                Result,"ID3D12Device::CreateGraphicsPipelineState",
                string.IsNullOrEmpty(DebugContext)
                    ?"creating the Stage overlay pipeline state"
                    :"creating the Stage overlay pipeline state"
                        +Environment.NewLine+DebugContext);
            return PipelineState;
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

    /// <summary>
    /// Adds one mesh after creation (M0105): a game that spawns an Entity whose asset no earlier Entity used. The buffers are
    /// separate resources, so nothing the GPU draws with is touched. Returns the new mesh index. A skinned mesh (M0107: a character spawned
    /// after the level loaded) also joins the skinning pass; that grows the palette ring a frame in flight may still read, so it waits for the GPU once.
    /// </summary>
    public int AddMesh(StageMeshCreateInfo Mesh){
        this.ThrowIfDisposed();
        if(Mesh.Positions.Count==0||Mesh.Positions.Count!=Mesh.Normals.Count
            ||Mesh.Positions.Count!=Mesh.Tangents.Count||Mesh.Positions.Count!=Mesh.Uvs.Count
            ||Mesh.Indices.Count==0){
            throw new ArgumentException("A mesh must have at least one vertex with matching Positions/Normals/Tangents/Uvs counts, and at least one index.",nameof(Mesh));
        }
        if(Mesh.Skin is not null){
            this.WaitForGpuIdle();
        }
        var Index=this.MeshCount;
        this.GrowMeshArrays(Index+1);
        this.MeshBoundsCentres[Index]=BoundsCentre(Mesh.Positions);
        (this.MeshBoundsMinimums[Index],this.MeshBoundsMaximums[Index])=Bounds(Mesh.Positions);
        this.CreateVertexBuffer(Index,Mesh.Positions,Mesh.Normals,Mesh.Tangents,Mesh.Uvs);
        this.CreateIndexBuffer(Index,Mesh.Indices);
        if(Mesh.Skin is{ }Skin){
            // Every pass draws the skinned copy; the upload-heap buffer stays the rest pose it is skinned from.
            this.VertexBufferViews[Index]=this.Skinning!.AddMesh(Index,this.VertexBuffers[Index],(uint)Mesh.Positions.Count,(uint)sizeof(Vertex),Skin.JointCount,Skin.JointIndices,Skin.Weights);
        }
        // M0110: a skinned mesh moves its vertices every frame, so its rest-pose field would be wrong; Unreal leaves skinned meshes out too.
        this.GlobalDistanceField!.SetMesh(Index,Mesh.Skin is null?Mesh.DistanceField:null);
        this.MeshCount=Index+1;
        return Index;
    }

    private void GrowMeshArrays(int Required){
        if(this.VertexBuffers.Length>=Required){ return; }
        var Length=Math.Max(Required,Math.Max(16,this.VertexBuffers.Length*2));
        var VertexBuffers=new ID3D12Resource*[Length];
        var IndexBuffers=new ID3D12Resource*[Length];
        var VertexBufferViews=new D3D12_VERTEX_BUFFER_VIEW[Length];
        var IndexBufferViews=new D3D12_INDEX_BUFFER_VIEW[Length];
        var IndexCounts=new uint[Length];
        var MeshBoundsCentres=new Vector3[Length];
        var MeshBoundsMinimums=new Vector3[Length];
        var MeshBoundsMaximums=new Vector3[Length];
        for(var I=0;I<this.MeshCount;I++){
            VertexBuffers[I]=this.VertexBuffers[I];
            IndexBuffers[I]=this.IndexBuffers[I];
            VertexBufferViews[I]=this.VertexBufferViews[I];
            IndexBufferViews[I]=this.IndexBufferViews[I];
            IndexCounts[I]=this.IndexCounts[I];
            MeshBoundsCentres[I]=this.MeshBoundsCentres[I];
            MeshBoundsMinimums[I]=this.MeshBoundsMinimums[I];
            MeshBoundsMaximums[I]=this.MeshBoundsMaximums[I];
        }
        this.VertexBuffers=VertexBuffers;
        this.IndexBuffers=IndexBuffers;
        this.VertexBufferViews=VertexBufferViews;
        this.IndexBufferViews=IndexBufferViews;
        this.IndexCounts=IndexCounts;
        this.MeshBoundsCentres=MeshBoundsCentres;
        this.MeshBoundsMinimums=MeshBoundsMinimums;
        this.MeshBoundsMaximums=MeshBoundsMaximums;
    }

    private void CreateMeshBuffers(IReadOnlyList<StageMeshCreateInfo>Meshes){
        this.MeshCount=Meshes.Count;
        this.VertexBuffers=new ID3D12Resource*[this.MeshCount];
        this.VertexBufferViews=new D3D12_VERTEX_BUFFER_VIEW[this.MeshCount];
        this.IndexBuffers=new ID3D12Resource*[this.MeshCount];
        this.IndexBufferViews=new D3D12_INDEX_BUFFER_VIEW[this.MeshCount];
        this.IndexCounts=new uint[this.MeshCount];

        this.MeshBoundsCentres=new Vector3[this.MeshCount];
        this.MeshBoundsMinimums=new Vector3[this.MeshCount];
        this.MeshBoundsMaximums=new Vector3[this.MeshCount];
        for(var I=0;I< this.MeshCount;I++){
            StageMeshCreateInfo Mesh=Meshes[I];
            this.MeshBoundsCentres[I]=BoundsCentre(Mesh.Positions);
            (this.MeshBoundsMinimums[I],this.MeshBoundsMaximums[I])=Bounds(Mesh.Positions);
            this.CreateVertexBuffer(I,Mesh.Positions,Mesh.Normals,Mesh.Tangents,Mesh.Uvs);
            this.CreateIndexBuffer(I,Mesh.Indices);
            if(Mesh.Skin is{ }Skin){
                // Every pass draws the skinned copy; the upload-heap buffer stays the rest pose it is skinned from.
                this.VertexBufferViews[I]=this.Skinning!.AddMesh(I,this.VertexBuffers[I],(uint)Mesh.Positions.Count,(uint)sizeof(Vertex),Skin.JointCount,Skin.JointIndices,Skin.Weights);
            }
            this.GlobalDistanceField!.SetMesh(I,Mesh.Skin is null?Mesh.DistanceField:null);
        }
    }

    /// <summary>A mesh's axis-aligned bounds (M0111: the box of its surface cache cards).</summary>
    private static (Vector3 Minimum,Vector3 Maximum)Bounds(IReadOnlyList<Vector3>Positions){
        if(Positions.Count==0){ return (Vector3.Zero,Vector3.Zero); }
        var Minimum=Positions[0];
        var Maximum=Positions[0];
        for(var I=1;I<Positions.Count;I++){
            Minimum=Vector3.Min(Minimum,Positions[I]);
            Maximum=Vector3.Max(Maximum,Positions[I]);
        }
        return (Minimum,Maximum);
    }

    /// <summary>The centre of a mesh's axis-aligned bounds (its rest pose for a skinned mesh).</summary>
    private static Vector3 BoundsCentre(IReadOnlyList<Vector3>Positions){
        if(Positions.Count==0){ return Vector3.Zero; }
        var Minimum=Positions[0];
        var Maximum=Positions[0];
        for(var I=1;I<Positions.Count;I++){
            Minimum=Vector3.Min(Minimum,Positions[I]);
            Maximum=Vector3.Max(Maximum,Positions[I]);
        }
        return (Minimum+Maximum)*0.5f;
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
        this.MaterialCapacity=this.MaterialCount+DynamicMaterialHeadroom;
        this.MaterialTextures=new ID3D12Resource*[this.MaterialCapacity*4];
        this.MaterialBaseColorFactors=new Vector4[this.MaterialCapacity];
        this.MaterialRoughnessFactors=new float[this.MaterialCapacity];
        this.MaterialMetallicFactors=new float[this.MaterialCapacity];
        this.MaterialEmissiveNits=new Vector3[this.MaterialCapacity];
        this.MaterialWorldGrids=new StageWorldGrid?[this.MaterialCapacity];
        this.MaterialWorldGridAnchors=new Vector3[this.MaterialCapacity];

        D3D12_DESCRIPTOR_HEAP_DESC SrvHeapDesc=new(){
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
            NumDescriptors=(uint)(this.MaterialCapacity*5+EnvironmentDescriptorCount),
            Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
        };
        ID3D12DescriptorHeap*SrvHeap;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateDescriptorHeap(&SrvHeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&SrvHeap),
            "ID3D12Device::CreateDescriptorHeap","creating the material SRV descriptor heap");
        this.SrvHeap=SrvHeap;
        this.SrvDescriptorSize= this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

        try {
            for(var I=0;I< this.MaterialCount;I++){
                this.SetUpMaterial(I,Materials[I]);
            }
        } catch {
            this.ReleaseOwnedMaterialTextures();
            Array.Clear(this.MaterialTextures);
            throw;
        }
        this.WriteEnvironmentDescriptors();
    }

    /// <summary>
    /// Adds one material after creation (M0105). Its textures are uploaded now, which waits for the GPU once, so the first spawn of an
    /// asset the game has not drawn yet costs a frame. Returns the new material index, or -1 when the
    /// <see cref="DynamicMaterialHeadroom"/> is used up.
    /// </summary>
    public int AddMaterial(StageMaterialCreateInfo Material){
        this.ThrowIfDisposed();
        if(this.MaterialCount>=this.MaterialCapacity){ return -1; }
        // The texture upload resets the first command allocator, which a frame still in flight may be using.
        this.WaitForGpuIdle();
        var Index=this.MaterialCount;
        var OwnedBefore=this.OwnedMaterialTextures.Count;
        try {
            this.SetUpMaterial(Index,Material);
        } catch {
            for(var I=this.OwnedMaterialTextures.Count-1;I>=OwnedBefore;I--){
                var Pointer=this.OwnedMaterialTextures[I];
                this.ResidentTextures.Remove(this.OwnedTextureHashes[Pointer]);
                this.OwnedTextureHashes.Remove(Pointer);
                _=((ID3D12Resource*)Pointer)->Release();
                this.OwnedMaterialTextures.RemoveAt(I);
            }
            throw;
        }
        this.MaterialCount=Index+1;
        return Index;
    }

    private readonly Dictionary<nint,(Hash256 Hash,RenderTextureColorSpace ColorSpace)>OwnedTextureHashes=[];

    /// <summary>Fills one material's per-material state, uploads its textures (sharing the resident ones) and writes its five descriptors.</summary>
    private void SetUpMaterial(int I,StageMaterialCreateInfo Material){
        this.MaterialBaseColorFactors[I]=Material.BaseColorFactor;
        this.MaterialRoughnessFactors[I]=Material.RoughnessFactor;
        this.MaterialMetallicFactors[I]=Material.MetallicFactor;
        this.MaterialWorldGrids[I]=Material.WorldGrid;
        this.MaterialWorldGridAnchors[I]=Vector3.Zero;
        this.MaterialEmissiveNits[I]=Material.EmissiveNits!=Vector3.Zero
            ?Material.EmissiveNits
            :Material.EmissiveFactor*GltfImportEmissiveDefaultNits;

        // Slots 0 (base colour) and 3 (emissive) hold sRGB colour; 1 (normal) and 2 (metallic-roughness) hold linear data. The
        // colour space also decides how the mips are averaged (M0110).
        this.MaterialTextures[(I*4)+0]=GetOrCreateTexture(
            Material.BaseColorTexture??RenderMaterialDefaults.White,RenderTextureColorSpace.Srgb);
        this.MaterialTextures[(I*4)+1]=GetOrCreateTexture(
            Material.NormalTexture??RenderMaterialDefaults.FlatNormal,RenderTextureColorSpace.Linear);
        this.MaterialTextures[(I*4)+2]=GetOrCreateTexture(
            Material.MetallicRoughnessTexture??RenderMaterialDefaults.White,RenderTextureColorSpace.Linear);
        this.MaterialTextures[(I*4)+3]=GetOrCreateTexture(
            Material.EmissiveTexture??RenderMaterialDefaults.White,RenderTextureColorSpace.Srgb);

        D3D12_CPU_DESCRIPTOR_HANDLE SrvHeapStart=this.SrvHeap->GetCPUDescriptorHandleForHeapStart();
        D3D12_SHADER_RESOURCE_VIEW_DESC SrvDesc=new(){
            Format=MaterialTextureFormat,
            ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        // Every mip of the texture (M0110); -1 is D3D12's "all levels from the most detailed one".
        SrvDesc.Texture2D.MipLevels=uint.MaxValue;
        for(var Slot=0;Slot<4;Slot++){
            D3D12_CPU_DESCRIPTOR_HANDLE Handle=new(SrvHeapStart,(I*5)+Slot, this.SrvDescriptorSize);
            // Slots 0 (base colour) and 3 (emissive) hold colour data; 1 (normal) and 2
            // (metallic-roughness) hold linear data.
            SrvDesc.Format=Slot is 0 or 3?MaterialColorTextureViewFormat:MaterialTextureFormat;
            this.Device!.Device->CreateShaderResourceView(this.MaterialTextures[(I*4)+Slot],&SrvDesc,Handle);
        }
        D3D12_SHADER_RESOURCE_VIEW_DESC ShadowSrvDesc=new(){
            Format=ShadowAtlasSrvFormat,
            ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        ShadowSrvDesc.Texture2D.MipLevels=1;
        D3D12_CPU_DESCRIPTOR_HANDLE ShadowHandle=new(SrvHeapStart,(I*5)+4, this.SrvDescriptorSize);
        this.Device!.Device->CreateShaderResourceView(this.ShadowAtlas,&ShadowSrvDesc,ShadowHandle);

        ID3D12Resource*GetOrCreateTexture(RenderTextureResource Texture,RenderTextureColorSpace ColorSpace){
            var Key=(Texture.ContentHash,ColorSpace);
            if(this.ResidentTextures.TryGetValue(Key,out nint Existing)){
                return(ID3D12Resource*)Existing;
            }
            var Chain=RenderTextureMipChain.Build(Texture,ColorSpace);
            var LevelCount=MaterialMipLevelLimitForTesting>0?Math.Min(MaterialMipLevelLimitForTesting,Chain.Levels.Count):Chain.Levels.Count;
            ID3D12Resource*Created=this.CreateTextureResource(Chain.Levels,LevelCount);
            this.ResidentTextures.Add(Key,(nint)Created);
            this.OwnedMaterialTextures.Add((nint)Created);
            this.OwnedTextureHashes[(nint)Created]=Key;
            return Created;
        }
    }

    /// <summary>
    /// Test-only cap on the mip levels uploaded for material textures (M0110), so one test can render the same texture without mips (1) and with
    /// them (0 = every level) and measure the difference. Production code never sets it; GPU tests run one at a time.
    /// </summary>
    internal static int MaterialMipLevelLimitForTesting { get;set; }
    private void ReleaseOwnedMaterialTextures(){
        foreach(var Pointer in this.OwnedMaterialTextures){
            _=((ID3D12Resource*)Pointer)->Release();
        }
        this.OwnedMaterialTextures.Clear();
        this.OwnedTextureHashes.Clear();
        this.ResidentTextures.Clear();
    }

    /// <summary>Uploads one already-validated canonical RGBA8 texture with a single level (the screen layer's). Decode/fallback policy lives above L0.</summary>
    private ID3D12Resource*CreateTextureResource(RenderTextureResource Texture){
        return this.CreateTextureResource(RenderTextureMipChain.TopLevelOnly(Texture).Levels,1);
    }
    /// <summary>Uploads the first <paramref name="LevelCount"/> levels of a texture (M0110: a material texture's whole mip chain) in one copy.</summary>
    private ID3D12Resource*CreateTextureResource(IReadOnlyList<RenderTextureMipLevel>Levels,int LevelCount){
        var Base=Levels[0];
        D3D12_RESOURCE_DESC TextureDesc=D3D12_RESOURCE_DESC.Tex2D(
            MaterialTextureResourceFormat,(uint)Base.Width,(uint)Base.Height,mipLevels:(ushort)LevelCount);

        D3D12_HEAP_PROPERTIES DefaultHeapProps=new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource*GpuTexture;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&DefaultHeapProps,D3D12_HEAP_FLAG_NONE,&TextureDesc,D3D12_RESOURCE_STATE_COPY_DEST,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&GpuTexture),
            "ID3D12Device::CreateCommittedResource","creating a material texture");

        var Footprints=new D3D12_PLACED_SUBRESOURCE_FOOTPRINT[LevelCount];
        ulong TotalBytes;
        fixed(D3D12_PLACED_SUBRESOURCE_FOOTPRINT*PFootprints=Footprints){
            this.Device.Device->GetCopyableFootprints(&TextureDesc,0,(uint)LevelCount,0,PFootprints,null,null,&TotalBytes);
        }

        D3D12_HEAP_PROPERTIES UploadHeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC StagingDesc=D3D12_RESOURCE_DESC.Buffer(TotalBytes);

        ID3D12Resource*Staging=null;
        try {
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&UploadHeapProps,D3D12_HEAP_FLAG_NONE,&StagingDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&Staging),
                "ID3D12Device::CreateCommittedResource","creating a material texture upload staging buffer");
            // GetCopyableFootprints' RowPitch is 256-byte aligned; the decoded image is tightly
            // packed (stride = Width*4). These differ whenever Width*4 isn't already a multiple of
            // 256, so this must be a per-row copy, not one bulk CopyBlock to respect the texture row pitch.
            D3D12_RANGE NoRead=new();
            byte*Mapped;
            D3D12Diagnostics.EnsureSuccess(Staging->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping a material texture upload staging buffer");
            for(var Level=0;Level<LevelCount;Level++){
                var Source=Levels[Level];
                var SrcRowPitch=(uint)Source.Width*4;
                var DstRowPitch=Footprints[Level].Footprint.RowPitch;
                var Destination=Mapped+Footprints[Level].Offset;
                fixed(byte*PSrc=Source.Pixels){
                    for(uint Y=0;Y<(uint)Source.Height;Y++){
                        Unsafe.CopyBlock(Destination+(Y*DstRowPitch),PSrc+(Y*SrcRowPitch),SrcRowPitch);
                    }
                }
            }
            Staging->Unmap(0,null);

            D3D12Diagnostics.EnsureSuccess(this.CommandAllocators[0]->Reset(),"ID3D12CommandAllocator::Reset","resetting the allocator for a material texture upload");
            D3D12Diagnostics.EnsureSuccess(this.CommandList->Reset(this.CommandAllocators[0],null),"ID3D12GraphicsCommandList::Reset","resetting the command list for a material texture upload");

            for(var Level=0;Level<LevelCount;Level++){
                D3D12_TEXTURE_COPY_LOCATION Dst=new(){ pResource=GpuTexture,Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
                Dst.SubresourceIndex=(uint)Level;
                D3D12_TEXTURE_COPY_LOCATION Src=new(){ pResource=Staging,Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
                Src.PlacedFootprint=Footprints[Level];
                this.CommandList->CopyTextureRegion(&Dst,0,0,0,&Src,null);
            }

            D3D12_RESOURCE_BARRIER ToShaderResource=D3D12_RESOURCE_BARRIER.InitTransition(GpuTexture,D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
            this.CommandList->ResourceBarrier(1,&ToShaderResource);

            D3D12Diagnostics.EnsureSuccess(this.CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing a material texture upload command list");

            ID3D12CommandList*CommandListBase=(ID3D12CommandList*)this.CommandList;
            this.Device.CommandQueue->ExecuteCommandLists(1,&CommandListBase);

            this.WaitForGpuIdle();
        } catch {
            _=GpuTexture->Release();
            throw;
        } finally {
            if(Staging!=null){ _=Staging->Release(); }
        }

        return GpuTexture;
    }

    private void CreateObjectMappings(IReadOnlyList<StageObjectCreateInfo>Objects){
        this.ObjectCount=Objects.Count;
        this.ObjectCapacity=this.ObjectCount+DynamicObjectHeadroom;
        this.ObjectMeshIndices=new int[this.ObjectCapacity];
        this.ObjectMaterialIndices=new int[this.ObjectCapacity];
        this.ObjectTransforms=new Matrix4x4[this.ObjectCapacity];
        this.ObjectSelectionWeights=new float[this.ObjectCapacity];
        this.ObjectActive=new bool[this.ObjectCapacity];
        this.ObjectHiddenFromView=new bool[this.ObjectCapacity];
        this.ObjectNoShadow=new bool[this.ObjectCapacity];
        this.ObjectEmissiveScales=new float[this.ObjectCapacity];
        Array.Fill(this.ObjectEmissiveScales,1f);

        for(var I=0;I< this.ObjectCount;I++){
            this.ObjectMeshIndices[I]=Objects[I].MeshIndex;
            this.ObjectMaterialIndices[I]=Objects[I].MaterialIndex;
            this.ObjectTransforms[I]=Matrix4x4.Identity;
            this.ObjectActive[I]=!Objects[I].Removed;
            if(Objects[I].Removed){ this.FreeObjectSlots.Add(I); }
        }
    }

    /// <summary>
    /// Adds one drawn object after creation (M0105): an Entity spawned while the game runs. Reuses the slot of a removed object when
    /// there is one. Returns the object index, or -1 when the <see cref="DynamicObjectHeadroom"/> is used up. The object starts at
    /// the origin; set its transform every frame like any other. Only the current frame's constants are written, so a frame still in
    /// flight keeps drawing what it was given.
    /// </summary>
    public int AddObject(int MeshIndex,int MaterialIndex){
        this.ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(MeshIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(MeshIndex,this.MeshCount);
        ArgumentOutOfRangeException.ThrowIfNegative(MaterialIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(MaterialIndex,this.MaterialCount);
        int Index;
        if(this.FreeObjectSlots.Count>0){
            Index=this.FreeObjectSlots[^1];
            this.FreeObjectSlots.RemoveAt(this.FreeObjectSlots.Count-1);
        } else if(this.ObjectCount<this.ObjectCapacity){
            Index=this.ObjectCount++;
        } else {
            return -1;
        }
        this.ObjectMeshIndices[Index]=MeshIndex;
        this.ObjectMaterialIndices[Index]=MaterialIndex;
        this.ObjectTransforms[Index]=Matrix4x4.Identity;
        this.ObjectSelectionWeights[Index]=0f;
        this.ObjectActive[Index]=true;
        this.ObjectHiddenFromView[Index]=false;
        this.ObjectNoShadow[Index]=false;
        this.ObjectEmissiveScales[Index]=1f;
        this.CameraPreviewDiagnosticsDirty=true;
        this.WriteObjectConstants(this.FrameIndex,Index);
        return Index;
    }

    /// <summary>
    /// Chooses where one object is drawn (M0107; UE's OwnerNoSee / OnlyOwnerSee and CastShadow): <paramref name="VisibleInView"/> false keeps it out of the camera's
    /// pass (and the GI probes' and previews'), <paramref name="CastsShadow"/> false keeps it out of every shadow map. Both default to true when an object is
    /// added. The choice stays until it is changed; it needs no per-frame call.
    /// </summary>
    public void SetObjectVisibility(int ObjectIndex,bool VisibleInView,bool CastsShadow){
        this.ThrowIfDisposed();
        if((uint)ObjectIndex>=(uint)this.ObjectCount){
            throw new ArgumentOutOfRangeException(nameof(ObjectIndex));
        }
        this.ObjectHiddenFromView[ObjectIndex]=!VisibleInView;
        this.ObjectNoShadow[ObjectIndex]=!CastsShadow;
    }

    /// <summary>
    /// Multiplies one object's material emission (its emissive nits, which the shader multiplies by the emissive texture) by <paramref name="Scale"/>
    /// (ADR-0078/M0109). It writes the object's existing emissive constant, so the Stage shader contract does not change. 1 is the material's own
    /// emission; the value stays until it is changed.
    /// </summary>
    public void SetObjectEmissiveScale(int ObjectIndex,float Scale){
        this.ThrowIfDisposed();
        if((uint)ObjectIndex>=(uint)this.ObjectCount){
            throw new ArgumentOutOfRangeException(nameof(ObjectIndex));
        }
        if(!float.IsFinite(Scale)||Scale<0f){
            throw new ArgumentOutOfRangeException(nameof(Scale),"Emissive scale must be finite and not negative.");
        }
        if(this.ObjectEmissiveScales[ObjectIndex]==Scale){ return; }
        this.ObjectEmissiveScales[ObjectIndex]=Scale;
        this.WriteObjectConstants(this.FrameIndex,ObjectIndex);
    }

    /// <summary>Stops drawing one object (M0105). Its slot is handed out again by a later <see cref="AddObject"/>. Removing a removed object does nothing.</summary>
    public void RemoveObject(int ObjectIndex){
        this.ThrowIfDisposed();
        if((uint)ObjectIndex>=(uint)this.ObjectCount){
            throw new ArgumentOutOfRangeException(nameof(ObjectIndex));
        }
        if(!this.ObjectActive[ObjectIndex]){ return; }
        if(this.ObjectSelectionWeights[ObjectIndex]>0f){ this.SelectedObjectCount--; }
        this.ObjectSelectionWeights[ObjectIndex]=0f;
        this.ObjectActive[ObjectIndex]=false;
        this.FreeObjectSlots.Add(ObjectIndex);
        this.CameraPreviewDiagnosticsDirty=true;
    }

    private void CreateConstantBuffers(){
        var FrameBufferSize=checked(
            FrameConstantsStride*(uint)FrameCount*(uint)CameraPassCount);
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

        var ObjectBufferSize=ConstantAlignment*(uint)this.ObjectCapacity*FrameCount;
        this.CreateUploadBuffer(ObjectBufferSize,out this.ObjectConstantsBuffer,out this.ObjectConstantsMapped,"creating the object constant buffer");

        uint OverlayBufferSize=checked(
            (uint)(Unsafe.SizeOf<OverlayVertexGpu>()*OverlayFrameVertexCapacity*FrameCount));
        this.CreateUploadBuffer(
            OverlayBufferSize,out this.OverlayVertexBuffer,
            out this.OverlayVertexMapped,"creating the overlay vertex buffer");
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
            this.WriteViewDepth(Frame);
            this.WriteDirectionalCascades(Frame,DefaultLightDirectionTowardLight);
            this.WriteLightCounts(Frame); // D3D12 does not guarantee zero-initialized upload-heap memory; without this the very first frame could loop over garbage PointLights/SpotLights entries.
            this.WriteAreaShadowDefaults(Frame);

            for(var ObjectIndex=0;ObjectIndex< this.ObjectCount;ObjectIndex++){
                if(this.ObjectActive[ObjectIndex]){ this.WriteObjectConstants(Frame,ObjectIndex); }
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
        if(!RadialDepthCurve.TryCreate(Camera.RadialTransitionMeters,out var Curve)
            ||!RadialDepthProjection.TryCreate(Curve,Camera.NearZ,Camera.FarZ,Camera.FovYRadians,AspectRatio,out var RadialProjection)){
            throw new ArgumentException("A logarithmic perspective camera needs a finite positive transition length, 0 < NearZ < FarZ and a valid field of view.",nameof(Camera));
        }
        var View=Camera.GetView();
        this.WriteViewProjection(this.FrameIndex,View*Camera.GetProjection(AspectRatio));
        this.WriteCameraPosition(this.FrameIndex,Camera.GetEyePosition());
        this.WriteExposure(this.FrameIndex,Camera.ExposureEv100);
        this.CurrentViewDepth=RenderViewDepth.ForPerspective(RadialProjection);
        this.CurrentAuxiliaryViewProjection=View*RadialProjection.CreateAuxiliaryProjection(Camera.FovYRadians,AspectRatio);
        this.WriteViewDepth(this.FrameIndex);
        // Right-handed look-at: the view matrix's columns are the camera's right, up and backward axes.
        this.CurrentCameraRight=new Vector3(View.M11,View.M21,View.M31);
        this.CurrentCameraUp=new Vector3(View.M12,View.M22,View.M32);
        this.CurrentCameraForward=-new Vector3(View.M13,View.M23,View.M33);
        this.CurrentCameraTanHalfFovY=MathF.Tan(Camera.FovYRadians*0.5f);
        this.CurrentCameraAspectRatio=AspectRatio;
        this.HasCameraBasis=true;
    }

    /// <summary>
    /// The invertible projection used for sky direction and sun cascades. A logarithmic perspective uses
    /// an auxiliary clip range because its physical far plane may make the original matrix singular in float.
    /// </summary>
    private Matrix4x4 InvertibleViewProjection=>
        this.CurrentViewDepth.Encoding==RenderDepthEncoding.Logarithmic?this.CurrentAuxiliaryViewProjection:this.CurrentViewProjection;

    private void WriteViewDepth(uint FrameIndex){
        var Depth=this.CurrentViewDepth;
        var DepthEncoding0=Depth.Packet0;
        var DepthEncoding1=Depth.Packet1;
        var Slot=this.FrameSlot(FrameIndex);
        Unsafe.CopyBlock(Slot+FrameDepthEncoding0Offset,&DepthEncoding0,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Slot+FrameDepthEncoding1Offset,&DepthEncoding1,(uint)sizeof(Vector4));
        Matrix4x4.Invert(this.InvertibleViewProjection,out var Inverse);
        Vector4 ViewportSize=new(this.Width,this.Height,1f/this.Width,1f/this.Height);
        Unsafe.CopyBlock(Slot+FrameInverseViewProjectionOffset,&Inverse,(uint)sizeof(Matrix4x4));
        Unsafe.CopyBlock(Slot+FrameViewportSizeOffset,&ViewportSize,(uint)sizeof(Vector4));
    }

    /// <summary>
    /// M0100: each object's conservative radial raster depth, tangent at the view depth of its mesh's bounds centre
    /// (the view-projection's W), so hidden fragments are rejected early at every distance and not only near one.
    /// </summary>
    private void WriteRadialRasterDepths(uint FrameIndex){
        var ViewProjection=this.CurrentViewProjection;
        for(var ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            if(!this.ObjectActive[ObjectIndex]){ continue; }
            var Centre=Vector3.Transform(this.MeshBoundsCentres[this.ObjectMeshIndices[ObjectIndex]],this.ObjectTransforms[ObjectIndex]);
            var ViewDepth=Centre.X*ViewProjection.M14+Centre.Y*ViewProjection.M24+Centre.Z*ViewProjection.M34+ViewProjection.M44;
            var Depth=this.CurrentViewDepth.Projection.ConservativeDepthAt(ViewDepth);
            Vector4 Packet=new(Depth.Limit,Depth.Slope,0f,0f);
            var Slot=((ulong)FrameIndex*(ulong)this.ObjectCapacity)+(ulong)ObjectIndex;
            Unsafe.CopyBlock(this.ObjectConstantsMapped+(Slot*ConstantAlignment)+ObjectConservativeRasterDepthOffset,&Packet,(uint)sizeof(Vector4));
        }
    }

    /// <summary>
    /// Accepts an already camera-relative preview matrix through the owning Render Server.
    /// Internal visibility prevents L8 clients from acquiring the native backend directly.
    /// </summary>
    internal void SetViewProjection(in Matrix4x4 ViewProjection){
        this.SetViewProjection(ViewProjection,Vector3.Zero);
    }

    internal void SetViewProjection(
        in Matrix4x4 ViewProjection,in Vector3 CameraPosition){
        this.ThrowIfDisposed();
        var Depth=RenderViewDepth.ForOrthographic(ViewProjection);
        this.ApplyViewProjection(ViewProjection,CameraPosition,Depth,ViewProjection);
    }

    private void ApplyViewProjection(in Matrix4x4 ViewProjection,in Vector3 CameraPosition,
        in RenderViewDepth Depth,in Matrix4x4 Auxiliary){
        if(!IsFiniteMatrix(ViewProjection)||!IsFiniteMatrix(Auxiliary)
            ||!Matrix4x4.Invert(Auxiliary,out var Inverse)||!IsFiniteMatrix(Inverse)){
            throw new ArgumentException("A view needs a finite matrix and an invertible auxiliary projection.",nameof(ViewProjection));
        }
        if(!IsFinite(CameraPosition)){
            throw new ArgumentException("Camera position must be finite.",nameof(CameraPosition));
        }
        this.WriteViewProjection(this.FrameIndex,ViewProjection);
        this.WriteCameraPosition(this.FrameIndex,CameraPosition);
        this.WriteExposure(this.FrameIndex,0f);
        this.CurrentViewDepth=Depth;
        this.CurrentAuxiliaryViewProjection=Auxiliary;
        this.WriteViewDepth(this.FrameIndex);
        this.HasCameraBasis=false;
    }

    /// <summary>
    /// M0100: an isolated preview's camera-relative perspective matrix drawn with radial depth. The matrix is used as
    /// given, so coverage, overlays and picking stay those of the caller's matrix; the radial packet, the
    /// auxiliary matrix and each object's conservative raster depth are derived from it and <paramref name="Radial"/>.
    /// </summary>
    internal void SetViewProjection(
        in Matrix4x4 ViewProjection,in Vector3 CameraPosition,in RadialViewParameters Radial){
        this.ThrowIfDisposed();
        if(!RadialDepthProjection.IsPerspective(ViewProjection)
            ||!RadialDepthCurve.TryCreate(Radial.TransitionMeters,out var Curve)
            ||!RadialDepthProjection.TryCreate(Curve,Radial.NearZ,Radial.FarZ,Radial.FovYRadians,(float)this.Width/ this.Height,out var Projection)){
            throw new ArgumentException("A radial view-projection must be a perspective one with a finite positive transition length, 0 < NearZ < FarZ (finite) and a valid field of view.",nameof(Radial));
        }
        var Depth=RenderViewDepth.ForPerspective(Projection);
        this.ApplyViewProjection(ViewProjection,CameraPosition,Depth,Projection.CreateAuxiliaryViewProjection(ViewProjection));
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
                this.WriteDirectionalCascades(this.FrameIndex,DirectionTowardLight);
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
            this.RecordAtmosphereSun(Submission.DirectionTowardLight,Submission.ColorTimesIntensity);
            this.WriteDirectionalLight(
                FrameIndex,0,Submission.DirectionTowardLight,
                this.AttenuateForAtmosphere(Submission.DirectionTowardLight,Submission.ColorTimesIntensity),
                Submission.ShadowBiasMeters);
            if(Submission.CastsShadows){
                var Candidate=new PhysicalLightCandidate(
                    Submission.StableSubmissionOrder,PhysicalLightKind.Directional,
                    Submission.ShadowPriority,CalculateDirectionalScreenContribution(Submission),
                    CastsShadows:true);
                this.DirectionalShadowCandidates.Add(Candidate);
                this.DirectionalShadowIndex=0;
                this.SelectedDirectionalShadowAtlasCandidate=Candidate;
                this.WriteDirectionalCascades(FrameIndex,Submission.DirectionTowardLight);
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
        if(this.DirectionalLightCount>0){
            this.RecordAtmosphereSun(Ranked[0].Submission.DirectionTowardLight,Ranked[0].Submission.ColorTimesIntensity);
        }
        for(int Index=0;Index<this.DirectionalLightCount;Index++){
            DirectionalLightSubmission Submission=Ranked[Index].Submission;
            this.WriteDirectionalLight(
                FrameIndex,Index,Submission.DirectionTowardLight,
                this.AttenuateForAtmosphere(Submission.DirectionTowardLight,Submission.ColorTimesIntensity),
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
                this.WriteDirectionalCascades(FrameIndex,Ranked[Index].Submission.DirectionTowardLight);
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

    /// <summary>
    /// Replaces the persistent scene environment (ADR-0069): physical sky, sky light, physical fog,
    /// exposure model and tone mapper. <see cref="EnvironmentLightingSettings.Legacy"/> restores the
    /// original clear-colour, flat-ambient, legacy-fog and linear ACES output path.
    /// </summary>
    public void SetEnvironmentLighting(in EnvironmentLightingSettings Settings){
        this.ThrowIfDisposed();
        if(!Settings.IsValid){
            throw new ArgumentException("Environment lighting settings must be finite and physically valid.",nameof(Settings));
        }
        this.EnvironmentSettings=Settings;
        this.WriteExposure(this.FrameIndex,this.CurrentCameraEv100);
    }

    /// <summary>Absolute camera height above the world origin in metres, used by the atmosphere and height fog.</summary>
    public void SetCameraAltitude(float Meters){
        this.ThrowIfDisposed();
        if(!float.IsFinite(Meters)){
            throw new ArgumentOutOfRangeException(nameof(Meters),"The camera altitude must be finite.");
        }
        this.CameraAltitudeMeters=Meters;
    }

    /// <summary>
    /// Height above the world origin, in metres, of the render-space origin. The simplified height fog adds it to
    /// the receiver's render-space height. Callers that render in world coordinates leave it at zero; the
    /// camera-relative runtime passes the camera's altitude. Persists until changed.
    /// </summary>
    public void SetRenderOriginAltitude(float Meters){
        this.ThrowIfDisposed();
        if(!float.IsFinite(Meters)){
            throw new ArgumentOutOfRangeException(nameof(Meters),"The render origin altitude must be finite.");
        }
        this.RenderOriginAltitudeMeters=Meters;
        this.WriteFog(this.FrameIndex,this.CurrentFog);
    }

    /// <summary>
    /// The render-space origin's world position reduced modulo the material's grid size, so the world-space grid
    /// stays on the world lattice when the frame is rendered camera-relative. Each component lies in
    /// [0, <see cref="StageWorldGrid.GridSizeMeters"/>]. Callers that render in world coordinates leave it at
    /// zero. Persists until changed.
    /// </summary>
    public void SetWorldGridAnchor(int MaterialIndex,Vector3 Anchor){
        this.ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(MaterialIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(MaterialIndex,this.MaterialCount);
        if(this.MaterialWorldGrids[MaterialIndex] is not{ }Grid){
            throw new ArgumentException($"Material {MaterialIndex} has no world grid.",nameof(MaterialIndex));
        }
        if(!(Anchor.X>=0f&&Anchor.X<=Grid.GridSizeMeters&&Anchor.Y>=0f&&Anchor.Y<=Grid.GridSizeMeters&&Anchor.Z>=0f&&Anchor.Z<=Grid.GridSizeMeters)){
            throw new ArgumentOutOfRangeException(nameof(Anchor),"The world grid anchor must lie within one grid cell.");
        }
        this.MaterialWorldGridAnchors[MaterialIndex]=Anchor;
    }

    private Vector3 AttenuateForAtmosphere(Vector3 DirectionTowardLight,Vector3 ColorTimesIntensity)=>
        this.EnvironmentSettings.Atmosphere is SkyAtmosphereSettings Atmosphere
            ?ColorTimesIntensity*AtmosphereTransmittance.Evaluate(Atmosphere,this.CameraAltitudeMeters,DirectionTowardLight)
            :ColorTimesIntensity;

    private void RecordAtmosphereSun(Vector3 DirectionTowardLight,Vector3 ColorTimesIntensity){
        this.AtmosphereSunDirection=Vector3.Normalize(DirectionTowardLight);
        this.AtmosphereSunIlluminance=ColorTimesIntensity;
    }

    private void PrepareEnvironment(uint FrameIndex){
        var Settings=this.EnvironmentSettings;
        var SkyLightEnabled=0f;
        if(Settings.Atmosphere is SkyAtmosphereSettings Atmosphere){
            if(!Matrix4x4.Invert(this.InvertibleViewProjection,out var Inverse)){
                Inverse=Matrix4x4.Identity;
            }
            // Reserved atmosphere packet retained for ABI compatibility; sky composition reads SceneDistance.
            var DepthMapping=this.CurrentViewDepth.Encoding==RenderDepthEncoding.Logarithmic
                ?new Vector3(1f,this.CurrentViewDepth.Projection.Curve.TransitionMeters,1f/this.CurrentViewDepth.Projection.InverseMaximumG)
                :Vector3.Zero;
            this.CurrentAtmosphereFrame=new AtmosphereFrame(
                Atmosphere,Settings.SkyLight??new SkyLightSettings(0f,Vector3.Zero),Settings.Fog,
                this.AtmosphereSunDirection,this.AtmosphereSunIlluminance,Settings.SunAngularRadiusRadians,
                this.CameraAltitudeMeters,this.CurrentExposureScale(),Inverse,
                Settings.Clouds,Settings.SkySphere,DepthMapping,this.CurrentCameraPosition);
            this.Atmosphere!.Update(this.CommandList,FrameIndex,this.CurrentAtmosphereFrame);
            SkyLightEnabled=Settings.SkyLight is null?0f:1f;
        }
        Vector4 SkyLightParams=new(SkyLightEnabled,Settings.Fog is null?0f:1f,this.CameraAltitudeMeters,0f);
        Vector4 FogParams=Settings.Fog is PhysicalHeightFogSettings Fog
            ?new(Fog.RayOriginTerms(this.CameraAltitudeMeters),Fog.HeightFalloffPerMeter,1f-Fog.MaxOpacity,Fog.SkyAtmosphereContributionScale)
            :Vector4.Zero;
        Vector4 FogColor=Settings.Fog is PhysicalHeightFogSettings ColoredFog?new(ColoredFog.InscatteringLuminance,0f):Vector4.Zero;
        var Slot=this.FrameSlot(FrameIndex);
        Unsafe.CopyBlock(Slot+FrameSkyLightParamsOffset,&SkyLightParams,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Slot+FramePhysicalFogParamsOffset,&FogParams,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Slot+FramePhysicalFogColorOffset,&FogColor,(uint)sizeof(Vector4));
        this.WriteGlobalIlluminationConstants(Slot);
        var AmbientOcclusion=Settings.AmbientOcclusion is AmbientOcclusionSettings Occlusion
            ?new Vector4(1f,Occlusion.Intensity,1f/this.Width,1f/this.Height)
            :Vector4.Zero;
        Unsafe.CopyBlock(Slot+FrameAmbientOcclusionOffset,&AmbientOcclusion,(uint)sizeof(Vector4));
    }

    /// <summary>Produces the physical scene distance before indirect-light shading needs AO.</summary>
    private void RenderSceneDistances(int Width,int Height){
        D3D12_RESOURCE_BARRIER ToTarget=D3D12_RESOURCE_BARRIER.InitTransition(this.HdrColorBuffer,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
        this.CommandList->ResourceBarrier(1,&ToTarget);
        D3D12_CPU_DESCRIPTOR_HANDLE NormalTarget=new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),FrameCount,this.RtvDescriptorSize);
        D3D12_CPU_DESCRIPTOR_HANDLE DistanceTarget=new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),FrameCount+1,this.RtvDescriptorSize);
        var Dsv=this.DsvHeap->GetCPUDescriptorHandleForHeapStart();
        var Clear=Vector4.Zero;
        this.CommandList->ClearRenderTargetView(DistanceTarget,(float*)&Clear,0,null);
        this.CommandList->ClearDepthStencilView(Dsv,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,1f,0,0,null);
        this.CommandList->OMSetRenderTargets(2,&NormalTarget,TRUE,&Dsv);
        // The existing HDR allocation is scratch normal storage here; the colour pass clears it.
        this.BindMainPassState(this.LumenSurfacePipelineState);
        // The consumer's active region occupies the top-left of the reusable full-size targets.
        D3D12_VIEWPORT Viewport=new(){ Width=Width,Height=Height,MinDepth=0f,MaxDepth=1f };
        RECT Scissor=new(){ right=Width,bottom=Height };
        this.CommandList->RSSetViewports(1,&Viewport);
        this.CommandList->RSSetScissorRects(1,&Scissor);
        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        this.DrawObjects(ExcludedObjectIndex:-1);
        var ToReadable=D3D12_RESOURCE_BARRIER.InitTransition(this.HdrColorBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        this.CommandList->ResourceBarrier(1,&ToReadable);
    }
    /// <summary>AO reads the shared physical distance and keeps its half-resolution obscurance/blur.</summary>
    private void RenderAmbientOcclusion(){
        if(this.EnvironmentSettings.AmbientOcclusion is not AmbientOcclusionSettings Settings){
            return;
        }
        var AmbientOcclusion=this.AmbientOcclusion!;
        if(AmbientOcclusion.Generation==0){
            this.WaitForGpuIdle();
        }
        AmbientOcclusion.EnsureSize(this.Width,this.Height,this.SceneDistanceBuffer);
        if(this.AmbientOcclusionDescriptorGeneration!=AmbientOcclusion.Generation){
            this.WriteEnvironmentDescriptors();
        }
        this.RenderSceneDistances(AmbientOcclusion.Width,AmbientOcclusion.Height);
        AmbientOcclusion.RecordCompute(this.CommandList,this.FrameIndex,this.InvertibleViewProjection,this.CurrentCameraPosition,Settings);
    }

    private bool AutoExposureActive=>this.EnvironmentSettings.Exposure.Mode==ExposureMode.AutoHistogram;

    /// <summary>
    /// ADR-0078/M0110: keeps the global distance field around the camera up to date (for M0111's tracing); nothing when the environment has none.
    /// It reads only the objects' render-space matrices, so a camera move rewrites only the bricks the clipmaps newly cover.
    /// </summary>
    private void UpdateGlobalDistanceField(){
        if(this.EnvironmentSettings.DistanceField is not GlobalDistanceFieldSettings Settings){
            return;
        }
        this.GlobalDistanceField!.Update(this.CommandList,this.FrameIndex,Settings,this.ObjectActive,this.ObjectMeshIndices,this.ObjectTransforms,this.ObjectCount,this.WaitForGpuIdle);
    }
    /// <summary>Test-only copy of one global distance field level (M0110); waits for the GPU.</summary>
    internal (float[]Values,int Resolution,LongVector3 ClipMinimum) ReadGlobalDistanceFieldForTesting(int Level){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.GlobalDistanceField!.ReadLevelForTesting(Level);
    }
    /// <summary>Test-only sphere tracing through the global distance field as M0111 will (M0110); waits for the GPU.</summary>
    internal Vector4[] TraceGlobalDistanceFieldForTesting(ReadOnlySpan<(Vector3 Origin,Vector3 Direction,float Length)>Rays){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.GlobalDistanceField!.TraceForTesting(Rays);
    }
    /// <summary>The global distance field's state for tests and diagnostics (M0110).</summary>
    internal StageGlobalDistanceField GlobalDistanceFieldForTesting=>this.GlobalDistanceField!;
    /// <summary>
    /// The CPU-side exposure scale. In histogram mode the Stage program and the sky composite read
    /// the GPU-metered scale instead (ExposureParams.z); this nominal minimum-EV100 value remains for
    /// consumers without access to the GPU state.
    /// </summary>
    private float CurrentExposureScale()=>this.EnvironmentSettings.Exposure.Scale(this.CurrentCameraEv100);

    /// <summary>Seconds since the previous frame for exposure adaptation, clamped against stalls.</summary>
    private float NextAutoExposureDeltaSeconds(){
        var Now=this.AutoExposureClock.Elapsed;
        var DeltaSeconds=this.AutoExposureDeltaSecondsForTesting??Math.Clamp((Now-this.LastAutoExposureTime).Ticks/(float)TimeSpan.TicksPerSecond,0f,0.25f);
        this.LastAutoExposureTime=Now;
        return DeltaSeconds;
    }

    private bool GlobalIlluminationActive=>
        this.EnvironmentSettings.GlobalIllumination is not null&&this.GlobalIllumination!.IsSupported&&!this.LumenActiveThisFrame;

    private void WriteGlobalIlluminationConstants(byte*Slot){
        var Origin=Vector4.Zero;
        var Spacing=Vector4.Zero;
        uint*Counts=stackalloc uint[4]{ 0,0,0,0 };
        if(this.GlobalIlluminationActive&&this.EnvironmentSettings.GlobalIllumination is GlobalIlluminationSettings Settings){
            var ProbeCounts=Settings.ProbeCounts;
            Origin=new Vector4(Settings.GridOrigin,0f);
            Spacing=new Vector4(Settings.GridSpacing,Settings.Intensity);
            Counts[0]=(uint)ProbeCounts.X;
            Counts[1]=(uint)ProbeCounts.Y;
            Counts[2]=(uint)ProbeCounts.Z;
            Counts[3]=StageGlobalIllumination.ProbesPerAtlasRow;
        }
        var AtlasSize=new Vector4(
            StageGlobalIllumination.IrradianceAtlasWidth,StageGlobalIllumination.IrradianceAtlasHeight,
            StageGlobalIllumination.DistanceAtlasWidth,StageGlobalIllumination.DistanceAtlasHeight);
        Unsafe.CopyBlock(Slot+FrameGiGridOriginOffset,&Origin,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Slot+FrameGiGridSpacingOffset,&Spacing,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Slot+FrameGiGridCountsOffset,Counts,16);
        Unsafe.CopyBlock(Slot+FrameGiAtlasSizeOffset,&AtlasSize,(uint)sizeof(Vector4));
        // M0111: the Stage program reads the Lumen diffuse (x) and reflections (y) only while Lumen runs; zero keeps every earlier frame's path.
        var Lumen=this.LumenActiveThisFrame
            ?new Vector4(1f,this.EnvironmentSettings.Lumen is { Reflections:true }&&this.Scalability.AllowsLumenReflections?1f:0f,0f,0f)
            :Vector4.Zero;
        Unsafe.CopyBlock(Slot+FrameLumenParamsOffset,&Lumen,(uint)sizeof(Vector4));
    }

    /// <summary>
    /// Re-captures this frame's scheduled GI probes (ADR-0069). Every face is drawn with the lit
    /// Stage pipeline from a copy of the frame constants: the probe is the camera, exposure is 1 so
    /// the capture holds scene radiance, fog is off, and local lights are off because the Forward+
    /// tile lists belong to the main camera. The previous GI result stays bound, so every refresh
    /// adds one more bounce.
    /// </summary>
    private void RenderGlobalIlluminationProbes(){
        if(!this.GlobalIlluminationActive||this.EnvironmentSettings.GlobalIllumination is not GlobalIlluminationSettings Settings){
            return;
        }
        var GlobalIllumination=this.GlobalIllumination!;
        GlobalIllumination.Schedule(Settings);
        if(!this.GlobalIlluminationDescriptorsLive){
            this.WriteEnvironmentDescriptors();
        }
        var Source=this.FrameSlot(this.FrameIndex);
        if(!RadialDepthProjection.TryCreate(RadialDepthCurve.Default,StageGlobalIllumination.CaptureNearMeters,StageGlobalIllumination.CaptureFarMeters,MathF.PI/2f,1f,out var CaptureProjection)){
            throw new InvalidOperationException("Invalid probe capture projection.");
        }
        GlobalIllumination.BeginCapture(this.CommandList);
        this.BindMainPassState(this.PipelineState);
        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        D3D12_VIEWPORT Viewport=new(){ Width=StageGlobalIllumination.CaptureResolution,Height=StageGlobalIllumination.CaptureResolution,MaxDepth=1f };
        RECT Scissor=new(){ right=StageGlobalIllumination.CaptureResolution,bottom=StageGlobalIllumination.CaptureResolution };
        this.CommandList->RSSetViewports(1,&Viewport);
        this.CommandList->RSSetScissorRects(1,&Scissor);
        Vector4 ClearColor=Vector4.Zero;
        for(var Index=0;Index<GlobalIllumination.ScheduledCount;Index++){
            var ProbePosition=Settings.ProbePosition(GlobalIllumination.ScheduledProbe(Index));
            for(var Face=0;Face<6;Face++){
                var Capture=Index*6+Face;
                var Destination=GlobalIllumination.CaptureFrameSlot(this.FrameIndex,Capture);
                Unsafe.CopyBlock(Destination,Source,FrameConstantsSize);
                var ViewProjection=StageGlobalIllumination.FaceViewProjection(ProbePosition,Face);
                Unsafe.CopyBlock(Destination+FrameViewProjectionOffset,&ViewProjection,(uint)sizeof(Matrix4x4));
                var CameraPosition=new Vector4(ProbePosition,0f);
                Unsafe.CopyBlock(Destination+FrameCameraPositionOffset,&CameraPosition,(uint)sizeof(Vector4));
                ((float*)(Destination+FrameExposureOffset))[0]=1f;
                ((float*)(Destination+FrameExposureOffset))[2]=0f;
                ((float*)(Destination+FrameFogParamsOffset))[0]=0f;
                ((float*)(Destination+FrameSkyLightParamsOffset))[1]=0f;
                ((int*)(Destination+FrameLightCountsOffset))[0]=0;
                ((float*)(Destination+FrameAmbientOcclusionOffset))[0]=0f;
                *(Vector4*)(Destination+FrameLumenParamsOffset)=Vector4.Zero;
                var CaptureDepth=RenderViewDepth.ForPerspective(CaptureProjection,ConservativeRasterDepth:false);
                *(Vector4*)(Destination+FrameDepthEncoding0Offset)=CaptureDepth.Packet0;
                *(Vector4*)(Destination+FrameDepthEncoding1Offset)=CaptureDepth.Packet1;
                var Rtv=GlobalIllumination.CaptureRtv(Capture);
                var Dsv=GlobalIllumination.CaptureDsv(Capture);
                this.CommandList->OMSetRenderTargets(2,&Rtv,TRUE,&Dsv);
                var DistanceRtv=GlobalIllumination.CaptureDistanceRtv(Capture);
                this.CommandList->ClearRenderTargetView(DistanceRtv,(float*)&ClearColor,0,null);
                this.CommandList->ClearRenderTargetView(Rtv,(float*)&ClearColor,0,null);
                this.CommandList->ClearDepthStencilView(Dsv,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,1f,0,0,null);
                this.CommandList->SetGraphicsRootConstantBufferView(0,GlobalIllumination.CaptureFrameAddress(this.FrameIndex,Capture));
                this.DrawObjects(ExcludedObjectIndex:-1);
            }
        }
        GlobalIllumination.RecordConvolution(this.CommandList,this.FrameIndex,Settings,this.Atmosphere!.ShBufferAddress,
            this.EnvironmentSettings.SkyLight is not null&&this.EnvironmentSettings.Atmosphere is not null);
    }

    /// <summary>
    /// Writes the Stage t12-t17 views (GI irradiance, GI distance, ambient occlusion, exposure state, and M0111's Lumen diffuse irradiance and
    /// reflections) after the material views.
    /// </summary>
    private void WriteEnvironmentDescriptors(){
        var Start=this.SrvHeap->GetCPUDescriptorHandleForHeapStart();
        this.GlobalIllumination!.WriteShadingDescriptors(
            new D3D12_CPU_DESCRIPTOR_HANDLE(Start,this.MaterialCapacity*5,this.SrvDescriptorSize),
            new D3D12_CPU_DESCRIPTOR_HANDLE(Start,this.MaterialCapacity*5+1,this.SrvDescriptorSize));
        this.AmbientOcclusion!.WriteShadingDescriptor(new D3D12_CPU_DESCRIPTOR_HANDLE(Start,this.MaterialCapacity*5+2,this.SrvDescriptorSize));
        this.AutoExposureStage!.WriteShadingDescriptor(new D3D12_CPU_DESCRIPTOR_HANDLE(Start,this.MaterialCapacity*5+3,this.SrvDescriptorSize));
        this.Lumen!.WriteShadingDescriptors(
            new D3D12_CPU_DESCRIPTOR_HANDLE(Start,this.MaterialCapacity*5+4,this.SrvDescriptorSize),
            new D3D12_CPU_DESCRIPTOR_HANDLE(Start,this.MaterialCapacity*5+5,this.SrvDescriptorSize));
        this.GlobalIlluminationDescriptorsLive=this.GlobalIllumination.HasResources;
        this.AmbientOcclusionDescriptorGeneration=this.AmbientOcclusion.Generation;
        this.LumenDescriptorGeneration=this.Lumen.Generation;
    }

    private D3D12_GPU_DESCRIPTOR_HANDLE GetGlobalIlluminationDescriptorTable()=>
        new(this.SrvHeap->GetGPUDescriptorHandleForHeapStart(),this.MaterialCapacity*5,this.SrvDescriptorSize);

    private void BindMainPassState(ID3D12PipelineState*ActivePipelineState,ulong FrameConstantsAddress=0){
        this.CommandList->SetGraphicsRootSignature(this.RootSignature);
        this.CommandList->SetPipelineState(ActivePipelineState);
        ID3D12DescriptorHeap*SrvHeap=this.SrvHeap;
        this.CommandList->SetDescriptorHeaps(1,&SrvHeap);
        this.CommandList->SetGraphicsRootConstantBufferView(0,FrameConstantsAddress!=0?FrameConstantsAddress:this.GetFrameConstantsAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootDescriptorTable(10,this.GetGlobalIlluminationDescriptorTable());
        this.CommandList->SetGraphicsRootShaderResourceView(3,this.GetLocalLightsAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(4,this.GetForwardPlusTileListAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(5,this.GetSpotShadowProjectionAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(6,this.GetPointShadowProjectionAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(7,this.GetAreaShadowMetadataAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(8,this.GetAreaShadowProjectionAddress(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(9,this.Atmosphere!.ShBufferAddress);
    }

    /// <summary>
    /// Sets the flat ambient term (a stand-in for sky/indirect lighting). It persists until changed:
    /// <see cref="RenderFrame"/> writes it into whichever frame slot it records, like the directional
    /// lights and exposure, so a value set once never alternates with the other slot's contents.
    /// </summary>
    public void SetAmbientLight(Vector3 Color){
        this.ThrowIfDisposed();
        this.CurrentAmbientColor=Color;
        this.WriteAmbientLight(this.FrameIndex,Color);
    }

    /// <summary>Sets the simplified exponential height fog; persists like <see cref="SetAmbientLight"/>. Pass <see cref="FogParams.None"/> to disable it (zero density).</summary>
    public void SetFog(FogParams Fog){
        this.ThrowIfDisposed();
        this.CurrentFog=Fog;
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
        this.CameraPreviewDiagnosticsDirty=true;
        this.WriteObjectConstants(this.FrameIndex,ObjectIndex);
    }

    public void SetPresentationMode(RenderPresentationMode Mode){
        this.ThrowIfDisposed();
        if(!Enum.IsDefined(Mode)){ throw new ArgumentOutOfRangeException(nameof(Mode)); }
        this.PresentationMode=Mode;
        for(int ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            if(this.ObjectActive[ObjectIndex]){ this.WriteObjectConstants(this.FrameIndex,ObjectIndex); }
        }
    }

    public void SetObjectSelectionWeight(int ObjectIndex,float Weight){
        this.ThrowIfDisposed();
        if((uint)ObjectIndex>=(uint)this.ObjectCount){
            throw new ArgumentOutOfRangeException(nameof(ObjectIndex));
        }
        if(!float.IsFinite(Weight)||Weight<0f||Weight>1f){
            throw new ArgumentOutOfRangeException(
                nameof(Weight),"Selection weight must be finite and between zero and one.");
        }
        if(!this.ObjectActive[ObjectIndex]){ return; }
        float Previous=this.ObjectSelectionWeights[ObjectIndex];
        if(Previous<=0f&&Weight>0f){ this.SelectedObjectCount++; }
        if(Previous>0f&&Weight<=0f){ this.SelectedObjectCount--; }
        this.ObjectSelectionWeights[ObjectIndex]=Weight;
        this.WriteObjectConstants(this.FrameIndex,ObjectIndex);
    }

    public void SetOverlay(
        ReadOnlySpan<RenderOverlayVertex>Triangles,
        ReadOnlySpan<RenderOverlayVertex>Lines){
        this.ThrowIfDisposed();
        int TotalCount=checked(Triangles.Length+Lines.Length);
        if(TotalCount>MaxOverlayVertices){
            throw new ArgumentOutOfRangeException(
                nameof(Triangles),$"Overlay exceeds {MaxOverlayVertices} vertices.");
        }
        if(Triangles.Length%3!=0){
            throw new ArgumentException(
                "Overlay triangle vertices must form complete triangles.",
                nameof(Triangles));
        }
        if(Lines.Length%2!=0){
            throw new ArgumentException(
                "Overlay line vertices must form complete lines.",nameof(Lines));
        }
        int Stride=Unsafe.SizeOf<OverlayVertexGpu>();
        byte*Destination=this.OverlayVertexMapped+
            ((nuint)this.FrameIndex*(nuint)OverlayFrameVertexCapacity*(nuint)Stride);
        int OutputIndex=0;
        foreach(RenderOverlayVertex Vertex in Triangles){
            WriteOverlayVertex(Destination,Stride,OutputIndex++,Vertex);
        }
        foreach(RenderOverlayVertex Vertex in Lines){
            WriteOverlayVertex(Destination,Stride,OutputIndex++,Vertex);
        }
        this.OverlayTriangleVertexCount=Triangles.Length;
        this.OverlayLineVertexCount=Lines.Length;
    }

    internal void SetCameraPreviews(ReadOnlySpan<RenderCameraPreview>Previews){
        this.ThrowIfDisposed();
        if(Previews.Length>RenderCameraPreview.MaximumCount){
            throw new ArgumentOutOfRangeException(
                nameof(Previews),
                $"At most {RenderCameraPreview.MaximumCount} camera previews are supported.");
        }
        for(int Index=0;Index<Previews.Length;Index++){
            RenderCameraPreview Preview=Previews[Index];
            ValidateCameraPreview(Preview,Index,this.ObjectCount);
            this.RequestedCameraPreviews[Index]=Preview;
        }
        Array.Clear(
            this.RequestedCameraPreviews,Previews.Length,
            this.RequestedCameraPreviews.Length-Previews.Length);
        this.RequestedCameraPreviewCount=Previews.Length;
        this.RebuildEffectiveCameraPreviews();
    }

    internal RenderCameraPreviewDiagnostics[]GetCameraPreviewDiagnostics(){
        this.ThrowIfDisposed();
        this.RefreshCameraPreviewDiagnostics();
        RenderCameraPreviewDiagnostics[]Result=
            new RenderCameraPreviewDiagnostics[this.CameraPreviewCount];
        Array.Copy(this.CameraPreviewDiagnostics,Result,this.CameraPreviewCount);
        return Result;
    }

    private static void ValidateCameraPreview(
        in RenderCameraPreview Preview,int PreviewIndex,int ObjectCount){
        if(!IsFiniteMatrix(Preview.ViewProjection)){
            throw new ArgumentException(
                $"Camera preview {PreviewIndex} view-projection must be finite.",
                nameof(Preview));
        }
        if(!IsFinite(Preview.CameraPosition)){
            throw new ArgumentException(
                $"Camera preview {PreviewIndex} position must be finite.",
                nameof(Preview));
        }
        if(Preview.ExcludedObjectIndex<-1
            ||Preview.ExcludedObjectIndex>=ObjectCount){
            throw new ArgumentOutOfRangeException(
                nameof(Preview),
                $"Camera preview {PreviewIndex} excluded object is outside the roster.");
        }
        if(!Preview.Depth.IsValid
            ||RadialDepthProjection.IsPerspective(Preview.ViewProjection)!=(Preview.Depth.Encoding!=RenderDepthEncoding.Linear)
            ||Preview.Depth.Encoding==RenderDepthEncoding.Linear&&Preview.Depth!=RenderViewDepth.ForOrthographic(Preview.ViewProjection)
            ||Preview.Depth.Encoding==RenderDepthEncoding.Logarithmic&&Preview.Depth.Packet1.W!=0f){
            throw new ArgumentException(
                $"Camera preview {PreviewIndex} needs validated depth matching its projection.",
                nameof(Preview));
        }
    }

    private static bool IsFiniteMatrix(in Matrix4x4 Value)=>
        float.IsFinite(Value.M11)&&float.IsFinite(Value.M12)
        &&float.IsFinite(Value.M13)&&float.IsFinite(Value.M14)
        &&float.IsFinite(Value.M21)&&float.IsFinite(Value.M22)
        &&float.IsFinite(Value.M23)&&float.IsFinite(Value.M24)
        &&float.IsFinite(Value.M31)&&float.IsFinite(Value.M32)
        &&float.IsFinite(Value.M33)&&float.IsFinite(Value.M34)
        &&float.IsFinite(Value.M41)&&float.IsFinite(Value.M42)
        &&float.IsFinite(Value.M43)&&float.IsFinite(Value.M44);

    private void RebuildEffectiveCameraPreviews(){
        int Count=0;
        for(int Index=0;Index<this.RequestedCameraPreviewCount;Index++){
            RenderCameraPreview Preview=this.RequestedCameraPreviews[Index];
            if(Preview.Width<=0||Preview.Height<=0){ continue; }
            int Left=ClampToSurface(Preview.X,this.Width);
            int Top=ClampToSurface(Preview.Y,this.Height);
            int Right=ClampToSurface((long)Preview.X+Preview.Width,this.Width);
            int Bottom=ClampToSurface((long)Preview.Y+Preview.Height,this.Height);
            if(Right<=Left||Bottom<=Top){ continue; }
            this.EffectiveCameraPreviews[Count++]=Preview with {
                X=Left,Y=Top,Width=Right-Left,Height=Bottom-Top,
            };
        }
        Array.Clear(
            this.EffectiveCameraPreviews,Count,
            this.EffectiveCameraPreviews.Length-Count);
        this.CameraPreviewCount=Count;
        this.CameraPreviewDiagnosticsDirty=true;
    }

    private static int ClampToSurface(long Value,int Maximum)=>
        (int)Math.Min(Math.Max(Value,0L),Maximum);

    private void RefreshCameraPreviewDiagnostics(){
        if(!this.CameraPreviewDiagnosticsDirty){ return; }
        for(int PreviewIndex=0;PreviewIndex<this.CameraPreviewCount;PreviewIndex++){
            RenderCameraPreview Preview=this.EffectiveCameraPreviews[PreviewIndex];
            int Candidates=0;
            for(int ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
                if(!this.ObjectActive[ObjectIndex]){ continue; }
                Vector3 Position=Vector3.Transform(
                    Vector3.Zero,this.ObjectTransforms[ObjectIndex]);
                Vector4 Clip=Vector4.Transform(
                    new Vector4(Position,1f),Preview.ViewProjection);
                if(Clip.W>1e-5f&&MathF.Abs(Clip.X)<=Clip.W
                    &&MathF.Abs(Clip.Y)<=Clip.W&&Clip.Z>=0f&&Clip.Z<=Clip.W){
                    Candidates++;
                }
            }
            int DrawCalls=this.ActiveObjectCount-
                (Preview.ExcludedObjectIndex>=0&&this.ObjectActive[Preview.ExcludedObjectIndex]?1:0);
            this.CameraPreviewDiagnostics[PreviewIndex]=
                new RenderCameraPreviewDiagnostics(
                    this.ActiveObjectCount,Candidates,DrawCalls,
                    ComputeMatrixHash(Preview.ViewProjection));
        }
        Array.Clear(
            this.CameraPreviewDiagnostics,this.CameraPreviewCount,
            this.CameraPreviewDiagnostics.Length-this.CameraPreviewCount);
        this.CameraPreviewDiagnosticsDirty=false;
    }

    private static uint ComputeMatrixHash(in Matrix4x4 Value){
        unchecked {
            uint Hash=2166136261;
            Hash=(Hash^(uint)Value.M11.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M12.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M13.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M14.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M21.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M22.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M23.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M24.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M31.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M32.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M33.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M34.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M41.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M42.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M43.GetHashCode())*16777619;
            return(Hash^(uint)Value.M44.GetHashCode())*16777619;
        }
    }

    private static void WriteOverlayVertex(
        byte*Destination,int Stride,int Index,RenderOverlayVertex Vertex){
        if(!IsFinite(Vertex.Position)
            ||!float.IsFinite(Vertex.Color.X)
            ||!float.IsFinite(Vertex.Color.Y)
            ||!float.IsFinite(Vertex.Color.Z)
            ||!float.IsFinite(Vertex.Color.W)
            ||Vertex.Color.W<0f||Vertex.Color.W>1f){
            throw new ArgumentException(
                "Overlay vertices require finite positions/colors and alpha between zero and one.",
                nameof(Vertex));
        }
        OverlayVertexGpu Packed=new(){
            Position=Vertex.Position,
            Color=Vertex.Color,
        };
        Unsafe.CopyBlock(
            Destination+(Index*Stride),&Packed,(uint)Stride);
    }

    private void ThrowIfDisposed(){
        if(this.Disposed){
            throw new ObjectDisposedException(nameof(StageRenderer));
        }
    }

    private byte*FrameSlot(uint FrameIndex,int PassIndex=0){
        if((uint)PassIndex>=(uint)CameraPassCount){
            throw new ArgumentOutOfRangeException(nameof(PassIndex));
        }
        uint LinearSlot=checked((FrameIndex*(uint)CameraPassCount)+(uint)PassIndex);
        return this.FrameConstantsMapped+(LinearSlot*FrameConstantsStride);
    }

    private void WriteViewProjection(uint FrameIndex,Matrix4x4 ViewProjection){
        this.CurrentViewProjection=ViewProjection;
        this.FrameViewProjections[FrameIndex]=ViewProjection;
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameViewProjectionOffset,&ViewProjection,(uint)sizeof(Matrix4x4));
    }

    private void WriteCameraPosition(uint FrameIndex,Vector3 CameraPosition){
        this.CurrentCameraPosition=CameraPosition;
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
        Vector4 FogParams0=new(Fog.Density,Fog.HeightFalloff,this.RenderOriginAltitudeMeters,0f);
        Vector4 FogColor=new(Fog.Color,0f);
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameFogParamsOffset,&FogParams0,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameFogColorOffset,&FogColor,(uint)sizeof(Vector4));
    }

    private void WriteExposure(uint FrameIndex,float ExposureEv100){
        if(!float.IsFinite(ExposureEv100)){
            throw new ArgumentOutOfRangeException(nameof(ExposureEv100),"Camera exposure must be finite.");
        }
        // Legacy: EV100 is one stop per integer (+1 halves the HDR signal). PhysicalCamera mode
        // replaces the camera EV with the environment's exposure settings (ADR-0069).
        this.CurrentCameraEv100=ExposureEv100;
        Vector4 Packed=new(this.CurrentExposureScale(),ExposureEv100,this.AutoExposureActive?1f:0f,0f);
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameExposureOffset,&Packed,(uint)sizeof(Vector4));
    }

    /// <summary>
    /// Fits the four directional cascades to the current camera (ADR-0069) and writes their
    /// projections for both the shadow pass and the Stage program. Cascade 0 also fills the
    /// single-projection fields that the preview shaders still sample.
    /// </summary>
    private void WriteDirectionalCascades(uint FrameIndex,Vector3 DirectionTowardLight){
        var Cascades=DirectionalShadowCascades.Build(this.InvertibleViewProjection,this.CurrentCameraPosition,DirectionTowardLight);
        var Slot=this.FrameSlot(FrameIndex);
        var DepthRanges=Vector4.Zero;
        for(var Cascade=0;Cascade<Cascades.Length;Cascade++){
            var ViewProjection=Cascades[Cascade].ViewProjection;
            var Sphere=new Vector4(Cascades[Cascade].Center,Cascades[Cascade].Radius);
            var UvScaleOffset=Cascades[Cascade].Tile.UvScaleOffset;
            Unsafe.CopyBlock(Slot+FrameCascadeViewProjectionOffset+(uint)(Cascade*64),&ViewProjection,(uint)sizeof(Matrix4x4));
            Unsafe.CopyBlock(Slot+FrameCascadeSpheresOffset+(uint)(Cascade*16),&Sphere,(uint)sizeof(Vector4));
            Unsafe.CopyBlock(Slot+FrameCascadeUvScaleOffsetsOffset+(uint)(Cascade*16),&UvScaleOffset,(uint)sizeof(Vector4));
            Unsafe.CopyBlock(this.ShadowConstantsSlot(FrameIndex,CascadeConstantSlot(Cascade)),&ViewProjection,(uint)sizeof(Matrix4x4));
            DepthRanges[Cascade]=Cascades[Cascade].DepthRangeMeters;
        }
        Unsafe.CopyBlock(Slot+FrameCascadeDepthRangesOffset,&DepthRanges,(uint)sizeof(Vector4));

    }

    private static int CascadeConstantSlot(int Cascade)=>Cascade==0?0:CascadeShadowConstantSlotOffset+Cascade-1;

    private byte*ShadowConstantsSlot(uint FrameIndex,int Slot){
        if((uint)Slot>=(uint)ShadowAtlasProjectionConstantSlotCount){
            throw new ArgumentOutOfRangeException(nameof(Slot));
        }
        uint LinearSlot=(FrameIndex*(uint)ShadowAtlasProjectionConstantSlotCount)+(uint)Slot;
        return this.ShadowConstantsMapped+(LinearSlot*ConstantAlignment);
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
        var Slot=((ulong)FrameIndex*(ulong)this.ObjectCapacity)+(ulong)ObjectIndex;
        var Destination= this.ObjectConstantsMapped+(Slot*ConstantAlignment);

        Matrix4x4 World= this.ObjectTransforms[ObjectIndex];
        Unsafe.CopyBlock(Destination+ObjectWorldOffset,&World,(uint)sizeof(Matrix4x4));

        var MaterialIndex= this.ObjectMaterialIndices[ObjectIndex];
        Vector4 BaseColorFactor= this.MaterialBaseColorFactors[MaterialIndex];
        Unsafe.CopyBlock(Destination+ObjectBaseColorFactorOffset,&BaseColorFactor,(uint)sizeof(Vector4));

        Vector4 MaterialParams=new(
            this.MaterialRoughnessFactors[MaterialIndex],
            this.MaterialMetallicFactors[MaterialIndex],
            (float)this.PresentationMode,0f);
        Unsafe.CopyBlock(Destination+ObjectMaterialParamsOffset,&MaterialParams,(uint)sizeof(Vector4));

        Vector4 EmissiveNits=new(
            this.MaterialEmissiveNits[MaterialIndex]*this.ObjectEmissiveScales[ObjectIndex],
            this.ObjectSelectionWeights[ObjectIndex]);
        Unsafe.CopyBlock(Destination+ObjectEmissiveFactorOffset,&EmissiveNits,(uint)sizeof(Vector4));

        // A zero grid size means "no grid": the shader keeps the plain base colour.
        var WorldGrid=this.MaterialWorldGrids[MaterialIndex];
        Vector4 WorldGridParams=WorldGrid is{ }Grid?new Vector4(Grid.GridSizeMeters,Grid.SubGridDivisions,Grid.WireHalfWidth,0f):Vector4.Zero;
        Vector4 WorldGridColor=WorldGrid is{ }MainGrid?new Vector4(MainGrid.GridColor,0f):Vector4.Zero;
        Vector4 WorldSubGridColor=WorldGrid is{ }SubGrid?new Vector4(SubGrid.SubGridColor,0f):Vector4.Zero;
        Unsafe.CopyBlock(Destination+ObjectWorldGridParamsOffset,&WorldGridParams,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Destination+ObjectWorldGridColorOffset,&WorldGridColor,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Destination+ObjectWorldSubGridColorOffset,&WorldSubGridColor,(uint)sizeof(Vector4));
        Vector4 WorldGridAnchor=new(this.MaterialWorldGridAnchors[MaterialIndex],0f);
        Unsafe.CopyBlock(Destination+ObjectWorldGridAnchorOffset,&WorldGridAnchor,(uint)sizeof(Vector4));
    }

    private ulong GetFrameConstantsAddress(uint FrameIndex,int PassIndex=0){
        uint LinearSlot=checked((FrameIndex*(uint)CameraPassCount)+(uint)PassIndex);
        return this.FrameConstantsBuffer->GetGPUVirtualAddress()
            +(LinearSlot*FrameConstantsStride);
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
        var Slot=((ulong)FrameIndex*(ulong)this.ObjectCapacity)+(ulong)ObjectIndex;
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

        // See ADR-0003 §8: ResizeBuffers restarts the swap chain's
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

        if(this.SceneDistanceBuffer!=null){
            _=this.SceneDistanceBuffer->Release();
            this.SceneDistanceBuffer=null;
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
        this.CreateSceneDistanceBuffer(Width,Height);
        this.CreateForwardPlusTileBuffer(Width,Height);
        this.Atmosphere?.InvalidateSceneDescriptors();
        if(this.Lumen is not null){
            // The GPU is idle: the Stage t16/t17 views go back to null until the next Lumen frame makes targets of the new size.
            this.Lumen.ReleaseScreen();
            this.WriteEnvironmentDescriptors();
        }
        if(this.AmbientOcclusion is { Generation:>0 }){
            this.AmbientOcclusion.EnsureSize(Width,Height,this.SceneDistanceBuffer);
        }
        this.FrameIndex= this.SwapChain->GetCurrentBackBufferIndex();
        this.UpdateViewportAndScissor(Width,Height);
        this.RebuildEffectiveCameraPreviews();
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
            for(var Cascade=0;Cascade<DirectionalShadowCascades.Count;Cascade++){
                this.RenderShadowAtlasProjection(new ShadowAtlasProjection(DirectionalShadowCascades.TileFor(Cascade),CascadeConstantSlot(Cascade)),ShadowDsv);
            }
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
            if(!this.ObjectActive[I]||this.ObjectNoShadow[I]){ continue; }
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

    private void RenderOverlay(){
        int TotalCount=
            this.OverlayTriangleVertexCount+this.OverlayLineVertexCount;
        int MarkerCount=this.PrepareObserverMarkers();
        if(TotalCount==0&&MarkerCount==0){ return; }
        int Stride=Unsafe.SizeOf<OverlayVertexGpu>();
        ulong FrameOffset=checked(
            (ulong)this.FrameIndex*(ulong)OverlayFrameVertexCapacity*(ulong)Stride);
        ulong BaseAddress=this.OverlayVertexBuffer->GetGPUVirtualAddress()+FrameOffset;
        this.CommandList->SetGraphicsRootSignature(this.RootSignature);
        this.CommandList->SetGraphicsRootConstantBufferView(
            0,this.GetFrameConstantsAddress(this.FrameIndex));
        fixed(D3D12_VIEWPORT*PViewport=&this.Viewport)
        fixed(RECT*PScissorRect=&this.ScissorRect){
            this.CommandList->RSSetViewports(1,PViewport);
            this.CommandList->RSSetScissorRects(1,PScissorRect);
        }
        if(this.OverlayTriangleVertexCount>0){
            D3D12_VERTEX_BUFFER_VIEW View=new(){
                BufferLocation=BaseAddress,
                SizeInBytes=checked(
                    (uint)(this.OverlayTriangleVertexCount*Stride)),
                StrideInBytes=(uint)Stride,
            };
            this.CommandList->SetPipelineState(
                this.OverlayTrianglePipelineState);
            this.CommandList->IASetPrimitiveTopology(
                D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            this.CommandList->IASetVertexBuffers(0,1,&View);
            this.CommandList->DrawInstanced(
                (uint)this.OverlayTriangleVertexCount,1,0,0);
        }
        if(this.OverlayLineVertexCount>0){
            D3D12_VERTEX_BUFFER_VIEW View=new(){
                BufferLocation=BaseAddress+
                    (ulong)(this.OverlayTriangleVertexCount*Stride),
                SizeInBytes=checked(
                    (uint)(this.OverlayLineVertexCount*Stride)),
                StrideInBytes=(uint)Stride,
            };
            this.CommandList->SetPipelineState(this.OverlayLinePipelineState);
            this.CommandList->IASetPrimitiveTopology(
                D3D_PRIMITIVE_TOPOLOGY_LINELIST);
            this.CommandList->IASetVertexBuffers(0,1,&View);
            this.CommandList->DrawInstanced(
                (uint)this.OverlayLineVertexCount,1,0,0);
        }
        if(MarkerCount>0){
            // Each marker is drawn inside its observer preview with that preview's constants (observer view-projection).
            this.CommandList->SetPipelineState(this.OverlayLinePipelineState);
            this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_LINELIST);
            for(int PreviewIndex=0;PreviewIndex<this.CameraPreviewCount;PreviewIndex++){
                RenderCameraPreview Preview=this.EffectiveCameraPreviews[PreviewIndex];
                if(!IsMarkedObserver(Preview)){ continue; }
                D3D12_VERTEX_BUFFER_VIEW View=new(){
                    BufferLocation=BaseAddress+(ulong)((MaxOverlayVertices+PreviewIndex*ObserverMarkerVertexCount)*Stride),
                    SizeInBytes=(uint)(ObserverMarkerVertexCount*Stride),
                    StrideInBytes=(uint)Stride,
                };
                RECT Rect=new(){ left=Preview.X,top=Preview.Y,right=Preview.X+Preview.Width,bottom=Preview.Y+Preview.Height };
                D3D12_VIEWPORT Viewport=new(){ TopLeftX=Preview.X,TopLeftY=Preview.Y,Width=Preview.Width,Height=Preview.Height,MinDepth=0f,MaxDepth=1f };
                this.CommandList->SetGraphicsRootConstantBufferView(0,this.GetFrameConstantsAddress(this.FrameIndex,PreviewIndex+1));
                this.CommandList->RSSetViewports(1,&Viewport);
                this.CommandList->RSSetScissorRects(1,&Rect);
                this.CommandList->IASetVertexBuffers(0,1,&View);
                this.CommandList->DrawInstanced(ObserverMarkerVertexCount,1,0,0);
            }
        }
    }

    private bool IsMarkedObserver(in RenderCameraPreview Preview)=>
        Preview.Depth.Encoding==RenderDepthEncoding.Observer&&Preview.MarkRadialCenter&&this.HasCameraBasis;

    /// <summary>
    /// Writes the centre-camera marker of every marked observer preview into its overlay slot: the main camera's view
    /// frustum with its apex at the compressed world's centre, 5 % of that world's radius long, in observer-relative
    /// coordinates. Returns how many markers were written.
    /// </summary>
    private int PrepareObserverMarkers(){
        int Count=0;
        int Stride=Unsafe.SizeOf<OverlayVertexGpu>();
        byte*Destination=this.OverlayVertexMapped+((nuint)this.FrameIndex*(nuint)OverlayFrameVertexCapacity*(nuint)Stride);
        Vector4 Color=new(1f,0.85f,0.1f,1f);
        Span<Vector3>Corners=stackalloc Vector3[4];
        for(int PreviewIndex=0;PreviewIndex<this.CameraPreviewCount;PreviewIndex++){
            RenderCameraPreview Preview=this.EffectiveCameraPreviews[PreviewIndex];
            if(!IsMarkedObserver(Preview)){ continue; }
            float Length=0.05f*Preview.Depth.Projection.ObservedWorldRadius;
            Vector3 Apex=new Vector3(Preview.Depth.Packet1.X,Preview.Depth.Packet1.Y,Preview.Depth.Packet1.Z)-Preview.CameraPosition;
            Vector3 HalfUp=this.CurrentCameraUp*(Length*this.CurrentCameraTanHalfFovY);
            Vector3 HalfRight=this.CurrentCameraRight*(Length*this.CurrentCameraTanHalfFovY*this.CurrentCameraAspectRatio);
            Vector3 BaseCenter=Apex+this.CurrentCameraForward*Length;
            Corners[0]=BaseCenter-HalfRight-HalfUp;
            Corners[1]=BaseCenter+HalfRight-HalfUp;
            Corners[2]=BaseCenter+HalfRight+HalfUp;
            Corners[3]=BaseCenter-HalfRight+HalfUp;
            int Output=MaxOverlayVertices+PreviewIndex*ObserverMarkerVertexCount;
            for(int Corner=0;Corner<4;Corner++){
                WriteOverlayVertex(Destination,Stride,Output++,new RenderOverlayVertex(Apex,Color));
                WriteOverlayVertex(Destination,Stride,Output++,new RenderOverlayVertex(Corners[Corner],Color));
                WriteOverlayVertex(Destination,Stride,Output++,new RenderOverlayVertex(Corners[Corner],Color));
                WriteOverlayVertex(Destination,Stride,Output++,new RenderOverlayVertex(Corners[(Corner+1)%4],Color));
            }
            Count++;
        }
        return Count;
    }

    private void PrepareCameraPreviewConstants(uint FrameIndex){
        this.RefreshCameraPreviewDiagnostics();
        byte*Source=this.FrameSlot(FrameIndex);
        for(int PreviewIndex=0;PreviewIndex<this.CameraPreviewCount;PreviewIndex++){
            RenderCameraPreview Preview=this.EffectiveCameraPreviews[PreviewIndex];
            byte*Destination=this.FrameSlot(FrameIndex,PreviewIndex+1);
            Unsafe.CopyBlock(Destination,Source,FrameConstantsSize);
            Matrix4x4 ViewProjection=Preview.ViewProjection;
            Unsafe.CopyBlock(
                Destination+FrameViewProjectionOffset,&ViewProjection,
                (uint)sizeof(Matrix4x4));
            Vector4 CameraPosition=new(Preview.CameraPosition,0f);
            Unsafe.CopyBlock(
                Destination+FrameCameraPositionOffset,&CameraPosition,
                (uint)sizeof(Vector4));
            // Forward+ tile lists were prepared for the primary camera. Preview passes retain
            // directional/ambient light but disable local lights rather than sampling a stale
            // camera-dependent active set.
            int*LightCounts=(int*)(Destination+FrameLightCountsOffset);
            LightCounts[0]=0;
            // Screen-space occlusion and the Lumen outputs belong to the primary camera's pixels.
            ((float*)(Destination+FrameAmbientOcclusionOffset))[0]=0f;
            *(Vector4*)(Destination+FrameLumenParamsOffset)=Vector4.Zero;
            var Depth=Preview.Depth;
            var DepthEncoding0=Depth.Packet0;
            var DepthEncoding1=Depth.Packet1;
            Unsafe.CopyBlock(Destination+FrameDepthEncoding0Offset,&DepthEncoding0,(uint)sizeof(Vector4));
            Unsafe.CopyBlock(Destination+FrameDepthEncoding1Offset,&DepthEncoding1,(uint)sizeof(Vector4));
        }
    }

    private void DrawObjects(int ExcludedObjectIndex){
        for(int ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            if(ObjectIndex==ExcludedObjectIndex||!this.ObjectActive[ObjectIndex]||this.ObjectHiddenFromView[ObjectIndex]){ continue; }
            int MeshIndex=this.ObjectMeshIndices[ObjectIndex];
            this.CommandList->SetGraphicsRootConstantBufferView(
                1,this.GetObjectConstantsAddress(this.FrameIndex,ObjectIndex));
            this.CommandList->SetGraphicsRootDescriptorTable(
                2,this.GetMaterialDescriptorTable(
                    this.ObjectMaterialIndices[ObjectIndex]));
            fixed(D3D12_VERTEX_BUFFER_VIEW*PVertexBufferView=
                &this.VertexBufferViews[MeshIndex])
            fixed(D3D12_INDEX_BUFFER_VIEW*PIndexBufferView=
                &this.IndexBufferViews[MeshIndex]){
                this.CommandList->IASetVertexBuffers(0,1,PVertexBufferView);
                this.CommandList->IASetIndexBuffer(PIndexBufferView);
                this.CommandList->DrawIndexedInstanced(
                    this.IndexCounts[MeshIndex],1,0,0,0);
            }
        }
    }

    private void RenderCameraPreviews(
        D3D12_CPU_DESCRIPTOR_HANDLE HdrRtvHandle,
        D3D12_CPU_DESCRIPTOR_HANDLE DsvHandle){
        if(this.CameraPreviewCount==0){ return; }
        var Wireframe=this.PresentationMode==RenderPresentationMode.Wireframe;
        Vector4 ClearColor=ClearColorVector;
        for(int PreviewIndex=0;PreviewIndex<this.CameraPreviewCount;PreviewIndex++){
            RenderCameraPreview Preview=this.EffectiveCameraPreviews[PreviewIndex];
            ID3D12PipelineState*Pipeline=Preview.Depth.Encoding==RenderDepthEncoding.Observer
                ?Wireframe?this.ObserverWireframePipelineState:this.ObserverPipelineState
                :Wireframe?this.CameraPreviewWireframePipelineState:this.CameraPreviewPipelineState;
            RECT Rect=new(){
                left=Preview.X,top=Preview.Y,
                right=Preview.X+Preview.Width,bottom=Preview.Y+Preview.Height,
            };
            D3D12_VIEWPORT Viewport=new(){
                TopLeftX=Preview.X,TopLeftY=Preview.Y,
                Width=Preview.Width,Height=Preview.Height,
                MinDepth=0f,MaxDepth=1f,
            };
            this.CommandList->ClearRenderTargetView(
                HdrRtvHandle,(float*)&ClearColor,1,&Rect);
            this.CommandList->ClearDepthStencilView(
                DsvHandle,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,
                1f,0,1,&Rect);
            this.CommandList->SetPipelineState(Pipeline);
            this.CommandList->SetGraphicsRootConstantBufferView(
                0,this.GetFrameConstantsAddress(
                    this.FrameIndex,PreviewIndex+1));
            this.CommandList->RSSetViewports(1,&Viewport);
            this.CommandList->RSSetScissorRects(1,&Rect);
            this.DrawObjects(Preview.ExcludedObjectIndex);
        }
    }

    /// <summary>Sets the skin palette (inverse bind * joint model per joint, row-vector convention) of a skinned mesh from the next frame on.</summary>
    public void SetSkinPalette(int MeshIndex,ReadOnlySpan<Matrix4x4>Palette){
        this.ThrowIfDisposed();
        this.Skinning!.SetPalette(MeshIndex,Palette);
    }

    /// <summary>
    /// Test-only: from the next frame on, brackets the main pass's draws with GPU timestamps so performance evidence
    /// measures GPU time rather than presentation pacing (M0099).
    /// </summary>
    internal void EnableMainPassTimingForTesting(){
        this.ThrowIfDisposed();
        if(this.MainPassTimestampHeap!=null){ return; }
        D3D12_QUERY_HEAP_DESC Desc=new(){ Type=D3D12_QUERY_HEAP_TYPE.D3D12_QUERY_HEAP_TYPE_TIMESTAMP,Count=FrameCount*2 };
        ID3D12QueryHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateQueryHeap(&Desc,__uuidof<ID3D12QueryHeap>(),(void**)&Heap),
            "ID3D12Device::CreateQueryHeap","creating the main-pass timestamp heap");
        D3D12_HEAP_PROPERTIES ReadbackHeap=new(D3D12_HEAP_TYPE_READBACK);
        var BufferDesc=D3D12_RESOURCE_DESC.Buffer(FrameCount*2*sizeof(ulong));
        ID3D12Resource*Readback;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&ReadbackHeap,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_COPY_DEST,null,__uuidof<ID3D12Resource>(),(void**)&Readback),
            "ID3D12Device::CreateCommittedResource","creating the main-pass timestamp readback");
        ulong Frequency;
        D3D12Diagnostics.EnsureSuccess(this.Device.CommandQueue->GetTimestampFrequency(&Frequency),"ID3D12CommandQueue::GetTimestampFrequency","reading the timestamp frequency");
        this.MainPassTimestampHeap=Heap;
        this.MainPassTimestampReadback=Readback;
        this.MainPassTimestampFrequency=Frequency;
    }

    /// <summary>Test-only: GPU milliseconds of the last timed frame's main-pass draws, after the GPU is idle.</summary>
    internal float ReadMainPassGpuMillisecondsForTesting(){
        this.ThrowIfDisposed();
        if(this.MainPassTimestampReadback==null||this.LastTimedFrameIndex==uint.MaxValue){
            throw new InvalidOperationException("Main-pass timing is not enabled or no frame has been timed yet.");
        }
        this.WaitForGpuIdle();
        ulong*Mapped;
        D3D12_RANGE Range=new(){ Begin=(nuint)(this.LastTimedFrameIndex*2*sizeof(ulong)),End=(nuint)((this.LastTimedFrameIndex*2+2)*sizeof(ulong)) };
        D3D12Diagnostics.EnsureSuccess(this.MainPassTimestampReadback->Map(0,&Range,(void**)&Mapped),"ID3D12Resource::Map","reading the main-pass timestamps");
        try {
            var Start=Mapped[this.LastTimedFrameIndex*2];
            var End=Mapped[this.LastTimedFrameIndex*2+1];
            return End>=Start?(End-Start)*1000f/this.MainPassTimestampFrequency:0f;
        } finally {
            D3D12_RANGE NoWrite=new();
            this.MainPassTimestampReadback->Unmap(0,&NoWrite);
        }
    }

    /// <summary>Test-only copy of the main depth buffer after the GPU is idle (row-major, width x height).</summary>
    internal float[]ReadDepthForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        var (Data,RowPitch,Width,Height)=GpuTextureReadback.Read(this.Device!,this.DepthBuffer,D3D12_RESOURCE_STATE_DEPTH_WRITE);
        var Result=new float[Width*Height];
        fixed(byte*Bytes=Data){
            for(var Y=0;Y<Height;Y++){
                new ReadOnlySpan<float>(Bytes+Y*RowPitch,Width).CopyTo(Result.AsSpan(Y*Width,Width));
            }
        }
        return Result;
    }

    /// <summary>Test-only physical ray distances, preserved across previews (row-major).</summary>
    internal float[]ReadSceneDistancesForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        var (Data,RowPitch,Width,Height)=GpuTextureReadback.Read(this.Device!,this.SceneDistanceBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET);
        var Result=new float[Width*Height];
        fixed(byte*Bytes=Data){
            for(var Y=0;Y<Height;Y++){
                new ReadOnlySpan<float>(Bytes+Y*RowPitch,Width).CopyTo(Result.AsSpan(Y*Width,Width));
            }
        }
        return Result;
    }

    /// <summary>Test-only copy of a skinned mesh's current vertices (position, normal, tangent, uv; 48 bytes each).</summary>
    internal byte[]ReadSkinnedVerticesForTesting(int MeshIndex){
        this.WaitForGpuIdle();
        return this.Skinning!.ReadSkinnedVerticesForTesting(MeshIndex);
    }

    public void RenderFrame(bool CaptureForVerification=false){
        this.ThrowIfDisposed();
        this.TryReloadShaderArtifact();
        if(this.SizeIsZero){
            return;
        }

        var Radial=this.CurrentViewDepth.Encoding==RenderDepthEncoding.Logarithmic;
        ID3D12PipelineState*ActivePipelineState=this.PresentationMode==RenderPresentationMode.Wireframe
            ?this.WireframePipelineState
            :this.PresentationMode!=RenderPresentationMode.Lit||this.SelectedObjectCount>0?this.PresentationPipelineState:this.PipelineState;
        ID3D12CommandAllocator*Allocator= this.CommandAllocators[this.FrameIndex];
        D3D12Diagnostics.EnsureSuccess(Allocator->Reset(),"ID3D12CommandAllocator::Reset","resetting the frame command allocator");
        D3D12Diagnostics.EnsureSuccess(this.CommandList->Reset(Allocator,ActivePipelineState),"ID3D12GraphicsCommandList::Reset","resetting the graphics command list");
        if(this.Skinning is{ HasMeshes:true }){
            this.Skinning.Record(this.CommandList,this.FrameIndex);
            this.CommandList->SetPipelineState(ActivePipelineState);
        }
        this.LumenActiveThisFrame=this.LumenWanted;
        if(!this.LumenActiveThisFrame){
            this.Lumen!.InvalidateHistory();
        }
        this.Atmosphere!.SkyRadianceMapRequested=this.LumenActiveThisFrame;
        this.PrepareDirectionalLights(this.FrameIndex);
        this.PrepareForwardPlusLocalLights(this.FrameIndex);

        this.PrepareLocalShadowAtlasProjections(this.FrameIndex);
        this.RenderShadowAtlasProjections();
        this.WriteAmbientLight(this.FrameIndex,this.CurrentAmbientColor);
        this.WriteFog(this.FrameIndex,this.CurrentFog);
        this.WriteViewDepth(this.FrameIndex);
        if(Radial){
            this.WriteRadialRasterDepths(this.FrameIndex);
        }
        this.PrepareCameraPreviewConstants(this.FrameIndex);
        this.WriteExposure(this.FrameIndex,this.CurrentCameraEv100);
        this.PrepareEnvironment(this.FrameIndex);
        var AutoExposureDeltaSeconds=this.NextAutoExposureDeltaSeconds();
        if(!this.AutoExposureActive){
            this.AutoExposureInitialized=false;
        } else if(!this.AutoExposureInitialized){
            this.AutoExposureStage!.RecordInitialize(this.CommandList,this.EnvironmentSettings.Exposure);
            this.AutoExposureInitialized=true;
        }
        this.UpdateGlobalDistanceField();
        this.RenderGlobalIlluminationProbes();
        this.RenderLumen();
        this.RenderAmbientOcclusion();

        ID3D12Resource*BackBuffer= this.BackBuffers[this.FrameIndex];

        D3D12_RESOURCE_BARRIER ToHdrRenderTarget=D3D12_RESOURCE_BARRIER.InitTransition(this.HdrColorBuffer,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
        this.CommandList->ResourceBarrier(1,&ToHdrRenderTarget);

        D3D12_CPU_DESCRIPTOR_HANDLE HdrRtvHandle=new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),FrameCount, this.RtvDescriptorSize);
        D3D12_CPU_DESCRIPTOR_HANDLE DsvHandle= this.DsvHeap->GetCPUDescriptorHandleForHeapStart();
        this.CommandList->OMSetRenderTargets(2,&HdrRtvHandle,TRUE,&DsvHandle);
        D3D12_CPU_DESCRIPTOR_HANDLE DistanceRtv=new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),FrameCount+1,this.RtvDescriptorSize);
        var ClearDistance=Vector4.Zero;
        this.CommandList->ClearRenderTargetView(DistanceRtv,(float*)&ClearDistance,0,null);

        Vector4 ClearColor=ClearColorVector;
        this.CommandList->ClearRenderTargetView(HdrRtvHandle,(float*)&ClearColor,0,null);
        this.CommandList->ClearDepthStencilView(DsvHandle,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,1.0f,0,0,null);

        this.BindMainPassState(ActivePipelineState);

        fixed(D3D12_VIEWPORT*PViewport=&this.Viewport)
        fixed(RECT*PScissorRect=&this.ScissorRect){
            this.CommandList->RSSetViewports(1,PViewport);
            this.CommandList->RSSetScissorRects(1,PScissorRect);
        }

        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

        if(this.MainPassTimestampHeap!=null){
            this.CommandList->EndQuery(this.MainPassTimestampHeap,D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP,this.FrameIndex*2);
        }
        this.DrawObjects(ExcludedObjectIndex:-1);
        if(this.MainPassTimestampHeap!=null){
            this.CommandList->EndQuery(this.MainPassTimestampHeap,D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP,this.FrameIndex*2+1);
            this.CommandList->ResolveQueryData(this.MainPassTimestampHeap,D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP,
                this.FrameIndex*2,2,this.MainPassTimestampReadback,(ulong)this.FrameIndex*2*sizeof(ulong));
            this.LastTimedFrameIndex=this.FrameIndex;
        }
        var RanComputeAfterMainPass=false;
        if(this.EnvironmentSettings.Atmosphere is not null){
            this.Atmosphere!.CompositeSky(this.CommandList,this.FrameIndex,this.CurrentAtmosphereFrame,this.SceneDistanceBuffer,this.HdrColorBuffer,this.Width,this.Height,this.AutoExposureActive);
            RanComputeAfterMainPass=true;
        }
        if(this.AutoExposureActive){
            // Metered before camera previews draw into the same target.
            this.AutoExposureStage!.RecordMetering(this.CommandList,this.FrameIndex,this.HdrColorBuffer,this.Width,this.Height,this.EnvironmentSettings.Exposure,AutoExposureDeltaSeconds);
            RanComputeAfterMainPass=true;
        }
        if(this.LumenActiveThisFrame){
            // M0111: the lit frame (sky included, before the previews) is next frame's screen traces' colour.
            this.Lumen!.CopySceneColor(this.CommandList,this.HdrColorBuffer);
            if(this.LumenVisualization!=LumenVisualization.None){
                this.Lumen.RecordVisualize(this.CommandList,this.FrameIndex,this.LumenVisualization,this.HdrColorBuffer);
                RanComputeAfterMainPass=true;
            }
        }
        if(RanComputeAfterMainPass){
            // Compute replaces the graphics PSO and descriptor heap, so the main-pass bindings are
            // restored before camera previews draw with the Stage pipeline again.
            this.BindMainPassState(ActivePipelineState);
            fixed(D3D12_VIEWPORT*PViewport=&this.Viewport)
            fixed(RECT*PScissorRect=&this.ScissorRect){
                this.CommandList->RSSetViewports(1,PViewport);
                this.CommandList->RSSetScissorRects(1,PScissorRect);
            }
        }
        this.RenderCameraPreviews(HdrRtvHandle,DsvHandle);

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
        float*PostProcessConstants=stackalloc float[4]{
            this.EnvironmentSettings.ToneMapper==ToneMapper.UnrealFilmicSrgb?1f:0f,
            this.EnvironmentSettings.BloomIntensity,0f,0f,
        };
        this.CommandList->SetGraphicsRoot32BitConstants(1,4,PostProcessConstants,0);
        fixed(D3D12_VIEWPORT*PViewport=&this.Viewport)
        fixed(RECT*PScissorRect=&this.ScissorRect){
            this.CommandList->RSSetViewports(1,PViewport);
            this.CommandList->RSSetScissorRects(1,PScissorRect);
        }

        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        this.CommandList->DrawInstanced(3,1,0,0);
        this.RenderOverlay();
        this.DrawScreenLayer();

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
        this.LumenActiveLastFrame=this.LumenActiveThisFrame;

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

        try {
            this.WaitForGpuIdle();
        } catch(D3D12Exception){
            // A removed/reset device cannot signal its fence. Native objects still have to be
            // released so the Render Server can create the next generation.
        }

        this.ReleaseReadbackBuffer();
        this.ReleaseForwardPlusTileBuffer();
        this.Atmosphere?.Dispose();
        this.Atmosphere=null;
        this.GlobalIllumination?.Dispose();
        this.GlobalIllumination=null;
        this.AmbientOcclusion?.Dispose();
        this.AmbientOcclusion=null;
        this.AutoExposureStage?.Dispose();
        this.AutoExposureStage=null;
        this.Skinning?.Dispose();
        this.Skinning=null;
        this.GlobalDistanceField?.Dispose();
        this.GlobalDistanceField=null;
        this.Lumen?.Dispose();
        this.Lumen=null;
        if(this.LumenSurfacePipelineState!=null){
            _=this.LumenSurfacePipelineState->Release();
            this.LumenSurfacePipelineState=null;
        }
        if(this.LumenCardPipelineState!=null){
            _=this.LumenCardPipelineState->Release();
            this.LumenCardPipelineState=null;
        }

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
        this.ReleaseScreenResources();
        if(this.OverlayVertexBuffer!=null){
            this.OverlayVertexBuffer->Unmap(0,null);
            this.OverlayVertexMapped=null;
            _=this.OverlayVertexBuffer->Release();
            this.OverlayVertexBuffer=null;
        }

        if(this.ShadowConstantsBuffer!=null){
            this.ShadowConstantsBuffer->Unmap(0,null);
            this.ShadowConstantsMapped=null;
            _= this.ShadowConstantsBuffer->Release();
            this.ShadowConstantsBuffer=null;
        }

        if(this.SceneDistanceBuffer!=null){
            _=this.SceneDistanceBuffer->Release();
            this.SceneDistanceBuffer=null;
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

        this.ReleaseOwnedMaterialTextures();
        this.MaterialTextures=[];

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

        if(this.ObserverPipelineState!=null){ _=this.ObserverPipelineState->Release();this.ObserverPipelineState=null; }
        if(this.ObserverWireframePipelineState!=null){ _=this.ObserverWireframePipelineState->Release();this.ObserverWireframePipelineState=null; }
        if(this.PipelineState!=null){
            _= this.PipelineState->Release();
            this.PipelineState=null;
        }
        if(this.PresentationPipelineState!=null){
            _=this.PresentationPipelineState->Release();
            this.PresentationPipelineState=null;
        }
        if(this.WireframePipelineState!=null){
            _=this.WireframePipelineState->Release();
            this.WireframePipelineState=null;
        }
        if(this.CameraPreviewPipelineState!=null){
            _=this.CameraPreviewPipelineState->Release();
            this.CameraPreviewPipelineState=null;
        }
        if(this.CameraPreviewWireframePipelineState!=null){
            _=this.CameraPreviewWireframePipelineState->Release();
            this.CameraPreviewWireframePipelineState=null;
        }
        if(this.OverlayTrianglePipelineState!=null){
            _=this.OverlayTrianglePipelineState->Release();
            this.OverlayTrianglePipelineState=null;
        }
        if(this.OverlayLinePipelineState!=null){
            _=this.OverlayLinePipelineState->Release();
            this.OverlayLinePipelineState=null;
        }
        if(this.MainPassTimestampHeap!=null){
            _=this.MainPassTimestampHeap->Release();
            this.MainPassTimestampHeap=null;
        }
        if(this.MainPassTimestampReadback!=null){
            _=this.MainPassTimestampReadback->Release();
            this.MainPassTimestampReadback=null;
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
