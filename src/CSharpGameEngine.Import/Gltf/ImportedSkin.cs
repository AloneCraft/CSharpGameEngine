using System.Numerics;
namespace CSharpGameEngine.Import.Gltf;
/// <summary>
/// エンジン座標系における glTF スキン1つ（ADR-0066）。関節はスキン内の記載順に並び、頂点の <see cref="ImportedSkinInfluences"/> はこの順序を
/// インデックスとして参照する。静止姿勢は各関節のローカル変換。姿勢に対するパレットは <c>InverseBindMatrices[j] * JointModel[j]</c>（行ベクトル規約）で求める
/// 。ルート関節のモデル行列は <see cref="SkeletonTransform"/> から始まる。
/// </summary>
public sealed class ImportedSkin {
    public required IReadOnlyList<string>JointNames { get; init;}
    /// <summary>各関節の親関節のインデックス。ルート関節なら -1。</summary>
    public required IReadOnlyList<int>ParentIndices { get; init;}
    public required IReadOnlyList<Vector3>RestTranslations { get; init;}
    public required IReadOnlyList<Quaternion>RestRotations { get; init;}
    public required IReadOnlyList<Vector3>RestScales { get; init;}
    public required IReadOnlyList<Matrix4x4>InverseBindMatrices { get; init;}
    /// <summary>ルート関節の親ノードのワールド変換（ルート関節がシーンルートなら単位変換）。</summary>
    public required Matrix4x4 SkeletonTransform { get; init;}
}
/// <summary>頂点ごとのスキン影響。各頂点に <see cref="InfluencesPerVertex"/> 個の関節インデックスと重みを持ち、重みの合計は1に正規化する。未使用スロットの重みは0。</summary>
public sealed class ImportedSkinInfluences {
    public const int InfluencesPerVertex=8;
    public required IReadOnlyList<ushort>JointIndices { get; init;}
    public required IReadOnlyList<float>Weights { get; init;}
}
