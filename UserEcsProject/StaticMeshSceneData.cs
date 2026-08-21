using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Runtime.Windows;

namespace UserEcsProject;

/// <summary>宣言された個別StaticMeshを、固定ロースター型StageRendererの生成入力へ変換する。</summary>
internal sealed class StaticMeshSceneData {
    private StaticMeshSceneData(
        StageMeshCreateInfo[]Meshes,
        StageMaterialCreateInfo[]Materials,
        StageObjectCreateInfo[]Objects,
        Transform[]ObjectTransforms,
        Matrix4x4[]ObjectLocalTransforms,
        Entity[]SourceEntities){
        this.Meshes=Meshes;
        this.Materials=Materials;
        this.Objects=Objects;
        this.ObjectTransforms=ObjectTransforms;
        this.ObjectLocalTransforms=ObjectLocalTransforms;
        this.SourceEntities=SourceEntities;
    }

    public StageMeshCreateInfo[]Meshes { get;}
    public StageMaterialCreateInfo[]Materials { get;}
    public StageObjectCreateInfo[]Objects { get;}
    public Transform[]ObjectTransforms { get;}
    public Matrix4x4[]ObjectLocalTransforms { get;}
    /// <summary>Authoring Entity whose fixed hierarchy owns each renderer object.</summary>
    public Entity[]SourceEntities { get;}

    public static StaticMeshSceneData Build(
        IReadOnlyList<(Entity Entity,Transform Transform,StaticMesh Mesh)>StaticMeshes,
        IReadOnlyList<(Entity Entity,Transform Transform,PrimitiveMesh Mesh)>Primitives){
        List<StageMeshCreateInfo>Meshes=[];
        List<StageMaterialCreateInfo>Materials=[];
        List<StageObjectCreateInfo>Objects=[];
        List<Transform>ObjectTransforms=[];
        List<Matrix4x4>ObjectLocalTransforms=[];
        List<Entity>SourceEntities=[];
        Dictionary<string,AssetTemplate>Templates=new(StringComparer.OrdinalIgnoreCase);

        foreach(var Placement in StaticMeshes){
            var FullPath=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,Placement.Mesh.AssetPath));
            if(!Templates.TryGetValue(FullPath,out AssetTemplate?Template)){
                Template=LoadAsset(FullPath,Meshes,Materials);
                Templates.Add(FullPath,Template);
            }

            foreach(AssetObject Object in Template.Objects){
                Objects.Add(new StageObjectCreateInfo { MeshIndex=Object.MeshIndex,MaterialIndex=Object.MaterialIndex });
                ObjectTransforms.Add(Placement.Transform);
                ObjectLocalTransforms.Add(Object.LocalTransform);
                SourceEntities.Add(Placement.Entity);
            }
        }

        if(Primitives.Count>0){
            (var PrimitiveMeshes,var PrimitiveMaterials,var PrimitiveObjects)=
                RenderServer.BuildPrimitiveRoster([.. Primitives.Select(Item=>Item.Mesh.Type)],Meshes.Count,Materials.Count);
            Meshes.AddRange(PrimitiveMeshes);
            Materials.AddRange(PrimitiveMaterials);
            Objects.AddRange(PrimitiveObjects);
            foreach(var Primitive in Primitives){
                ObjectTransforms.Add(Primitive.Transform);
                ObjectLocalTransforms.Add(Matrix4x4.Identity);
                SourceEntities.Add(Primitive.Entity);
            }
        }

        if(Objects.Count==0){
            throw new InvalidOperationException("宣言型Sceneに描画可能なStaticMeshまたはPrimitiveMeshがありません。");
        }

        return new StaticMeshSceneData([.. Meshes],[.. Materials],[.. Objects],
            [.. ObjectTransforms],[.. ObjectLocalTransforms],[.. SourceEntities]);
    }

    private static AssetTemplate LoadAsset(string FullPath,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        var Result=GltfImporter.ImportScene(FullPath);
        if(!Result.Success||Result.Scene is not{ }Scene){
            throw new InvalidOperationException($"StaticMesh '{FullPath}' の読み込みに失敗しました: {string.Join("; ",Result.Diagnostics)}");
        }

        var MeshOffset=Meshes.Count;
        var MaterialOffset=Materials.Count;
        Meshes.AddRange(Scene.Meshes.Select(Mesh=>new StageMeshCreateInfo {
            Positions=Mesh.Positions,
            Normals=Mesh.Normals,
            Tangents=Mesh.Tangents,
            Uvs=Mesh.Uvs,
            Indices=Mesh.Indices,
        }));
        Materials.AddRange(Scene.Materials.Select(Material=>new StageMaterialCreateInfo {
            BaseColorFactor=Material.BaseColorFactor,
            RoughnessFactor=Material.RoughnessFactor,
            MetallicFactor=Material.MetallicFactor,
            EmissiveFactor=Material.EmissiveFactor,
            EmissiveNits=Material.EmissiveNits,
            EmissiveTextureLuminanceScale=Material.EmissiveTextureLuminanceScale,
            BaseColorTextureBytes=Material.BaseColorTextureBytes,
            NormalTextureBytes=Material.NormalTextureBytes,
            MetallicRoughnessTextureBytes=Material.MetallicRoughnessTextureBytes,
            EmissiveTextureBytes=Material.EmissiveTextureBytes,
        }));

        List<AssetObject>Objects=[];
        foreach(var Node in Scene.Nodes){
            if(Node.MeshIndex is int MeshIndex&&Node.MaterialIndex is int MaterialIndex){
                Objects.Add(new AssetObject(MeshOffset+MeshIndex,MaterialOffset+MaterialIndex,Node.WorldTransform));
            }
        }
        if(Objects.Count==0){
            throw new InvalidOperationException($"StaticMesh '{FullPath}' に描画可能なノードがありません。");
        }
        return new AssetTemplate([.. Objects]);
    }


    private sealed record AssetTemplate(AssetObject[]Objects);
    private readonly record struct AssetObject(int MeshIndex,int MaterialIndex,Matrix4x4 LocalTransform);
}
