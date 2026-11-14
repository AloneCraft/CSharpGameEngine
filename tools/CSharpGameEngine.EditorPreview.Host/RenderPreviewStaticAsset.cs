using CSharpGameEngine.Contracts;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.EditorPreview.Host;
/// <summary>生成済みモデルの CPU データを保持し、床の変更時にも再配置する。</summary>
internal sealed class RenderPreviewStaticAsset {
    private readonly string PathValue;
    private readonly StageMeshCreateInfo[]Meshes;
    private readonly StageMaterialCreateInfo[]Materials;
    private readonly StageModelAssets.Placement[]Placements;
    private readonly int MeshOffset;
    private readonly int MaterialOffset;
    internal RenderPreviewAssetBounds Bounds { get;}
    private RenderPreviewStaticAsset(string PathValue,StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,StageModelAssets.Placement[]Placements,int MeshOffset,int MaterialOffset,RenderPreviewAssetBounds Bounds){
        this.PathValue=PathValue;this.Meshes=Meshes;this.Materials=Materials;this.Placements=Placements;this.MeshOffset=MeshOffset;this.MaterialOffset=MaterialOffset;this.Bounds=Bounds;
    }
    internal ReadOnlySpan<StageModelAssets.Placement>ObjectPlacements=>this.Placements;
    internal bool Matches(RenderPreviewModelSource?Source)=>this.PathValue==RenderPreviewModelLoader.Identity(Source);
    internal static RenderPreviewStaticAsset Load(RenderPreviewModelSource?Source,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        using var Bundle=RenderPreviewModelLoader.Open(Source);
        var MeshOffset=Meshes.Count;
        var MaterialOffset=Materials.Count;
        var Placements=StageModelAssets.AppendStatic(Bundle,Meshes,Materials);
        if(Placements.Length==0){ throw new InvalidDataException("The static preview asset has no renderable nodes."); }
        var Bounds=RenderPreviewModelBounds.Calculate(Meshes,Placements,[],false);
        return new RenderPreviewStaticAsset(RenderPreviewModelLoader.Identity(Source),Meshes.Skip(MeshOffset).ToArray(),Materials.Skip(MaterialOffset).ToArray(),Placements,MeshOffset,MaterialOffset,Bounds);
    }
    internal RenderPreviewStaticAsset AppendTo(List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        var MeshOffset=Meshes.Count;
        var MaterialOffset=Materials.Count;
        Meshes.AddRange(this.Meshes);Materials.AddRange(this.Materials);
        var Placements=this.Placements.Select(Value=>new StageModelAssets.Placement(Value.MeshIndex-this.MeshOffset+MeshOffset,Value.MaterialIndex-this.MaterialOffset+MaterialOffset,Value.LocalTransform)).ToArray();
        return new RenderPreviewStaticAsset(this.PathValue,this.Meshes,this.Materials,Placements,MeshOffset,MaterialOffset,this.Bounds);
    }
}
