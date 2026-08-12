using System.Numerics;
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
        Matrix4x4[]Transforms){
        this.Meshes=Meshes;
        this.Materials=Materials;
        this.Objects=Objects;
        this.Transforms=Transforms;
    }

    public StageMeshCreateInfo[]Meshes { get;}
    public StageMaterialCreateInfo[]Materials { get;}
    public StageObjectCreateInfo[]Objects { get;}
    public Matrix4x4[]Transforms { get;}

    public static StaticMeshSceneData Build(
        IReadOnlyList<(Transform Transform,StaticMesh Mesh)>StaticMeshes,
        IReadOnlyList<(Transform Transform,PrimitiveMesh Mesh)>Primitives){
        List<StageMeshCreateInfo>Meshes=[];
        List<StageMaterialCreateInfo>Materials=[];
        List<StageObjectCreateInfo>Objects=[];
        List<Matrix4x4>Transforms=[];
        Dictionary<string,AssetTemplate>Templates=new(StringComparer.OrdinalIgnoreCase);

        foreach((Transform Transform,StaticMesh Mesh)Placement in StaticMeshes){
            string FullPath=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,Placement.Mesh.AssetPath));
            if(!Templates.TryGetValue(FullPath,out AssetTemplate?Template)){
                Template=LoadAsset(FullPath,Meshes,Materials);
                Templates.Add(FullPath,Template);
            }

            Matrix4x4 PlacementTransform=ToMatrix(Placement.Transform);
            foreach(AssetObject Object in Template.Objects){
                Objects.Add(new StageObjectCreateInfo { MeshIndex=Object.MeshIndex,MaterialIndex=Object.MaterialIndex });
                Transforms.Add(Object.LocalTransform*PlacementTransform);
            }
        }

        if(Primitives.Count>0){
            (StageMeshCreateInfo[]PrimitiveMeshes,StageMaterialCreateInfo[]PrimitiveMaterials,StageObjectCreateInfo[]PrimitiveObjects)=
                RenderServer.BuildPrimitiveRoster([.. Primitives.Select(Item=>Item.Mesh.Type)],Meshes.Count,Materials.Count);
            Meshes.AddRange(PrimitiveMeshes);
            Materials.AddRange(PrimitiveMaterials);
            Objects.AddRange(PrimitiveObjects);
            Transforms.AddRange(Primitives.Select(Item=>ToMatrix(Item.Transform)));
        }

        if(Objects.Count==0){
            throw new InvalidOperationException("宣言型Sceneに描画可能なStaticMeshまたはPrimitiveMeshがありません。");
        }

        return new StaticMeshSceneData([.. Meshes],[.. Materials],[.. Objects],[.. Transforms]);
    }

    private static AssetTemplate LoadAsset(string FullPath,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        GltfSceneImportResult Result=GltfImporter.ImportScene(FullPath);
        if(!Result.Success||Result.Scene is not{ }Scene){
            throw new InvalidOperationException($"StaticMesh '{FullPath}' の読み込みに失敗しました: {string.Join("; ",Result.Diagnostics)}");
        }

        int MeshOffset=Meshes.Count;
        int MaterialOffset=Materials.Count;
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
            BaseColorTextureBytes=Material.BaseColorTextureBytes,
            NormalTextureBytes=Material.NormalTextureBytes,
            MetallicRoughnessTextureBytes=Material.MetallicRoughnessTextureBytes,
        }));

        List<AssetObject>Objects=[];
        foreach(ImportedSceneNode Node in Scene.Nodes){
            if(Node.MeshIndex is int MeshIndex&&Node.MaterialIndex is int MaterialIndex){
                Objects.Add(new AssetObject(MeshOffset+MeshIndex,MaterialOffset+MaterialIndex,Node.WorldTransform));
            }
        }
        if(Objects.Count==0){
            throw new InvalidOperationException($"StaticMesh '{FullPath}' に描画可能なノードがありません。");
        }
        return new AssetTemplate([.. Objects]);
    }

    private static Matrix4x4 ToMatrix(Transform Transform)=>
        Matrix4x4.CreateScale(Transform.ScaleX,Transform.ScaleY,Transform.ScaleZ)
        *Matrix4x4.CreateFromQuaternion(new Quaternion(Transform.RotationX,Transform.RotationY,Transform.RotationZ,Transform.RotationW))
        *Matrix4x4.CreateTranslation(Transform.PositionX,Transform.PositionY,Transform.PositionZ);

    private sealed record AssetTemplate(AssetObject[]Objects);
    private readonly record struct AssetObject(int MeshIndex,int MaterialIndex,Matrix4x4 LocalTransform);
}
