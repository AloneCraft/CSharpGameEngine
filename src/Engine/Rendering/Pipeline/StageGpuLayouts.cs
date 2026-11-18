using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace CSharpGameEngine.Rendering.Pipeline;
[StructLayout(LayoutKind.Sequential)]
public struct StageInt4 {
    public int X;
    public int Y;
    public int Z;
    public int W;
}
[StructLayout(LayoutKind.Sequential)]
public struct StageUInt4 {
    public uint X;
    public uint Y;
    public uint Z;
    public uint W;
}
[StructLayout(LayoutKind.Sequential)]
public struct StageDirectionalLightData {
    public Vector4 DirectionAndShadowBias;
    public Vector4 ColorTimesIntensity;
}
[InlineArray(4)]
public struct StageFloat4Array4 {
    private Vector4 Element0;
}
[InlineArray(4)]
public struct StageMatrixArray4 {
    private Matrix4x4 Element0;
}
/// <summary>仮想シャドウの段ごとの行列（M0132 の 16 段）。</summary>
[InlineArray(16)]
public struct StageMatrixArray16 {
    private Matrix4x4 Element0;
}
[InlineArray(4)]
public struct StageDirectionalLightArray4 {
    private StageDirectionalLightData Element0;
}
[StructLayout(LayoutKind.Sequential)]
public struct StageFrameConstants {
    public Matrix4x4 ViewProjection;
    public Vector4 CameraPositionWS;
    public StageDirectionalLightArray4 DirectionalLights;
    public Vector4 AmbientColor;
    public Vector4 FogParams0;
    public Vector4 FogColor;
    public Vector4 ExposureParams;
    public StageInt4 LightCounts;
    public StageUInt4 ForwardPlusGrid;
    public Matrix4x4 InverseViewProjection;
    public Vector4 ViewportSize;
    public Vector4 SkyLightParams;
    public Vector4 PhysicalFogParams;
    public Vector4 PhysicalFogColor;
    public StageMatrixArray4 CascadeViewProjection;
    public StageFloat4Array4 CascadeSpheres;
    public StageFloat4Array4 CascadeUvScaleOffsets;
    public Vector4 CascadeDepthRanges;
    public Vector4 GiGridOrigin;
    public Vector4 GiGridSpacing;
    public StageUInt4 GiGridCounts;
    public Vector4 GiAtlasSize;
    public Vector4 AmbientOcclusionParams;
    public Vector4 DepthEncoding0;
    public Vector4 DepthEncoding1;
    public Vector4 LumenParams;
    public StageMatrixArray16 VirtualShadowProjection;
    public Vector4 VirtualShadowParams;
    public Vector4 DirectionalSoftShadowParams;
}
/// <summary>物体の表（Stage シェーダーの <c>Instances</c>、t20）の 1 件。物体 1 つにつき 1 件を、フレームごとに書く（ADR-0100 決定 2）。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct StageInstanceData {
    public Matrix4x4 World;
    /// <summary>対数深度のビューで頂点シェーダーが使う、保守的なラスタ深度の（Limit, Slope）。それ以外のビューでは 0。</summary>
    public Vector2 ConservativeRasterDepth;
    /// <summary>材質の発光に掛ける、物体ごとの倍率。</summary>
    public float EmissiveScale;
    /// <summary>選択の強調の重み（0〜1）。表示用のエントリーだけが読む。</summary>
    public float SelectionWeight;
    /// <summary>材質の表の番号。</summary>
    public uint Material;
}
/// <summary>材質の表（Stage シェーダーの <c>Materials</c>、t21）の 1 件。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct StageMaterialData {
    public Vector4 BaseColorFactor;
    /// <summary>x は粗さの係数、y は metallic の係数。</summary>
    public Vector4 MaterialParams;
    public Vector4 EmissiveNits;
    /// <summary>x は格子の間隔（0 なら格子なし）、y は副格子の分割数、z は線の半幅。</summary>
    public Vector4 WorldGridParams;
    public Vector4 WorldGridColor;
    public Vector4 WorldSubGridColor;
    /// <summary>xyz は格子の原点のずれ。w が 1 なら、ずれの代わりに物体の位置を原点にする。</summary>
    public Vector4 WorldGridAnchor;
    /// <summary>材質のテクスチャの配列（Stage シェーダーの <c>MaterialTextures</c>、t0 space1）での番号。x 基本色、y 法線、z metallic と粗さ、w 発光。</summary>
    public StageUInt4 Textures;
    /// <summary>材質の値から表の 1 件を作る。格子が無い材質は、格子の欄がすべて 0 になる。</summary>
    public static StageMaterialData Create(Vector4 BaseColorFactor,float RoughnessFactor,float MetallicFactor,Vector3 EmissiveNits,StageWorldGrid?WorldGrid,Vector3 WorldGridAnchor,StageUInt4 Textures){
        StageMaterialData Data=new(){
            BaseColorFactor=BaseColorFactor,
            MaterialParams=new(RoughnessFactor,MetallicFactor,0f,0f),
            EmissiveNits=new(EmissiveNits,0f),
            Textures=Textures,
        };
        if(WorldGrid is{ }Grid){
            Data.WorldGridParams=new(Grid.GridSizeMeters,Grid.SubGridDivisions,Grid.WireHalfWidth,0f);
            Data.WorldGridColor=new(Grid.GridColor,0f);
            Data.WorldSubGridColor=new(Grid.SubGridColor,0f);
            Data.WorldGridAnchor=Grid.AnchorToObject?new(0f,0f,0f,1f):new(WorldGridAnchor,0f);
        }
        return Data;
    }
}
/// <summary>描画ごとの印（Stage シェーダーの <c>DrawFlags</c>）。</summary>
[Flags]
public enum StageDrawFlags:uint {
    None=0,
    /// <summary>物体のメッシュを、メッシュ自身の空間で描く（Lumen のカード撮影）。</summary>
    MeshSpace=1,
}
/// <summary>描画ごとのルート定数（Stage シェーダーの <c>DrawCB</c>、b1）。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct StageDrawConstants {
    /// <summary>描く物体の、物体の表での番号。</summary>
    public uint Instance;
    /// <summary>表示モード（<see cref="RenderPresentationMode"/> の値）。表示用のエントリーだけが読む。</summary>
    public uint PresentationMode;
    /// <summary><see cref="StageDrawFlags"/> の値。</summary>
    public uint Flags;
    /// <summary>
    /// 物体の向きの法線マップ（仮想ジオメトリ、M0130 単位 6）の、材質のテクスチャの配列での番号に 1 を足した値。0 なら無い。
    /// あれば、画素シェーダーは頂点の法線の代わりにこの向きを使い、材質の法線マップをその上に重ねる。
    /// </summary>
    public uint ObjectNormalMap;
}
/// <summary>仮想ジオメトリの頂点 1 つ(Stage シェーダーの <c>VirtualGeometryVertices</c>、t22、48 バイト)。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct StageVirtualGeometryVertex {
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Tangent;
    public Vector2 Uv;
}
/// <summary>仮想ジオメトリの塊 1 つ(<c>VirtualGeometryClusters</c>、t24、16 バイト)。<see cref="ParentGroup"/> が -1 なら根の段。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct StageVirtualGeometryCluster {
    public uint FirstIndex;
    public uint IndexCount;
    public uint Group;
    public int ParentGroup;
}
/// <summary>仮想ジオメトリのまとまり 1 つ(選択の計算シェーダーだけが読む、32 バイト)。誤差と、誤差を画素に直すための包む球(メッシュの中の座標)。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct StageVirtualGeometryGroup {
    public Vector3 BoundsCentre;
    public float BoundsRadius;
    public float Error;
    /// <summary>三角形の辺の長さの平均（メートル。親へ行くほど大きい）。三角形の下限で使う。</summary>
    public float TriangleSize;
    public float Reserved1;
    public float Reserved2;
}
/// <summary>
/// 仮想ジオメトリの塊の選択の定数(選択の計算シェーダーの b0、ルート定数)。物体 1 つ分。位置はすべて描画空間(カメラが原点のことが多い)。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct StageVirtualGeometrySelection {
    /// <summary>メッシュの座標から描画空間への変換(行ベクトル)。</summary>
    public Matrix4x4 World;
    /// <summary>xyz はカメラの位置(描画空間)、w は <see cref="World"/> の最大の拡大率(球の半径と誤差に掛ける)。</summary>
    public Vector4 CameraAndScale;
    /// <summary><c>ScreenSizeMetric.ShaderParameters</c>。</summary>
    public Vector4 Metric;
    /// <summary>塊の数。</summary>
    public uint ClusterCount;
    /// <summary>誤差の画素数のしきい値。これ以下なら細かくしない。</summary>
    public float ThresholdPixels;
    /// <summary>三角形の下限（画素）。三角形の大きさの画素数がこれ以下なら、誤差が大きくても細かくしない。0 なら使わない。</summary>
    public float TriangleFloorPixels;
    /// <summary><see cref="StageVirtualGeometrySelectionFlags"/>。</summary>
    public uint Flags;
}
/// <summary>選択の計算シェーダーの働きの印（M0131 単位 2）。</summary>
[Flags]
public enum StageVirtualGeometrySelectionFlags:uint {
    None=0,
    /// <summary>選ぶとき、前のフレームで見えた印のある塊を 1 パス目の一覧にも詰める。</summary>
    Occlusion=1,
    /// <summary>選ばずに、選んだ一覧の塊を Hi-Z で調べ、見えた印を書き直して 2 パス目の一覧を作る。</summary>
    Test=2,
}
/// <summary>遮蔽を調べるときの定数（選択の計算シェーダーの b1、M0131 単位 2）。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct StageVirtualGeometryOcclusion {
    /// <summary>主ビューのビュー投影（行ベクトル）。</summary>
    public Matrix4x4 ViewProjection;
    public uint ScreenWidth;
    public uint ScreenHeight;
    /// <summary>Hi-Z の段 0 の大きさ。</summary>
    public uint HiZWidth;
    public uint HiZHeight;
    public uint HiZLevelCount;
}
/// <summary>描画器へ渡す仮想ジオメトリ 1 つ(GPU の表の形のまま)。</summary>
public sealed record StageVirtualGeometryCreateInfo {
    public required StageVirtualGeometryVertex[]Vertices { get;init; }
    public required uint[]Indices { get;init; }
    public required StageVirtualGeometryCluster[]Clusters { get;init; }
    public required StageVirtualGeometryGroup[]Groups { get;init; }
    /// <summary>元の細かい形から焼いた、物体の向きの法線マップ(UV で引く。無ければ頂点の法線を使う)。</summary>
    public RenderTextureResource?ObjectNormalMap { get;init; }
    /// <summary>塊の三角形の上限(頂点シェーダーが塊 1 つに描く頂点の数は、この 3 倍)。</summary>
    public const int MaxTrianglesPerCluster=128;
}
/// <summary>Stage シェーダー ABI の管理コード側におけるレイアウトの正本。オフセットは転送する構造体から取得する。</summary>
public static class StageGpuLayouts {
    public static readonly uint FrameConstantsSize=checked((uint)Marshal.SizeOf<StageFrameConstants>());
    public static readonly uint InstanceDataSize=checked((uint)Marshal.SizeOf<StageInstanceData>());
    public static readonly uint MaterialDataSize=checked((uint)Marshal.SizeOf<StageMaterialData>());
    /// <summary>描画ごとのルート定数の、32 ビット値の個数。</summary>
    public static readonly uint DrawConstantCount=checked((uint)Marshal.SizeOf<StageDrawConstants>())/sizeof(uint);
    public static readonly uint VirtualGeometryVertexSize=checked((uint)Marshal.SizeOf<StageVirtualGeometryVertex>());
    public static readonly uint VirtualGeometryClusterSize=checked((uint)Marshal.SizeOf<StageVirtualGeometryCluster>());
    public static readonly uint VirtualGeometryGroupSize=checked((uint)Marshal.SizeOf<StageVirtualGeometryGroup>());
    /// <summary>選択のルート定数の、32 ビット値の個数。</summary>
    public static readonly uint VirtualGeometrySelectionConstantCount=checked((uint)Marshal.SizeOf<StageVirtualGeometrySelection>())/sizeof(uint);
    public static readonly uint VirtualGeometryOcclusionConstantCount=checked((uint)Marshal.SizeOf<StageVirtualGeometryOcclusion>())/sizeof(uint);
    public static readonly uint FrameViewProjectionOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.ViewProjection));
    public static readonly uint FrameCameraPositionOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.CameraPositionWS));
    public static readonly uint FrameDirectionalLightsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.DirectionalLights));
    public static readonly uint FrameAmbientColorOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.AmbientColor));
    public static readonly uint FrameFogParamsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.FogParams0));
    public static readonly uint FrameFogColorOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.FogColor));
    public static readonly uint FrameExposureOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.ExposureParams));
    public static readonly uint FrameLightCountsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.LightCounts));
    public static readonly uint FrameForwardPlusGridOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.ForwardPlusGrid));
    public static readonly uint FrameInverseViewProjectionOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.InverseViewProjection));
    public static readonly uint FrameViewportSizeOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.ViewportSize));
    public static readonly uint FrameSkyLightParamsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.SkyLightParams));
    public static readonly uint FramePhysicalFogParamsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.PhysicalFogParams));
    public static readonly uint FramePhysicalFogColorOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.PhysicalFogColor));
    public static readonly uint FrameCascadeViewProjectionOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.CascadeViewProjection));
    public static readonly uint FrameCascadeSpheresOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.CascadeSpheres));
    public static readonly uint FrameCascadeUvScaleOffsetsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.CascadeUvScaleOffsets));
    public static readonly uint FrameCascadeDepthRangesOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.CascadeDepthRanges));
    public static readonly uint FrameGiGridOriginOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.GiGridOrigin));
    public static readonly uint FrameGiGridSpacingOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.GiGridSpacing));
    public static readonly uint FrameGiGridCountsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.GiGridCounts));
    public static readonly uint FrameGiAtlasSizeOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.GiAtlasSize));
    public static readonly uint FrameAmbientOcclusionOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.AmbientOcclusionParams));
    public static readonly uint FrameDepthEncoding0Offset=Offset<StageFrameConstants>(nameof(StageFrameConstants.DepthEncoding0));
    public static readonly uint FrameDepthEncoding1Offset=Offset<StageFrameConstants>(nameof(StageFrameConstants.DepthEncoding1));
    public static readonly uint FrameLumenParamsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.LumenParams));
    public static readonly uint FrameVirtualProjectionOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.VirtualShadowProjection));
    public static readonly uint FrameVirtualParamsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.VirtualShadowParams));
    public static readonly uint FrameDirectionalSoftShadowOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.DirectionalSoftShadowParams));
    private static uint Offset<T>(string Name) where T:struct=>checked((uint)Marshal.OffsetOf<T>(Name).ToInt64());
}
