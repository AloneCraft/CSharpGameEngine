using System;
using System.Collections.Generic;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>FollowViewをドラッグ開始時の視点へ対するtotal deltaとして再計算する純粋snapshot。</summary>
internal readonly struct SceneViewFollowSnapshot {
    private SceneViewFollowSnapshot(Vector3 FocusPosition,float FocusDistance,float Yaw,float Pitch){
        this.FocusPosition=FocusPosition;
        this.FocusDistance=FocusDistance;
        this.Yaw=Yaw;
        this.Pitch=Pitch;
    }

    private Vector3 FocusPosition { get; }
    private float FocusDistance { get; }
    private float Yaw { get; }
    private float Pitch { get; }

    internal static SceneViewFollowSnapshot Capture(SceneViewState State)=>
        new(State.FocusPosition,State.FocusDistance,State.Yaw,State.Pitch);

    internal void ApplyMove(SceneViewState State,Vector3 TotalDelta){
        this.Restore(State);
        State.FocusPosition+=TotalDelta;
    }

    internal void ApplyRotation(SceneViewState State,Vector3 Pivot,Quaternion TotalDelta){
        var Rotation=SceneTransformMath.NormalizeRotation(TotalDelta);
        var OriginalForward=SceneViewCamera.GetForward(this.Yaw,this.Pitch);
        var OriginalEye=this.FocusPosition-(OriginalForward*this.FocusDistance);
        var NewEye=Pivot+Vector3.Transform(OriginalEye-Pivot,Rotation);
        var NewForward=Vector3.Normalize(Vector3.Transform(OriginalForward,Rotation));
        State.FocusDistance=this.FocusDistance;
        State.Yaw=(float)Math.Atan2(NewForward.X,NewForward.Z);
        State.Pitch=(float)Math.Asin(Math.Max(-1f,Math.Min(1f,NewForward.Y)));
        State.FocusPosition=NewEye+(NewForward*this.FocusDistance);
    }

    internal void Restore(SceneViewState State){
        State.FocusPosition=this.FocusPosition;
        State.FocusDistance=this.FocusDistance;
        State.Yaw=this.Yaw;
        State.Pitch=this.Pitch;
    }
}

internal readonly struct DuplicateNodePlanItem {
    internal DuplicateNodePlanItem(EditorNodeId NodeId,NodePath SourcePath,NodePath ResultPath){
        this.NodeId=NodeId;
        this.SourcePath=SourcePath;
        this.ResultPath=ResultPath;
    }

    internal EditorNodeId NodeId { get; }
    internal NodePath SourcePath { get; }
    internal NodePath ResultPath { get; }
}

internal readonly struct DuplicateRenderPreview {
    internal DuplicateRenderPreview(NodePath SourcePath,Matrix4x4 OriginalWorld,int Handle){
        this.SourcePath=SourcePath;
        this.OriginalWorld=OriginalWorld;
        this.Handle=Handle;
    }

    internal NodePath SourcePath { get; }
    internal Matrix4x4 OriginalWorld { get; }
    internal int Handle { get; }
}

/// <summary>構造編集後に選択を復元できるよう、兄弟末尾へ追加されるNodePathをsource変更前に確定する。</summary>
internal static class DuplicateNodePlanner {
    internal static NodePath RemapPath(NodePath Path,NodePath SourceRoot,NodePath ResultRoot){
        if(!Path.Equals(SourceRoot)&&!Path.IsDescendantOf(SourceRoot)){
            throw new ArgumentException(nameof(Path));
        }

        var Result=ResultRoot;
        for(var I=SourceRoot.Depth;I<Path.Depth;I++){
            Result=Result.Append(Path.Indices[I]);
        }
        return Result;
    }

    internal static bool TryResolveResultSelection(
        SceneProjection Projection,IReadOnlyList<NodePath>Paths,out IReadOnlyList<EditorNodeId>Ids){
        List<EditorNodeId>Resolved=[];
        foreach(var Path in Paths){
            if(!Projection.TryGetId(Path,out var Id)){
                Ids=Array.Empty<EditorNodeId>();
                return false;
            }
            Resolved.Add(Id);
        }
        Ids=Resolved;
        return Resolved.Count>0;
    }

    internal static bool TryCreate(
        IReadOnlyList<SceneProjectionNode>Roots,SceneProjection Projection,
        out IReadOnlyList<DuplicateNodePlanItem>Plan){
        List<DuplicateNodePlanItem>Items=[];
        Dictionary<NodePath,int>NextChildIndices=[];
        foreach(var Root in Roots){
            if(Root.Path.Parent is not { }ParentPath
                ||!Projection.TryGetId(ParentPath,out var ParentId)
                ||!Projection.TryGetNode(ParentId,out var Parent)){
                Plan=Array.Empty<DuplicateNodePlanItem>();
                return false;
            }

            if(!NextChildIndices.TryGetValue(ParentPath,out var NextIndex)){
                NextIndex=Parent.Node.Children.Length;
            }
            var ResultPath=ParentPath.Append(NextIndex);
            NextChildIndices[ParentPath]=NextIndex+1;
            Items.Add(new DuplicateNodePlanItem(Root.Id,Root.Path,ResultPath));
        }

        Plan=Items;
        return Items.Count>0;
    }
}
