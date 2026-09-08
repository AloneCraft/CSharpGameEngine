using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class EditTransactionControllerTests {
    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");

    [Fact]
    public void PreviewTranslation_ChangesPreviewButKeepsOriginalForCancel(){
        var Snapshot=CreateSnapshot(DocumentVersion:1);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(1f,2f,3f),Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);

        var Preview=Controller.PreviewTranslation(new Vector3(4f,0f,0f));
        var Restored=Controller.Cancel().RestoredTransforms;

        Assert.Equal(5f,Assert.Single(Preview).World.Translation.X);
        Assert.Equal(1f,Assert.Single(Restored).World.Translation.X);
        Assert.Equal(EditTransactionState.Idle,Controller.State);
    }

    /// <summary>
    /// コミット直後にギズモが一瞬元の位置へ戻る不具合の対策(SceneHwndHost.CompleteEditCommitが
    /// CurrentRenderObjectsをこの値でパッチしてからUpdateMoveGizmoを呼ぶ)。TryCreateCommitで
    /// Committing状態に入った後もCompleteが呼ばれるまでは最終ドラッグ値を読み続けられることを
    /// 固定する — Completeを呼ぶ前にこの値を退避する設計が成立する前提。
    /// </summary>
    [Fact]
    public void CurrentPreviews_AfterTryCreateCommit_StillReflectsTheFinalDraggedValueUntilComplete(){
        var Snapshot=CreateSnapshot(DocumentVersion:1);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(1f,0f,0f),Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);
        Controller.PreviewTranslation(new Vector3(9f,0f,0f));

        Assert.True(Controller.TryCreateCommit("Move Cube",out _));
        var PreviewBeforeComplete=Assert.Single(Controller.CurrentPreviews);
        Controller.Complete();

        Assert.Equal(10f,PreviewBeforeComplete.World.Translation.X,3);
        Assert.Empty(Controller.CurrentPreviews);
    }

    [Fact]
    public void RepeatedPreviewUpdates_CreateOneCommitContainingOnlyTheFinalValue(){
        var Snapshot=CreateSnapshot(DocumentVersion:4);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.Identity,Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);

        Controller.PreviewTranslation(new Vector3(1f,0f,0f));
        Controller.PreviewTranslation(new Vector3(2f,0f,0f));
        Controller.PreviewTranslation(new Vector3(3f,0f,0f));

        Assert.True(Controller.TryCreateCommit("Move Cube",out var Request));
        SceneTransformChange Change=Assert.Single(Request!.Changes);
        Assert.Equal(3f,Change.Local.Position.X,4);
        Assert.Throws<InvalidOperationException>(()=>
            Controller.TryCreateCommit("Move Cube again",out _));
    }

    [Fact]
    public void QueueSnapshot_DuringDrag_KeepsOnlyTheLatestSnapshot(){
        var First=CreateSnapshot(DocumentVersion:1);
        var Second=CreateSnapshot(DocumentVersion:2);
        var Third=CreateSnapshot(DocumentVersion:3);
        EditTransactionController Controller=new();
        Controller.Begin(First,[new SceneTransformTarget(new EditorNodeId(2),NodePath.Root.Append(0),Matrix4x4.Identity,Matrix4x4.Identity)]);

        Controller.QueueSnapshot(Second);
        Controller.QueueSnapshot(Third);
        var Cancellation=Controller.Cancel();

        Assert.Same(Third,Cancellation.PendingSnapshot);
    }

    [Fact]
    public void CreateCommit_ConvertsPreviewWorldBackToParentLocalTransform(){
        var Snapshot=CreateSnapshot(DocumentVersion:7);
        var Parent=Matrix4x4.CreateTranslation(10f,0f,0f);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(11f,0f,0f),Parent);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);
        Controller.PreviewTranslation(new Vector3(2f,0f,0f));

        Assert.True(Controller.TryCreateCommit("Move Cube",out var Request));
        var Change=Assert.Single(Request!.Changes);

        Assert.Equal(3f,Change.Local.Position.X,4);
        Assert.Equal(Key,Request.SceneKey);
        Assert.Equal(7,Request.DocumentVersion);
        Assert.Equal(EditTransactionState.Committing,Controller.State);
    }

    [Fact]
    public void PreviewRotation_RotatesWorldAroundSharedPivotAndCarriesCommitMask(){
        var Snapshot=CreateSnapshot(DocumentVersion:8);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(2f,0f,0f),Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);

        var Preview=Controller.PreviewRotation(
                                              Vector3.Zero,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,(float)(Math.PI/2d)));
        Assert.True(Controller.TryCreateCommit(
            "Rotate Cube",SceneTransformMemberMask.Position|SceneTransformMemberMask.Rotation,
            out var Request));

        Assert.Equal(0f,Assert.Single(Preview).World.Translation.X,4);
        Assert.Equal(2f,Assert.Single(Preview).World.Translation.Y,4);
        Assert.Equal(SceneTransformMemberMask.Position|SceneTransformMemberMask.Rotation,Request!.Members);
    }

    [Fact]
    public void PreviewScale_ScalesWorldAroundSharedPivot(){
        var Snapshot=CreateSnapshot(DocumentVersion:9);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(2f,0f,0f),Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);

        var Preview=Controller.PreviewScale(
                                           Vector3.Zero,Quaternion.Identity,new Vector3(2f,1f,1f));

        Assert.Equal(4f,Assert.Single(Preview).World.Translation.X,4);
        Assert.Equal(2f,Assert.Single(Preview).World.M11,4);
    }

    [Fact]
    public void PreviewScale_LocalBasisScalesAlongTheRotatedPrimaryAxis(){
        var Snapshot=CreateSnapshot(DocumentVersion:10);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(0f,1f,0f),Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);

        var Preview=Controller.PreviewScale(
                                           Vector3.Zero,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,(float)(Math.PI/2d)),
                                           new Vector3(2f,1f,1f));

        Assert.Equal(0f,Assert.Single(Preview).World.Translation.X,4);
        Assert.Equal(2f,Assert.Single(Preview).World.Translation.Y,4);
    }

    [Fact]
    public void PreviewSurfaceSnap_MovesPivotAndAlignsPrimaryUpToSurfaceNormal(){
        var Snapshot=CreateSnapshot(DocumentVersion:11);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.Identity,Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);

        var Preview=Controller.PreviewSurfaceSnap(
                                                 Pivot:Vector3.Zero,TargetPosition:new Vector3(3f,4f,5f),
                                                 PrimaryRotation:Quaternion.Identity,SurfaceNormal:Vector3.UnitZ);

        var Value=Assert.Single(Preview);
        Assert.Equal(new Vector3(3f,4f,5f),Value.World.Translation);
        var AlignedUp=Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY,Value.World));
        Assert.Equal(0f,AlignedUp.X,5);
        Assert.Equal(0f,AlignedUp.Y,5);
        Assert.Equal(1f,AlignedUp.Z,5);
    }

    [Fact]
    public void CreateDuplicateCommit_CarriesPredictedResultPathsInTargetOrder(){
        var Snapshot=CreateSnapshot(DocumentVersion:12);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[new SceneTransformTarget(
            new EditorNodeId(2),NodePath.Root.Append(0),Matrix4x4.Identity,Matrix4x4.Identity)]);
        Controller.PreviewTranslation(Vector3.UnitX);
        var ResultPath=NodePath.Root.Append(1);

        Assert.True(Controller.TryCreateDuplicateCommit(
            "Duplicate Cube",SceneTransformMemberMask.Position,[ResultPath],out var Request));

        Assert.True(Request!.IsDuplicate);
        Assert.Equal([ResultPath],Request.DuplicateResultPaths);
        var Operation=Assert.IsType<DuplicateNodeOperation>(
                                                            Assert.Single(SceneEditorSessionService.CreateOperations(Request)));
        Assert.Equal(new EditorNodeId(2),Operation.NodeId);
        Assert.Equal(SceneTransformMemberMask.Position,Operation.Members);
    }

    [Fact]
    public void DirectPropertyCommit_CarriesSourceOperationsThroughTheSameTransaction(){
        var Snapshot=CreateSnapshot(DocumentVersion:13);
        var Cube=Assert.Single(Snapshot.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SetComponentMemberOperation Operation=new(
            Key,Cube,13,"global::CSharpGameEngine.Runtime.Windows.Camera","NearZ","0.5f");
        EditTransactionController Controller=new();
        Controller.BeginDirect(Snapshot);

        Assert.True(Controller.TryCreateDirectCommit(
            "Edit Camera Near",[Operation],out var Request));

        Assert.Same(Operation,Assert.Single(Request!.DirectOperations));
        Assert.Same(Operation,Assert.Single(SceneEditorSessionService.CreateOperations(Request)));
        Assert.Equal(EditTransactionState.Committing,Controller.State);
    }

    /// <summary>
    /// M0030 監査 §8「解析失敗時は変更を確定しない」の純粋層。親のworld行列が退化していると
    /// local へ戻せないので、commit要求を作らずDragging のまま留まり、呼び出し元(<c>SceneHwndHost</c>
    /// の<c>CommitEditTransaction</c>)が Cancel へ倒せる状態を保つ。
    /// </summary>
    [Fact]
    public void TryCreateCommit_WhenParentWorldIsNotInvertible_ReturnsFalseAndStaysDragging(){
        var Snapshot=CreateSnapshot(DocumentVersion:3);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(1f,0f,0f),ParentWorld:default);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);
        Controller.PreviewTranslation(new Vector3(4f,0f,0f));

        Assert.False(Controller.TryCreateCommit("Move Cube",out var Request));

        Assert.Null(Request);
        Assert.Equal(EditTransactionState.Dragging,Controller.State);
        Assert.Equal(1f,Assert.Single(Controller.Cancel().RestoredTransforms).World.Translation.X);
    }

    /// <summary>
    /// M0030 監査 §4-6 / §8 ロールバック。source 反映が失敗すると<c>CompleteEditCommit(false,…)</c>が
    /// Committing 状態のまま Cancel する。そこで開始時のworldが返らないと、プレビューだけ動いた
    /// ビューポートが確定値と食い違ったまま残る。
    /// </summary>
    [Fact]
    public void Cancel_DuringCommitting_RestoresOriginalWorldsAndReturnsPendingSnapshot(){
        var Started=CreateSnapshot(DocumentVersion:1);
        var Reparsed=CreateSnapshot(DocumentVersion:2);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(1f,2f,3f),Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Started,[Target]);
        Controller.PreviewTranslation(new Vector3(40f,0f,0f));
        Controller.QueueSnapshot(Reparsed);
        Assert.True(Controller.TryCreateCommit("Move Cube",out _));
        Assert.Equal(EditTransactionState.Committing,Controller.State);

        var Cancellation=Controller.Cancel();

        var Restored=Assert.Single(Cancellation.RestoredTransforms);
        Assert.Equal(1f,Restored.World.Translation.X);
        Assert.Equal(Target.NodePath,Restored.NodePath);
        Assert.Same(Reparsed,Cancellation.PendingSnapshot);
        Assert.Equal(EditTransactionState.Idle,Controller.State);
    }

    private static SceneDocumentSnapshot CreateSnapshot(long DocumentVersion){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public Node Root=>new() { Children=[new Node { Name="Cube" }] };
                                                  }
                                                  """);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion,DocumentVersion,Model,HasBlockingErrors:false,
            Previous:null,new SceneProjectionReconciler());
    }
}
