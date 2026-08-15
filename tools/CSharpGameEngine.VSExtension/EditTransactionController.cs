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
    internal SceneTransformChange(EditorNodeId NodeId,SceneTransformValue Local){
        this.NodeId=NodeId;
        this.Local=Local;
    }

    internal EditorNodeId NodeId { get; }

    internal SceneTransformValue Local { get; }
}

internal sealed class SceneEditCommitRequest {
    internal SceneEditCommitRequest(
        SceneKey SceneKey,long DocumentVersion,string UndoDescription,IReadOnlyList<SceneTransformChange>Changes,
        SceneTransformMemberMask Members){
        this.SceneKey=SceneKey;
        this.DocumentVersion=DocumentVersion;
        this.UndoDescription=UndoDescription;
        this.Changes=Changes;
        this.Members=Members;
    }

    internal SceneKey SceneKey { get; }

    internal long DocumentVersion { get; }

    internal string UndoDescription { get; }

    internal IReadOnlyList<SceneTransformChange>Changes { get; }

    internal SceneTransformMemberMask Members { get; }
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
        EnsureState(EditTransactionState.Dragging);
        if(this.StartedSnapshot is not { } Started){
            Request=null;
            return false;
        }

        List<SceneTransformChange>Changes=[];
        for(var I=0;I<this.Targets.Length;I++){
            SceneTransformTarget Target=this.Targets[I];
            if(!SceneTransformMath.TryCalculateLocal(this.Previews[I].World,Target.ParentWorld,out SceneTransformValue Local)){
                Request=null;
                return false;
            }
            Changes.Add(new SceneTransformChange(Target.NodeId,Local));
        }

        State=EditTransactionState.Committing;
        Request=new SceneEditCommitRequest(Started.Key,Started.DocumentVersion,UndoDescription,Changes,Members);
        return true;
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
