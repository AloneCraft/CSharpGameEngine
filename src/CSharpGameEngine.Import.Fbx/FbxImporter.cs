using System.Numerics;
using Assimp;

namespace CSharpGameEngine.Import.Fbx;

/// <summary>
/// Imports the first mesh of an .fbx file via AssimpNet (ADR-0009). No scene graph, multi-mesh,
/// or full PBR material support - minimal vertical slice, matching
/// <c>CSharpGameEngine.Import.Gltf.GltfImporter</c>'s scope for glTF.
/// </summary>
public static class FbxImporter
{
    public static FbxImportResult Import(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            return Failed($"FBX file not found: {fullPath}");
        }

        // Assimp's own native logging (warnings/errors from the FBX parser itself, e.g. unknown
        // elements in a particular exporter's output) is collected as diagnostics rather than
        // written straight to the console, matching GltfImporter's contract - callers decide how
        // to surface diagnostics. An "Error"-level Assimp log line does not necessarily mean the
        // import failed overall (confirmed empirically); only Import()'s own explicit checks
        // below (empty scene, no vertices, no triangles) are treated as fatal.
        List<FbxImportDiagnostic> diagnostics = [];
        LogStream logStream = new((msg, _) =>
        {
            string trimmed = msg.Trim();
            if (trimmed.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Warn", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Warning, $"Assimp: {trimmed}"));
            }
        });
        logStream.Attach();

        using AssimpContext context = new();
        Scene scene;
        try
        {
            scene = context.ImportFile(
                fullPath,
                PostProcessSteps.Triangulate | PostProcessSteps.GenerateSmoothNormals | PostProcessSteps.JoinIdenticalVertices);
        }
        catch (Exception ex)
        {
            // Assimp has no single documented exhaustive exception list for malformed input; a
            // bad file must never escape as an unhandled exception from this importer.
            return Failed($"Failed to parse FBX file '{fullPath}': {ex.Message}");
        }
        finally
        {
            logStream.Detach();
        }

        if (scene is null || scene.MeshCount == 0)
        {
            return Failed("FBX file contains no meshes.", diagnostics);
        }

        Assimp.Mesh mesh = scene.Meshes[0];
        if (mesh.VertexCount == 0)
        {
            return Failed("FBX mesh has no vertices.", diagnostics);
        }

        Vector3[] positions = new Vector3[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            Vector3D v = mesh.Vertices[i];
            positions[i] = new Vector3(v.X, v.Y, v.Z);
        }

        Vector3[] normals;
        if (mesh.HasNormals)
        {
            normals = new Vector3[mesh.VertexCount];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Vector3D n = mesh.Normals[i];
                normals[i] = new Vector3(n.X, n.Y, n.Z);
            }
        }
        else
        {
            diagnostics.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Warning,
                "Mesh has no normals even after GenerateSmoothNormals; defaulting to +Y for every vertex."));
            normals = new Vector3[mesh.VertexCount];
            Array.Fill(normals, Vector3.UnitY);
        }

        Vector2[] uvs;
        if (mesh.HasTextureCoords(0))
        {
            uvs = new Vector2[mesh.VertexCount];
            List<Vector3D> channel = mesh.TextureCoordinateChannels[0];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Vector3D uv = channel[i];
                uvs[i] = new Vector2(uv.X, uv.Y);
            }
        }
        else
        {
            diagnostics.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Warning,
                "Mesh has no TEXCOORD channel 0; synthesized (0,0) UV for every vertex."));
            uvs = new Vector2[mesh.VertexCount];
        }

        List<uint> indices = [];
        foreach (Face face in mesh.Faces)
        {
            if (face.IndexCount != 3)
            {
                // Triangulate should guarantee this; skip any degenerate leftovers defensively.
                continue;
            }

            indices.Add((uint)face.Indices[0]);
            indices.Add((uint)face.Indices[1]);
            indices.Add((uint)face.Indices[2]);
        }

        if (indices.Count == 0)
        {
            return Failed("FBX mesh has no triangles after triangulation.", diagnostics);
        }

        Vector4 baseColorFactor = new(1f, 1f, 1f, 1f);
        if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < scene.MaterialCount)
        {
            Assimp.Material material = scene.Materials[mesh.MaterialIndex];
            if (material.HasColorDiffuse)
            {
                Color4D c = material.ColorDiffuse;
                baseColorFactor = new Vector4(c.R, c.G, c.B, c.A);
            }
        }

        ImportedMesh importedMesh = new()
        {
            Positions = positions,
            Normals = normals,
            Uvs = uvs,
            BaseColorTextureBytes = null,
            BaseColorFactor = baseColorFactor,
            Indices = indices,
            Bounds = BoundingBox.FromPoints(positions),
        };

        return FbxImportResult.Succeeded(importedMesh, diagnostics);
    }

    private static FbxImportResult Failed(string message, IReadOnlyList<FbxImportDiagnostic>? extra = null)
    {
        List<FbxImportDiagnostic> all = extra is null ? [] : [.. extra];
        all.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Error, message));
        return FbxImportResult.Failed(all);
    }
}
