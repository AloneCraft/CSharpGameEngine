using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Import.Gltf;

namespace UserEcsProject;

/// <summary>CPU-side resources for the complete imported stage.</summary>
public sealed class StageSceneData {
    private StageSceneData(StageMeshCreateInfo[] meshes,StageMaterialCreateInfo[] materials,StageObjectCreateInfo[] objects,Matrix4x4[] transforms){
        Meshes=meshes;
        Materials=materials;
        Objects=objects;
        ObjectTransforms=transforms;
    }

    public StageMeshCreateInfo[] Meshes { get; }
    public StageMaterialCreateInfo[] Materials { get; }
    public StageObjectCreateInfo[] Objects { get; }
    public Matrix4x4[] ObjectTransforms { get; }

    public static StageSceneData Load(string glbPath){
        GltfSceneImportResult importResult=GltfImporter.ImportScene(glbPath);
        if(!importResult.Success||importResult.Scene is not { } scene){
            throw new InvalidOperationException($"Failed to import '{glbPath}': {string.Join("; ",importResult.Diagnostics)}");
        }

        StageMeshCreateInfo[] meshes=[.. scene.Meshes.Select(mesh=>new StageMeshCreateInfo {
            Positions=mesh.Positions,
            Normals=mesh.Normals,
            Tangents=mesh.Tangents,
            Uvs=mesh.Uvs,
            Indices=mesh.Indices,
        })];
        StageMaterialCreateInfo[] materials=[.. scene.Materials.Select(material=>new StageMaterialCreateInfo {
            BaseColorFactor=material.BaseColorFactor,
            RoughnessFactor=material.RoughnessFactor,
            MetallicFactor=material.MetallicFactor,
            EmissiveFactor=material.EmissiveFactor,
            BaseColorTextureBytes=material.BaseColorTextureBytes,
            NormalTextureBytes=material.NormalTextureBytes,
            MetallicRoughnessTextureBytes=material.MetallicRoughnessTextureBytes,
        })];

        List<StageObjectCreateInfo> objects=[];
        List<Matrix4x4> transforms=[];
        foreach(ImportedSceneNode node in scene.Nodes){
            if(node.MeshIndex is not int meshIndex||node.MaterialIndex is not int materialIndex){
                continue;
            }
            objects.Add(new StageObjectCreateInfo { MeshIndex=meshIndex,MaterialIndex=materialIndex });
            transforms.Add(node.WorldTransform);
        }

        if(objects.Count==0){
            throw new InvalidOperationException($"The stage '{glbPath}' contains no drawable mesh nodes.");
        }
        return new StageSceneData(meshes,materials,[.. objects],[.. transforms]);
    }
}
