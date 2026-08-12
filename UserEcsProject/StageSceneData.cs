using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Import.Gltf;

namespace UserEcsProject;

/// <summary>CPU-side resources for the complete imported stage.</summary>
public sealed class StageSceneData {
    private StageSceneData(StageMeshCreateInfo[] Meshes,StageMaterialCreateInfo[] Materials,StageObjectCreateInfo[] Objects,Matrix4x4[] Transforms){
        this.Meshes=Meshes;
        this.Materials=Materials;
        this.Objects=Objects;
        ObjectTransforms=Transforms;
    }

    public StageMeshCreateInfo[] Meshes { get; }
    public StageMaterialCreateInfo[] Materials { get; }
    public StageObjectCreateInfo[] Objects { get; }
    public Matrix4x4[] ObjectTransforms { get; }

    public static StageSceneData Load(string GlbPath){
        GltfSceneImportResult ImportResult=GltfImporter.ImportScene(GlbPath);
        if(!ImportResult.Success||ImportResult.Scene is not { } Scene){
            throw new InvalidOperationException($"Failed to import '{GlbPath}': {string.Join("; ",ImportResult.Diagnostics)}");
        }

        StageMeshCreateInfo[] Meshes=[.. Scene.Meshes.Select(Mesh=>new StageMeshCreateInfo {
            Positions=Mesh.Positions,
            Normals=Mesh.Normals,
            Tangents=Mesh.Tangents,
            Uvs=Mesh.Uvs,
            Indices=Mesh.Indices,
        })];
        StageMaterialCreateInfo[] Materials=[.. Scene.Materials.Select(Material=>new StageMaterialCreateInfo {
            BaseColorFactor=Material.BaseColorFactor,
            RoughnessFactor=Material.RoughnessFactor,
            MetallicFactor=Material.MetallicFactor,
            EmissiveFactor=Material.EmissiveFactor,
            BaseColorTextureBytes=Material.BaseColorTextureBytes,
            NormalTextureBytes=Material.NormalTextureBytes,
            MetallicRoughnessTextureBytes=Material.MetallicRoughnessTextureBytes,
        })];

        List<StageObjectCreateInfo> Objects=[];
        List<Matrix4x4> Transforms=[];
        foreach(ImportedSceneNode Node in Scene.Nodes){
            if(Node.MeshIndex is not int MeshIndex||Node.MaterialIndex is not int MaterialIndex){
                continue;
            }
            Objects.Add(new StageObjectCreateInfo { MeshIndex=MeshIndex,MaterialIndex=MaterialIndex });
            Transforms.Add(Node.WorldTransform);
        }

        if(Objects.Count==0){
            throw new InvalidOperationException($"The stage '{GlbPath}' contains no drawable mesh nodes.");
        }
        return new StageSceneData(Meshes,Materials,[.. Objects],[.. Transforms]);
    }
}
