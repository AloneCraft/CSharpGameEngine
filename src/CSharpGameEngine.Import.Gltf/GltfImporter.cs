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

    /// <summary>
    /// Imports every mesh-bearing and light-bearing node of a glTF scene graph, flattened into
    /// <see cref="ImportedScene.Nodes"/> with fully-resolved world transforms (see
    /// <see cref="ImportedSceneNode"/>). Unlike <see cref="Import(string)"/> this walks the whole
    /// node hierarchy and every material's Normal/MetallicRoughness channels - needed to port a
    /// multi-object level rather than preview a single asset. Existing callers of
    /// <see cref="Import(string)"/> are unaffected; this is a separate, additive entry point.
    /// </summary>
    public static GltfSceneImportResult ImportScene(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            return SceneFailed($"glTF file not found: {fullPath}");
        }

        ModelRoot model;
        try
        {
            model = ModelRoot.Load(fullPath);
        }
        catch (Exception ex)
        {
            // Mirrors Import(string): SharpGLTF has no single documented exhaustive exception list
            // for malformed input, so a bad file must never escape as an unhandled exception.
            return SceneFailed($"Failed to parse glTF file '{fullPath}': {ex.Message}");
        }

        if (model.DefaultScene is null)
        {
            return SceneFailed("glTF file has no default scene.");
        }

        List<GltfImportDiagnostic> diagnostics = [];

        var meshIndexByGltfMesh = new Dictionary<Mesh, List<(int PrimitiveIndex, int MeshIndex)>>();
        List<ImportedMeshData> meshes = [];
        var materialIndexByGltfMaterial = new Dictionary<Material, int>();
        List<ImportedPbrMaterial> materials = [];
        List<ImportedLight> lights = [];
        var lightIndexByGltfLight = new Dictionary<PunctualLight, int>();
        List<ImportedSceneNode> nodes = [];

        foreach (Node node in Flatten(model.DefaultScene.VisualChildren))
        {
            Mesh? mesh = node.Mesh;
            PunctualLight? light = node.PunctualLight;

            if (mesh is null && light is null)
            {
                continue;
            }

            int? lightIndex = null;
            if (light is not null)
            {
                if (!lightIndexByGltfLight.TryGetValue(light, out int existingLightIndex))
                {
                    existingLightIndex = lights.Count;
                    lights.Add(ImportLight(light));
                    lightIndexByGltfLight[light] = existingLightIndex;
                }
                lightIndex = existingLightIndex;
            }

            if (mesh is null)
            {
                nodes.Add(new ImportedSceneNode
                {
                    Name = node.Name ?? string.Empty,
                    WorldTransform = node.WorldMatrix,
                    LightIndex = lightIndex,
                });
                continue;
            }

            if (!meshIndexByGltfMesh.TryGetValue(mesh, out List<(int PrimitiveIndex, int MeshIndex)>? primitiveMeshIndices))
            {
                primitiveMeshIndices = [];
                for (int primitiveIndex = 0; primitiveIndex < mesh.Primitives.Count; primitiveIndex++)
                {
                    MeshPrimitive primitive = mesh.Primitives[primitiveIndex];
                    ImportedMeshData? meshData = ImportPrimitiveGeometry(primitive, diagnostics, mesh.Name ?? node.Name ?? "(unnamed)");
                    if (meshData is null)
                    {
                        continue;
                    }

                    primitiveMeshIndices.Add((primitiveIndex, meshes.Count));
                    meshes.Add(meshData);
                }
                meshIndexByGltfMesh[mesh] = primitiveMeshIndices;
            }

            foreach ((int primitiveIndex, int meshIndex) in primitiveMeshIndices)
            {
                MeshPrimitive primitive = mesh.Primitives[primitiveIndex];
                Material? material = primitive.Material;

                int materialIndex;
                if (material is null)
                {
                    materialIndex = GetOrAddDefaultMaterial(materials, materialIndexByGltfMaterial);
                }
                else if (!materialIndexByGltfMaterial.TryGetValue(material, out materialIndex))
                {
                    materialIndex = materials.Count;
                    materials.Add(ImportMaterial(material));
                    materialIndexByGltfMaterial[material] = materialIndex;
                }

                nodes.Add(new ImportedSceneNode
                {
                    Name = node.Name ?? string.Empty,
                    WorldTransform = node.WorldMatrix,
                    MeshIndex = meshIndex,
                    MaterialIndex = materialIndex,
                    LightIndex = lightIndex,
                });
            }
        }

        ImportedScene scene = new()
        {
            Nodes = nodes,
            Meshes = meshes,
            Materials = materials,
            Lights = lights,
        };

        return GltfSceneImportResult.Succeeded(scene, diagnostics);
    }

    /// <summary>Depth-first walk of the full visual node hierarchy - top-level scene children are not guaranteed to be the only nodes (a source scene may nest transform/pivot nodes), so every descendant is visited.</summary>
    private static IEnumerable<Node> Flatten(IEnumerable<Node> roots)
    {
        foreach (Node node in roots)
        {
            yield return node;
            foreach (Node descendant in Flatten(node.VisualChildren))
            {
                yield return descendant;
            }
        }
    }

    private static int GetOrAddDefaultMaterial(List<ImportedPbrMaterial> materials, Dictionary<Material, int> materialIndexByGltfMaterial)
    {
        // Keyed by null via a sentinel-free path: a primitive with no Material at all is rare in
        // practice for this importer's real content, so it's not worth a Dictionary<Material?,int>
        // (Material is a reference type but SharpGLTF never hands out a boxed "null key" instance).
        for (int i = 0; i < materials.Count; i++)
        {
            ImportedPbrMaterial candidate = materials[i];
            if (candidate is { BaseColorTextureBytes: null, NormalTextureBytes: null, MetallicRoughnessTextureBytes: null }
                && candidate.BaseColorFactor == new Vector4(1f, 1f, 1f, 1f))
            {
                return i;
            }
        }

        materials.Add(new ImportedPbrMaterial
        {
            BaseColorFactor = new Vector4(1f, 1f, 1f, 1f),
            MetallicFactor = 1f,
            RoughnessFactor = 1f,
        });
        return materials.Count - 1;
    }

    private static ImportedPbrMaterial ImportMaterial(Material material)
    {
        MaterialChannel? baseColorChannel = material.FindChannel("BaseColor");
        MaterialChannel? normalChannel = material.FindChannel("Normal");
        MaterialChannel? metallicRoughnessChannel = material.FindChannel("MetallicRoughness");

        return new ImportedPbrMaterial
        {
            BaseColorFactor = baseColorChannel?.Color ?? new Vector4(1f, 1f, 1f, 1f),
            MetallicFactor = metallicRoughnessChannel?.GetFactor("MetallicFactor") ?? 1f,
            RoughnessFactor = metallicRoughnessChannel?.GetFactor("RoughnessFactor") ?? 1f,
            BaseColorTextureBytes = ExtractTextureBytes(baseColorChannel),
            NormalTextureBytes = ExtractTextureBytes(normalChannel),
            MetallicRoughnessTextureBytes = ExtractTextureBytes(metallicRoughnessChannel),
        };
    }

    private static byte[]? ExtractTextureBytes(MaterialChannel? channel) =>
        channel?.Texture?.PrimaryImage is Image image ? image.Content.Content.ToArray() : null;

    private static ImportedLight ImportLight(PunctualLight light) => new()
    {
        LightType = light.LightType switch
        {
            PunctualLightType.Point => ImportedLightType.Point,
            PunctualLightType.Spot => ImportedLightType.Spot,
            _ => ImportedLightType.Directional,
        },
        Color = light.Color,
        Intensity = light.Intensity,
    };

    /// <summary>Builds one drawable's geometry from a single glTF primitive, applying the same missing-NORMAL/TEXCOORD_0 fallbacks as <see cref="Import(string)"/> plus TANGENT generation (not needed by the single-mesh preview path). Returns null (with a diagnostic) only when POSITION or triangle data is entirely absent.</summary>
    private static ImportedMeshData? ImportPrimitiveGeometry(MeshPrimitive primitive, List<GltfImportDiagnostic> diagnostics, string context)
    {
        Accessor? positionAccessor = primitive.GetVertexAccessor("POSITION");
        if (positionAccessor is null)
        {
            diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{context}': primitive has no POSITION attribute; skipped."));
            return null;
        }

        Vector3[] rawPositions = [.. positionAccessor.AsVector3Array()];

        Accessor? normalAccessor = primitive.GetVertexAccessor("NORMAL");
        Vector3[]? rawNormals = normalAccessor is null ? null : [.. normalAccessor.AsVector3Array()];
        bool normalsUsable = rawNormals is not null && rawNormals.Length == rawPositions.Length;

        Accessor? uvAccessor = primitive.GetVertexAccessor("TEXCOORD_0");
        Vector2[]? rawUvs = uvAccessor is null ? null : [.. uvAccessor.AsVector2Array()];
        bool uvsUsable = rawUvs is not null && rawUvs.Length == rawPositions.Length;

        List<(int A, int B, int C)> triangles = [.. primitive.GetTriangleIndices()];
        if (triangles.Count == 0)
        {
            diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{context}': primitive has no triangles; skipped."));
            return null;
        }

        Vector3[] positions;
        Vector3[] normals;
        Vector2[] uvs;
        uint[] indices;

        if (!uvsUsable)
        {
            diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{context}': TEXCOORD_0 attribute missing or inconsistent; synthesized (0,0) UV for every vertex."));
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
            diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{context}': NORMAL attribute missing or inconsistent; synthesized {triangles.Count} flat per-face normal(s)."));

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

        Accessor? tangentAccessor = primitive.GetVertexAccessor("TANGENT");
        Vector4[]? rawTangents = tangentAccessor is null ? null : [.. tangentAccessor.AsVector4Array()];
        Vector4[] tangents = rawTangents is not null && rawTangents.Length == positions.Length
            ? rawTangents
            : ComputeTangents(positions, normals, uvs, indices);

        return new ImportedMeshData
        {
            Positions = positions,
            Normals = normals,
            Tangents = tangents,
            Uvs = uvs,
            Indices = indices,
        };
    }

    private static GltfSceneImportResult SceneFailed(string message) =>
        GltfSceneImportResult.Failed([new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Error, message)]);

    /// <summary>
    /// Per-vertex tangent generation (Lengyel's method: "Computing Tangent Space Basis Vectors for
    /// an Arbitrary Mesh") for primitives with no TANGENT accessor. Accumulates each triangle's UV-
    /// space tangent/bitangent onto its three vertices, then Gram-Schmidt-orthogonalizes against the
    /// (possibly-shared) vertex normal and derives the handedness sign (w) from the bitangent.
    /// </summary>
    private static Vector4[] ComputeTangents(IReadOnlyList<Vector3> positions, IReadOnlyList<Vector3> normals, IReadOnlyList<Vector2> uvs, IReadOnlyList<uint> indices)
    {
        int vertexCount = positions.Count;
        Vector3[] tan = new Vector3[vertexCount];
        Vector3[] bitan = new Vector3[vertexCount];

        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            uint i0 = indices[i];
            uint i1 = indices[i + 1];
            uint i2 = indices[i + 2];

            Vector3 edge1 = positions[(int)i1] - positions[(int)i0];
            Vector3 edge2 = positions[(int)i2] - positions[(int)i0];
            Vector2 deltaUv1 = uvs[(int)i1] - uvs[(int)i0];
            Vector2 deltaUv2 = uvs[(int)i2] - uvs[(int)i0];

            float denominator = (deltaUv1.X * deltaUv2.Y) - (deltaUv2.X * deltaUv1.Y);
            if (MathF.Abs(denominator) < 1e-12f)
            {
                continue;
            }

            float f = 1f / denominator;
            Vector3 tangent = f * ((deltaUv2.Y * edge1) - (deltaUv1.Y * edge2));
            Vector3 bitangent = f * ((deltaUv1.X * edge2) - (deltaUv2.X * edge1));

            tan[i0] += tangent; tan[i1] += tangent; tan[i2] += tangent;
            bitan[i0] += bitangent; bitan[i1] += bitangent; bitan[i2] += bitangent;
        }

        Vector4[] result = new Vector4[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 n = normals[i];
            Vector3 t = tan[i];

            Vector3 orthogonalized = t - (n * Vector3.Dot(n, t));
            Vector3 safeTangent = orthogonalized.LengthSquared() > 1e-12f
                ? Vector3.Normalize(orthogonalized)
                : ArbitraryTangentFor(n);

            float handedness = Vector3.Dot(Vector3.Cross(n, safeTangent), bitan[i]) < 0f ? -1f : 1f;
            result[i] = new Vector4(safeTangent, handedness);
        }

        return result;
    }

    private static Vector3 ArbitraryTangentFor(Vector3 normal)
    {
        Vector3 reference = MathF.Abs(normal.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        return Vector3.Normalize(Vector3.Cross(reference, normal));
    }
}
