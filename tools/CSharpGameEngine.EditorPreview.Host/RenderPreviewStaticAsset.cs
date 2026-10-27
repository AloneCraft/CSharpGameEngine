using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.EditorPreview.Host;
/// <summary>静的 glTF の CPU データを保持し、床の変更時にもソースを再読込せず再配置する。</summary>
internal sealed class RenderPreviewStaticAsset {
    private readonly string PathValue;
    private readonly StageMeshCreateInfo[]Meshes;
    private readonly StageMaterialCreateInfo[]Materials;
    private readonly StageGltfAssets.Placement[]Placements;
    private readonly int MeshOffset;
    private readonly int MaterialOffset;
    internal RenderPreviewAssetBounds Bounds { get;}
    private RenderPreviewStaticAsset(string PathValue,StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,StageGltfAssets.Placement[]Placements,int MeshOffset,int MaterialOffset,RenderPreviewAssetBounds Bounds){
        this.PathValue=PathValue;this.Meshes=Meshes;this.Materials=Materials;this.Placements=Placements;this.MeshOffset=MeshOffset;this.MaterialOffset=MaterialOffset;this.Bounds=Bounds;
    }
    internal ReadOnlySpan<StageGltfAssets.Placement>ObjectPlacements=>this.Placements;
    internal bool Matches(string PathValue)=>string.Equals(this.PathValue,Path.GetFullPath(PathValue),StringComparison.OrdinalIgnoreCase);
    internal static RenderPreviewStaticAsset Load(string PathValue,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        var FullPath=Path.GetFullPath(PathValue);
        var Result=GltfImporter.ImportScene(FullPath);
        if(!Result.Success||Result.Scene is not{ }Scene){ throw new InvalidDataException($"The static mesh '{FullPath}' could not be imported: {string.Join("; ",Result.Diagnostics)}"); }
        if(Scene.Skins.Count!=0){ throw new InvalidDataException("A static preview asset must not contain a skin."); }
        var MeshOffset=Meshes.Count;
        var MaterialOffset=Materials.Count;
        var Placements=StageGltfAssets.AppendStatic(Scene,FullPath,Meshes,Materials);
        if(Placements.Length==0){ throw new InvalidDataException("The static preview asset has no renderable nodes."); }
        var Bounds=CalculateBounds(Scene);
        return new RenderPreviewStaticAsset(FullPath,Meshes.Skip(MeshOffset).ToArray(),Materials.Skip(MaterialOffset).ToArray(),Placements,MeshOffset,MaterialOffset,Bounds);
    }
    private static RenderPreviewAssetBounds CalculateBounds(ImportedScene Scene){
        var Min=new Vector3(float.PositiveInfinity);
        var Max=new Vector3(float.NegativeInfinity);
        var Found=false;
        var ReferencedMeshes=new HashSet<int>();
        foreach(var Node in Scene.Nodes){
            if(Node.MeshIndex is not int Index||Node.MaterialIndex is null){ continue; }
            ReferencedMeshes.Add(Index);
            foreach(var Position in Scene.Meshes[Index].Positions){
                var World=Vector3.Transform(Position,Node.WorldTransform);
                if(!float.IsFinite(World.X)||!float.IsFinite(World.Y)||!float.IsFinite(World.Z)){ throw new InvalidDataException("The static asset contains a non-finite transformed vertex."); }
                Min=Vector3.Min(Min,World);Max=Vector3.Max(Max,World);Found=true;
            }
        }
        if(!Found){ throw new InvalidDataException("The static preview asset has no vertices."); }
        var VertexCount=0;
        var TriangleCount=0;
        foreach(var Index in ReferencedMeshes){
            VertexCount=checked(VertexCount+Scene.Meshes[Index].Positions.Count);
            TriangleCount=checked(TriangleCount+Scene.Meshes[Index].Indices.Count/3);
        }
        return new RenderPreviewAssetBounds { Min=[Min.X,Min.Y,Min.Z],Max=[Max.X,Max.Y,Max.Z],VertexCount=VertexCount,TriangleCount=TriangleCount };
    }
    internal RenderPreviewStaticAsset AppendTo(List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        var MeshOffset=Meshes.Count;
        var MaterialOffset=Materials.Count;
        Meshes.AddRange(this.Meshes);Materials.AddRange(this.Materials);
        var Placements=this.Placements.Select(Value=>new StageGltfAssets.Placement(Value.MeshIndex-this.MeshOffset+MeshOffset,Value.MaterialIndex-this.MaterialOffset+MaterialOffset,Value.LocalTransform)).ToArray();
        return new RenderPreviewStaticAsset(this.PathValue,this.Meshes,this.Materials,Placements,MeshOffset,MaterialOffset,this.Bounds);
    }
}
