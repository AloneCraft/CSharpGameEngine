using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

internal enum EditTransactionState { Idle, Dragging, Committing }

internal readonly struct SceneTransformTarget {
    internal SceneTransformTarget(
        EditorNodeId NodeId,NodePath NodePath,Matrix4x4 OriginalWorld,Matrix4x4 ParentWorld){
        this.NodeId=NodeId;
        this.NodePath=NodePath;
        this.OriginalWorld=OriginalWorld;
        this.ParentWorld=ParentWorld;
    }

    internal EditorNodeId NodeId { get; }

    internal NodePath NodePath { get; }

    internal Matrix4x4 OriginalWorld { get; }

    internal Matrix4x4 ParentWorld { get; }
}

internal readonly struct SceneTransformPreview {
    internal SceneTransformPreview(EditorNodeId NodeId,NodePath NodePath,Matrix4x4 World){
        this.NodeId=NodeId;
        this.NodePath=NodePath;
        this.World=World;
    }

    internal EditorNodeId NodeId { get; }

    internal NodePath NodePath { get; }

    internal Matrix4x4 World { get; }
}

internal readonly struct SceneTransformChange {
    internal SceneTransformChange(EditorNodeId NodeId,SceneTransformValue Local)
        :this(NodeId,Local,Local){
    }

    internal SceneTransformChange(
        EditorNodeId NodeId,SceneTransformValue OriginalLocal,SceneTransformValue Local){
        this.NodeId=NodeId;
        this.OriginalLocal=OriginalLocal;
        this.Local=Local;
    }

    internal EditorNodeId NodeId { get; }

    internal SceneTransformValue OriginalLocal { get; }

    internal SceneTransformValue Local { get; }
}

internal sealed class SceneEditCommitRequest {
    internal SceneEditCommitRequest(
        SceneKey SceneKey,long DocumentVersion,string UndoDescription,IReadOnlyList<SceneTransformChange>Changes,
        SceneTransformMemberMask Members,IReadOnlyList<NodePath>?DuplicateResultPaths=null,
        IReadOnlyList<SceneEditOperation>?DirectOperations=null){
        this.SceneKey=SceneKey;
        this.DocumentVersion=DocumentVersion;
        this.UndoDescription=UndoDescription;
        this.Changes=Changes;
        this.Members=Members;
        this.DuplicateResultPaths=DuplicateResultPaths??Array.Empty<NodePath>();
        this.DirectOperations=DirectOperations??Array.Empty<SceneEditOperation>();
    }

    internal SceneKey SceneKey { get; }

    internal long DocumentVersion { get; }

    internal string UndoDescription { get; }

    internal IReadOnlyList<SceneTransformChange>Changes { get; }

    internal SceneTransformMemberMask Members { get; }

    internal IReadOnlyList<NodePath>DuplicateResultPaths { get; }

    internal IReadOnlyList<SceneEditOperation>DirectOperations { get; }

    internal bool IsDuplicate=>this.DuplicateResultPaths.Count>0;
}

internal readonly struct SceneEditCommitResult {
    internal SceneEditCommitResult(bool Succeeded,string?ErrorMessage,bool WasRebased){
        this.Succeeded=Succeeded;
        this.ErrorMessage=ErrorMessage;
        this.WasRebased=WasRebased;
    }

    internal bool Succeeded { get; }

    internal string?ErrorMessage { get; }

    internal bool WasRebased { get; }
}

internal sealed class SceneEditCancellation {
    internal SceneEditCancellation(
        IReadOnlyList<SceneTransformPreview>RestoredTransforms,SceneDocumentSnapshot?PendingSnapshot){
        this.RestoredTransforms=RestoredTransforms;
        this.PendingSnapshot=PendingSnapshot;
    }

    internal IReadOnlyList<SceneTransformPreview>RestoredTransforms { get; }

    internal SceneDocumentSnapshot?PendingSnapshot { get; }
}

/// <summary>
/// 1ユーザー操作をpreviewと単一source commitへまとめる純粋な状態機械。snapshot更新は最新1件だけ
/// 保持し、キャンセルまたはcommit完了時に所有者へ返す。
/// </summary>
internal sealed class EditTransactionController {
    private SceneDocumentSnapshot?StartedSnapshot;
    private SceneDocumentSnapshot?PendingSnapshot;
    private SceneTransformTarget[]Targets=[];
    private SceneTransformPreview[]Previews=[];
    private Matrix4x4 WorldDelta=Matrix4x4.Identity;

    internal EditTransactionState State { get;private set;}=EditTransactionState.Idle;

    internal void Begin(SceneDocumentSnapshot Snapshot,IReadOnlyList<SceneTransformTarget>Targets){
        if(State!=EditTransactionState.Idle){
            throw new InvalidOperationException("編集トランザクションは既に進行中です。");
        }
        if(Targets.Count==0){
            throw new ArgumentException("編集対象がありません。",nameof(Targets));
        }

        this.StartedSnapshot=Snapshot??throw new ArgumentNullException(nameof(Snapshot));
        this.Targets=Targets.ToArray();
        this.Previews=this.Targets.Select(static Target=>
            new SceneTransformPreview(Target.NodeId,Target.NodePath,Target.OriginalWorld)).ToArray();
        this.PendingSnapshot=null;
        State=EditTransactionState.Dragging;
    }

    internal void BeginDirect(SceneDocumentSnapshot Snapshot){
        if(State!=EditTransactionState.Idle){
            throw new InvalidOperationException("編集トランザクションは既に進行中です。");
        }
        this.StartedSnapshot=Snapshot??throw new ArgumentNullException(nameof(Snapshot));
        this.Targets=[];
        this.Previews=[];
        this.PendingSnapshot=null;
        State=EditTransactionState.Dragging;
    }

    internal IReadOnlyList<SceneTransformPreview>PreviewTranslation(Vector3 WorldDelta){
        Matrix4x4 Delta=Matrix4x4.CreateTranslation(WorldDelta);
        return PreviewWorldDelta(Delta);
    }

    internal IReadOnlyList<SceneTransformPreview>PreviewRotation(Vector3 Pivot,Quaternion Rotation){
        Matrix4x4 Delta=Matrix4x4.CreateTranslation(-Pivot)
            *Matrix4x4.CreateFromQuaternion(SceneTransformMath.NormalizeRotation(Rotation))
            *Matrix4x4.CreateTranslation(Pivot);
        return PreviewWorldDelta(Delta);
    }

    internal IReadOnlyList<SceneTransformPreview>PreviewScale(
        Vector3 Pivot,Quaternion BasisRotation,Vector3 Factors){
        Matrix4x4 Basis=Matrix4x4.CreateFromQuaternion(SceneTransformMath.NormalizeRotation(BasisRotation));
        Matrix4x4.Invert(Basis,out Matrix4x4 InverseBasis);
        Matrix4x4 Delta=Matrix4x4.CreateTranslation(-Pivot)
            *InverseBasis
            *Matrix4x4.CreateScale(Factors)
            *Basis
            *Matrix4x4.CreateTranslation(Pivot);
        return PreviewWorldDelta(Delta);
    }

    internal IReadOnlyList<SceneTransformPreview>PreviewSurfaceSnap(
        Vector3 Pivot,Vector3 TargetPosition,Quaternion PrimaryRotation,Vector3 SurfaceNormal){
        Quaternion Alignment=CreateFromToRotation(
            Vector3.Transform(Vector3.UnitY,SceneTransformMath.NormalizeRotation(PrimaryRotation)),SurfaceNormal);
        Matrix4x4 Delta=Matrix4x4.CreateTranslation(-Pivot)
            *Matrix4x4.CreateFromQuaternion(Alignment)
            *Matrix4x4.CreateTranslation(TargetPosition);
        return PreviewWorldDelta(Delta);
    }

    internal Matrix4x4 CurrentWorldDelta=>this.WorldDelta;

    private IReadOnlyList<SceneTransformPreview>PreviewWorldDelta(Matrix4x4 Delta){
        EnsureState(EditTransactionState.Dragging);
        this.WorldDelta=Delta;
        this.Previews=this.Targets.Select(Target=>
            new SceneTransformPreview(Target.NodeId,Target.NodePath,Target.OriginalWorld*Delta)).ToArray();
        return this.Previews;
    }

    internal void QueueSnapshot(SceneDocumentSnapshot Snapshot){
        if(State==EditTransactionState.Idle){
            throw new InvalidOperationException("保留できる編集トランザクションがありません。");
        }
        if(this.StartedSnapshot is not { } Started||!Started.Key.Equals(Snapshot.Key)){
            throw new ArgumentException("編集中Sceneと異なるsnapshotは保留できません。",nameof(Snapshot));
        }

        this.PendingSnapshot=Snapshot;
    }

    internal bool TryCreateCommit(string UndoDescription,out SceneEditCommitRequest?Request){
        return TryCreateCommit(UndoDescription,SceneTransformMemberMask.Position,out Request);
    }

    internal bool TryCreateCommit(
        string UndoDescription,SceneTransformMemberMask Members,out SceneEditCommitRequest?Request){
        return TryCreateCommitCore(
            UndoDescription,Members,Array.Empty<NodePath>(),out Request);
    }

    internal bool TryCreateDuplicateCommit(
        string UndoDescription,SceneTransformMemberMask Members,IReadOnlyList<NodePath>DuplicateResultPaths,
        out SceneEditCommitRequest?Request){
        if(DuplicateResultPaths.Count!=this.Targets.Length){
            Request=null;
            return false;
        }
        return TryCreateCommitCore(UndoDescription,Members,DuplicateResultPaths,out Request);
    }

    internal bool TryCreateDirectCommit(
        string UndoDescription,IReadOnlyList<SceneEditOperation>Operations,out SceneEditCommitRequest?Request){
        EnsureState(EditTransactionState.Dragging);
        if(this.StartedSnapshot is not { }Started||Operations.Count==0
            ||Operations.Any(Operation=>!Operation.SceneKey.Equals(Started.Key))){
            Request=null;
            return false;
        }

        State=EditTransactionState.Committing;
        Request=new SceneEditCommitRequest(
            Started.Key,Started.DocumentVersion,UndoDescription,Array.Empty<SceneTransformChange>(),
            SceneTransformMemberMask.None,DirectOperations:Operations);
        return true;
    }

    private bool TryCreateCommitCore(
        string UndoDescription,SceneTransformMemberMask Members,IReadOnlyList<NodePath>DuplicateResultPaths,
        out SceneEditCommitRequest?Request){
        EnsureState(EditTransactionState.Dragging);
        if(this.StartedSnapshot is not { } Started){
            Request=null;
            return false;
        }

        List<SceneTransformChange>Changes=[];
        for(var I=0;I<this.Targets.Length;I++){
            SceneTransformTarget Target=this.Targets[I];
            if(!SceneTransformMath.TryCalculateLocal(
                Target.OriginalWorld,Target.ParentWorld,out SceneTransformValue OriginalLocal)
                ||!SceneTransformMath.TryCalculateLocal(
                    this.Previews[I].World,Target.ParentWorld,out SceneTransformValue Local)){
                Request=null;
                return false;
            }
            Changes.Add(new SceneTransformChange(Target.NodeId,OriginalLocal,Local));
        }

        State=EditTransactionState.Committing;
        Request=new SceneEditCommitRequest(
            Started.Key,Started.DocumentVersion,UndoDescription,Changes,Members,DuplicateResultPaths);
        return true;
    }

    private static Quaternion CreateFromToRotation(Vector3 From,Vector3 To){
        if(From.LengthSquared()<=1e-10f||To.LengthSquared()<=1e-10f){ return Quaternion.Identity; }
        From=Vector3.Normalize(From);
        To=Vector3.Normalize(To);
        float Dot=Math.Max(-1f,Math.Min(1f,Vector3.Dot(From,To)));
        if(Dot>=1f-1e-6f){ return Quaternion.Identity; }
        if(Dot<=-1f+1e-6f){
            Vector3 Reference=Math.Abs(Vector3.Dot(From,Vector3.UnitX))<0.9f?Vector3.UnitX:Vector3.UnitZ;
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(From,Reference)),(float)Math.PI);
        }
        Vector3 Axis=Vector3.Normalize(Vector3.Cross(From,To));
        return Quaternion.CreateFromAxisAngle(Axis,(float)Math.Acos(Dot));
    }

    internal SceneEditCancellation Cancel(){
        if(State==EditTransactionState.Idle){
            return new SceneEditCancellation([],PendingSnapshot:null);
        }

        SceneTransformPreview[]Restored=this.Targets.Select(static Target=>
            new SceneTransformPreview(Target.NodeId,Target.NodePath,Target.OriginalWorld)).ToArray();
        SceneDocumentSnapshot?Pending=this.PendingSnapshot;
        Clear();
        return new SceneEditCancellation(Restored,Pending);
    }

    internal SceneDocumentSnapshot?Complete(){
        EnsureState(EditTransactionState.Committing);
        SceneDocumentSnapshot?Pending=this.PendingSnapshot;
        Clear();
        return Pending;
    }

    private void Clear(){
        this.StartedSnapshot=null;
        this.PendingSnapshot=null;
        this.Targets=[];
        this.Previews=[];
        this.WorldDelta=Matrix4x4.Identity;
        State=EditTransactionState.Idle;
    }

    private void EnsureState(EditTransactionState Expected){
        if(State!=Expected){
            throw new InvalidOperationException($"編集トランザクション状態が不正です。expected={Expected}, actual={State}");
        }
    }
}
