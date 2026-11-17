using System.Numerics;
using CSharpGameEngine.Content;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Content.VirtualGeometry;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Runtime.Windows;
internal static class StageModelAssets {
    internal readonly record struct Placement(int MeshIndex,int MaterialIndex,Matrix4x4 LocalTransform);
    internal static Placement[]AppendStatic(ModelAssetBundle Bundle,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials,MeshDistanceField?[]?Fields=null)=>Append(Bundle,Meshes,Materials,false,Fields).Placements;
    internal static (Placement[]Placements,int[]Skinned)AppendSkeletal(ModelAssetBundle Bundle,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials)=>Append(Bundle,Meshes,Materials,true,null);
    private static (Placement[]Placements,int[]Skinned)Append(ModelAssetBundle Bundle,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials,bool Skeletal,MeshDistanceField?[]?Fields){
        var Model=Bundle.Model;
        if(Skeletal&&Model.Skins.Length!=1){ throw new InvalidDataException("A skeletal model must have exactly one normalized skin."); }
        if(Fields is not null&&Fields.Length!=Bundle.Meshes.Length){ throw new ArgumentException("Distance field count differs from the model mesh count.",nameof(Fields)); }
        var MaterialOffset=Materials.Count;
        AppendMaterials(Bundle,Materials);
        var World=new Matrix4x4[Model.Nodes.Length];
        var Geometry=new Dictionary<(int Mesh,int Submesh,int Skin),int>();
        // 取り込みで作った仮想ジオメトリ（M0130）。スキニングするメッシュには作らないので、静的なモデルだけが持つ。
        Dictionary<(int Mesh,int Submesh),VirtualGeometryMesh>VirtualGeometry=Skeletal?new():VirtualGeometryCodec.Read(Model).ToDictionary(static Entry=>(Entry.MeshIndex,Entry.SubmeshIndex),static Entry=>Entry.Mesh);
        var Placements=new List<Placement>();var Skinned=new List<int>();
        for(var NodeIndex=0;NodeIndex<Model.Nodes.Length;NodeIndex++){
            var Node=Model.Nodes[NodeIndex];
            World[NodeIndex]=Node.Local.ToMatrix()*(Node.ParentIndex<0?Matrix4x4.Identity:World[Node.ParentIndex]);
            if(Node.MeshIndex<0){ continue; }
            var Mesh=Bundle.Meshes[Node.MeshIndex];
            var SkinIndex=Skeletal?Node.SkinIndex:-1;
            for(var SubmeshIndex=0;SubmeshIndex<Mesh.Submeshes.Length;SubmeshIndex++){
                var Submesh=Mesh.Submeshes[SubmeshIndex];
                var Slots=Node.MaterialSlots.IsEmpty?Mesh.DefaultMaterials:Node.MaterialSlots;
                if(Submesh.MaterialSlot<0||Submesh.MaterialSlot>=Slots.Length||Slots[Submesh.MaterialSlot]<0||Slots[Submesh.MaterialSlot]>=Model.Materials.Length){ throw new InvalidDataException("A model draw has no valid material slot."); }
                var Key=(Node.MeshIndex,SubmeshIndex,SkinIndex);
                if(!Geometry.TryGetValue(Key,out var MeshIndex)){
                    StageSkinCreateInfo?Skin=null;
                    if(SkinIndex>=0){
                        if(Mesh.Influences is not{}Influences){ throw new InvalidDataException("A skinned model mesh has no joint and weight streams."); }
                        var Binding=Model.Skins[SkinIndex];var Skeleton=Bundle.Skeletons[Binding.SkeletonIndex];
                        var Joints=new ushort[Influences.JointIndices.Length];
                        for(var Index=0;Index<Joints.Length;Index++){
                            var Source=Influences.JointIndices[Index];
                            if(Source>=Binding.JointMap.Length){ throw new InvalidDataException("A mesh influence is absent from its skin joint map."); }
                            Joints[Index]=checked((ushort)Binding.JointMap[Source]);
                        }
                        Skin=new(){JointCount=Skeleton.JointNames.Length,JointIndices=Joints,Weights=Influences.Weights};
                    }
                    MeshIndex=Meshes.Count;Geometry.Add(Key,MeshIndex);
                    Meshes.Add(new(){Positions=Mesh.Positions,Normals=Mesh.Normals,Tangents=Mesh.Tangents,Uvs=Mesh.TextureCoordinates[0],
                        Indices=Mesh.Indices.AsSpan(Submesh.FirstIndex,Submesh.IndexCount).ToArray(),Skin=Skin,DistanceField=Fields?[Node.MeshIndex],
                        VirtualGeometry=SkinIndex<0&&VirtualGeometry.TryGetValue((Node.MeshIndex,SubmeshIndex),out var Built)?ToStage(Built):null});
                    if(Skin is not null){ Skinned.Add(MeshIndex); }
                }
                Placements.Add(new(MeshIndex,MaterialOffset+Slots[Submesh.MaterialSlot],SkinIndex>=0?Matrix4x4.Identity:World[NodeIndex]));
            }
        }
        return ([..Placements],[..Skinned]);
    }
    /// <summary>仮想ジオメトリを、描画器の GPU の表の形へ写す。</summary>
    internal static StageVirtualGeometryCreateInfo ToStage(VirtualGeometryMesh Mesh){
        var Vertices=new StageVirtualGeometryVertex[Mesh.Vertices.Length];
        for(var Index=0;Index<Vertices.Length;Index++){
            var Vertex=Mesh.Vertices[Index];
            Vertices[Index]=new(){ Position=Vertex.Position,Normal=Vertex.Normal,Tangent=Vertex.Tangent,Uv=Vertex.Uv };
        }
        var Clusters=new StageVirtualGeometryCluster[Mesh.Clusters.Length];
        for(var Index=0;Index<Clusters.Length;Index++){
            var Cluster=Mesh.Clusters[Index];
            Clusters[Index]=new(){ FirstIndex=checked((uint)Cluster.FirstIndex),IndexCount=checked((uint)Cluster.IndexCount),Group=checked((uint)Cluster.Group),ParentGroup=Cluster.ParentGroup };
        }
        var Groups=new StageVirtualGeometryGroup[Mesh.Groups.Length];
        var TriangleSizes=Mesh.GroupTriangleSizes();
        for(var Index=0;Index<Groups.Length;Index++){
            var Group=Mesh.Groups[Index];
            Groups[Index]=new(){ BoundsCentre=Group.BoundsCentre,BoundsRadius=Group.BoundsRadius,Error=Group.Error,TriangleSize=TriangleSizes[Index] };
        }
        return new(){ Vertices=Vertices,Indices=Mesh.Indices,Clusters=Clusters,Groups=Groups,
            ObjectNormalMap=Mesh.NormalMap is{ }NormalMap?RenderArtifactCodec.CreateProceduralTexture(Mesh.NormalMapSize,Mesh.NormalMapSize,NormalMap):null };
    }
    private static void AppendMaterials(ModelAssetBundle Bundle,List<StageMaterialCreateInfo>Materials){
        var Catalog=new RenderMaterialCatalog();
        foreach(var Entry in Bundle.Manifest.Entries.Where(static Entry=>Entry.Kind==ContentArtifactKind.Texture)){
            var Bytes=Bundle.ReadEnvelopeAsync(ModelReference.From(Entry.Reference)).AsTask().GetAwaiter().GetResult();
            Catalog.RegisterTexture(Entry.Reference.Key,Bytes.Span);
        }
        foreach(var Reference in Bundle.Model.Materials){
            var Bytes=Bundle.ReadEnvelopeAsync(Reference).AsTask().GetAwaiter().GetResult();
            var Material=Catalog.RegisterMaterial(Reference.ToReference().Key,Bytes.Span);
            Materials.Add(new(){BaseColorFactor=Material.BaseColorFactor,RoughnessFactor=Material.RoughnessFactor,MetallicFactor=Material.MetallicFactor,
                EmissiveNits=Material.EmissiveNits,EmissiveTextureLuminanceScale=Material.EmissiveTextureLuminanceScale,BaseColorTexture=Material.BaseColorTexture,
                NormalTexture=Material.NormalTexture,MetallicRoughnessTexture=Material.MetallicRoughnessTexture,EmissiveTexture=Material.EmissiveTexture,WorldGrid=Material.WorldGrid});
        }
    }
}
