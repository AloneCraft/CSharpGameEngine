using System.Numerics;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>Plain positions/normals/indices, ready to feed directly into <see cref="StageRenderer"/> - no importer needed for these primitive shapes.</summary>
public readonly struct ProceduralMeshData
{
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required uint[] Indices { get; init; }
}

/// <summary>
/// Builds simple placeholder meshes (ground plane, box) directly in code. No procedural mesh
/// generator existed anywhere in this repo before - every prior mesh came from an imported
/// glTF/FBX file (see <see cref="CSharpGameEngine.Import.Gltf.GltfImporter"/>/
/// <c>CSharpGameEngine.Import.Fbx.FbxImporter</c>). Winding order is picked for consistency but
/// does not currently affect visibility: <see cref="StageRenderer"/>, like <see cref="ModelRenderer"/>,
/// disables backface culling (ADR-0004 §"real backface culling is deferred").
/// </summary>
public static class ProceduralMeshFactory
{
    /// <summary>An XZ-plane rectangle centered at the origin, Y=0, facing +Y. Used as the ground.</summary>
    public static ProceduralMeshData CreatePlane(float width, float depth)
    {
        if (width <= 0f || depth <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "width and depth must both be positive.");
        }

        float halfWidth = width / 2f;
        float halfDepth = depth / 2f;

        Vector3[] positions =
        [
            new Vector3(-halfWidth, 0f, -halfDepth),
            new Vector3(halfWidth, 0f, -halfDepth),
            new Vector3(halfWidth, 0f, halfDepth),
            new Vector3(-halfWidth, 0f, halfDepth),
        ];

        Vector3[] normals = [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY];

        uint[] indices = [0, 1, 2, 0, 2, 3];

        return new ProceduralMeshData { Positions = positions, Normals = normals, Indices = indices };
    }

    /// <summary>An axis-aligned box centered at the origin (24 vertices, one normal per face) sized <paramref name="size"/> on each axis.</summary>
    public static ProceduralMeshData CreateBox(Vector3 size)
    {
        if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "size components must all be positive.");
        }

        Vector3 half = size / 2f;

        (Vector3 Normal, Vector3 U, Vector3 V)[] faces =
        [
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),     // +Z
            (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),   // -Z
            (Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY),    // +X
            (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),    // -X
            (Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ),    // +Y
            (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),    // -Y
        ];

        Vector3[] positions = new Vector3[faces.Length * 4];
        Vector3[] normals = new Vector3[faces.Length * 4];
        uint[] indices = new uint[faces.Length * 6];

        for (int face = 0; face < faces.Length; face++)
        {
            (Vector3 normal, Vector3 u, Vector3 v) = faces[face];
            Vector3 center = normal * half;
            int vertexBase = face * 4;

            positions[vertexBase + 0] = center - (u * half) - (v * half);
            positions[vertexBase + 1] = center + (u * half) - (v * half);
            positions[vertexBase + 2] = center + (u * half) + (v * half);
            positions[vertexBase + 3] = center - (u * half) + (v * half);

            for (int i = 0; i < 4; i++)
            {
                normals[vertexBase + i] = normal;
            }

            int indexBase = face * 6;
            indices[indexBase + 0] = (uint)(vertexBase + 0);
            indices[indexBase + 1] = (uint)(vertexBase + 1);
            indices[indexBase + 2] = (uint)(vertexBase + 2);
            indices[indexBase + 3] = (uint)(vertexBase + 0);
            indices[indexBase + 4] = (uint)(vertexBase + 2);
            indices[indexBase + 5] = (uint)(vertexBase + 3);
        }

        return new ProceduralMeshData { Positions = positions, Normals = normals, Indices = indices };
    }
}
