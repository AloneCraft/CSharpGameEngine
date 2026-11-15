using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Server;
using CSharpGameEngine.Runtime.Animation;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.EditorPreview.Host;

/// <summary>
/// 制作側が生成したモデルのメッシュ・マテリアルをゲームと同じ <c>StageModelAssets</c> で作り、
/// 姿勢はゲームと同じ <see cref="Skeleton"/>／<see cref="AnimationClip"/> で評価する。元ファイルは表示ホストで解析しない。
/// </summary>
internal sealed class RenderPreviewSkeletalAsset {
    /// <summary>骨の色。</summary>
    private static readonly Vector4 BoneColor=new(1f,0.82f,0.18f,1f);
    /// <summary>選択したジョイントへ入る骨と、そこから出る骨の色。</summary>
    private static readonly Vector4 SelectedBoneColor=new(0.2f,0.9f,1f,1f);

    private readonly Skeleton Skeleton;
    private readonly AnimationClip[]Clips;
    private readonly int[]SkinnedMeshIndices;
    private readonly JointPose[]Pose;
    private readonly Matrix4x4[]Model;
    private readonly Matrix4x4[]Palette;
    private readonly StageModelAssets.Placement[]Placements;
    private StageMeshCreateInfo[]Meshes=[];
    private StageMaterialCreateInfo[]Materials=[];
    private string MeshPath=string.Empty;
    private string AnimationPath=string.Empty;
    private int MeshOffset;
    private int MaterialOffset;

    private RenderPreviewSkeletalAsset(
        Skeleton Skeleton,AnimationClip[]Clips,int[]SkinnedMeshIndices,StageModelAssets.Placement[]Placements,RenderPreviewAssetSummary Summary){
        this.Skeleton=Skeleton;
        this.Clips=Clips;
        this.SkinnedMeshIndices=SkinnedMeshIndices;
        this.Placements=Placements;
        this.Summary=Summary;
        this.Pose=new JointPose[Skeleton.JointCount];
        this.Model=new Matrix4x4[Skeleton.JointCount];
        this.Palette=new Matrix4x4[Skeleton.JointCount];
        Skeleton.Rest.CopyTo(this.Pose);
        Skeleton.ComputePalette(this.Pose,this.Model,this.Palette);
    }

    internal RenderPreviewAssetSummary Summary { get;}
    /// <summary>このアセットの描画オブジェクト（メッシュ・マテリアルのインデックスは明示分の後ろへ足した番号）。</summary>
    internal ReadOnlySpan<StageModelAssets.Placement>ObjectPlacements=>this.Placements;
    internal bool Matches(RenderPreviewAssetSource Source)=>
        this.MeshPath==RenderPreviewModelLoader.Identity(Source.MeshModel)&&this.AnimationPath==RenderPreviewModelLoader.Identity(Source.AnimationModel);
    /// <summary>読み込み済みのCPUデータを新しい描画構成へ配置する。ソースファイルやテクスチャは読み直さない。</summary>
    internal RenderPreviewSkeletalAsset AppendTo(List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        var MeshOffset=Meshes.Count;
        var MaterialOffset=Materials.Count;
        Meshes.AddRange(this.Meshes);
        Materials.AddRange(this.Materials);
        var Placements=this.Placements.Select(Value=>new StageModelAssets.Placement(
            Value.MeshIndex-this.MeshOffset+MeshOffset,Value.MaterialIndex-this.MaterialOffset+MaterialOffset,Value.LocalTransform)).ToArray();
        var Skinned=this.SkinnedMeshIndices.Select(Index=>Index-this.MeshOffset+MeshOffset).ToArray();
        var Attached=new RenderPreviewSkeletalAsset(this.Skeleton,this.Clips,Skinned,Placements,this.Summary){
            Meshes=this.Meshes,Materials=this.Materials,MeshPath=this.MeshPath,AnimationPath=this.AnimationPath,
            MeshOffset=MeshOffset,MaterialOffset=MaterialOffset,
        };
        this.Pose.CopyTo(Attached.Pose,0);
        this.Model.CopyTo(Attached.Model,0);
        this.Palette.CopyTo(Attached.Palette,0);
        return Attached;
    }

    /// <summary>
    /// <paramref name="Source"/> を読み、メッシュとマテリアルを <paramref name="Meshes"/>／<paramref name="Materials"/> の末尾へ足す。
    /// スキンを正確に 1 つ持たない、読めない、などの場合は、利用者に見せる理由を持つ例外をスローする。
    /// </summary>
    internal static RenderPreviewSkeletalAsset Load(RenderPreviewAssetSource Source,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        using var Bundle=RenderPreviewModelLoader.Open(Source.MeshModel);
        if(Bundle.Model.Skins.Length!=1){ throw new InvalidDataException("A skeletal preview model must contain exactly one skin."); }
        var Skin=Bundle.Model.Skins[0];var Rig=Skeleton.FromModel(Bundle.Skeletons[Skin.SkeletonIndex],Skin);
        var MeshOffset=Meshes.Count;
        var MaterialOffset=Materials.Count;
        var (Placements,Skinned)=StageModelAssets.AppendSkeletal(Bundle,Meshes,Materials);
        var Clips=LoadClips(Source,Bundle,Rig);
        var Model=new Matrix4x4[Rig.JointCount];
        var Palette=new Matrix4x4[Rig.JointCount];
        Rig.ComputePalette(Rig.Rest,Model,Palette);
        var Summary=new RenderPreviewAssetSummary {
            JointNames=[.. Rig.JointNames],
            JointParents=[.. Enumerable.Range(0,Rig.JointCount).Select(Rig.ParentOf)],
            SkinnedMeshCount=Skinned.Length,
            Clips=[.. Clips.Select(static Clip=>new RenderPreviewClipSummary { Name=Clip.Name,DurationSeconds=Clip.DurationSeconds,BoundTrackCount=Clip.BoundTrackCount })],
            RestBounds=RenderPreviewModelBounds.Calculate(Meshes,Placements,Palette,true),
        };
        return new RenderPreviewSkeletalAsset(Rig,Clips,Skinned,Placements,Summary){
            Meshes=Meshes.Skip(MeshOffset).ToArray(),Materials=Materials.Skip(MaterialOffset).ToArray(),
            MeshPath=RenderPreviewModelLoader.Identity(Source.MeshModel),AnimationPath=RenderPreviewModelLoader.Identity(Source.AnimationModel),
            MeshOffset=MeshOffset,MaterialOffset=MaterialOffset,
        };
    }

    private static AnimationClip[]LoadClips(RenderPreviewAssetSource Source,ModelAssetBundle Mesh,Skeleton Skeleton){
        if(string.IsNullOrEmpty(Source.AnimationPath)||RenderPreviewModelLoader.Identity(Source.AnimationModel)==RenderPreviewModelLoader.Identity(Source.MeshModel)){
            return [..Mesh.Clips.Select(Clip=>AnimationClip.Bind(Clip,Skeleton))];
        }
        using var Animation=RenderPreviewModelLoader.Open(Source.AnimationModel);
        return [..Animation.Clips.Select(Clip=>AnimationClip.Bind(Clip,Skeleton))];
    }

    /// <summary>フレームのアニメーションを評価してスキンのパレットを反映し、ボーンの線（<paramref name="BoneLines"/>）を作る。</summary>
    internal void Apply(RenderServer Server,RenderPreviewFrame Frame,List<RenderOverlayVertex>BoneLines){
        if(Frame.Animation is{ }Animation){
            if(Animation.ClipIndex>=0&&Animation.ClipIndex<this.Clips.Length&&this.Clips[Animation.ClipIndex].BoundTrackCount>0){
                this.Clips[Animation.ClipIndex].Sample(this.Skeleton,Animation.TimeSeconds,Animation.Loop,this.Pose);
            } else {
                this.Skeleton.Rest.CopyTo(this.Pose);
            }
            this.Skeleton.ComputePalette(this.Pose,this.Model,this.Palette);
        }
        foreach(int MeshIndex in this.SkinnedMeshIndices){
            Server.SetSkinPalette(MeshIndex,this.Palette);
        }
        if(!Frame.ShowSkeleton){ return; }
        int Selected=Frame.SelectedJoint;
        for(int Joint=0;Joint<this.Skeleton.JointCount;Joint++){
            int Parent=this.Skeleton.ParentOf(Joint);
            if(Parent<0){ continue; }
            Vector4 Color=Joint==Selected||Parent==Selected?SelectedBoneColor:BoneColor;
            BoneLines.Add(new RenderOverlayVertex(this.Model[Parent].Translation,Color));
            BoneLines.Add(new RenderOverlayVertex(this.Model[Joint].Translation,Color));
        }
    }
}
