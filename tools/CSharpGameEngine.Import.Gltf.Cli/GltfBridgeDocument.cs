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

    public static GltfBridgeDocument ForMesh(ImportedMesh Mesh) => new()
    {
        Success = true,
        Positions = Flatten3(Mesh.Positions),
        Normals = Flatten3(Mesh.Normals),
        Uvs = Flatten2(Mesh.Uvs),
        Indices = [.. Mesh.Indices],
        BaseColorFactor = [Mesh.BaseColorFactor.X, Mesh.BaseColorFactor.Y, Mesh.BaseColorFactor.Z, Mesh.BaseColorFactor.W],
    };

    public static GltfBridgeDocument ForFailure() => new() { Success = false };

    private static float[] Flatten3(IReadOnlyList<System.Numerics.Vector3> Values)
    {
        float[] Flat = new float[Values.Count * 3];
        for (int i = 0; i < Values.Count; i++)
        {
            Flat[(i * 3) + 0] = Values[i].X;
            Flat[(i * 3) + 1] = Values[i].Y;
            Flat[(i * 3) + 2] = Values[i].Z;
        }

        return Flat;
    }

    private static float[] Flatten2(IReadOnlyList<System.Numerics.Vector2> Values)
    {
        float[] Flat = new float[Values.Count * 2];
        for (int i = 0; i < Values.Count; i++)
        {
            Flat[(i * 2) + 0] = Values[i].X;
            Flat[(i * 2) + 1] = Values[i].Y;
        }

        return Flat;
    }
}
