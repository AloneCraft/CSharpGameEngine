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
    /// <summary>Authoring Entity whose fixed hierarchy owns each renderer object.</summary>
    public Entity[]SourceEntities { get;}
    /// <summary>The renderer meshes each SkeletalMesh entity skins with its palette.</summary>
    public (Entity Entity,int[]MeshIndices)[]SkinnedMeshes { get;}
    /// <summary>The meshes, materials and objects each SkeletalMesh entity at load became: what a later Entity of the same asset reuses once this one is gone (M0107).</summary>
    internal SkeletalInstance[]SkeletalInstances { get;}
    /// <summary>The meshes and materials each StaticMesh asset became, by resolved path: what an Entity spawned later reuses (M0105).</summary>
    internal Dictionary<string,AssetTemplate>StaticAssets { get;}
    /// <summary>The renderer mesh of each primitive type a PrimitiveMesh used, and the one material they share (M0105).</summary>
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
            // Each placement skins its own copy of the meshes, so two characters can hold different poses.
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
            // BuildPrimitiveRoster gives each distinct type one mesh, in the order the types first appear, and all of them one material.
            foreach(var Primitive in Primitives){
                PrimitiveMeshIndices.TryAdd(Primitive.Mesh.Type,Meshes.Count+PrimitiveMeshIndices.Count);
            }
            PrimitiveMaterialIndex=Materials.Count;
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

    /// <summary>
    /// ADR-0078/M0110: the mesh distance fields the build wrote next to the glTF (<c>&lt;asset&gt;.glb.cgesdf</c>), one per mesh in the importer's order.
    /// A missing or unreadable file, or an entry made from other triangles than the loaded mesh (its content hash differs), leaves that mesh without
    /// a field: it is then not in the global distance field, and the game still runs.
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
    /// Appends one copy of a skeletal glTF's meshes and materials to <paramref name="Meshes"/> and <paramref name="Materials"/> (the renderer skins each copy with
    /// its own palette) and returns its objects, with the renderer mesh indices of the skinned ones. The indices count from the lists' lengths on entry.
    /// </summary>
    internal static (AssetTemplate Template,int[]SkinnedMeshIndices)LoadSkeletalAsset(string AssetPath,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        var Imported=SkeletalAssetCache.Scene(AssetPath);
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
        var FullPath=SkeletalAssetCache.ResolvePath(AssetPath);
        var SourceHash=Hash256.Compute(File.ReadAllBytes(FullPath));
        RenderMaterialCatalog MaterialCatalog=new();
        for(var Index=0;Index<Imported.Materials.Count;Index++){
            Materials.Add(BakeMaterial(MaterialCatalog,Imported.Materials[Index],SourceHash,Index));
        }
        List<AssetObject>Objects=[];
        foreach(var Node in Imported.Nodes){
            if(Node.MeshIndex is int MeshIndex&&Node.MaterialIndex is int MaterialIndex){
                // The skin places skinned geometry (glTF ignores a skinned node's own transform).
                Objects.Add(new AssetObject(MeshOffset+MeshIndex,MaterialOffset+MaterialIndex,Node.SkinIndex is null?Node.WorldTransform:Matrix4x4.Identity));
            }
        }
        return (new AssetTemplate([.. Objects]),[.. Skinned]);
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
            // The procedural grid is not part of the render-material artifact (a texture-based PBR record);
            // it rides beside it so the artifact schema stays unchanged (ADR-0075).
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


    internal sealed record AssetTemplate(AssetObject[]Objects);
    /// <summary>A SkeletalMesh Entity's share of the renderer: the asset it came from, its objects and the renderer meshes it skins.</summary>
    internal sealed record SkeletalInstance(Entity Entity,string FullPath,AssetTemplate Template,int[]SkinnedMeshIndices);
    internal readonly record struct AssetObject(int MeshIndex,int MaterialIndex,Matrix4x4 LocalTransform);
}
