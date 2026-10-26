using System.Numerics;
namespace CSharpGameEngine.Rendering;
/// <summary>発光三角形を決定論的に長方形で近似したもの。<see cref="Forward"/> 側だけが発光し、背面からの直接光への寄与はゼロ。</summary>
public readonly record struct EmissivePatch(
    Vector3 Center,
    Vector3 Forward,
    Vector3 Right,
    Vector3 Up,
    Vector3 LinearRgb,
    float LuminanceNits,
    float WidthMeters,
    float HeightMeters,
    int SourceTriangleCount);
/// <summary>オブジェクト移動後に発光パッチを再構築するための形状とマテリアル状態。</summary>
public sealed class EmissivePatchBuildInput {
    public required IReadOnlyList<Vector3> Positions { get; init; }
    public required IReadOnlyList<uint> Indices { get; init; }
    public required Matrix4x4 ObjectWorld { get; init; }
    public required Vector3 EmissiveNits { get; init; }
    /// <summary>発光テクスチャからサンプリングした平均リニア輝度。後でテクスチャデコーダーが値を精密化できる。値が1なら、因子と強度だけで決まる glTF マテリアルの結果を維持する。</summary>
    public float EmissiveTextureLuminanceScale { get; init; }=1f;
    public int MaxPatches { get; init; }=16;
}
