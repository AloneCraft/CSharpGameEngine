using System.Numerics;

namespace CSharpGameEngine.Import.Fbx;

/// <summary>
/// Mirrors <c>CSharpGameEngine.Import.Gltf.ImportedMesh</c>'s shape so callers can treat glTF and
/// FBX imports uniformly, without a shared/reference dependency between the two importers
/// (deliberate duplication - see ADR-0004 §3's precedent and ADR-0009).
/// </summary>
public sealed class ImportedMesh {
    public required IReadOnlyList<Vector3>Positions { get; init;}

    public required IReadOnlyList<Vector3>Normals { get; init;}

    public required IReadOnlyList<Vector2>Uvs { get; init;}

    /// <summary>Raw encoded (e.g. PNG/JPEG) base-color texture bytes, undecoded. Null if the material has no embedded texture.</summary>
    public byte[]?BaseColorTextureBytes { get; init;}

    /// <summary>Base-color tint; defaults to (1,1,1,1) when the mesh has no material.</summary>
    public required Vector4 BaseColorFactor { get; init;}

    /// <summary>Triangle list; always a multiple of 3.</summary>
    public required IReadOnlyList<uint>Indices { get; init;}

    public required BoundingBox Bounds { get; init;}
}
