using System.Numerics;
namespace CSharpGameEngine.Rendering;
/// <summary>
/// レンダラーに依存しないメッシュ形状データ（ADR-0018）。位置、法線、接線、UV を GPU バッファとして個別に持つ。メッシュインデックスを参照する各
/// <see cref="StageObjectCreateInfo"/> で共有する。
/// </summary>
public readonly struct StageMeshCreateInfo {
    public required IReadOnlyList<Vector3>Positions { get; init;}
    public required IReadOnlyList<Vector3>Normals { get; init;}
    /// <summary>xyz は接線方向、w は従法線の符号を表す。全メッシュで法線マップを使えるよう必須項目とする（省略不可）。glTF インポーターは TANGENT を読み取るか生成して必ず用意する。</summary>
    public required IReadOnlyList<Vector4>Tangents { get; init;}
    public required IReadOnlyList<Vector2>Uvs { get; init;}
    public required IReadOnlyList<uint>Indices { get; init;}
    /// <summary>スキニングメッシュのスキン影響（ADR-0066）。剛体ジオメトリでは null。各スキニングメッシュを個別にスキニングするため、インスタンスごとに独自のメッシュを用意する。</summary>
    public StageSkinCreateInfo?Skin { get; init;}
    /// <summary>事前生成したメッシュの符号付き距離場（ADR-0078/M0110）。距離場のあるメッシュを使うオブジェクトは、グローバル距離場に含まれる。</summary>
    public MeshDistanceField?DistanceField { get; init;}
}
/// <summary><see cref="JointCount"/> 個の行列パレットを参照する、頂点ごとの関節インデックスと重み（8組）。</summary>
public sealed class StageSkinCreateInfo {
    public required int JointCount { get; init;}
    public required IReadOnlyList<ushort>JointIndices { get; init;}
    public required IReadOnlyList<float>Weights { get; init;}
}
/// <summary>検証済みの glTF metallic-roughness 形式マテリアルを、レンダラーに依存せず表す。テクスチャスロットには不変の正準 RGBA8 リソースを格納し、符号化された PNG/JPEG バイト列はこの境界を越えない。</summary>
public readonly struct StageMaterialCreateInfo() {
    public required Vector4 BaseColorFactor { get; init;}
    public required float RoughnessFactor { get; init;}
    public required float MetallicFactor { get; init;}
    /// <summary>トーンマッピングとブルームの前に加算する HDR 発光 RGB 係数。</summary>
    public Vector3 EmissiveFactor { get; init;}
    /// <summary>物理的に設定された発光輝度（cd/m²、nit）。</summary>
    public Vector3 EmissiveNits { get; init;}
    /// <summary>直接光の発光パッチのスケーリングに使う、発光テクスチャの平均リニア輝度。1なら係数と強度だけを使うマテリアルの結果を維持する。</summary>
    public float EmissiveTextureLuminanceScale { get; init;}=1f;
    public RenderTextureResource?BaseColorTexture { get; init;}
    public RenderTextureResource?NormalTexture { get; init;}
    /// <summary>任意の glTF 生発光テクスチャ。可視の自己発光と発光パッチの両方に反映する。</summary>
    public RenderTextureResource?EmissiveTexture { get; init;}
    /// <summary>glTF 標準のパック済みテクスチャ。G はラフネス、B はメタリック。</summary>
    public RenderTextureResource?MetallicRoughnessTexture { get; init;}
    /// <summary>通常のマテリアルでは null、そうでなければベースカラーの上にワールド空間で描画するプロシージャルグリッド（M0098）。</summary>
    public StageWorldGrid?WorldGrid { get; init;}
}
/// <summary>ワールド位置から各ピクセルのグリッドを生成するため、メッシュの大きさや UV にかかわらず床や壁で同じ大きさを保つ（シェーダーが支配的なワールド軸に沿って投影する）。マテリアルのベースカラーは線の間の表面色。</summary>
/// <param name="GridSizeMeters">主グリッド線の間隔。</param>
/// <param name="SubGridDivisions">主グリッドの1区画に含める補助グリッド区画の数。</param>
/// <param name="WireHalfWidth">各区画の幅に対する線幅の半分の割合（主グリッドと補助グリッドで共通）。</param>
/// <param name="GridColor">主グリッド線のリニア RGB 色。</param>
/// <param name="SubGridColor">補助グリッド線のリニア RGB 色。</param>
public readonly record struct StageWorldGrid(float GridSizeMeters,int SubGridDivisions,float WireHalfWidth,Vector3 GridColor,Vector3 SubGridColor);
/// <summary>描画要素1つ。メッシュとマテリアルの組み合わせ。複数のオブジェクトで同じメッシュやマテリアルのインデックスを参照できる（1つの焼き付け済みメッシュを共有する20個の立方体、または単色マテリアルを共有する形状の異なる2つの小道具など）。</summary>
public readonly struct StageObjectCreateInfo {
    public required int MeshIndex { get; init;}
    public required int MaterialIndex { get; init;}
    /// <summary>空きスロット。作成後にオブジェクトが削除された状態（M0105）。デバイス消失後にレンダラーを再構築するとき、Render Server だけが設定する。</summary>
    public bool Removed { get; init;}
}
/// <summary>簡易指数高さフォグ（ADR-0018）。UE の独立した第2フォグ層や散乱キューブマップ機能はモデル化せず、地面（Y=0）より高いほど密度を下げる。</summary>
public readonly record struct FogParams(float Density,float HeightFalloff,Vector3 Color){
    public static FogParams None=>new(0f,0f,Vector3.Zero);
}
