using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Server;
using CSharpGameEngine.Runtime.Animation;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.EditorPreview.Host;

/// <summary>
/// プレビューセッションが読み込んだスケルタル glTF 1 つ分（ADR-0090）。メッシュ・マテリアルはゲームと同じ <c>StageGltfAssets</c> で作り、
/// 姿勢はゲームと同じ <see cref="Skeleton"/>／<see cref="AnimationClip"/> で評価する。VSIX にはパスを受け取り、要約を返すだけで、glTF の中身は渡らない。
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
    private readonly StageGltfAssets.Placement[]Placements;

    private RenderPreviewSkeletalAsset(
        Skeleton Skeleton,AnimationClip[]Clips,int[]SkinnedMeshIndices,StageGltfAssets.Placement[]Placements,RenderPreviewAssetSummary Summary){
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
    internal ReadOnlySpan<StageGltfAssets.Placement>ObjectPlacements=>this.Placements;

    /// <summary>
    /// <paramref name="Source"/> を読み、メッシュとマテリアルを <paramref name="Meshes"/>／<paramref name="Materials"/> の末尾へ足す。
    /// スキンを正確に 1 つ持たない、読めない、などの場合は、利用者に見せる理由を持つ例外をスローする。
    /// </summary>
    internal static RenderPreviewSkeletalAsset Load(RenderPreviewAssetSource Source,List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        string MeshPath=Path.GetFullPath(Source.MeshPath);
        var Result=GltfImporter.ImportScene(MeshPath);
        if(!Result.Success||Result.Scene is not{ }Imported){
            throw new InvalidDataException($"The skeletal mesh '{MeshPath}' could not be imported: {string.Join("; ",Result.Diagnostics)}");
        }
        if(Imported.Skins.Count!=1){
            throw new InvalidDataException($"The skeletal mesh '{MeshPath}' must contain exactly one skin, found {Imported.Skins.Count}.");
        }
        var Rig=Skeleton.FromImported(Imported.Skins[0]);
        var (Placements,Skinned)=StageGltfAssets.AppendSkeletal(Imported,MeshPath,MeshPath,Meshes,Materials);
        var Clips=LoadClips(Source,MeshPath,Rig);
        var Summary=new RenderPreviewAssetSummary {
            JointNames=[.. Rig.JointNames],
            JointParents=[.. Enumerable.Range(0,Rig.JointCount).Select(Rig.ParentOf)],
            SkinnedMeshCount=Skinned.Length,
            Clips=[.. Clips.Select(static Clip=>new RenderPreviewClipSummary { Name=Clip.Name,DurationSeconds=Clip.DurationSeconds,BoundTrackCount=Clip.BoundTrackCount })],
        };
        return new RenderPreviewSkeletalAsset(Rig,Clips,Skinned,Placements,Summary);
    }

    private static AnimationClip[]LoadClips(RenderPreviewAssetSource Source,string MeshPath,Skeleton Skeleton){
        bool Explicit=!string.IsNullOrEmpty(Source.AnimationPath)&&!string.Equals(Path.GetFullPath(Source.AnimationPath),MeshPath,StringComparison.OrdinalIgnoreCase);
        string AnimationPath=Explicit?Path.GetFullPath(Source.AnimationPath):MeshPath;
        var Result=GltfImporter.ImportAnimations(AnimationPath);
        if(!Result.Success){
            if(Explicit){
                throw new InvalidDataException($"The animation '{AnimationPath}' could not be imported: {string.Join("; ",Result.Diagnostics)}");
            }
            return [];
        }
        return [.. Result.Clips.Select(Clip=>AnimationClip.Bind(Clip,Skeleton))];
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
