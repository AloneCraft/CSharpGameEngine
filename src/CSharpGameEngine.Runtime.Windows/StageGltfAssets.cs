using System.Numerics;
using System.Text;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// glTF のインポート結果を、固定ロースター型 StageRenderer の生成入力（メッシュ・マテリアル）へ変換する、ECS に依存しない部分（ADR-0090）。
/// ゲームの <see cref="StageSceneDataBuilder"/> とエディターのプレビューホストが同じソースを使うので、プレビューとゲームで見た目と姿勢の数式が一致する。
/// このファイルは Runtime.Windows の共有プロジェクトに属し、プレビューホストはリンクとして取り込む。
/// </summary>
internal static class StageGltfAssets {
    private static readonly StableId RenderAssetNamespace=
        StableId.Parse("98a2fc1e-d67f-5d92-8813-00ce97d444fd");
    private static readonly Hash256 RenderImportOptionsHash=
        Hash256.Compute(Encoding.UTF8.GetBytes("render-material-v1"));
    /// <summary>描画オブジェクト1つ分の配置（メッシュ・マテリアルのインデックスと、アセット内のローカル変換）。</summary>
    internal readonly record struct Placement(int MeshIndex,int MaterialIndex,Matrix4x4 LocalTransform);

    /// <summary>静的 glTF をゲームとプレビューで同じメッシュ・マテリアル・配置に変換する。</summary>
    internal static Placement[]AppendStatic(ImportedScene Imported,string FullPath,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials,MeshDistanceField?[]?Fields=null){
        var MeshOffset=Meshes.Count;
        var MaterialOffset=Materials.Count;
        for(var Index=0;Index<Imported.Meshes.Count;Index++){
            var Mesh=Imported.Meshes[Index];
            Meshes.Add(new StageMeshCreateInfo {
                Positions=Mesh.Positions,Normals=Mesh.Normals,Tangents=Mesh.Tangents,Uvs=Mesh.Uvs,Indices=Mesh.Indices,
                DistanceField=Fields is null?null:Fields[Index],
            });
        }
        var SourceHash=Hash256.Compute(File.ReadAllBytes(FullPath));
        RenderMaterialCatalog Catalog=new();
        for(var Index=0;Index<Imported.Materials.Count;Index++){
            Materials.Add(BakeMaterial(Catalog,Imported.Materials[Index],SourceHash,Index));
        }
        List<Placement>Placements=[];
        foreach(var Node in Imported.Nodes){
            if(Node.MeshIndex is int MeshIndex&&Node.MaterialIndex is int MaterialIndex){
                Placements.Add(new Placement(MeshOffset+MeshIndex,MaterialOffset+MaterialIndex,Node.WorldTransform));
            }
        }
        return [.. Placements];
    }

    /// <summary>スキニングされた glTF のメッシュとマテリアルを <paramref name="Meshes"/> と <paramref name="Materials"/> の末尾に追加し、各描画オブジェクトの配置とスキニング対象のレンダラーメッシュインデックスを返す。インデックスは追加前の各リストの長さから数える。</summary>
    internal static (Placement[]Placements,int[]SkinnedMeshIndices)AppendSkeletal(ImportedScene Imported,string FullPath,string AssetPath,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        var Skin=Imported.Skins[0];
        var MeshOffset=Meshes.Count;
        var MaterialOffset=Materials.Count;
        List<int>Skinned=[];
        for(var Index=0;Index<Imported.Meshes.Count;Index++){
            var Mesh=Imported.Meshes[Index];
            var IsSkinned=Imported.Nodes.Any(Node=>Node.MeshIndex==Index&&Node.SkinIndex is not null);
            if(IsSkinned&&Mesh.Influences is null){
                throw new InvalidDataException($"SkeletalMesh '{AssetPath}' has a skinned primitive without JOINTS/WEIGHTS.");
            }
            if(IsSkinned){
                Skinned.Add(Meshes.Count);
            }
            Meshes.Add(new StageMeshCreateInfo {
                Positions=Mesh.Positions,
                Normals=Mesh.Normals,
                Tangents=Mesh.Tangents,
                Uvs=Mesh.Uvs,
                Indices=Mesh.Indices,
                Skin=IsSkinned?new StageSkinCreateInfo { JointCount=Skin.JointNames.Count,JointIndices=Mesh.Influences!.JointIndices,Weights=Mesh.Influences.Weights }:null,
            });
        }
        var SourceHash=Hash256.Compute(File.ReadAllBytes(FullPath));
        RenderMaterialCatalog MaterialCatalog=new();
        for(var Index=0;Index<Imported.Materials.Count;Index++){
            Materials.Add(BakeMaterial(MaterialCatalog,Imported.Materials[Index],SourceHash,Index));
        }
        List<Placement>Placements=[];
        foreach(var Node in Imported.Nodes){
            if(Node.MeshIndex is int MeshIndex&&Node.MaterialIndex is int MaterialIndex){
                // スキンがスキニング対象ジオメトリを配置する（glTF ではスキニングノード自身の変換を無視する）。
                Placements.Add(new Placement(MeshOffset+MeshIndex,MaterialOffset+MaterialIndex,Node.SkinIndex is null?Node.WorldTransform:Matrix4x4.Identity));
            }
        }
        return ([.. Placements],[.. Skinned]);
    }

    internal static StageMaterialCreateInfo BakeMaterial(
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
            // プロシージャルグリッドはレンダーマテリアルのアーティファクト（テクスチャベースの PBR 記録）には含めず、
            // その隣に保持して、アーティファクトのスキーマを変更しない（ADR-0075）。
            WorldGrid=Material.WorldGrid is{ }Grid
                ?new StageWorldGrid(Grid.GridSizeMeters,Grid.SubGridDivisions,Grid.WireHalfWidth,Grid.GridColor,Grid.SubGridColor)
                :null,
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
}
