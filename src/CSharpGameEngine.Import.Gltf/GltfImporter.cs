using System.Numerics;
using SharpGLTF.Schema2;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// Imports the first mesh/primitive of a .gltf or .glb file, including its base-color texture
/// (raw encoded bytes, undecoded) and baseColorFactor. No scene graph, multi-mesh, or full PBR
/// material support - this is a minimal vertical slice, not a general glTF importer.
/// </summary>
public static class GltfImporter
{
    public static GltfImportResult Import(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            return Failed($"glTF file not found: {fullPath}");
        }

        ModelRoot model;
        try
        {
            model = ModelRoot.Load(fullPath);
        }
        catch (Exception ex)
        {
            // SharpGLTF has no single documented exhaustive exception list for malformed input;
            // a bad file must never escape as an unhandled exception from this importer.
            return Failed($"Failed to parse glTF file '{fullPath}': {ex.Message}");
        }

        if (model.LogicalMeshes.Count == 0)
        {
            return Failed("glTF file contains no meshes.");
        }

        Mesh mesh = model.LogicalMeshes[0];
        if (mesh.Primitives.Count == 0)
        {
            return Failed("glTF mesh contains no primitives.");
        }

        MeshPrimitive primitive = mesh.Primitives[0];

        Accessor? positionAccessor = primitive.GetVertexAccessor("POSITION");
        if (positionAccessor is null)
        {
            return Failed("glTF primitive has no POSITION attribute.");
        }

        Vector3[] rawPositions = [.. positionAccessor.AsVector3Array()];

        Accessor? normalAccessor = primitive.GetVertexAccessor("NORMAL");
        Vector3[]? rawNormals = normalAccessor is null ? null : [.. normalAccessor.AsVector3Array()];
        bool normalsUsable = rawNormals is not null && rawNormals.Length == rawPositions.Length;

        Accessor? uvAccessor = primitive.GetVertexAccessor("TEXCOORD_0");
        Vector2[]? rawUvs = uvAccessor is null ? null : [.. uvAccessor.AsVector2Array()];
        bool uvsUsable = rawUvs is not null && rawUvs.Length == rawPositions.Length;

        Material? material = primitive.Material;
        MaterialChannel? baseColorChannel = material?.FindChannel("BaseColor");
        Vector4 baseColorFactor = baseColorChannel?.Color ?? new Vector4(1f, 1f, 1f, 1f);
        byte[]? baseColorTextureBytes = baseColorChannel?.Texture?.PrimaryImage is Image image
            ? image.Content.Content.ToArray()
            : null;

        List<(int A, int B, int C)> triangles = [.. primitive.GetTriangleIndices()];
        if (triangles.Count == 0)
        {
            return Failed("glTF primitive has no triangles.");
        }

        List<GltfImportDiagnostic> diagnostics = [];
        Vector3[] positions;
        Vector3[] normals;
        Vector2[] uvs;
        uint[] indices;

        if (!uvsUsable)
        {
            diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,
                "TEXCOORD_0 attribute missing or inconsistent; synthesized (0,0) UV for every vertex."));
        }

        if (normalsUsable)
        {
            positions = rawPositions;
            normals = rawNormals!;
            uvs = uvsUsable ? rawUvs! : new Vector2[rawPositions.Length];
            indices = new uint[triangles.Count * 3];

            for (int i = 0; i < triangles.Count; i++)
            {
                indices[(i * 3) + 0] = (uint)triangles[i].A;
                indices[(i * 3) + 1] = (uint)triangles[i].B;
                indices[(i * 3) + 2] = (uint)triangles[i].C;
            }
        }
        else
        {
            // A vertex shared by multiple faces can only hold one NORMAL value, so a missing/
            // inconsistent NORMAL attribute cannot be patched in-place: every triangle is expanded
            // into 3 unshared vertices carrying one computed flat face normal. UV rides along in
            // the same expansion (driven solely by the NORMAL case) rather than a second pass.
            diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,
                $"NORMAL attribute missing or inconsistent; synthesized {triangles.Count} flat per-face normal(s)."));

            positions = new Vector3[triangles.Count * 3];
            normals = new Vector3[triangles.Count * 3];
            uvs = new Vector2[triangles.Count * 3];
            indices = new uint[triangles.Count * 3];

            for (int i = 0; i < triangles.Count; i++)
            {
                (int a, int b, int c) = triangles[i];
                Vector3 pa = rawPositions[a];
                Vector3 pb = rawPositions[b];
                Vector3 pc = rawPositions[c];

                Vector3 faceNormalUnnormalized = Vector3.Cross(pb - pa, pc - pa);
                Vector3 faceNormal = faceNormalUnnormalized.LengthSquared() > 1e-12f
                    ? Vector3.Normalize(faceNormalUnnormalized)
                    : Vector3.UnitY;

                int baseIndex = i * 3;
                positions[baseIndex + 0] = pa;
                positions[baseIndex + 1] = pb;
                positions[baseIndex + 2] = pc;
                normals[baseIndex + 0] = faceNormal;
                normals[baseIndex + 1] = faceNormal;
                normals[baseIndex + 2] = faceNormal;
                uvs[baseIndex + 0] = uvsUsable ? rawUvs![a] : Vector2.Zero;
                uvs[baseIndex + 1] = uvsUsable ? rawUvs![b] : Vector2.Zero;
                uvs[baseIndex + 2] = uvsUsable ? rawUvs![c] : Vector2.Zero;
                indices[baseIndex + 0] = (uint)(baseIndex + 0);
                indices[baseIndex + 1] = (uint)(baseIndex + 1);
                indices[baseIndex + 2] = (uint)(baseIndex + 2);
            }
        }

        ImportedMesh importedMesh = new()
        {
            Positions = positions,
            Normals = normals,
            Uvs = uvs,
            BaseColorTextureBytes = baseColorTextureBytes,
            BaseColorFactor = baseColorFactor,
            Indices = indices,
            Bounds = BoundingBox.FromPoints(positions),
        };

        return GltfImportResult.Succeeded(importedMesh, diagnostics);
    }

    private static GltfImportResult Failed(string message) =>
        GltfImportResult.Failed([new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Error, message)]);
}
