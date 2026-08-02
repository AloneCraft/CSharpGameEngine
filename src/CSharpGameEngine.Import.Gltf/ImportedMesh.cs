using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

public sealed class ImportedMesh
{
    public required IReadOnlyList<Vector3> Positions { get; init; }

    public required IReadOnlyList<Vector3> Normals { get; init; }

    public required IReadOnlyList<Vector2> Uvs { get; init; }

    /// <summary>Raw encoded (e.g. PNG/JPEG) base-color texture bytes, undecoded. Null if the material has no texture.</summary>
    public byte[]? BaseColorTextureBytes { get; init; }

    /// <summary>glTF baseColorFactor tint; defaults to (1,1,1,1) when the primitive has no material.</summary>
    public required Vector4 BaseColorFactor { get; init; }

    /// <summary>Triangle list; always a multiple of 3.</summary>
    public required IReadOnlyList<uint> Indices { get; init; }

    public required BoundingBox Bounds { get; init; }
}
