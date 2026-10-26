using System.Numerics;
using System.Text;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Animation;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>宣言された個別StaticMeshを、固定ロースター型StageRendererの生成入力へ変換する。</summary>
internal sealed class StageSceneDataBuilder {
    private StageSceneDataBuilder(
        StageMeshCreateInfo[]Meshes,
        StageMaterialCreateInfo[]Materials,
        StageObjectCreateInfo[]Objects,
        Transform[]ObjectTransforms,
        Matrix4x4[]ObjectLocalTransforms,
        Entity[]SourceEntities,
        (Entity Entity,int[]MeshIndices)[]SkinnedMeshes,
        SkeletalInstance[]SkeletalInstances,
        Dictionary<string,AssetTemplate>StaticAssets,
        Dictionary<PrimitiveType,int>PrimitiveMeshIndices,
        int PrimitiveMaterialIndex){
        this.StaticAssets=StaticAssets;
        this.PrimitiveMeshIndices=PrimitiveMeshIndices;
        this.PrimitiveMaterialIndex=PrimitiveMaterialIndex;
        this.SkinnedMeshes=SkinnedMeshes;
        this.SkeletalInstances=SkeletalInstances;
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
    /// <summary>各レンダラーオブジェクトを所有する、作成元の固定階層 Entity。</summary>
    public Entity[]SourceEntities { get;}
    /// <summary>各 SkeletalMesh Entity がパレットでスキニングするレンダラーメッシュ。</summary>
    public (Entity Entity,int[]MeshIndices)[]SkinnedMeshes { get;}
    /// <summary>各 SkeletalMesh Entity が読み込み時に取得したメッシュ、マテリアル、オブジェクト。後から同じアセットの別 Entity を作成した場合、この Entity がなくなった後に再利用する（M0107）。</summary>
    internal SkeletalInstance[]SkeletalInstances { get;}
    /// <summary>解決済みパスごとに記録した StaticMesh アセットのメッシュとマテリアル。後から生成した Entity が再利用する（M0105）。</summary>
    internal Dictionary<string,AssetTemplate>StaticAssets { get;}
    /// <summary>各 PrimitiveMesh で使ったプリミティブ種別ごとのレンダラーメッシュと、全種別で共有する1つのマテリアル（M0105）。</summary>
    internal Dictionary<PrimitiveType,int>PrimitiveMeshIndices { get;}
    internal int PrimitiveMaterialIndex { get;}

    public static StageSceneDataBuilder Build(
        IReadOnlyList<(Entity Entity,Transform Transform,StaticMesh Mesh)>StaticMeshes,
        IReadOnlyList<(Entity Entity,Transform Transform,PrimitiveMesh Mesh)>Primitives,
        IReadOnlyList<(Entity Entity,Transform Transform,SkeletalMesh Mesh)>?SkeletalMeshes=null){
        List<StageMeshCreateInfo>Meshes=[];
        List<(Entity Entity,int[]MeshIndices)>SkinnedMeshes=[];
        List<SkeletalInstance>SkeletalInstances=[];
        List<StageMaterialCreateInfo>Materials=[];
        List<StageObjectCreateInfo>Objects=[];
        List<Transform>ObjectTransforms=[];
        List<Matrix4x4>ObjectLocalTransforms=[];
        List<Entity>SourceEntities=[];
        Dictionary<string,AssetTemplate>Templates=new(StringComparer.OrdinalIgnoreCase);
        Dictionary<PrimitiveType,int>PrimitiveMeshIndices=[];
        var PrimitiveMaterialIndex=-1;

        foreach(var Placement in StaticMeshes){
            var FullPath=ContentRoot.Resolve(Placement.Mesh.AssetPath);
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

        foreach(var Placement in SkeletalMeshes??[]){
            // 各配置にメッシュの独立コピーを割り当ててスキニングするため、キャラクターごとに異なる姿勢を保持できる。
            var (Template,Skinned)=LoadSkeletalAsset(Placement.Mesh.AssetPath??string.Empty,Meshes,Materials);
            foreach(AssetObject Object in Template.Objects){
                Objects.Add(new StageObjectCreateInfo { MeshIndex=Object.MeshIndex,MaterialIndex=Object.MaterialIndex });
                ObjectTransforms.Add(Placement.Transform);
                ObjectLocalTransforms.Add(Object.LocalTransform);
                SourceEntities.Add(Placement.Entity);
            }
            SkinnedMeshes.Add((Placement.Entity,Skinned));
            SkeletalInstances.Add(new SkeletalInstance(Placement.Entity,SkeletalAssetCache.ResolvePath(Placement.Mesh.AssetPath??string.Empty),Template,Skinned));
        }

        if(Primitives.Count>0){
            // BuildPrimitiveRoster は、異なる種類ごとに、最初に現れた順でメッシュを1つ割り当てる。すべての種類でマテリアルを共有する。
            foreach(var Primitive in Primitives){
                PrimitiveMeshIndices.TryAdd(Primitive.Mesh.Type,Meshes.Count+PrimitiveMeshIndices.Count);
            }
            PrimitiveMaterialIndex=Materials.Count;
            (var PrimitiveMeshes,var PrimitiveMaterials,var PrimitiveObjects)=
                PrimitiveRoster.Build([.. Primitives.Select(Item=>Item.Mesh.Type)],Meshes.Count,Materials.Count);
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
            throw new InvalidOperationException("Runtime requires at least one StaticMesh or PrimitiveMesh.");
        }

        return new StageSceneDataBuilder([.. Meshes],[.. Materials],[.. Objects],
            [.. ObjectTransforms],[.. ObjectLocalTransforms],[.. SourceEntities],[.. SkinnedMeshes],[.. SkeletalInstances],
            Templates,PrimitiveMeshIndices,PrimitiveMaterialIndex);
    }

    internal static AssetTemplate LoadAsset(string FullPath,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        var Result=GltfImporter.ImportScene(FullPath);
        if(!Result.Success||Result.Scene is not{ }Scene){
            throw new InvalidOperationException($"StaticMesh '{FullPath}' の読み込みに失敗しました: {string.Join("; ",Result.Diagnostics)}");
        }

        var MeshOffset=Meshes.Count;
        var MaterialOffset=Materials.Count;
        var Fields=LoadDistanceFields(FullPath,Scene);
        Meshes.AddRange(Scene.Meshes.Select((Mesh,Index)=>new StageMeshCreateInfo {
            Positions=Mesh.Positions,
            Normals=Mesh.Normals,
            Tangents=Mesh.Tangents,
            Uvs=Mesh.Uvs,
            Indices=Mesh.Indices,
            DistanceField=Fields[Index],
        }));
        byte[]SourceBytes=File.ReadAllBytes(FullPath);
        Hash256 SourceHash=Hash256.Compute(SourceBytes);
        RenderMaterialCatalog MaterialCatalog=new();
        for(int Index=0;Index<Scene.Materials.Count;Index++){
            Materials.Add(StageGltfAssets.BakeMaterial(
                MaterialCatalog,Scene.Materials[Index],SourceHash,Index));
        }

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

    /// <summary>
    /// ADR-0078/M0110：ビルドが glTF の隣へ書き出したメッシュ距離場（<c>&lt;asset&gt;.glb.cgesdf</c>）。インポーター順にメッシュごとに1つ格納する。ファイル
    /// がない、読み込めない、または読み込んだメッシュとは異なる三角形から作られている（コンテンツハッシュが違う）場合、そのメッシュには距離場を設定しない。そのメッシュはグローバル距離場に含まれないが、ゲーム
    /// は引き続き実行される。
    /// </summary>
    internal static MeshDistanceField?[]LoadDistanceFields(string FullPath,ImportedScene Scene){
        var Fields=new MeshDistanceField?[Scene.Meshes.Count];
        var FieldPath=FullPath+".cgesdf";
        if(!File.Exists(FieldPath)){
            return Fields;
        }
        if(!MeshDistanceFieldCodec.TryRead(File.ReadAllBytes(FieldPath),out var Entries,out _)||Entries.Count!=Scene.Meshes.Count){
            return Fields;
        }
        for(var Index=0;Index<Fields.Length;Index++){
            if(Entries[Index].Field is{ }Field&&Field.MeshHash==MeshDistanceField.ComputeMeshHash(Scene.Meshes[Index].Positions,Scene.Meshes[Index].Indices)){
                Fields[Index]=Field;
            }
        }
        return Fields;
    }
    /// <summary>
    /// スキニングされた glTF のメッシュとマテリアルのコピー1組を <paramref name="Meshes"/> と <paramref name="Materials"/> に追加する（
    /// レンダラーは各コピーを専用パレットでスキニングする）。そのオブジェクトを返し、スキニング対象のレンダラーメッシュインデックスも設定する。インデックスは追加前の各リストの長さから数える。
    /// </summary>
    internal static (AssetTemplate Template,int[]SkinnedMeshIndices)LoadSkeletalAsset(string AssetPath,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        var Imported=SkeletalAssetCache.Scene(AssetPath);
        var FullPath=SkeletalAssetCache.ResolvePath(AssetPath);
        var (Placements,Skinned)=StageGltfAssets.AppendSkeletal(Imported,FullPath,AssetPath,Meshes,Materials);
        return (new AssetTemplate([.. Placements.Select(Item=>new AssetObject(Item.MeshIndex,Item.MaterialIndex,Item.LocalTransform))]),Skinned);
    }

    internal sealed record AssetTemplate(AssetObject[]Objects);
    /// <summary>SkeletalMesh Entity がレンダラーで使う情報。元アセット、そのオブジェクト、スキニングするレンダラーメッシュ。</summary>
    internal sealed record SkeletalInstance(Entity Entity,string FullPath,AssetTemplate Template,int[]SkinnedMeshIndices);
    internal readonly record struct AssetObject(int MeshIndex,int MaterialIndex,Matrix4x4 LocalTransform);
}
