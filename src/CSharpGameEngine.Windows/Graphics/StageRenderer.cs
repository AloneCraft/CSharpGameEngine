using CSharpGameEngine.Rendering.Backend;
using CSharpGameEngine.Rendering.View;
using static CSharpGameEngine.Rendering.Pipeline.StageGpuLayouts;
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

/// <summary>
/// 従来方式（ADR-0018以前）の非テクスチャAPI用。ソリッドなベースカラーを持つ、プロシージャルまたはインポート済みメッシュ1つ。
/// <see cref="StageRenderer.Create(nint, int, int, GraphicsDeviceOptions, string, IReadOnlyList{MeshCreateInfo})"/>
/// の入力形状としてのみ残し、各項目を<see cref="StageMeshCreateInfo"/>＋<see cref="StageMaterialCreateInfo"/>＋
/// <see cref="StageObjectCreateInfo"/>の組へ変換する。
/// </summary>
public readonly struct MeshCreateInfo {
    public required IReadOnlyList<Vector3>Positions { get; init;}
    public required IReadOnlyList<Vector3>Normals { get; init;}
    public required IReadOnlyList<uint>Indices { get; init;}
    public required Vector4 BaseColorFactor { get; init;}
}

/// <summary>
/// テクスチャ付きメッシュを変換してネイティブの対数Stageパイプラインで描画する。物理ライティング、影、環境効果、プレゼンテーションに対応する。
/// 単一の所有者が、インポート済み／プロシージャルジオメトリのスワップチェーン、フレームリソース、ホットリロード、リサイズを管理する。
/// </summary>
public sealed unsafe partial class StageRenderer:IDisposable {
    public const int FrameCount=2;
    private const int CameraPassCount=1+RenderCameraPreview.MaximumCount;
    public const DXGI_FORMAT BackBufferFormat=DXGI_FORMAT_R8G8B8A8_UNORM;
    public const DXGI_FORMAT DepthBufferFormat=DXGI_FORMAT_D32_FLOAT;
    private const DXGI_FORMAT HdrColorFormat=DXGI_FORMAT_R16G16B16A16_FLOAT;
    private const DXGI_FORMAT MaterialTextureFormat=DXGI_FORMAT_R8G8B8A8_UNORM;
    /// <summary>
    /// マテリアルのテクスチャは型なしで確保し、各スロットに必要なビューから同じ常駐テクスチャを読み取れるようにする。
    /// glTFのベースカラーと発光はsRGBエンコード、法線とmetallic-roughnessはリニア値である。
    /// </summary>
    private const DXGI_FORMAT MaterialTextureResourceFormat=DXGI_FORMAT_R8G8B8A8_TYPELESS;
    private const DXGI_FORMAT MaterialColorTextureViewFormat=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    private const float GltfImportEmissiveDefaultNits=100f;
    /// <summary>マテリアルサンプラーの異方性（M0110、Unrealのr.MaxAnisotropy既定値）。</summary>
    internal const uint MaterialMaxAnisotropy=8;
    private const DXGI_FORMAT ShadowAtlasResourceFormat=DXGI_FORMAT_R32_TYPELESS;
    private const DXGI_FORMAT ShadowAtlasDsvFormat=DXGI_FORMAT_D32_FLOAT;
    private const DXGI_FORMAT ShadowAtlasSrvFormat=DXGI_FORMAT_R32_FLOAT;
    public const int ShadowAtlasSizePixels=PhysicalShadowAtlasPlan.AtlasSizePixels;
    public const int ShadowAtlasTileSizePixels=PhysicalShadowAtlasPlan.TileSizePixels;
    public const int DirectionalShadowAtlasTileIndex=PhysicalShadowAtlasPlan.DirectionalTileIndex;

    /// <summary>D3D12の定数バッファビューは256バイト境界にそろったGPUアドレスから始める必要がある。</summary>
    private const uint ConstantAlignment=512;

    /// <summary>1フレームに受け付ける加算式ディレクショナルライトの最大数。StageシェーダーのABIと一致させる。</summary>
    public const int MaxDirectionalLights=4;

    /// <summary>ポイントライトとスポットライトの送信を合わせた、1フレームに受け付けるローカルライトの最大数。</summary>
    public const int MaxLocalLights=1024;
    public const int MaxOverlayVertices=32_768;
    /// <summary>観測者プレビューの上に描画する中央カメラマーカーの線頂点（M0099）。8本の線からなる視錐台。</summary>
    private const int ObserverMarkerVertexCount=16;
    /// <summary>フレームスロットごとのオーバーレイ頂点数。呼び出し側のオーバーレイに、カメラプレビューごとに1つのマーカーを加える。</summary>
    private const int OverlayFrameVertexCapacity=MaxOverlayVertices+ObserverMarkerVertexCount*RenderCameraPreview.MaximumCount;

    /// <summary>ソース互換性のため残している従来の公開上限。現在はポイントライトとスポットライトで1024ライトのStructuredBufferを共有する。</summary>
    public const int MaxPointLights=MaxLocalLights;

    /// <summary>ソース互換性のため残している従来の公開上限。現在はポイントライトとスポットライトで1024ライトのStructuredBufferを共有する。</summary>
    public const int MaxSpotLights=MaxLocalLights;

    /// <summary>Forward+のスクリーンスペースタイルの1辺の長さ（物理ピクセル）。</summary>
    public const int ForwardPlusTileSizePixels=16;

    /// <summary>Forward+の各タイルが評価するローカルライト候補の最大数。</summary>
    public const int MaxLightsPerForwardPlusTile=64;

    /// <summary>アトラス計画が選んだ物理Pointライト1つに用いる、90度の投影6面。</summary>
    public const int MaxPointShadowProjections=
        PhysicalShadowAtlasPlan.PointLightBudget*PhysicalShadowAtlasPlan.PointTilesPerLight;

    /// <summary>物理アトラス計画で選択する、投影対象スポットライトの最大数。</summary>
    public const int MaxSpotShadowProjections=PhysicalShadowAtlasPlan.SpotTileBudget;

    /// <summary>物理アトラス予算を共有する、投影対象Area／Emissiveライトの最大数。</summary>
    public const int MaxAreaAndEmissiveShadowProjections=
        PhysicalShadowAtlasPlan.AreaAndEmissiveTileBudget;

    private const uint DirectionalLightStride=32; // float4を2つ。StageシェーダーABIのDirectionalLightDataを参照。
    private const uint LocalLightStride=96; // float4を6つ。StageシェーダーABIのLocalLightDataを参照。
    private const uint PointShadowProjectionStride=80; // float4x4とfloat4。StageシェーダーABIを参照。
    private const uint SpotShadowProjectionStride=80; // float4x4とfloat4。StageシェーダーABIを参照。
    private const uint AreaShadowMetadataStride=16; // float4を1つ。StageシェーダーABIを参照。
    private const uint AreaShadowProjectionStride=80; // float4x4とfloat4。StageシェーダーABIを参照。
    private const int PointShadowProjectionConstantSlotOffset=1+MaxSpotShadowProjections;
    private const int AreaShadowProjectionConstantSlotOffset=
        PointShadowProjectionConstantSlotOffset+MaxPointShadowProjections;
    /// <summary>Cascade 0はslot 0を使う。Cascade 1～3は各ローカルライト投影スロットの後に続く。</summary>
    private const int CascadeShadowConstantSlotOffset=
        AreaShadowProjectionConstantSlotOffset+MaxAreaAndEmissiveShadowProjections;
    private const int ShadowAtlasProjectionConstantSlotCount=
        CascadeShadowConstantSlotOffset+DirectionalShadowCascades.Count-1;
    private const uint ForwardPlusTileRecordUIntCount=1u+(uint)MaxLightsPerForwardPlusTile;
    private const uint ForwardPlusTileRecordStride=sizeof(uint)*ForwardPlusTileRecordUIntCount;

    /// <summary>
    /// FrameConstantsBuffer専用のフレームスロット幅。FrameConstantsSize以上、かつD3D12のroot CBV
    /// アライメント要件(256バイト)の倍数でなければならない。汎用のConstantAlignmentを流用せず
    /// FrameConstantsSizeから計算するのは、FrameCBを将来また拡張したとき(ShadowViewProjection追加時に
    /// スロット幅の更新を忘れ、フレームスロット間でデータが衝突・範囲外書き込みを起こしていた実際の
    /// 回帰の再発を防ぐため)にスロット幅が自動的に追従するようにするため。
    /// </summary>
    private static readonly uint FrameConstantsStride=((FrameConstantsSize+255u)/256u)*256u;

    // HDRクリア値には、公開されるLDRのClearColorに対応する逆ACES値を使う。これにより、
    // 新しいポストプロセスパスでも空シーンの背景を安定させつつ、ジオメトリには
    // HDRライティングとブルームの利点を保てる。
    private static readonly Vector4 ClearColorVector=StageRenderDefaults.HdrClearColor;
    public static readonly PixelColor ClearColor=StageRenderDefaults.ClearColor;
    /// <summary>メインパスとカメラプレビューのクリア色。環境設定に背景色があればそれを、なければ従来の <see cref="ClearColorVector"/> を使う（インスタンスごとに独立する）。</summary>
    private Vector4 BackgroundClearVector=>this.EnvironmentSettings.Background is{ }Color?StageRenderDefaults.HdrFromDisplay(Color):ClearColorVector;

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

    /// <summary>シャドウ優先度とは独立して直接照明を順位付けする、遅延ディレクショナルライト送信データ。</summary>
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

    /// <summary>CPU側に保持する遅延ローカルライト送信データ。フレームのカメラが確定してから一度だけ順位を決める。</summary>
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

    /// <summary>StageシェーダーABIの6要素float4 LocalLightDataと完全に一致する。</summary>
    private struct LocalLightGpuData {
        public Vector4 PositionAndRange;
        public Vector4 DirectionAndInnerCos;
        public Vector4 ColorAndOuterCos;
        public Vector4 TypeAndPadding;
        public Vector4 RightAndHalfWidth;
        public Vector4 UpAndHalfHeight;
    }

    /// <summary>Pointライトの各面に使う、行列とPhysicalShadowAtlasPlanのUV矩形レコード。StageシェーダーABIと一致する。</summary>
    private struct PointShadowProjectionGpuData {
        public Matrix4x4 ViewProjection;
        public Vector4 UvScaleOffset;
    }

    /// <summary>行列とPhysicalShadowAtlasPlanのUV矩形レコード。StageシェーダーABIと一致する。</summary>
    private struct SpotShadowProjectionGpuData {
        public Matrix4x4 ViewProjection;
        public Vector4 UvScaleOffset;
    }

    /// <summary>t9にあるArea／Emissive用PCSSの光源半径とクリップ距離レコード。StageシェーダーABIと一致する。</summary>
    private struct AreaShadowMetadataGpuData {
        public Vector4 SourceRadiusAndPadding;
    }

    /// <summary>t10にあるArea／Emissive用の独立した中心投影レコード。StageシェーダーABIと一致する。</summary>
    private struct AreaShadowProjectionGpuData {
        public Matrix4x4 ViewProjection;
        public Vector4 UvScaleOffset;
    }

    /// <summary>アトラス投影1面分のレンダーターゲットと、シャドウ専用定数バッファのスロット。</summary>
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
    /// <summary>カメラの回転と、放射状投影用の有限な補助平面（放射状モードのみ）。</summary>
    private Matrix4x4 CurrentAuxiliaryViewProjection=Matrix4x4.Identity;
    /// <summary>放射状メインパスのビュー投影。カメラの投影に保守的な深度行を加える（放射状モードのみ）。</summary>
    /// <summary>最後の<see cref="SetCamera"/>で設定したカメラ基底と視野角。観測者マーカーに使う。</summary>
    private bool HasCameraBasis;
    /// <summary>メインパスの描画前後で取得するテスト専用GPUタイムスタンプ（性能の証拠）。有効化されていなければnull。</summary>
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

    // メッシュ単位（meshIndexでインデックス化）。
    private int MeshCount;
    private ID3D12Resource*[]VertexBuffers=[];
    private D3D12_VERTEX_BUFFER_VIEW[]VertexBufferViews=[];
    private ID3D12Resource*[]IndexBuffers=[];
    private D3D12_INDEX_BUFFER_VIEW[]IndexBufferViews=[];
    private uint[]IndexCounts=[];

    // マテリアル単位（materialIndexでインデックス化）。4つのマテリアルテクスチャと共有シャドウSRVを持つ。
    private int MaterialCount;
    /// <summary>
    /// ディスクリプタヒープが収容できるマテリアル数。作成済みのものに<see cref="DynamicMaterialHeadroom"/>を加えた容量で、後から
    /// <see cref="AddMaterial"/>で追加できる（M0105）。
    /// </summary>
    private int MaterialCapacity;
    private ID3D12Resource*[]MaterialTextures=[];
    private readonly List<nint>OwnedMaterialTextures=[];
    /// <summary>コンテンツと色空間ごとのマテリアルテクスチャ（M0110：同じテクセルでもsRGBとリニアではミップの平均が異なる）。</summary>
    private readonly Dictionary<(Hash256 Hash,RenderTextureColorSpace ColorSpace),nint>ResidentTextures=[];
    private ID3D12DescriptorHeap*SrvHeap;
    private uint SrvDescriptorSize;
    private Vector4[]MaterialBaseColorFactors=[];
    private float[]MaterialRoughnessFactors=[];
    private float[]MaterialMetallicFactors=[];
    private Vector3[]MaterialEmissiveNits=[];
    private StageWorldGrid?[]MaterialWorldGrids=[];
    private Vector3[]MaterialWorldGridAnchors=[];

    // オブジェクト単位（objectIndexでインデックス化）。ObjectCountはこれまで割り当てたスロット数。オブジェクトを削除するとそのスロットは
    // 次のAddObjectで再利用する。ObjectCapacityは定数バッファに収容できる数（M0105）。
    private int ObjectCount;
    private int ObjectCapacity;
    private bool[]ObjectActive=[];
    /// <summary>カメラから見えないオブジェクト（UEのOwnerNoSee）。メインパス、GIプローブ、カメラプレビューでは省くが、影には使う（M0107）。</summary>
    private bool[]ObjectHiddenFromView=[];
    /// <summary>影を落とさないオブジェクト（UEでCastShadowを無効化）。シャドウパスから省く（M0107）。</summary>
    private bool[]ObjectNoShadow=[];
    /// <summary>各オブジェクトでマテリアルの発光に掛ける係数（ADR-0078/M0109。ブレーキライトなど、ゲーム実行中に変更するマテリアルのスカラー値）。既定値は1。</summary>
    private float[]ObjectEmissiveScales=[];
    private readonly List<int>FreeObjectSlots=[];
    private int[]ObjectMeshIndices=[];
    /// <summary>各メッシュの境界中心。オブジェクトの保守的な放射状ラスター深度が接触する位置（M0100）。</summary>
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

    // AddDirectionalLightTowardLight、ポイントライト、スポットライトの追加はフレームごとに有効。従来の
    // SetDirectionalLight単一設定値は、置き換えられるまでスワップチェーンの全フレームスロットへ反映する。
    // これは元のsetterの動作に合わせている。追加数はリセットするため、所有Entityを削除しても
    // ライトが残り続けない。ライトが送信されるまでは照明しない。InitializeConstantsToDefaultsが書く既定ライトを1にすると、
    // 最初のフレームだけ照明され、
    // フレーム末のリセット後はライトが消えて、1回のpresent後にシーンが暗転してしまう。
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

    /// <summary>4灯の上限を超えたDirectional送信数を表示する診断カウンター。</summary>
    public int DroppedDirectionalLightsThisFrame=>this.DroppedDirectionalLightsThisFrameValue;

    /// <summary>前フレームの送信時に記録したDirectionalの超過数。</summary>
    public int DroppedDirectionalLightsLastFrame { get;private set; }

    /// <summary>表示用診断カウンター。選択済みライトの直接照明は継続する。</summary>
    public int DroppedLocalLightsThisFrame=>this.DroppedLocalLightsThisFrameValue;

    /// <summary>前フレームの送信時に記録した超過数。</summary>
    public int DroppedLocalLightsLastFrame { get;private set; }

    /// <summary>今フレームに64灯／タイルの上限で除外した、候補参照の表示数。</summary>
    public int DroppedTileLightReferencesThisFrame=>this.DroppedTileLightReferencesThisFrameValue;

    /// <summary>前フレームの送信時に記録したタイルリストの超過数。</summary>
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

    public IReadOnlyList<RenderDeviceMessage>DebugMessages=> this.Device?.RenderMessages??[];

    public int ErrorOrWorseMessageCount=> this.Device?.ErrorOrWorseMessageCount??0;

    public static bool ShaderHotReloadEnabled=>ShaderArtifactMonitor.IsEnabled;

    public bool ShaderArtifactLoaded { get;private set;}

    public string ShaderSemanticHash { get;private set;}=string.Empty;

    public IReadOnlyList<string>ShaderArtifactDiagnostics { get;private set;}=[];

    public uint FramesRendered { get;private set;}
    public int ResidentMaterialTextureCount=>this.OwnedMaterialTextures.Count;
    /// <summary>レンダラー作成時の数を超えて<see cref="AddObject"/>で追加できるオブジェクト数（M0105）。</summary>
    public const int DynamicObjectHeadroom=StageRenderLimits.DynamicObjectHeadroom;
    /// <summary>レンダラー作成時の数を超えて<see cref="AddMaterial"/>で追加できるマテリアル数（M0105）。</summary>
    public const int DynamicMaterialHeadroom=StageRenderLimits.DynamicMaterialHeadroom;
    /// <summary><see cref="AddMaterial"/>が追加で受け付けるマテリアル数。</summary>
    public int FreeMaterialSlots=>this.MaterialCapacity-this.MaterialCount;
    /// <summary>現在描画中のオブジェクト数。</summary>
    public int ActiveObjectCount=>this.ObjectCount-this.FreeObjectSlots.Count;
    /// <summary>距離場を持つメッシュ数（ADR-0078/M0110）。</summary>
    public int DistanceFieldMeshCount=>this.GlobalDistanceField?.MeshesWithFields??0;
    /// <summary>直近フレームのグローバル距離場にあるオブジェクト数（距離場がなければ0）。</summary>
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

    /// <summary>雲のパノラマのテスト専用読み戻し（M0110）。GPUの完了を待つ。</summary>
    internal (Vector4[]Texels,int Width,int Height) ReadCloudPanoramaForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.Atmosphere!.ReadCloudPanoramaForTesting();
    }
    /// <summary>GPU自動露出状態（EV100、スケール、収束済み、有効）のテスト専用読み戻し。GPU完了を待つ。</summary>
    internal Vector4 ReadAutoExposureStateForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.AutoExposureStage!.ReadStateForTesting();
    }

    /// <summary>決定的テスト用の固定適応ステップ。nullならフレーム間の壁時計時間を計測する。</summary>
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
    /// 前の世代ですでに検証され、適用に成功したシェーダーパッケージから、ネイティブデバイスの世代を1つ作り直す。
    /// パスは以降のDebugホットリロード用としてのみ保持し、復旧時に再読み込みはしない。
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

    /// <summary>
    /// 従来方式のアダプター（ADR-0018以前）。オブジェクトごとに非テクスチャの単色メッシュを1つ割り当て、以前のメッシュ／マテリアル／オブジェクトの1対1インデックスを厳密に再現する。既存の呼び出し側
    /// と<c>StageGpuIntegrationTests</c>を変更せず動作させる。すべてのマテリアルが1x1の既定テクスチャへフォールバックするため、合成UV／接線の見た目への影響はない。
    /// </summary>
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
                Tangents[V]=StageMeshGeometry.ArbitraryTangentFor(Mesh.Normals[V]);
            }

            StageMeshes[I]=new StageMeshCreateInfo { Positions=Mesh.Positions,Normals=Mesh.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Mesh.Indices };
            StageMaterials[I]=new StageMaterialCreateInfo { BaseColorFactor=Mesh.BaseColorFactor,RoughnessFactor=1f,MetallicFactor=0f };
            StageObjects[I]=new StageObjectCreateInfo { MeshIndex=I,MaterialIndex=I };
        }

        return Create(Hwnd,Width,Height,DeviceOptions,ShaderArtifactPath,StageMeshes,StageMaterials,StageObjects);
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
        this.CreateVirtualShadows();

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
        // 深度読み戻し用には型なしリソースを使う。シーン効果は別の物理距離ターゲットを使う。
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

        // M0110：マテリアルテクスチャにはミップがある。異方性フィルタリング（Unrealの既定r.MaxAnisotropy 8）で斜めの面を鮮明に保つ。
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
    /// テクスチャ1つと定数4つを読むパスのルートシグネチャ。0 = ピクセルSRVテーブルt0、1 = 32ビット定数4つのb0、
    /// 線形クランプサンプラーs0。ポストプロセスパスではそのまま使い、スクリーンレイヤーでは入力アセンブラーのフラグを追加する。
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
            // 実際の裏面カリングは後で追加する（インポートメッシュの巻き順方針、ADR-0004）。
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            PsoDesc.RasterizerState.FillMode=FillMode;
            PsoDesc.RasterizerState.DepthClipEnable=DepthClip?TRUE:FALSE;
            PsoDesc.RTVFormats[0]=HdrColorFormat;
            // M0111：Lumenのサーフェスプリパスとカード撮影では複数のターゲットへ書き込む。
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
    /// 作成後にメッシュを1つ追加する（M0105）。ゲームでまだ使われていないアセットのEntityを新たに生成する場合に使う。
    /// バッファは個別のリソースなので、GPUが描画中のものには触れない。新しいメッシュインデックスを返す。
    /// スキニングメッシュ（M0107。レベル読み込み後に生成したキャラクター）もスキニングパスへ追加する。
    /// パレットリングが拡張され、処理中フレームがまだ読み取っている可能性があるため、その場合は一度GPUの完了を待つ。
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
            // すべてのパスでスキニング済みコピーを描画する。アップロードヒープのバッファには、スキニング元のレスト姿勢を保持する。
            this.VertexBufferViews[Index]=this.Skinning!.AddMesh(Index,this.VertexBuffers[Index],(uint)Mesh.Positions.Count,(uint)sizeof(Vertex),Skin.JointCount,Skin.JointIndices,Skin.Weights);
        }
        // M0110：スキニングメッシュは頂点が毎フレーム動くため、レスト姿勢の距離場では誤りになる。Unrealでもスキニングメッシュは対象外。
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
                // すべてのパスでスキニング済みコピーを描画する。アップロードヒープのバッファには、スキニング元のレスト姿勢を保持する。
                this.VertexBufferViews[I]=this.Skinning!.AddMesh(I,this.VertexBuffers[I],(uint)Mesh.Positions.Count,(uint)sizeof(Vertex),Skin.JointCount,Skin.JointIndices,Skin.Weights);
            }
            this.GlobalDistanceField!.SetMesh(I,Mesh.Skin is null?Mesh.DistanceField:null);
        }
    }

    /// <summary>メッシュの軸平行境界（M0111では、サーフェスキャッシュカードを囲むボックス）。</summary>
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

    /// <summary>メッシュの軸平行境界の中心（スキニングメッシュの場合はレスト姿勢での中心）。</summary>
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
    /// 作成後にマテリアルを1つ追加する（M0105）。そのテクスチャを今アップロードするため、一度GPUの完了を待つ。
    /// そのゲームでまだ描画していないアセットを初めて生成すると、1フレーム分のコストがかかる。
    /// 新しいマテリアルインデックスを返す。<see cref="DynamicMaterialHeadroom"/>を使い切った場合は-1。
    /// </summary>
    public int AddMaterial(StageMaterialCreateInfo Material){
        this.ThrowIfDisposed();
        if(this.MaterialCount>=this.MaterialCapacity){ return -1; }
        // テクスチャのアップロードでは最初のコマンドアロケーターをリセットするが、処理中のフレームが使用中の可能性がある。
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

    /// <summary>マテリアル1つ分のマテリアル単位状態を埋め、常駐テクスチャを共有してアップロードし、5つのディスクリプターを書き込む。</summary>
    private void SetUpMaterial(int I,StageMaterialCreateInfo Material){
        this.MaterialBaseColorFactors[I]=Material.BaseColorFactor;
        this.MaterialRoughnessFactors[I]=Material.RoughnessFactor;
        this.MaterialMetallicFactors[I]=Material.MetallicFactor;
        this.MaterialWorldGrids[I]=Material.WorldGrid;
        this.MaterialWorldGridAnchors[I]=Vector3.Zero;
        this.MaterialEmissiveNits[I]=Material.EmissiveNits!=Vector3.Zero
            ?Material.EmissiveNits
            :Material.EmissiveFactor*GltfImportEmissiveDefaultNits;

        // スロット0（ベースカラー）と3（発光）はsRGB色。1（法線）と2（metallic-roughness）はリニアデータ。
        // ミップの平均方法も色空間で決まる（M0110）。
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
        // テクスチャの全ミップ（M0110）。-1はD3D12で「最も詳細なレベルからすべて」を意味する。
        SrvDesc.Texture2D.MipLevels=uint.MaxValue;
        for(var Slot=0;Slot<4;Slot++){
            D3D12_CPU_DESCRIPTOR_HANDLE Handle=new(SrvHeapStart,(I*5)+Slot, this.SrvDescriptorSize);
            // スロット0（ベースカラー）と3（発光）は色データ。1（法線）と2は
            //（metallic-roughness）リニアデータ。
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
            ColorSpace=Texture.PreparedColorSpace??ColorSpace;
            var Key=(Texture.ContentHash,ColorSpace);
            if(this.ResidentTextures.TryGetValue(Key,out nint Existing)){
                return(ID3D12Resource*)Existing;
            }
            var Chain=Texture.PreparedMipChain??RenderTextureMipChain.Build(Texture,ColorSpace);
            var LevelCount=MaterialMipLevelLimitForTesting>0?Math.Min(MaterialMipLevelLimitForTesting,Chain.Levels.Count):Chain.Levels.Count;
            ID3D12Resource*Created=this.CreateTextureResource(Chain.Levels,LevelCount);
            this.ResidentTextures.Add(Key,(nint)Created);
            this.OwnedMaterialTextures.Add((nint)Created);
            this.OwnedTextureHashes[(nint)Created]=Key;
            return Created;
        }
    }

    /// <summary>
    /// マテリアルテクスチャのテスト用ミップレベル上限（M0110）。同じテクスチャをミップなし（1）とあり（0 = 全レベル）で描画し、
    /// 差を測定する。製品コードでは設定しない。GPUテストは一度に1つずつ実行する。
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

    /// <summary>検証済みの正規RGBA8テクスチャ1枚を、単一レベルでアップロードする（スクリーンレイヤー用）。デコード／フォールバック方針はL0より上位で扱う。</summary>
    private ID3D12Resource*CreateTextureResource(RenderTextureResource Texture){
        return this.CreateTextureResource(RenderTextureMipChain.TopLevelOnly(Texture).Levels,1);
    }
    /// <summary>テクスチャの先頭<paramref name="LevelCount"/>レベルを1回のコピーでアップロードする（M0110：マテリアルテクスチャの全ミップチェーンなど）。</summary>
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
            // GetCopyableFootprintsのRowPitchは256バイト境界。デコード済み画像は隙間なく
            // 配置される（stride = Width*4）。Width*4が256の倍数でなければ行ピッチは一致
            // しないため、テクスチャの行ピッチを守るには一括CopyBlockではなく行ごとにコピーする。
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
    /// 作成後に描画オブジェクトを1つ追加する（M0105）。ゲーム実行中にEntityを生成する場合に使う。
    /// 削除済みオブジェクトのスロットがあれば再利用する。オブジェクトインデックスを返し、<see cref="DynamicObjectHeadroom"/>を使い切った場合は-1。
    /// オブジェクトは原点から開始するため、他のオブジェクトと同様に毎フレーム変換を設定する。現在のフレームの定数だけを書き込むため、
    /// 処理中のフレームは既に渡された値で描画を続ける。
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
    /// オブジェクトの描画方法を選択する（M0107。UEのOwnerNoSee／OnlyOwnerSeeおよびCastShadowに相当）。
    /// <paramref name="VisibleInView"/>がfalseならカメラパス（GIプローブおよびプレビューも含む）から省く。
    /// <paramref name="CastsShadow"/>がfalseならすべてのシャドウマップから省く。どちらもオブジェクト追加時の既定値はtrue。
    /// 設定した値は変更するまで有効であり、フレームごとの呼び出しは不要。
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
    /// オブジェクトのマテリアル発光（シェーダーが発光テクスチャと乗算する発光ニト）に<paramref name="Scale"/>を掛ける
    ///（ADR-0078/M0109）。オブジェクト既存の発光定数に書き込むため、Stageシェーダーの契約は変わらない。
    /// 1ならマテリアル本来の発光。変更されるまで値を維持する。
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

    /// <summary>オブジェクト1つの描画を停止する（M0105）。後続の<see cref="AddObject"/>でそのスロットを再利用する。削除済みのものを再度削除しても何もしない。</summary>
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
    /// すべての処理中フレームスロットに、単位ワールド行列（各オブジェクトのマテリアル係数を含む）、既定カメラ、既定ライト／アンビエント／フォグ値を書き込む。
    /// これにより、コンストラクターから戻った後に呼び出し側が初めてSetCamera／SetObjectTransform／SetDirectionalLightなどを呼ぶ場合でも、
    /// 最初のPresentが完全に初期化されたデータを読み取る。
    /// </summary>
    private void InitializeConstantsToDefaults(){
        Camera DefaultCamera=Camera.CreateDefault(Vector3.Zero);
        this.SetCamera(DefaultCamera);

        for(uint Frame=0;Frame<FrameCount;Frame++){
            this.WriteViewProjection(Frame,DefaultCamera.GetView()*DefaultCamera.GetProjection((float)this.Width/ this.Height));
            this.WriteCameraPosition(Frame,DefaultCamera.GetEyePosition());
            this.WriteDirectionalLight(Frame,0,DefaultLightDirectionTowardLight,DefaultLightColor,0.0015f);
            this.WriteAmbientLight(Frame,DefaultAmbientColor);
            this.WriteFog(Frame,FogParams.None);
            this.WriteExposure(Frame,DefaultCamera.ExposureEv100);
            this.WriteViewDepth(Frame);
            this.WriteDirectionalCascades(Frame,DefaultLightDirectionTowardLight);
            this.WriteLightCounts(Frame); // D3D12はアップロードヒープメモリがゼロ初期化される保証がない。これをしないと最初のフレームでPointLights／SpotLightsの未初期化値を走査する可能性がある。
            this.WriteAreaShadowDefaults(Frame);

            for(var ObjectIndex=0;ObjectIndex< this.ObjectCount;ObjectIndex++){
                if(this.ObjectActive[ObjectIndex]){ this.WriteObjectConstants(Frame,ObjectIndex); }
            }
        }
    }

    /// <summary>
    /// 構築中の現在フレームに使うカメラを設定する。<see cref="RenderFrame"/>より前に毎フレーム呼び出すこと。
    /// 定数バッファは処理中フレームのスロットごとに二重化される（<see cref="FrameCount"/>）。ここでは現在のスロットだけに書き込む。
    /// </summary>
    public void SetCamera(in Camera Camera,float AspectRatioOverride=0f){
        this.ThrowIfDisposed();
        var AspectRatio=AspectRatioOverride>0f?AspectRatioOverride:(float)(this.ShadowSettings.Compare?Math.Max(1,this.Width/2):this.Width)/this.Height;
        this.SetView(RenderViewPolicy.FromCamera(Camera,AspectRatio));
        this.LastShadowCamera=Camera;
        this.LastShadowCameraAspect=AspectRatioOverride;
    }
    /// <summary>現在のフレームスロットに、検証済みのメインビューをアップロードする。</summary>
    public void SetView(in RenderView View){
        this.ThrowIfDisposed();
        if(!View.IsValid){ throw new ArgumentException("A main view must be validated.",nameof(View)); }
        this.LastShadowCamera=null;
        this.WriteViewProjection(this.FrameIndex,View.ViewProjection);
        this.WriteCameraPosition(this.FrameIndex,View.CameraPosition);
        this.WriteExposure(this.FrameIndex,View.ExposureEv100);
        this.CurrentViewDepth=View.Depth;
        this.CurrentAuxiliaryViewProjection=View.AuxiliaryViewProjection;
        this.WriteViewDepth(this.FrameIndex);
        this.HasCameraBasis=View.Basis.HasValue;
        if(View.Basis is { }Basis){
            this.CurrentCameraRight=Basis.Right;
            this.CurrentCameraUp=Basis.Up;
            this.CurrentCameraForward=Basis.Forward;
            this.CurrentCameraTanHalfFovY=Basis.TanHalfFovY;
            this.CurrentCameraAspectRatio=Basis.AspectRatio;
        }
    }

    /// <summary>
    /// 空の方向と太陽カスケードに使う、逆行列可能な投影行列。対数透視投影では物理的な遠クリップ面によって元の行列がfloatで特異になる場合があるため、
    /// 補助クリップ範囲を使う。
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
    /// M0100：オブジェクトごとの保守的な放射状ラスター深度。メッシュ境界中心におけるビュー深度（ビュー投影行列のW）で接線を求める。
    /// これにより、特定の距離付近だけでなく、すべての距離で隠れた破片を早期に除外できる。
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
    /// 所有するRender Server経由で、既にカメラ相対になっているプレビュー行列を受け取る。
    /// 内部可視性により、L8クライアントがネイティブバックエンドを直接取得することを防ぐ。
    /// </summary>
    internal void SetViewProjection(in Matrix4x4 ViewProjection){
        this.SetViewProjection(ViewProjection,Vector3.Zero);
    }

    internal void SetViewProjection(
        in Matrix4x4 ViewProjection,in Vector3 CameraPosition){
        this.ThrowIfDisposed();
        this.SetView(RenderViewPolicy.FromOrthographic(ViewProjection,CameraPosition));
    }

    /// <summary>
    /// M0100：放射状深度で描画する、分離プレビュー用のカメラ相対透視行列。指定された行列をそのまま使うため、
    /// 視野範囲、オーバーレイ、ピッキングは呼び出し側の行列どおりに保たれる。放射状パケット、補助行列、
    /// 各オブジェクトの保守的ラスター深度は、その行列と<paramref name="Radial"/>から導出する。
    /// </summary>
    internal void SetViewProjection(
        in Matrix4x4 ViewProjection,in Vector3 CameraPosition,in RadialViewParameters Radial){
        this.ThrowIfDisposed();
        this.SetView(RenderViewPolicy.FromPerspective(ViewProjection,CameraPosition,Radial,(float)this.Width/this.Height));
    }

    /// <summary>
    /// 後方互換の単一ライト設定。今フレームの加算式Directionalリストを、ライト1つだけの状態に置き換える。
    /// <paramref name="DirectionTowardLight"/>はシーンから光源へ向かう方向であり、光が進む方向ではない。
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
        // 値は遅延リストにも保持する。同じフレームで後からライトを追加しても、setterで設定したライトを
        // 黙って置き換えないようにする。
        this.DirectionalLightSubmissions.Add(Submission);
        this.PersistentDirectionalLightSubmission=Submission;
        this.DirectionalLightsPersistAcrossFrames=true;
    }

    /// <summary>
    /// 今フレーム用に、従来の面から光源へ向かうDirectional入力を追加する。直接照明の上限超過時は、画面への寄与が大きい4灯を残し、
    /// 同点なら送信順で安定させる。単一シャドウマップのパスは<paramref name="ShadowPriority"/>に従って別途選択する。
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
    /// フレームのカメラ確定後に、加算式Directional送信を確定する。直接照明の順位付けでは意図的に影の優先度を無視し、
    /// 寄与度、その後に安定した送信順で選ぶ。影の優先度は選択済みの直接ライトにだけ適用する。
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
    /// シーンの永続的な環境を置き換える（ADR-0069）。物理空、スカイライト、物理フォグ、露出モデル、トーンマッパーを設定する。
    /// <see cref="EnvironmentLightingSettings.Legacy"/>を指定すると、従来のクリアカラー、平坦なアンビエント、旧フォグ、線形ACES出力経路に戻る。
    /// </summary>
    public void SetEnvironmentLighting(in EnvironmentLightingSettings Settings){
        this.ThrowIfDisposed();
        if(!Settings.IsValid){
            throw new ArgumentException("Environment lighting settings must be finite and physically valid.",nameof(Settings));
        }
        if(Settings.DirectionalShadows is { Compare:true }&&(Settings.Atmosphere is not null||Settings.GlobalIllumination is not null||Settings.AmbientOcclusion is not null||Settings.Lumen is not null||Settings.Exposure.Mode==ExposureMode.AutoHistogram)){
            throw new ArgumentException("Shadow comparison requires directional/ambient lighting with fixed exposure; screen-space and sky environment passes need independent view execution.",nameof(Settings));
        }
        var ComparisonChanged=this.ShadowSettings.Compare!=(Settings.DirectionalShadows?.Compare??false);
        if(Settings.DirectionalShadows is { }ShadowPolicy&&(ShadowPolicy.Compare||ShadowPolicy.Mode==DirectionalShadowMode.Virtual)){ this.EnsureVirtualShadowAtlas(); }
        this.EnvironmentSettings=Settings;
        if(ComparisonChanged&&this.LastShadowCamera is Camera Camera){ this.SetCamera(Camera,this.LastShadowCameraAspect); }
        this.WriteExposure(this.FrameIndex,this.CurrentCameraEv100);
    }

    /// <summary>大気と高さフォグで使う、ワールド原点からの絶対カメラ高度（m）。</summary>
    public void SetCameraAltitude(float Meters){
        this.ThrowIfDisposed();
        if(!float.IsFinite(Meters)){
            throw new ArgumentOutOfRangeException(nameof(Meters),"The camera altitude must be finite.");
        }
        this.CameraAltitudeMeters=Meters;
    }

    /// <summary>
    /// 描画空間原点のワールド原点からの高さ（m）。簡易高さフォグでは受け取り側の描画空間高度に加算する。
    /// ワールド座標で描画する呼び出し側は0のままにする。カメラ相対ランタイム経路ではカメラの高度を指定する。
    /// 変更するまで値を保持する。
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
    /// マテリアルのグリッドサイズで割った、描画空間原点のワールド位置の剰余。カメラ相対描画でもワールド空間グリッドをワールド格子に沿わせる。
    /// 各成分は[0, <see cref="StageWorldGrid.GridSizeMeters"/>]の範囲に入る。ワールド座標で描画する呼び出し側は0のままにする。
    /// 変更するまで値を保持する。
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
            // ABI互換性のため予約済みの大気パケットを保持する。空の合成ではSceneDistanceを読む。
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

    /// <summary>間接照明のシェーディングでAOを使う前に、物理的なシーン距離を生成する。</summary>
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
        // 既存のHDR割り当て領域を、ここでは作業用法線ストレージとして使う。色パスでクリアする。
        this.BindMainPassState(this.LumenSurfacePipelineState);
        // 利用側の有効領域は、再利用する全サイズターゲットの左上部分に配置する。
        D3D12_VIEWPORT Viewport=new(){ Width=Width,Height=Height,MinDepth=0f,MaxDepth=1f };
        RECT Scissor=new(){ right=Width,bottom=Height };
        this.CommandList->RSSetViewports(1,&Viewport);
        this.CommandList->RSSetScissorRects(1,&Scissor);
        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        this.DrawObjects(ExcludedObjectIndex:-1);
        var ToReadable=D3D12_RESOURCE_BARRIER.InitTransition(this.HdrColorBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        this.CommandList->ResourceBarrier(1,&ToReadable);
    }
    /// <summary>AOは共有物理距離を読み取り、半解像度の遮蔽量とぼかしを維持する。</summary>
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
    /// ADR-0078/M0110：カメラ周囲のグローバル距離場を更新し、M0111のトレースで使えるようにする。
    /// 環境に距離場がなければ何もしない。オブジェクトの描画空間行列だけを読み取るため、カメラ移動時に書き換えるのは、
    /// クリップマップが新たに覆ったブリックだけ。
    /// </summary>
    private void UpdateGlobalDistanceField(){
        if(this.EnvironmentSettings.DistanceField is not GlobalDistanceFieldSettings Settings){
            return;
        }
        this.GlobalDistanceField!.Update(this.CommandList,this.FrameIndex,Settings,this.ObjectActive,this.ObjectMeshIndices,this.ObjectTransforms,this.ObjectCount,this.WaitForGpuIdle);
    }
    /// <summary>グローバル距離場のレベル1つ分のテスト専用コピー（M0110）。GPUの完了を待つ。</summary>
    internal (float[]Values,int Resolution,LongVector3 ClipMinimum) ReadGlobalDistanceFieldForTesting(int Level){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.GlobalDistanceField!.ReadLevelForTesting(Level);
    }
    /// <summary>M0111と同じ方法でグローバル距離場をトレースするテスト専用処理（M0110）。GPUの完了を待つ。</summary>
    internal Vector4[] TraceGlobalDistanceFieldForTesting(ReadOnlySpan<(Vector3 Origin,Vector3 Direction,float Length)>Rays){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.GlobalDistanceField!.TraceForTesting(Rays);
    }
    /// <summary>テストと診断で使うグローバル距離場の状態（M0110）。</summary>
    internal StageGlobalDistanceField GlobalDistanceFieldForTesting=>this.GlobalDistanceField!;
    /// <summary>
    /// CPU側の露出スケール。ヒストグラムモードではStageプログラムと空の合成がGPU測光済みスケールを読む（ExposureParams.z）。
    /// この名目最小EV100値は、GPU状態を参照できない利用側のために残す。
    /// </summary>
    private float CurrentExposureScale()=>this.EnvironmentSettings.Exposure.Scale(this.CurrentCameraEv100);

    /// <summary>露出適応用の、前フレームからの経過秒数。スタールがあっても大きく飛ばないよう制限する。</summary>
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
        // M0111：StageプログラムはLumen実行中だけLumen拡散出力（x）と反射出力（y）を読む。0なら従来の各フレーム経路を維持する。
        var Lumen=this.LumenActiveThisFrame
            ?new Vector4(1f,this.EnvironmentSettings.Lumen is { Reflections:true }&&this.Scalability.AllowsLumenReflections?1f:0f,0f,0f)
            :Vector4.Zero;
        Unsafe.CopyBlock(Slot+FrameLumenParamsOffset,&Lumen,(uint)sizeof(Vector4));
    }

    /// <summary>
    /// 今フレームに予定されたGIプローブを再撮影する（ADR-0069）。各面はフレーム定数のコピーを使い、照明済みStageパイプラインで描画する。
    /// プローブをカメラとして扱い、露出は1（シーン放射輝度を保持）、フォグはオフ、局所ライトはオフとする。Forward+のタイル一覧はメインカメラ用のためである。
    /// 前回のGI結果はバインドしたままにし、更新のたびに光の反射を1回追加する。
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
    /// マテリアルビューの後に、Stageのt12～t17ビュー（GI照度、GI距離、アンビエントオクルージョン、露出状態、およびM0111のLumen拡散照度と反射）を書き込む。
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
        this.WriteVirtualShadowDescriptors(new D3D12_CPU_DESCRIPTOR_HANDLE(Start,this.MaterialCapacity*5+6,this.SrvDescriptorSize),new D3D12_CPU_DESCRIPTOR_HANDLE(Start,this.MaterialCapacity*5+7,this.SrvDescriptorSize));
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
    /// 平坦なアンビエント項（空／間接照明の代用）を設定する。変更するまで保持する。
    /// <see cref="RenderFrame"/>はDirectionalライトや露出と同様、記録対象のフレームスロットへ書き込むため、一度設定すればスロット間で値が交互に変わらない。
    /// </summary>
    public void SetAmbientLight(Vector3 Color){
        this.ThrowIfDisposed();
        this.CurrentAmbientColor=Color;
        this.WriteAmbientLight(this.FrameIndex,Color);
    }

    /// <summary>簡易指数型高さフォグを設定する。<see cref="SetAmbientLight"/>と同様に保持する。無効にするには<see cref="FogParams.None"/>（密度0）を渡す。</summary>
    public void SetFog(FogParams Fog){
        this.ThrowIfDisposed();
        this.CurrentFog=Fog;
        this.WriteFog(this.FrameIndex,Fog);
    }

    /// <summary>
    /// 構築中のフレームにポイントライト（全方向へ均等に放射し、距離に応じて減衰して<paramref name="Range"/>で0になる滑らかな減衰）を1つ追加する。
    /// 以前の固定ポイントライト定数バッファAPIとソース互換である。<see cref="RenderFrame"/>まで送信を遅延し、その時点でポイント／スポットライトを
    /// 1024エントリのStructuredBufferと決定的なForward+順位付けでまとめて処理する。
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
    /// 構築中のフレームにスポットライト（ポイントライトを円錐状に絞ったもの）を1つ追加する。
    /// <paramref name="Direction"/>は円錐が向く方向で、正規化されていなくてもよい。
    /// <paramref name="InnerConeAngleRadians"/>の範囲内では最大輝度を保ち、<paramref name="OuterConeAngleRadians"/>で0となるよう滑らかに減衰する。
    /// <see cref="AddPointLight(Vector3,Vector3,float,float)"/>と同じ遅延StructuredBufferおよび1024件の全体選択を共有する。
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
    /// 今フレーム用に片面矩形ライトを1つキューへ追加する。シャドウ設定は送信内容に保持するが、
    /// 投影アトラス経路が接続されるまでは意図的に割り当てない。その間も直接照明は有効。
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


    /// <summary>構築中のフレーム用にオブジェクト1つのワールド変換を設定する（フレームスロットの契約は<see cref="SetCamera"/>を参照）。</summary>
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

    /// <summary>StageシェーダーABIのDirectionalLightData[index]を書き込む（方向＋バイアス、色×強度）。</summary>
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
        // 従来モードでは、EV100は整数1段ごとにHDR信号を半分にする。PhysicalCameraモードでは
        // ADR-0069に従い、カメラのEVを環境の露出設定で置き換える。
        this.CurrentCameraEv100=ExposureEv100;
        Vector4 Packed=new(this.CurrentExposureScale(),ExposureEv100,this.AutoExposureActive?1f:0f,0f);
        Unsafe.CopyBlock(this.FrameSlot(FrameIndex)+FrameExposureOffset,&Packed,(uint)sizeof(Vector4));
    }

    /// <summary>
    /// 現在のカメラに合わせて4つのDirectionalカスケードを設定し（ADR-0069）、シャドウパスとStageプログラム用に投影を書き込む。
    /// Cascade 0は、プレビューシェーダーがサンプリングする単一投影フィールドも設定する。
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
    /// 唯一の割り当て元としてPhysicalShadowAtlasPlanを使う。選択したPointライトには、計画が所有する6面タイルを割り当てる。
    /// 選択されたSpotおよびArea／Emissiveライトには、同じ計画内で一度の選択を通じて、中心投影タイルを各1つ割り当てる。
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
            // 元の直接ライト一覧の安定順に関係なく、アトラスにはDirectional用タイルが1つある。
            // そのため、最初の4灯を超えて選ばれた送信が、下記のローカルライト名前空間と
            // 衝突しないよう、インデックスを0に正規化する。
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
                    Tile,ProjectionIndex+1)); // スロット0はDirectional投影用。
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
    /// 真の矩形Area／Emissiveライト1つについて、光源の大きさと選択された中心投影インデックスを書き込む。
    /// メタデータレコードは投影インデックスで参照し、ローカルライトレコードにも後段のピクセルシャドウ参照用に同じインデックスを格納する。
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

    /// <summary>Areaライトが送信される前に、独立してバインドされるt9／t10レコードを初期化する。</summary>
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
    /// Area／Emissive矩形光源のアトラスタイルに使う、安全な中心投影を構築する。
    /// 直接照明では光源を矩形のまま扱う。後段のPCSS参照用に、光源法線を中心とする保守的な単一タイルの透視ビューを作る。
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
    /// アトラス面の順序+X、-X、+Y、-Y、+Z、-Zに合わせて、ポイントライト用の90度ビューを行ベクトル形式で6つ作る。
    /// HLSLの面選択処理もこの順序を使う。
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

    /// <summary>FrameCBの個数を書き込む。ローカルStructuredBuffer数、Directionalシャドウインデックス、Directional数。</summary>
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

        // グリッドサイズが0なら「グリッドなし」。シェーダーは通常のベースカラーを使う。
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

        // ADR-0003 §8を参照。ResizeBuffersによりスワップチェーンの
        // バックバッファインデックスの巡回がリセットされる。何もしないとスロットごとのフェンス値とずれる。
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
            // GPUはアイドル状態。次のLumenフレームが新サイズのターゲットを作るまで、Stageのt16／t17ビューをnullに戻す。
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

    /// <summary>選択されたDirectionalとSpotの投影タイルを、1組のアトラス遷移内で描画する。</summary>
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
            // 各マーカーは、観測者ビューの定数（観測者のビュー投影）を使って、その観測者プレビュー内に描画する。
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
    /// マーカー対象の各観測者プレビューについて、メインカメラの視錐台をオーバーレイスロットへ書き込む。
    /// 頂点は圧縮ワールド中心を頂点とし、半径の5 %の長さで、観測者相対座標を使う。書き込んだマーカー数を返す。
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
            // Forward+のタイル一覧は主カメラ向けに作成済み。プレビューのパスではその一覧を維持する。
            // Directional／アンビエントライトはそのまま使うが、カメラ依存のアクティブ集合を古いままサンプリングしないよう、ローカルライトは無効にする。
            //（次の行コメントと結合して読む）。
            int*LightCounts=(int*)(Destination+FrameLightCountsOffset);
            LightCounts[0]=0;
            // スクリーン空間オクルージョンとLumen出力は、主カメラのピクセルに対応する。
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
        Vector4 ClearColor=this.BackgroundClearVector;
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

    /// <summary>次フレーム以降、スキニングメッシュのスキンパレット（ジョイントごとに逆バインド×ジョイントモデル、行ベクトル規約）を設定する。</summary>
    public void SetSkinPalette(int MeshIndex,ReadOnlySpan<Matrix4x4>Palette){
        this.ThrowIfDisposed();
        this.Skinning!.SetPalette(MeshIndex,Palette);
    }

    /// <summary>
    /// テスト専用：次フレーム以降、メインパスの描画をGPUタイムスタンプで囲む。プレゼンテーションの待ち時間ではなく、
    /// GPU時間を性能の証拠として計測する（M0099）。
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

    /// <summary>テスト専用：最後に計測したフレームのメインパス描画にかかったGPU時間（ミリ秒）。GPUの完了後に読み取る。</summary>
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

    /// <summary>テスト専用のメイン深度バッファのコピー。GPU完了後に読み取る（行優先、width x height）。</summary>
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

    /// <summary>テスト専用の物理レイ距離。プレビュー描画をまたいで保持する（行優先）。</summary>
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

    /// <summary>スキニングメッシュの現在の頂点（位置、法線、接線、UV。1頂点48バイト）のテスト専用コピー。</summary>
    internal byte[]ReadSkinnedVerticesForTesting(int MeshIndex){
        this.WaitForGpuIdle();
        return this.Skinning!.ReadSkinnedVerticesForTesting(MeshIndex);
    }

    public void RenderFrame(bool CaptureForVerification=false){
        this.ThrowIfDisposed();
        // 削除済みデバイスへ処理（特にタイムスタンプクエリ）を記録しないこと。そうしないとWARPが、旧世代のリソース解放後に
        // その処理を実行する可能性がある。
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->GetDeviceRemovedReason(),"ID3D12Device::GetDeviceRemovedReason","checking the device before recording a frame");
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
        this.ReadShadowTimings();
        this.PrepareDirectionalLights(this.FrameIndex);
        this.PrepareForwardPlusLocalLights(this.FrameIndex);

        this.PrepareLocalShadowAtlasProjections(this.FrameIndex);
        this.ShadowTimestamp(0);
        this.RenderShadowAtlasProjections();
        this.ShadowTimestamp(1);
        this.ShadowTimestamp(2);
        this.PrepareVirtualShadows();
        this.ShadowTimestamp(3);
        this.CommandList->ResolveQueryData(this.ShadowTimestampHeap,D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP,this.FrameIndex*4,4,this.ShadowTimestampReadback,(ulong)this.FrameIndex*4*sizeof(ulong));
        this.ShadowTimestampsValid[this.FrameIndex]=true;
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

        Vector4 ClearColor=this.BackgroundClearVector;
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
        if(this.ShadowSettings.Compare){ this.DrawShadowComparison(); }
        else { this.DrawObjects(ExcludedObjectIndex:-1); }
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
            // 同じターゲットへカメラプレビューを描画する前に測光する。
            this.AutoExposureStage!.RecordMetering(this.CommandList,this.FrameIndex,this.HdrColorBuffer,this.Width,this.Height,this.EnvironmentSettings.Exposure,AutoExposureDeltaSeconds);
            RanComputeAfterMainPass=true;
        }
        if(this.LumenActiveThisFrame){
            // M0111：照明済みフレーム（空を含み、プレビュー前の状態）を、次フレームのスクリーントレース色として使う。
            this.Lumen!.CopySceneColor(this.CommandList,this.HdrColorBuffer);
            if(this.LumenVisualization!=LumenVisualization.None){
                this.Lumen.RecordVisualize(this.CommandList,this.FrameIndex,this.LumenVisualization,this.HdrColorBuffer);
                RanComputeAfterMainPass=true;
            }
        }
        if(RanComputeAfterMainPass){
            // ComputeによってグラフィックスPSOとディスクリプタヒープが置き換わるため、メインパスのバインド状態を
            // 復元してから、再びStageパイプラインでカメラプレビューを描画する。
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

        var CapturedShadowTimingSlot=this.FrameIndex;
        this.MoveToNextFrame();
        this.Device.PollDebugMessages();
        this.FramesRendered++;
        this.LumenActiveLastFrame=this.LumenActiveThisFrame;

        // 加算式ライト数（およびそれに対応するデータ）は上記で既にGPUへ送信した。
        // 次フレームが0から始まり、そのフレームで再びAddメソッドを呼んだライトだけを反映するよう、
        // リセットする（各フィールドの説明を参照）。
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
            this.ReadShadowTimings(CapturedShadowTimingSlot);
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
            Format=RenderPixelFormat.Rgba8Unorm,
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
            // 削除／リセットされたデバイスはフェンスを通知できない。Render Serverが次の世代を作成できるよう、
            // ネイティブオブジェクトを解放する必要がある。
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
        this.DisposeVirtualShadows();
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
