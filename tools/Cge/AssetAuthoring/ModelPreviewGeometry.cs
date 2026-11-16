using System.Numerics;
using CSharpGameEngine.Content;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Import.AssetAuthoring.Cli;
internal static class ModelPreviewGeometry {
    internal static async Task<RenderPreviewScene>ReadAsync(RenderPreviewModelSource Source,CancellationToken CancellationToken){
        var Error=RenderPreviewSkeletalValidation.ValidateModel(Source,Source.SourcePath);
        if(Error is not null){ throw new InvalidDataException(Error); }
        await using var Bundle=await ModelAssetBundle.OpenAsync(Source.ContentDirectory,Source.SourceUri,CancellationToken);
        if(Bundle.Index.Resolve(Source.SourceUri,ContentArtifactKind.Model).ContentHash.ToString()!=Source.ModelHash){ throw new InvalidDataException("Prepared model hash differs from the requested preview."); }
        var Materials=new List<RenderPreviewMaterial>();var Catalog=new RenderMaterialCatalog();
        foreach(var Entry in Bundle.Manifest.Entries.Where(static Entry=>Entry.Kind==ContentArtifactKind.Texture)){
            var Bytes=await Bundle.ReadEnvelopeAsync(ModelReference.From(Entry.Reference),CancellationToken);Catalog.RegisterTexture(Entry.Reference.Key,Bytes.Span);
        }
        foreach(var Reference in Bundle.Model.Materials){
            var Bytes=await Bundle.ReadEnvelopeAsync(Reference,CancellationToken);
            var Material=Catalog.RegisterMaterial(Reference.ToReference().Key,Bytes.Span);
            Materials.Add(new(){BaseColor=[Material.BaseColorFactor.X,Material.BaseColorFactor.Y,Material.BaseColorFactor.Z,Material.BaseColorFactor.W],Roughness=Material.RoughnessFactor,Metallic=Material.MetallicFactor,
                WorldGrid=Material.WorldGrid is{}Grid?new(){GridSizeMeters=Grid.GridSizeMeters,SubGridDivisions=Grid.SubGridDivisions,WireHalfWidth=Grid.WireHalfWidth,GridColor=[Grid.GridColor.X,Grid.GridColor.Y,Grid.GridColor.Z],SubGridColor=[Grid.SubGridColor.X,Grid.SubGridColor.Y,Grid.SubGridColor.Z],AnchorToObject=Grid.AnchorToObject}:null});
        }
        var Meshes=new List<RenderPreviewMesh>();var Objects=new List<RenderPreviewObject>();var World=new Matrix4x4[Bundle.Model.Nodes.Length];
        for(var NodeIndex=0;NodeIndex<World.Length;NodeIndex++){
            CancellationToken.ThrowIfCancellationRequested();var Node=Bundle.Model.Nodes[NodeIndex];
            World[NodeIndex]=Node.Local.ToMatrix()*(Node.ParentIndex<0?Matrix4x4.Identity:World[Node.ParentIndex]);
            if(Node.MeshIndex<0){ continue; }
            var Mesh=Bundle.Meshes[Node.MeshIndex];var Slots=Node.MaterialSlots.IsEmpty?Mesh.DefaultMaterials:Node.MaterialSlots;
            foreach(var Submesh in Mesh.Submeshes){
                var MeshIndex=Meshes.Count;
                Meshes.Add(new(){Positions=Flatten(Mesh.Positions),Normals=Flatten(Mesh.Normals),Indices=Mesh.Indices.AsSpan(Submesh.FirstIndex,Submesh.IndexCount).ToArray()});
                var Matrix=World[NodeIndex];
                Objects.Add(new(){ObjectId="model/"+Objects.Count,MeshIndex=MeshIndex,MaterialIndex=Slots[Submesh.MaterialSlot],World=new(){Values=[Matrix.M11,Matrix.M12,Matrix.M13,Matrix.M14,Matrix.M21,Matrix.M22,Matrix.M23,Matrix.M24,Matrix.M31,Matrix.M32,Matrix.M33,Matrix.M34,Matrix.M41,Matrix.M42,Matrix.M43,Matrix.M44]}});
            }
        }
        if(Objects.Count==0){ throw new InvalidDataException("Prepared model contains no drawable geometry."); }
        return new(){Meshes=[..Meshes],Materials=[..Materials],Objects=[..Objects]};
    }
    private static float[]Flatten(IReadOnlyList<Vector3>Values){
        var Result=new float[Values.Count*3];
        for(var Index=0;Index<Values.Count;Index++){ Result[Index*3]=Values[Index].X;Result[Index*3+1]=Values[Index].Y;Result[Index*3+2]=Values[Index].Z; }
        return Result;
    }
}
