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
    private StageMeshCreateInfo[]Meshes=[];
    private StageMaterialCreateInfo[]Materials=[];
    private string MeshPath=string.Empty;
    private string AnimationPath=string.Empty;
    private int MeshOffset;
    private int MaterialOffset;

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
    internal bool Matches(RenderPreviewAssetSource Source)=>
        string.Equals(this.MeshPath,Path.GetFullPath(Source.MeshPath),StringComparison.OrdinalIgnoreCase)
        &&string.Equals(this.AnimationPath,string.IsNullOrEmpty(Source.AnimationPath)?string.Empty:Path.GetFullPath(Source.AnimationPath),StringComparison.OrdinalIgnoreCase);
    /// <summary>読み込み済みのCPUデータを新しい描画構成へ配置する。ソースファイルやテクスチャは読み直さない。</summary>
    internal RenderPreviewSkeletalAsset AppendTo(List<StageMeshCreateInfo>Meshes,List<StageMaterialCreateInfo>Materials){
        var MeshOffset=Meshes.Count;
        var MaterialOffset=Materials.Count;
        Meshes.AddRange(this.Meshes);
        Materials.AddRange(this.Materials);
        var Placements=this.Placements.Select(Value=>new StageGltfAssets.Placement(
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
        string MeshPath=Path.GetFullPath(Source.MeshPath);
        var Result=GltfImporter.ImportScene(MeshPath);
        if(!Result.Success||Result.Scene is not{ }Imported){
            throw new InvalidDataException($"The skeletal mesh '{MeshPath}' could not be imported: {string.Join("; ",Result.Diagnostics)}");
        }
        if(Imported.Skins.Count!=1){
            throw new InvalidDataException($"The skeletal mesh '{MeshPath}' must contain exactly one skin, found {Imported.Skins.Count}.");
        }
        var Rig=Skeleton.FromImported(Imported.Skins[0]);
        var MeshOffset=Meshes.Count;
        var MaterialOffset=Materials.Count;
        var (Placements,Skinned)=StageGltfAssets.AppendSkeletal(Imported,MeshPath,MeshPath,Meshes,Materials);
        var Clips=LoadClips(Source,MeshPath,Rig);
        var Model=new Matrix4x4[Rig.JointCount];
        var Palette=new Matrix4x4[Rig.JointCount];
        Rig.ComputePalette(Rig.Rest,Model,Palette);
        var Summary=new RenderPreviewAssetSummary {
            JointNames=[.. Rig.JointNames],
            JointParents=[.. Enumerable.Range(0,Rig.JointCount).Select(Rig.ParentOf)],
            SkinnedMeshCount=Skinned.Length,
            Clips=[.. Clips.Select(static Clip=>new RenderPreviewClipSummary { Name=Clip.Name,DurationSeconds=Clip.DurationSeconds,BoundTrackCount=Clip.BoundTrackCount })],
            RestBounds=CalculateRestBounds(Imported,Palette),
        };
        return new RenderPreviewSkeletalAsset(Rig,Clips,Skinned,Placements,Summary){
            Meshes=Meshes.Skip(MeshOffset).ToArray(),Materials=Materials.Skip(MaterialOffset).ToArray(),
            MeshPath=MeshPath,AnimationPath=string.IsNullOrEmpty(Source.AnimationPath)?string.Empty:Path.GetFullPath(Source.AnimationPath),
            MeshOffset=MeshOffset,MaterialOffset=MaterialOffset,
        };
    }

    /// <summary>レスト姿勢の全頂点を CPU で評価する。GPU ウェイト量子化の差を含む余白を加える。</summary>
    internal static RenderPreviewAssetBounds CalculateRestBounds(ImportedScene Scene,ReadOnlySpan<Matrix4x4>Palette){
        var Min=new Vector3(float.PositiveInfinity);
        var Max=new Vector3(float.NegativeInfinity);
        var Found=false;
        var Magnitude=1f;
        var Referenced=new HashSet<int>();
        var SkinnedMeshes=new HashSet<int>();
        foreach(var Node in Scene.Nodes){ if(Node.MeshIndex is int Index&&Node.SkinIndex is not null){ SkinnedMeshes.Add(Index); } }
        foreach(var Node in Scene.Nodes){
            if(Node.MeshIndex is not int Index||Node.MaterialIndex is null){ continue; }
            Referenced.Add(Index);
            var Mesh=Scene.Meshes[Index];
            var Skin=SkinnedMeshes.Contains(Index)?Mesh.Influences:null;
            var Transform=Node.SkinIndex is null?Node.WorldTransform:Matrix4x4.Identity;
            for(var Vertex=0;Vertex<Mesh.Positions.Count;Vertex++){
                var Position=Mesh.Positions[Vertex];
                if(Skin is not null){
                    var Accumulated=Vector3.Zero;
                    var Base=Vertex*ImportedSkinInfluences.InfluencesPerVertex;
                    for(var Slot=0;Slot<ImportedSkinInfluences.InfluencesPerVertex;Slot++){
                        var Weight=Skin.Weights[Base+Slot];
                        if(Weight<=0f){ continue; }
                        var Joint=Skin.JointIndices[Base+Slot];
                        if(Joint>=Palette.Length){ throw new InvalidDataException("A skeletal vertex references an invalid joint."); }
                        var Part=Vector3.Transform(Position,Palette[Joint]);
                        var WorldPart=Vector3.Transform(Part,Transform);
                        if(!float.IsFinite(WorldPart.X)||!float.IsFinite(WorldPart.Y)||!float.IsFinite(WorldPart.Z)){ throw new InvalidDataException("The skeletal asset contains a non-finite joint transform."); }
                        Magnitude=Math.Max(Magnitude,Math.Max(Math.Abs(WorldPart.X),Math.Max(Math.Abs(WorldPart.Y),Math.Abs(WorldPart.Z))));
                        Accumulated+=Part*Weight;
                    }
                    Position=Accumulated;
                }
                var World=Vector3.Transform(Position,Transform);
                if(!float.IsFinite(World.X)||!float.IsFinite(World.Y)||!float.IsFinite(World.Z)){ throw new InvalidDataException("The skeletal asset contains a non-finite transformed vertex."); }
                Min=Vector3.Min(Min,World);Max=Vector3.Max(Max,World);Found=true;
            }
        }
        if(!Found){ throw new InvalidDataException("The skeletal preview asset has no vertices."); }
        var Padding=Magnitude*0.00025f;
        Min-=new Vector3(Padding);Max+=new Vector3(Padding);
        var VertexCount=0;
        var TriangleCount=0;
        foreach(var Index in Referenced){
            VertexCount=checked(VertexCount+Scene.Meshes[Index].Positions.Count);
            TriangleCount=checked(TriangleCount+Scene.Meshes[Index].Indices.Count/3);
        }
        return new RenderPreviewAssetBounds { Min=[Min.X,Min.Y,Min.Z],Max=[Max.X,Max.Y,Max.Z],VertexCount=VertexCount,TriangleCount=TriangleCount };
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
