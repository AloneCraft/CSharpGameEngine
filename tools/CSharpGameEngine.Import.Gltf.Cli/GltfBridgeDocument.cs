using CSharpGameEngine.Import.Gltf;

namespace CSharpGameEngine.Import.Gltf.Cli;

/// <summary>
/// Flat, JSON-friendly view of an <see cref="ImportedMesh"/> - the out-of-process counterpart to
/// <c>CSharpGameEngine.Import.Fbx.Cli</c>'s <c>FbxBridgeDocument</c> (ADR-0010). Kept deliberately
/// primitive (flat float arrays, no custom types) so the net472 VSIX side never needs to reference
/// <c>System.Numerics.Vector3</c>/<c>Vector4</c> serialization behavior or this net10.0 assembly.
/// </summary>
public sealed class GltfBridgeDocument
{
    public bool Success { get; init; }

    public float[] Positions { get; init; } = [];

    public float[] Normals { get; init; } = [];

    public float[] Uvs { get; init; } = [];

    public uint[] Indices { get; init; } = [];

    public float[] BaseColorFactor { get; init; } = [1f, 1f, 1f, 1f];

    public static GltfBridgeDocument ForMesh(ImportedMesh mesh) => new()
    {
        Success = true,
        Positions = Flatten3(mesh.Positions),
        Normals = Flatten3(mesh.Normals),
        Uvs = Flatten2(mesh.Uvs),
        Indices = [.. mesh.Indices],
        BaseColorFactor = [mesh.BaseColorFactor.X, mesh.BaseColorFactor.Y, mesh.BaseColorFactor.Z, mesh.BaseColorFactor.W],
    };

    public static GltfBridgeDocument ForFailure() => new() { Success = false };

    private static float[] Flatten3(IReadOnlyList<System.Numerics.Vector3> values)
    {
        float[] flat = new float[values.Count * 3];
        for (int i = 0; i < values.Count; i++)
        {
            flat[(i * 3) + 0] = values[i].X;
            flat[(i * 3) + 1] = values[i].Y;
            flat[(i * 3) + 2] = values[i].Z;
        }

        return flat;
    }

    private static float[] Flatten2(IReadOnlyList<System.Numerics.Vector2> values)
    {
        float[] flat = new float[values.Count * 2];
        for (int i = 0; i < values.Count; i++)
        {
            flat[(i * 2) + 0] = values[i].X;
            flat[(i * 2) + 1] = values[i].Y;
        }

        return flat;
    }
}
