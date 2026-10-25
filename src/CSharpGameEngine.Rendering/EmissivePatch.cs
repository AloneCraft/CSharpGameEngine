using System.Numerics;
namespace CSharpGameEngine.Rendering;
/// <summary>
/// A deterministic rectangular approximation of emissive triangles. <see cref="Forward"/> is the
/// one emitting side; its back face has zero direct-light contribution.
/// </summary>
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
/// <summary>Geometry and material state used to rebuild emissive patches after an object moves.</summary>
public sealed class EmissivePatchBuildInput {
    public required IReadOnlyList<Vector3> Positions { get; init; }
    public required IReadOnlyList<uint> Indices { get; init; }
    public required Matrix4x4 ObjectWorld { get; init; }
    public required Vector3 EmissiveNits { get; init; }
    /// <summary>
    /// Mean linear luminance sampled from the emissive texture. A texture decoder may refine this
    /// value later; one preserves the factor/strength-only glTF material result.
    /// </summary>
    public float EmissiveTextureLuminanceScale { get; init; }=1f;
    public int MaxPatches { get; init; }=16;
}
