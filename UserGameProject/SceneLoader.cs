using System.Numerics;
using System.Text.Json;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Import.Gltf;

namespace UserGameProject;

public enum PropMobility {
    Static,
    Movable,
}

/// <summary>One prop to spawn once <see cref="StageRenderer"/> exists: which render object index it owns (assigned 1:1 with <see cref="LoadedScene.Objects"/>) and its initial world transform, decomposed for direct use with the Position/Rotation/Scale components.</summary>
public sealed class LoadedSceneSpawn {
    public required int ObjectIndex { get; init;}

    public required MeshHandle Mesh { get; init;}

    public required MaterialHandle Material { get; init;}

    public required PropMobility Mobility { get; init;}

    public required Vector3 Position { get; init;}

    public required Quaternion Rotation { get; init;}

    public required Vector3 Scale { get; init;}
}

/// <summary>
/// Shared CPU-side scene resources. The arrays are passed once to <c>StageRenderer.Create</c>,
/// which creates one GPU mesh/material resource per entry. Entity components only retain the
/// compact handles established here.
/// </summary>
public sealed class SceneAssetResources {
    public SceneAssetResources(StageMeshCreateInfo[] meshes,StageMaterialCreateInfo[] materials,StageObjectCreateInfo[] objects){
        this.Meshes=meshes;
        this.Materials=materials;
        this.Objects=objects;
    }

    public StageMeshCreateInfo[] Meshes { get;}

    public StageMaterialCreateInfo[] Materials { get;}

    public StageObjectCreateInfo[] Objects { get;}

    public int RenderableCount=>this.Objects.Length;

    public bool Contains(MeshHandle mesh,MaterialHandle material)=>
        (uint)mesh.Value<(uint)this.Meshes.Length&&(uint)material.Value<(uint)this.Materials.Length;
}

public sealed class LoadedScene {
    public required SceneAssetResources Assets { get; init;}

    public required StageMeshCreateInfo[]Meshes { get; init;}

    public required StageMaterialCreateInfo[]Materials { get; init;}

    public required StageObjectCreateInfo[]Objects { get; init;}

    public required LoadedSceneSpawn[]Spawns { get; init;}

    public DirectionalLightData?Sun { get; init;}

    public AmbientLightData?Ambient { get; init;}

    public FogData?Fog { get; init;}

    public required Vector3 PlayerStartPosition { get; init;}

    public required float PlayerStartYawRadians { get; init;}
}

/// <summary>
/// Combines <see cref="GltfImporter.ImportScene"/>'s node graph (geometry, baked materials, the
/// DirectionalLight) with scene_meta.json's supplementary data (ambient/fog/PlayerStart/mobility -
/// the handful of things the glTF export cannot carry, see ADR-0017) into everything
/// <c>Program.cs</c> needs to create the renderer and spawn ECS entities.
/// </summary>
public static class SceneLoader {
    private static readonly JsonSerializerOptions JsonOptions=new(){ PropertyNameCaseInsensitive=true };

    public static LoadedScene Load(string GlbPath,string SceneMetaJsonPath){
        GltfSceneImportResult ImportResult=GltfImporter.ImportScene(GlbPath);
        if(!ImportResult.Success){
            throw new InvalidOperationException($"Failed to import '{GlbPath}': {string.Join("; ",ImportResult.Diagnostics)}");
        }
        ImportedScene Scene=ImportResult.Scene!;

        var Json=File.ReadAllText(SceneMetaJsonPath);
        SceneMetaDocument Meta=JsonSerializer.Deserialize<SceneMetaDocument>(Json,JsonOptions)
            ??throw new InvalidOperationException($"'{SceneMetaJsonPath}' deserialized to null.");

        StageMeshCreateInfo[]Meshes=[.. Scene.Meshes.Select(ToStageMesh)];
        StageMaterialCreateInfo[]Materials=[.. Scene.Materials.Select(ToStageMaterial)];

        List<StageObjectCreateInfo>Objects=[];
        List<LoadedSceneSpawn>Spawns=[];
        DirectionalLightData?Sun=null;

        foreach(ImportedSceneNode Node in Scene.Nodes){
            if(Node.MeshIndex is not int MeshIndex||Node.MaterialIndex is not int MaterialIndex){
                if(Node.LightIndex is int LightIndex&&Sun is null){
                    Sun=TryImportDirectionalLight(Scene.Lights[LightIndex],Node.WorldTransform);
                }
                continue;
            }

            var ObjectIndex=Objects.Count;
            Objects.Add(new StageObjectCreateInfo { MeshIndex=MeshIndex,MaterialIndex=MaterialIndex });

            Matrix4x4.Decompose(Node.WorldTransform,out Vector3 scale,out Quaternion rotation,out Vector3 Translation);
            var MobilityText=Meta.MobilityOverrides.GetValueOrDefault(Node.Name,"static");
            PropMobility Mobility=string.Equals(MobilityText,"movable",StringComparison.OrdinalIgnoreCase)?PropMobility.Movable:PropMobility.Static;

            Spawns.Add(new LoadedSceneSpawn {
                ObjectIndex=ObjectIndex,
                Mesh=new MeshHandle(MeshIndex),
                Material=new MaterialHandle(MaterialIndex),
                Mobility=Mobility,
                Position=Translation,
                Rotation=rotation,
                Scale=scale,
            });
        }

        (Vector3 PlayerStartPosition,var PlayerStartYawRadians)=ImportPlayerStart(Meta.PlayerStart);

        StageMeshCreateInfo[] MeshArray=[.. Meshes];
        StageMaterialCreateInfo[] MaterialArray=[.. Materials];
        StageObjectCreateInfo[] ObjectArray=[.. Objects];
        return new LoadedScene {
            Assets=new SceneAssetResources(MeshArray,MaterialArray,ObjectArray),
            Meshes=MeshArray,
            Materials=MaterialArray,
            Objects=ObjectArray,
            Spawns=[.. Spawns],
            Sun=Sun,
            Ambient=ToAmbientData(Meta.Ambient),
            Fog=ToFogData(Meta.Fog),
            PlayerStartPosition=PlayerStartPosition,
            PlayerStartYawRadians=PlayerStartYawRadians,
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
    private static(Vector3 Position,float YawRadians)ImportPlayerStart(SceneMetaPlayerStart?PlayerStart){
        if(PlayerStart is null){
            return(Vector3.Zero,0f);
        }

        Vector3 RawPosition=new(PlayerStart.Translation[0],PlayerStart.Translation[1],PlayerStart.Translation[2]);
        Matrix4x4 GltfSpaceTransform=
            Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY,PlayerStart.YawRadians))
            *Matrix4x4.CreateTranslation(RawPosition);

        Matrix4x4 EngineSpaceTransform=GltfAxisConversion.ConvertWorldTransform(GltfSpaceTransform);
        Matrix4x4.Decompose(EngineSpaceTransform,out _,out Quaternion rotation,out Vector3 position);

        // The source rotation is a pure Y-axis rotation, and Y is the mirror's fixed axis (ADR-0027),
        // so the converted rotation is guaranteed to still be a pure Y-axis rotation - extract its
        // angle directly instead of taking a general (and here unnecessary) Euler decomposition.
        var YawRadians=2f*MathF.Atan2(rotation.Y,rotation.W);

        return(position,YawRadians);
    }

    private static DirectionalLightData?TryImportDirectionalLight(ImportedLight Light,Matrix4x4 WorldTransform){
        if(Light.LightType!=ImportedLightType.Directional){
            return null;
        }

        Vector3 Direction=ImportedLightDirection.WorldForward(WorldTransform);
        return new DirectionalLightData {
            DirectionTowardLightX=Direction.X,
            DirectionTowardLightY=Direction.Y,
            DirectionTowardLightZ=Direction.Z,
            ColorR=Light.Color.X,
            ColorG=Light.Color.Y,
            ColorB=Light.Color.Z,
            Intensity=Light.Intensity,
        };
    }

    private static AmbientLightData?ToAmbientData(SceneMetaAmbient?Ambient){
        if(Ambient is null){
            return null;
        }

        // AmbientLightData has no separate intensity field, so intensity is folded into color here
        // (the renderer's ambient term is always just "a color to add", see Stage.hlsl AmbientColor).
        return new AmbientLightData {
            ColorR=Ambient.ColorRgb[0]*Ambient.Intensity,
            ColorG=Ambient.ColorRgb[1]*Ambient.Intensity,
            ColorB=Ambient.ColorRgb[2]*Ambient.Intensity,
        };
    }

    private static FogData?ToFogData(SceneMetaFog?Fog){
        if(Fog is null){
            return null;
        }

        return new FogData {
            Density=Fog.Density,
            HeightFalloff=Fog.HeightFalloff,
            ColorR=Fog.ColorRgb[0],
            ColorG=Fog.ColorRgb[1],
            ColorB=Fog.ColorRgb[2],
        };
    }

    private static StageMeshCreateInfo ToStageMesh(ImportedMeshData Mesh)=>new(){
        Positions=Mesh.Positions,
        Normals=Mesh.Normals,
        Tangents=Mesh.Tangents,
        Uvs=Mesh.Uvs,
        Indices=Mesh.Indices,
    };

    private static StageMaterialCreateInfo ToStageMaterial(ImportedPbrMaterial Material)=>new(){
        BaseColorFactor=Material.BaseColorFactor,
        RoughnessFactor=Material.RoughnessFactor,
        MetallicFactor=Material.MetallicFactor,
        EmissiveFactor=Material.EmissiveFactor,
        BaseColorTextureBytes=Material.BaseColorTextureBytes,
        NormalTextureBytes=Material.NormalTextureBytes,
        MetallicRoughnessTextureBytes=Material.MetallicRoughnessTextureBytes,
    };
}
