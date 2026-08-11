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
    public static GltfImportResult Import(string FilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FilePath);

        string FullPath = Path.GetFullPath(FilePath);
        if (!File.Exists(FullPath))
        {
            return Failed($"glTF file not found: {FullPath}");
        }

        ModelRoot Model;
        try
        {
            Model = ModelRoot.Load(FullPath);
        }
        catch (Exception Ex)
        {
            // SharpGLTF has no single documented exhaustive exception list for malformed input;
            // a bad file must never escape as an unhandled exception from this importer.
            return Failed($"Failed to parse glTF file '{FullPath}': {Ex.Message}");
        }

        if (Model.LogicalMeshes.Count == 0)
        {
            return Failed("glTF file contains no meshes.");
        }

        Mesh mesh = Model.LogicalMeshes[0];
        if (mesh.Primitives.Count == 0)
        {
            return Failed("glTF mesh contains no primitives.");
        }

        MeshPrimitive Primitive = mesh.Primitives[0];

        Accessor? PositionAccessor = Primitive.GetVertexAccessor("POSITION");
        if (PositionAccessor is null)
        {
            return Failed("glTF primitive has no POSITION attribute.");
        }

        Vector3[] RawPositions = [.. PositionAccessor.AsVector3Array()];

        Accessor? NormalAccessor = Primitive.GetVertexAccessor("NORMAL");
        Vector3[]? RawNormals = NormalAccessor is null ? null : [.. NormalAccessor.AsVector3Array()];
        bool NormalsUsable = RawNormals is not null && RawNormals.Length == RawPositions.Length;

        Accessor? UvAccessor = Primitive.GetVertexAccessor("TEXCOORD_0");
        Vector2[]? RawUvs = UvAccessor is null ? null : [.. UvAccessor.AsVector2Array()];
        bool UvsUsable = RawUvs is not null && RawUvs.Length == RawPositions.Length;

        Material? material = Primitive.Material;
        MaterialChannel? BaseColorChannel = material?.FindChannel("BaseColor");
        Vector4 BaseColorFactor = BaseColorChannel?.Color ?? new Vector4(1f, 1f, 1f, 1f);
        byte[]? BaseColorTextureBytes = BaseColorChannel?.Texture?.PrimaryImage is Image image
            ? image.Content.Content.ToArray()
            : null;

        List<(int A, int B, int C)> Triangles = [.. Primitive.GetTriangleIndices()];
        if (Triangles.Count == 0)
        {
            return Failed("glTF primitive has no triangles.");
        }

        List<GltfImportDiagnostic> Diagnostics = [];
        Vector3[] Positions;
        Vector3[] Normals;
        Vector2[] Uvs;
        uint[] Indices;

        if (!UvsUsable)
        {
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,
                "TEXCOORD_0 attribute missing or inconsistent; synthesized (0,0) UV for every vertex."));
        }

        if (NormalsUsable)
        {
            Positions = RawPositions;
            Normals = RawNormals!;
            Uvs = UvsUsable ? RawUvs! : new Vector2[RawPositions.Length];
            Indices = new uint[Triangles.Count * 3];

            for (int i = 0; i < Triangles.Count; i++)
            {
                Indices[(i * 3) + 0] = (uint)Triangles[i].A;
                Indices[(i * 3) + 1] = (uint)Triangles[i].B;
                Indices[(i * 3) + 2] = (uint)Triangles[i].C;
            }
        }
        else
        {
            // A vertex shared by multiple faces can only hold one NORMAL value, so a missing/
            // inconsistent NORMAL attribute cannot be patched in-place: every triangle is expanded
            // into 3 unshared vertices carrying one computed flat face normal. UV rides along in
            // the same expansion (driven solely by the NORMAL case) rather than a second pass.
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,
                $"NORMAL attribute missing or inconsistent; synthesized {Triangles.Count} flat per-face normal(s)."));

            Positions = new Vector3[Triangles.Count * 3];
            Normals = new Vector3[Triangles.Count * 3];
            Uvs = new Vector2[Triangles.Count * 3];
            Indices = new uint[Triangles.Count * 3];

            for (int i = 0; i < Triangles.Count; i++)
            {
                (int A, int B, int C) = Triangles[i];
                Vector3 Pa = RawPositions[A];
                Vector3 Pb = RawPositions[B];
                Vector3 Pc = RawPositions[C];

                Vector3 FaceNormalUnnormalized = Vector3.Cross(Pb - Pa, Pc - Pa);
                Vector3 FaceNormal = FaceNormalUnnormalized.LengthSquared() > 1e-12f
                    ? Vector3.Normalize(FaceNormalUnnormalized)
                    : Vector3.UnitY;

                int BaseIndex = i * 3;
                Positions[BaseIndex + 0] = Pa;
                Positions[BaseIndex + 1] = Pb;
                Positions[BaseIndex + 2] = Pc;
                Normals[BaseIndex + 0] = FaceNormal;
                Normals[BaseIndex + 1] = FaceNormal;
                Normals[BaseIndex + 2] = FaceNormal;
                Uvs[BaseIndex + 0] = UvsUsable ? RawUvs![A] : Vector2.Zero;
                Uvs[BaseIndex + 1] = UvsUsable ? RawUvs![B] : Vector2.Zero;
                Uvs[BaseIndex + 2] = UvsUsable ? RawUvs![C] : Vector2.Zero;
                Indices[BaseIndex + 0] = (uint)(BaseIndex + 0);
                Indices[BaseIndex + 1] = (uint)(BaseIndex + 1);
                Indices[BaseIndex + 2] = (uint)(BaseIndex + 2);
            }
        }

        ImportedMesh importedMesh = new()
        {
            Positions = Positions,
            Normals = Normals,
            Uvs = Uvs,
            BaseColorTextureBytes = BaseColorTextureBytes,
            BaseColorFactor = BaseColorFactor,
            Indices = Indices,
            Bounds = BoundingBox.FromPoints(Positions),
        };

        return GltfImportResult.Succeeded(importedMesh, Diagnostics);
    }

    private static GltfImportResult Failed(string Message) =>
        GltfImportResult.Failed([new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Error, Message)]);

    /// <summary>
    /// Imports every mesh-bearing and light-bearing node of a glTF scene graph, flattened into
    /// <see cref="ImportedScene.Nodes"/> with fully-resolved world transforms (see
    /// <see cref="ImportedSceneNode"/>). Unlike <see cref="Import(string)"/> this walks the whole
    /// node hierarchy and every material's Normal/MetallicRoughness channels - needed to port a
    /// multi-object level rather than preview a single asset. Existing callers of
    /// <see cref="Import(string)"/> are unaffected; this is a separate, additive entry point.
    /// </summary>
    public static GltfSceneImportResult ImportScene(string FilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FilePath);

        string FullPath = Path.GetFullPath(FilePath);
        if (!File.Exists(FullPath))
        {
            return SceneFailed($"glTF file not found: {FullPath}");
        }

        ModelRoot Model;
        try
        {
            Model = ModelRoot.Load(FullPath);
        }
        catch (Exception Ex)
        {
            // Mirrors Import(string): SharpGLTF has no single documented exhaustive exception list
            // for malformed input, so a bad file must never escape as an unhandled exception.
            return SceneFailed($"Failed to parse glTF file '{FullPath}': {Ex.Message}");
        }

        if (Model.DefaultScene is null)
        {
            return SceneFailed("glTF file has no default scene.");
        }

        List<GltfImportDiagnostic> Diagnostics = [];

        var MeshIndexByGltfMesh = new Dictionary<Mesh, List<(int PrimitiveIndex, int MeshIndex)>>();
        List<ImportedMeshData> Meshes = [];
        var MaterialIndexByGltfMaterial = new Dictionary<Material, int>();
        List<ImportedPbrMaterial> Materials = [];
        List<ImportedLight> Lights = [];
        var LightIndexByGltfLight = new Dictionary<PunctualLight, int>();
        List<ImportedSceneNode> Nodes = [];

        foreach (Node node in Flatten(Model.DefaultScene.VisualChildren))
        {
            Mesh? mesh = node.Mesh;
            PunctualLight? Light = node.PunctualLight;

            if (mesh is null && Light is null)
            {
                continue;
            }

            int? LightIndex = null;
            if (Light is not null)
            {
                if (!LightIndexByGltfLight.TryGetValue(Light, out int ExistingLightIndex))
                {
                    ExistingLightIndex = Lights.Count;
                    Lights.Add(ImportLight(Light));
                    LightIndexByGltfLight[Light] = ExistingLightIndex;
                }
                LightIndex = ExistingLightIndex;
            }

            if (mesh is null)
            {
                Nodes.Add(new ImportedSceneNode
                {
                    Name = node.Name ?? string.Empty,
                    WorldTransform = GltfAxisConversion.ConvertWorldTransform(node.WorldMatrix),
                    LightIndex = LightIndex,
                });
                continue;
            }

            if (!MeshIndexByGltfMesh.TryGetValue(mesh, out List<(int PrimitiveIndex, int MeshIndex)>? PrimitiveMeshIndices))
            {
                PrimitiveMeshIndices = [];
                for (int primitiveIndex = 0; primitiveIndex < mesh.Primitives.Count; primitiveIndex++)
                {
                    MeshPrimitive Primitive = mesh.Primitives[primitiveIndex];
                    ImportedMeshData? MeshData = ImportPrimitiveGeometry(Primitive, Diagnostics, mesh.Name ?? node.Name ?? "(unnamed)");
                    if (MeshData is null)
                    {
                        continue;
                    }

                    PrimitiveMeshIndices.Add((primitiveIndex, Meshes.Count));
                    Meshes.Add(MeshData);
                }
                MeshIndexByGltfMesh[mesh] = PrimitiveMeshIndices;
            }

            foreach ((int PrimitiveIndex, int MeshIndex) in PrimitiveMeshIndices)
            {
                MeshPrimitive primitive = mesh.Primitives[PrimitiveIndex];
                Material? material = primitive.Material;

                int MaterialIndex;
                if (material is null)
                {
                    MaterialIndex = GetOrAddDefaultMaterial(Materials, MaterialIndexByGltfMaterial);
                }
                else if (!MaterialIndexByGltfMaterial.TryGetValue(material, out MaterialIndex))
                {
                    MaterialIndex = Materials.Count;
                    Materials.Add(ImportMaterial(material));
                    MaterialIndexByGltfMaterial[material] = MaterialIndex;
                }

                Nodes.Add(new ImportedSceneNode
                {
                    Name = node.Name ?? string.Empty,
                    WorldTransform = GltfAxisConversion.ConvertWorldTransform(node.WorldMatrix),
                    MeshIndex = MeshIndex,
                    MaterialIndex = MaterialIndex,
                    LightIndex = LightIndex,
                });
            }
        }

        ImportedScene scene = new()
        {
            Nodes = Nodes,
            Meshes = Meshes,
            Materials = Materials,
            Lights = Lights,
        };

        return GltfSceneImportResult.Succeeded(scene, Diagnostics);
    }

    /// <summary>Depth-first walk of the full visual node hierarchy - top-level scene children are not guaranteed to be the only nodes (a source scene may nest transform/pivot nodes), so every descendant is visited.</summary>
    private static IEnumerable<Node> Flatten(IEnumerable<Node> Roots)
    {
        foreach (Node node in Roots)
        {
            yield return node;
            foreach (Node Descendant in Flatten(node.VisualChildren))
            {
                yield return Descendant;
            }
        }
    }

    private static int GetOrAddDefaultMaterial(List<ImportedPbrMaterial> Materials, Dictionary<Material, int> MaterialIndexByGltfMaterial)
    {
        // Keyed by null via a sentinel-free path: a primitive with no Material at all is rare in
        // practice for this importer's real content, so it's not worth a Dictionary<Material?,int>
        // (Material is a reference type but SharpGLTF never hands out a boxed "null key" instance).
        for (int i = 0; i < Materials.Count; i++)
        {
            ImportedPbrMaterial Candidate = Materials[i];
            if (Candidate is { BaseColorTextureBytes: null, NormalTextureBytes: null, MetallicRoughnessTextureBytes: null }
                && Candidate.BaseColorFactor == new Vector4(1f, 1f, 1f, 1f))
            {
                return i;
            }
        }

        Materials.Add(new ImportedPbrMaterial
        {
            BaseColorFactor = new Vector4(1f, 1f, 1f, 1f),
            MetallicFactor = 1f,
            RoughnessFactor = 1f,
        });
        return Materials.Count - 1;
    }

    private static ImportedPbrMaterial ImportMaterial(Material material)
    {
        MaterialChannel? BaseColorChannel = material.FindChannel("BaseColor");
        MaterialChannel? NormalChannel = material.FindChannel("Normal");
        MaterialChannel? MetallicRoughnessChannel = material.FindChannel("MetallicRoughness");

        return new ImportedPbrMaterial
        {
            BaseColorFactor = BaseColorChannel?.Color ?? new Vector4(1f, 1f, 1f, 1f),
            MetallicFactor = MetallicRoughnessChannel?.GetFactor("MetallicFactor") ?? 1f,
            RoughnessFactor = MetallicRoughnessChannel?.GetFactor("RoughnessFactor") ?? 1f,
            BaseColorTextureBytes = ExtractTextureBytes(BaseColorChannel),
            NormalTextureBytes = ExtractTextureBytes(NormalChannel),
            MetallicRoughnessTextureBytes = ExtractTextureBytes(MetallicRoughnessChannel),
        };
    }

    private static byte[]? ExtractTextureBytes(MaterialChannel? Channel) =>
        Channel?.Texture?.PrimaryImage is Image image ? image.Content.Content.ToArray() : null;

    private static ImportedLight ImportLight(PunctualLight Light) => new()
    {
        LightType = Light.LightType switch
        {
            PunctualLightType.Point => ImportedLightType.Point,
            PunctualLightType.Spot => ImportedLightType.Spot,
            _ => ImportedLightType.Directional,
        },
        Color = Light.Color,
        Intensity = Light.Intensity,
    };

    /// <summary>Builds one drawable's geometry from a single glTF primitive, applying the same missing-NORMAL/TEXCOORD_0 fallbacks as <see cref="Import(string)"/> plus TANGENT generation (not needed by the single-mesh preview path). Returns null (with a diagnostic) only when POSITION or triangle data is entirely absent.</summary>
    private static ImportedMeshData? ImportPrimitiveGeometry(MeshPrimitive Primitive, List<GltfImportDiagnostic> Diagnostics, string Context)
    {
        Accessor? PositionAccessor = Primitive.GetVertexAccessor("POSITION");
        if (PositionAccessor is null)
        {
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{Context}': primitive has no POSITION attribute; skipped."));
            return null;
        }

        Vector3[] RawPositions = [.. PositionAccessor.AsVector3Array()];

        Accessor? NormalAccessor = Primitive.GetVertexAccessor("NORMAL");
        Vector3[]? RawNormals = NormalAccessor is null ? null : [.. NormalAccessor.AsVector3Array()];
        bool NormalsUsable = RawNormals is not null && RawNormals.Length == RawPositions.Length;

        Accessor? UvAccessor = Primitive.GetVertexAccessor("TEXCOORD_0");
        Vector2[]? RawUvs = UvAccessor is null ? null : [.. UvAccessor.AsVector2Array()];
        bool UvsUsable = RawUvs is not null && RawUvs.Length == RawPositions.Length;

        List<(int A, int B, int C)> Triangles = [.. Primitive.GetTriangleIndices()];
        if (Triangles.Count == 0)
        {
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{Context}': primitive has no triangles; skipped."));
            return null;
        }

        Vector3[] Positions;
        Vector3[] Normals;
        Vector2[] Uvs;
        uint[] Indices;

        if (!UvsUsable)
        {
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{Context}': TEXCOORD_0 attribute missing or inconsistent; synthesized (0,0) UV for every vertex."));
        }

        if (NormalsUsable)
        {
            Positions = RawPositions;
            Normals = RawNormals!;
            Uvs = UvsUsable ? RawUvs! : new Vector2[RawPositions.Length];
            Indices = new uint[Triangles.Count * 3];

            for (int i = 0; i < Triangles.Count; i++)
            {
                Indices[(i * 3) + 0] = (uint)Triangles[i].A;
                Indices[(i * 3) + 1] = (uint)Triangles[i].B;
                Indices[(i * 3) + 2] = (uint)Triangles[i].C;
            }
        }
        else
        {
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{Context}': NORMAL attribute missing or inconsistent; synthesized {Triangles.Count} flat per-face normal(s)."));

            Positions = new Vector3[Triangles.Count * 3];
            Normals = new Vector3[Triangles.Count * 3];
            Uvs = new Vector2[Triangles.Count * 3];
            Indices = new uint[Triangles.Count * 3];

            for (int i = 0; i < Triangles.Count; i++)
            {
                (int A, int B, int C) = Triangles[i];
                Vector3 Pa = RawPositions[A];
                Vector3 Pb = RawPositions[B];
                Vector3 Pc = RawPositions[C];

                Vector3 FaceNormalUnnormalized = Vector3.Cross(Pb - Pa, Pc - Pa);
                Vector3 FaceNormal = FaceNormalUnnormalized.LengthSquared() > 1e-12f
                    ? Vector3.Normalize(FaceNormalUnnormalized)
                    : Vector3.UnitY;

                int BaseIndex = i * 3;
                Positions[BaseIndex + 0] = Pa;
                Positions[BaseIndex + 1] = Pb;
                Positions[BaseIndex + 2] = Pc;
                Normals[BaseIndex + 0] = FaceNormal;
                Normals[BaseIndex + 1] = FaceNormal;
                Normals[BaseIndex + 2] = FaceNormal;
                Uvs[BaseIndex + 0] = UvsUsable ? RawUvs![A] : Vector2.Zero;
                Uvs[BaseIndex + 1] = UvsUsable ? RawUvs![B] : Vector2.Zero;
                Uvs[BaseIndex + 2] = UvsUsable ? RawUvs![C] : Vector2.Zero;
                Indices[BaseIndex + 0] = (uint)(BaseIndex + 0);
                Indices[BaseIndex + 1] = (uint)(BaseIndex + 1);
                Indices[BaseIndex + 2] = (uint)(BaseIndex + 2);
            }
        }

        // From here on, mesh-local geometry moves from glTF space into engine space (ADR-0027) - a
        // single-axis mirror, so it must flip triangle winding to keep front-facing orientation
        // (GltfAxisConversion.FlipTriangleWinding) and does not affect ComputeTangents' output either
        // way (swapping a triangle's last two indices leaves its tangent/bitangent formula unchanged,
        // verified algebraically in ADR-0027).
        for (int i = 0; i < Positions.Length; i++)
        {
            Positions[i] = GltfAxisConversion.ConvertVector(Positions[i]);
            Normals[i] = GltfAxisConversion.ConvertVector(Normals[i]);
        }

        GltfAxisConversion.FlipTriangleWinding(Indices);

        Accessor? TangentAccessor = Primitive.GetVertexAccessor("TANGENT");
        Vector4[]? RawTangents = TangentAccessor is null ? null : [.. TangentAccessor.AsVector4Array()];
        Vector4[] Tangents;
        if (RawTangents is not null && RawTangents.Length == Positions.Length)
        {
            // A source-provided TANGENT accessor is glTF-space data and must be converted
            // explicitly (xyz direction plus a handedness-sign flip - see GltfAxisConversion.ConvertTangent).
            Tangents = new Vector4[RawTangents.Length];
            for (int i = 0; i < RawTangents.Length; i++)
            {
                Tangents[i] = GltfAxisConversion.ConvertTangent(RawTangents[i]);
            }
        }
        else
        {
            // Synthesized directly from the already-converted (engine-space) positions/normals/uvs/
            // indices above, so ComputeTangents derives a naturally-correct engine-space handedness
            // sign with no separate flip needed.
            Tangents = ComputeTangents(Positions, Normals, Uvs, Indices);
        }

        return new ImportedMeshData
        {
            Positions = Positions,
            Normals = Normals,
            Tangents = Tangents,
            Uvs = Uvs,
            Indices = Indices,
        };
    }

    private static GltfSceneImportResult SceneFailed(string Message) =>
        GltfSceneImportResult.Failed([new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Error, Message)]);

    /// <summary>
    /// Per-vertex tangent generation (Lengyel's method: "Computing Tangent Space Basis Vectors for
    /// an Arbitrary Mesh") for primitives with no TANGENT accessor. Accumulates each triangle's UV-
    /// space tangent/bitangent onto its three vertices, then Gram-Schmidt-orthogonalizes against the
    /// (possibly-shared) vertex normal and derives the handedness sign (w) from the bitangent.
    /// </summary>
    private static Vector4[] ComputeTangents(IReadOnlyList<Vector3> Positions, IReadOnlyList<Vector3> Normals, IReadOnlyList<Vector2> Uvs, IReadOnlyList<uint> Indices)
    {
        int VertexCount = Positions.Count;
        Vector3[] Tan = new Vector3[VertexCount];
        Vector3[] Bitan = new Vector3[VertexCount];

        for (int i = 0; i + 2 < Indices.Count; i += 3)
        {
            uint I0 = Indices[i];
            uint I1 = Indices[i + 1];
            uint I2 = Indices[i + 2];

            Vector3 Edge1 = Positions[(int)I1] - Positions[(int)I0];
            Vector3 Edge2 = Positions[(int)I2] - Positions[(int)I0];
            Vector2 DeltaUv1 = Uvs[(int)I1] - Uvs[(int)I0];
            Vector2 DeltaUv2 = Uvs[(int)I2] - Uvs[(int)I0];

            float Denominator = (DeltaUv1.X * DeltaUv2.Y) - (DeltaUv2.X * DeltaUv1.Y);
            if (MathF.Abs(Denominator) < 1e-12f)
            {
                continue;
            }

            float F = 1f / Denominator;
            Vector3 Tangent = F * ((DeltaUv2.Y * Edge1) - (DeltaUv1.Y * Edge2));
            Vector3 Bitangent = F * ((DeltaUv1.X * Edge2) - (DeltaUv2.X * Edge1));

            Tan[I0] += Tangent; Tan[I1] += Tangent; Tan[I2] += Tangent;
            Bitan[I0] += Bitangent; Bitan[I1] += Bitangent; Bitan[I2] += Bitangent;
        }

        Vector4[] Result = new Vector4[VertexCount];
        for (int i = 0; i < VertexCount; i++)
        {
            Vector3 N = Normals[i];
            Vector3 T = Tan[i];

            Vector3 Orthogonalized = T - (N * Vector3.Dot(N, T));
            Vector3 SafeTangent = Orthogonalized.LengthSquared() > 1e-12f
                ? Vector3.Normalize(Orthogonalized)
                : ArbitraryTangentFor(N);

            float Handedness = Vector3.Dot(Vector3.Cross(N, SafeTangent), Bitan[i]) < 0f ? -1f : 1f;
            Result[i] = new Vector4(SafeTangent, Handedness);
        }

        return Result;
    }

    private static Vector3 ArbitraryTangentFor(Vector3 Normal)
    {
        Vector3 Reference = MathF.Abs(Normal.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        return Vector3.Normalize(Vector3.Cross(Reference, Normal));
    }
}
