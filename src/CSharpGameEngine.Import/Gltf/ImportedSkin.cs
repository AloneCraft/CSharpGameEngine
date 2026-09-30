using System.Numerics;
namespace CSharpGameEngine.Import.Gltf;
/// <summary>
/// One glTF skin in engine space (ADR-0066). Joints are ordered as the skin lists them; a vertex's
/// <see cref="ImportedSkinInfluences"/> index this order. Rest poses are the joints' local
/// transforms; the palette for a pose is <c>InverseBindMatrices[j] * JointModel[j]</c>
/// (row-vector convention), where a root joint's model matrix starts from <see cref="SkeletonTransform"/>.
/// </summary>
public sealed class ImportedSkin {
    public required IReadOnlyList<string>JointNames { get; init;}
    /// <summary>Index of each joint's parent joint, or -1 for a root joint.</summary>
    public required IReadOnlyList<int>ParentIndices { get; init;}
    public required IReadOnlyList<Vector3>RestTranslations { get; init;}
    public required IReadOnlyList<Quaternion>RestRotations { get; init;}
    public required IReadOnlyList<Vector3>RestScales { get; init;}
    public required IReadOnlyList<Matrix4x4>InverseBindMatrices { get; init;}
    /// <summary>World transform of the root joints' parent node (identity when the roots are scene roots).</summary>
    public required Matrix4x4 SkeletonTransform { get; init;}
}
/// <summary>
/// Per-vertex skin influences: <see cref="InfluencesPerVertex"/> joint indices and weights per vertex,
/// weights normalized to sum to one; unused slots have weight zero.
/// </summary>
public sealed class ImportedSkinInfluences {
    public const int InfluencesPerVertex=8;
    public required IReadOnlyList<ushort>JointIndices { get; init;}
    public required IReadOnlyList<float>Weights { get; init;}
}
