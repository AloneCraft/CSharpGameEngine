using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>One glTF mesh primitive's geometry, used by <see cref="GltfImporter.ImportScene"/>. Unlike <see cref="ImportedMesh"/> this carries no material - materials are shared/indexed separately in <see cref="ImportedScene"/> since multiple nodes can reference the same baked mesh+material pair.</summary>
public sealed class ImportedMeshData {
    public required IReadOnlyList<Vector3>Positions { get; init;}

    public required IReadOnlyList<Vector3>Normals { get; init;}

    /// <summary>xyz = tangent direction, w = bitangent sign (+1/-1), per glTF TANGENT convention.</summary>
    public required IReadOnlyList<Vector4>Tangents { get; init;}

    public required IReadOnlyList<Vector2>Uvs { get; init;}

    /// <summary>Triangle list; always a multiple of 3.</summary>
    public required IReadOnlyList<uint>Indices { get; init;}
}
