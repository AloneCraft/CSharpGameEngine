using System.Numerics;
using System.Text.Json;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Import.Gltf;

namespace UserGameProject;

public enum PropMobility
{
    Static,
    Movable,
}

/// <summary>One prop to spawn once <see cref="StageRenderer"/> exists: which render object index it owns (assigned 1:1 with <see cref="LoadedScene.Objects"/>) and its initial world transform, decomposed for direct use with the Position/Rotation/Scale components.</summary>
public sealed class LoadedSceneSpawn
{
    public required int ObjectIndex { get; init; }

    public required PropMobility Mobility { get; init; }

    public required Vector3 Position { get; init; }

    public required Quaternion Rotation { get; init; }

    public required Vector3 Scale { get; init; }
}

public sealed class LoadedScene
{
    public required StageMeshCreateInfo[] Meshes { get; init; }

    public required StageMaterialCreateInfo[] Materials { get; init; }

    public required StageObjectCreateInfo[] Objects { get; init; }

    public required LoadedSceneSpawn[] Spawns { get; init; }

    public DirectionalLightData? Sun { get; init; }

    public AmbientLightData? Ambient { get; init; }

    public FogData? Fog { get; init; }

    public required Vector3 PlayerStartPosition { get; init; }

    public required float PlayerStartYawRadians { get; init; }
}

/// <summary>
/// Combines <see cref="GltfImporter.ImportScene"/>'s node graph (geometry, baked materials, the
/// DirectionalLight) with scene_meta.json's supplementary data (ambient/fog/PlayerStart/mobility -
/// the handful of things the glTF export cannot carry, see ADR-0017) into everything
/// <c>Program.cs</c> needs to create the renderer and spawn ECS entities.
/// </summary>
public static class SceneLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static LoadedScene Load(string glbPath, string sceneMetaJsonPath)
    {
        GltfSceneImportResult importResult = GltfImporter.ImportScene(glbPath);
        if (!importResult.Success)
        {
            throw new InvalidOperationException($"Failed to import '{glbPath}': {string.Join("; ", importResult.Diagnostics)}");
        }
        ImportedScene scene = importResult.Scene!;

        string json = File.ReadAllText(sceneMetaJsonPath);
        SceneMetaDocument meta = JsonSerializer.Deserialize<SceneMetaDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException($"'{sceneMetaJsonPath}' deserialized to null.");

        StageMeshCreateInfo[] meshes = [.. scene.Meshes.Select(ToStageMesh)];
        StageMaterialCreateInfo[] materials = [.. scene.Materials.Select(ToStageMaterial)];

        List<StageObjectCreateInfo> objects = [];
        List<LoadedSceneSpawn> spawns = [];
        DirectionalLightData? sun = null;

        foreach (ImportedSceneNode node in scene.Nodes)
        {
            if (node.MeshIndex is not int meshIndex || node.MaterialIndex is not int materialIndex)
            {
                if (node.LightIndex is int lightIndex && sun is null)
                {
                    sun = TryImportDirectionalLight(scene.Lights[lightIndex], node.WorldTransform);
                }
                continue;
            }

            int objectIndex = objects.Count;
            objects.Add(new StageObjectCreateInfo { MeshIndex = meshIndex, MaterialIndex = materialIndex });

            Matrix4x4.Decompose(node.WorldTransform, out Vector3 scale, out Quaternion rotation, out Vector3 translation);
            string mobilityText = meta.MobilityOverrides.GetValueOrDefault(node.Name, "static");
            PropMobility mobility = string.Equals(mobilityText, "movable", StringComparison.OrdinalIgnoreCase) ? PropMobility.Movable : PropMobility.Static;

            spawns.Add(new LoadedSceneSpawn { ObjectIndex = objectIndex, Mobility = mobility, Position = translation, Rotation = rotation, Scale = scale });
        }

        (Vector3 playerStartPosition, float playerStartYawRadians) = ImportPlayerStart(meta.PlayerStart);

        return new LoadedScene
        {
            Meshes = meshes,
            Materials = materials,
            Objects = [.. objects],
            Spawns = [.. spawns],
            Sun = sun,
            Ambient = ToAmbientData(meta.Ambient),
            Fog = ToFogData(meta.Fog),
            PlayerStartPosition = playerStartPosition,
            PlayerStartYawRadians = playerStartYawRadians,
        };
    }

    /// <summary>
    /// export_lvl_firstperson.py's <c>ue_to_gltf_translation</c>/<c>ue_yaw_to_gltf_yaw_radians</c>
    /// write <c>scene_meta.json</c>'s PlayerStart already converted from UE space into glTF space
    /// (ADR-0017) - unlike every other node, which comes from the .glb itself and is converted by
    /// <see cref="GltfImporter"/>, PlayerStart needs the same glTF-&gt;engine conversion applied here
    /// (ADR-0027), via the same <see cref="GltfAxisConversion.ConvertWorldTransform"/> path every
    /// other node already goes through (rather than re-deriving the yaw sign flip by hand).
    /// </summary>
    private static (Vector3 Position, float YawRadians) ImportPlayerStart(SceneMetaPlayerStart? playerStart)
    {
        if (playerStart is null)
        {
            return (Vector3.Zero, 0f);
        }

        Vector3 rawPosition = new(playerStart.Translation[0], playerStart.Translation[1], playerStart.Translation[2]);
        Matrix4x4 gltfSpaceTransform =
            Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, playerStart.YawRadians))
            * Matrix4x4.CreateTranslation(rawPosition);

        Matrix4x4 engineSpaceTransform = GltfAxisConversion.ConvertWorldTransform(gltfSpaceTransform);
        Matrix4x4.Decompose(engineSpaceTransform, out _, out Quaternion rotation, out Vector3 position);

        // The source rotation is a pure Y-axis rotation, and Y is the mirror's fixed axis (ADR-0027),
        // so the converted rotation is guaranteed to still be a pure Y-axis rotation - extract its
        // angle directly instead of taking a general (and here unnecessary) Euler decomposition.
        float yawRadians = 2f * MathF.Atan2(rotation.Y, rotation.W);

        return (position, yawRadians);
    }

    private static DirectionalLightData? TryImportDirectionalLight(ImportedLight light, Matrix4x4 worldTransform)
    {
        if (light.LightType != ImportedLightType.Directional)
        {
            return null;
        }

        Vector3 direction = ImportedLightDirection.WorldForward(worldTransform);
        return new DirectionalLightData
        {
            DirectionTowardLightX = direction.X,
            DirectionTowardLightY = direction.Y,
            DirectionTowardLightZ = direction.Z,
            ColorR = light.Color.X,
            ColorG = light.Color.Y,
            ColorB = light.Color.Z,
            Intensity = light.Intensity,
        };
    }

    private static AmbientLightData? ToAmbientData(SceneMetaAmbient? ambient)
    {
        if (ambient is null)
        {
            return null;
        }

        // AmbientLightData has no separate intensity field, so intensity is folded into color here
        // (the renderer's ambient term is always just "a color to add", see Stage.hlsl AmbientColor).
        return new AmbientLightData
        {
            ColorR = ambient.ColorRgb[0] * ambient.Intensity,
            ColorG = ambient.ColorRgb[1] * ambient.Intensity,
            ColorB = ambient.ColorRgb[2] * ambient.Intensity,
        };
    }

    private static FogData? ToFogData(SceneMetaFog? fog)
    {
        if (fog is null)
        {
            return null;
        }

        return new FogData
        {
            Density = fog.Density,
            HeightFalloff = fog.HeightFalloff,
            ColorR = fog.ColorRgb[0],
            ColorG = fog.ColorRgb[1],
            ColorB = fog.ColorRgb[2],
        };
    }

    private static StageMeshCreateInfo ToStageMesh(ImportedMeshData mesh) => new()
    {
        Positions = mesh.Positions,
        Normals = mesh.Normals,
        Tangents = mesh.Tangents,
        Uvs = mesh.Uvs,
        Indices = mesh.Indices,
    };

    private static StageMaterialCreateInfo ToStageMaterial(ImportedPbrMaterial material) => new()
    {
        BaseColorFactor = material.BaseColorFactor,
        RoughnessFactor = material.RoughnessFactor,
        MetallicFactor = material.MetallicFactor,
        BaseColorTextureBytes = material.BaseColorTextureBytes,
        NormalTextureBytes = material.NormalTextureBytes,
        MetallicRoughnessTextureBytes = material.MetallicRoughnessTextureBytes,
    };
}
