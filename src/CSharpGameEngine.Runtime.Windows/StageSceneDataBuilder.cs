using System.Numerics;
using System.Text;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>宣言された個別StaticMeshを、固定ロースター型StageRendererの生成入力へ変換する。</summary>
internal sealed class StageSceneDataBuilder {
    private static readonly StableId RenderAssetNamespace=
        StableId.Parse("98a2fc1e-d67f-5d92-8813-00ce97d444fd");
    private static readonly Hash256 RenderImportOptionsHash=
        Hash256.Compute(Encoding.UTF8.GetBytes("render-material-v1"));
    private StageSceneDataBuilder(
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

    public static StageSceneDataBuilder Build(
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
            throw new InvalidOperationException("Runtime requires at least one StaticMesh or PrimitiveMesh.");
        }

        return new StageSceneDataBuilder([.. Meshes],[.. Materials],[.. Objects],
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
        byte[]SourceBytes=File.ReadAllBytes(FullPath);
        Hash256 SourceHash=Hash256.Compute(SourceBytes);
        RenderMaterialCatalog MaterialCatalog=new();
        for(int Index=0;Index<Scene.Materials.Count;Index++){
            Materials.Add(BakeMaterial(
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

    private static StageMaterialCreateInfo BakeMaterial(
        RenderMaterialCatalog Catalog,ImportedPbrMaterial Material,
        Hash256 SourceHash,int MaterialIndex){
        ArtifactProvenance Provenance=new(
            "render-material-v1",$"asset-source://{SourceHash}",SourceHash,
            "CSharpGameEngine.Import.Gltf",RenderImportOptionsHash);
        AssetArtifactReference BaseColor=
            BakeTexture(Catalog,Provenance,SourceHash,MaterialIndex,"base-color",
                Material.BaseColorTextureBytes,RenderMaterialDefaults.White).Reference;
        AssetArtifactReference Normal=
            BakeTexture(Catalog,Provenance,SourceHash,MaterialIndex,"normal",
                Material.NormalTextureBytes,RenderMaterialDefaults.FlatNormal).Reference;
        AssetArtifactReference MetallicRoughness=
            BakeTexture(Catalog,Provenance,SourceHash,MaterialIndex,"metallic-roughness",
                Material.MetallicRoughnessTextureBytes,RenderMaterialDefaults.White).Reference;
        AssetArtifactReference Emissive=
            BakeTexture(Catalog,Provenance,SourceHash,MaterialIndex,"emissive",
                Material.EmissiveTextureBytes,RenderMaterialDefaults.White).Reference;
        Vector3 EmissiveNits=Material.EmissiveNits!=Vector3.Zero
            ?Material.EmissiveNits
            :Material.EmissiveFactor*100f;
        RenderMaterialArtifactDefinition Definition=new(
            Material.BaseColorFactor,Material.RoughnessFactor,Material.MetallicFactor,
            EmissiveNits,Material.EmissiveTextureLuminanceScale,
            BaseColor,Normal,MetallicRoughness,Emissive);
        AssetKey MaterialKey=CreateKey(SourceHash,MaterialIndex,"material");
        RenderMaterialResource Resource=Catalog.RegisterMaterial(
            MaterialKey,RenderArtifactCodec.CreateMaterialEnvelope(Provenance,Definition));
        return new StageMaterialCreateInfo {
            BaseColorFactor=Resource.BaseColorFactor,
            RoughnessFactor=Resource.RoughnessFactor,
            MetallicFactor=Resource.MetallicFactor,
            EmissiveNits=Resource.EmissiveNits,
            EmissiveTextureLuminanceScale=Resource.EmissiveTextureLuminanceScale,
            BaseColorTexture=Resource.BaseColorTexture,
            NormalTexture=Resource.NormalTexture,
            MetallicRoughnessTexture=Resource.MetallicRoughnessTexture,
            EmissiveTexture=Resource.EmissiveTexture,
        };
    }

    private static (AssetArtifactReference Reference,RenderTextureResource Resource)BakeTexture(
        RenderMaterialCatalog Catalog,ArtifactProvenance Provenance,Hash256 SourceHash,
        int MaterialIndex,string Slot,byte[]?EncodedBytes,RenderTextureResource Fallback){
        int Width;
        int Height;
        byte[]Pixels;
        if(EncodedBytes is null||EncodedBytes.Length==0){
            Width=Fallback.Width;
            Height=Fallback.Height;
            Pixels=Fallback.Pixels.ToArray();
        } else {
            WicDecodeResult Result=WicImageDecoder.Decode(EncodedBytes);
            if(!Result.Success||Result.Image is not{ }Image){
                throw new InvalidDataException(
                    $"Material {MaterialIndex} texture '{Slot}' decode failed: {Result.Error}");
            }
            Width=Image.Width;
            Height=Image.Height;
            Pixels=Image.Pixels;
        }
        AssetKey Key=CreateKey(SourceHash,MaterialIndex,Slot);
        RenderTextureResource Resource=Catalog.RegisterTexture(
            Key,RenderArtifactCodec.CreateTextureEnvelope(Provenance,Width,Height,Pixels));
        return(new AssetArtifactReference(Key,Resource.ContentHash),Resource);
    }

    private static AssetKey CreateKey(Hash256 SourceHash,int MaterialIndex,string Slot)=>new(
        new AssetId(StableId.FromName(
            RenderAssetNamespace,$"{SourceHash}/material/{MaterialIndex}/{Slot}")),null);


    private sealed record AssetTemplate(AssetObject[]Objects);
    private readonly record struct AssetObject(int MeshIndex,int MaterialIndex,Matrix4x4 LocalTransform);
}
