using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class EditTransactionControllerTests {
    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");

    [Fact]
    public void PreviewTranslation_ChangesPreviewButKeepsOriginalForCancel(){
        SceneDocumentSnapshot Snapshot=CreateSnapshot(DocumentVersion:1);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(1f,2f,3f),Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);

        IReadOnlyList<SceneTransformPreview>Preview=Controller.PreviewTranslation(new Vector3(4f,0f,0f));
        IReadOnlyList<SceneTransformPreview>Restored=Controller.Cancel().RestoredTransforms;

        Assert.Equal(5f,Assert.Single(Preview).World.Translation.X);
        Assert.Equal(1f,Assert.Single(Restored).World.Translation.X);
        Assert.Equal(EditTransactionState.Idle,Controller.State);
    }

    [Fact]
    public void QueueSnapshot_DuringDrag_KeepsOnlyTheLatestSnapshot(){
        SceneDocumentSnapshot First=CreateSnapshot(DocumentVersion:1);
        SceneDocumentSnapshot Second=CreateSnapshot(DocumentVersion:2);
        SceneDocumentSnapshot Third=CreateSnapshot(DocumentVersion:3);
        EditTransactionController Controller=new();
        Controller.Begin(First,[new SceneTransformTarget(new EditorNodeId(2),NodePath.Root.Append(0),Matrix4x4.Identity,Matrix4x4.Identity)]);

        Controller.QueueSnapshot(Second);
        Controller.QueueSnapshot(Third);
        SceneEditCancellation Cancellation=Controller.Cancel();

        Assert.Same(Third,Cancellation.PendingSnapshot);
    }

    [Fact]
    public void CreateCommit_ConvertsPreviewWorldBackToParentLocalTransform(){
        SceneDocumentSnapshot Snapshot=CreateSnapshot(DocumentVersion:7);
        Matrix4x4 Parent=Matrix4x4.CreateTranslation(10f,0f,0f);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(11f,0f,0f),Parent);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);
        Controller.PreviewTranslation(new Vector3(2f,0f,0f));

        Assert.True(Controller.TryCreateCommit("Move Cube",out SceneEditCommitRequest?Request));
        SceneTransformChange Change=Assert.Single(Request!.Changes);

        Assert.Equal(3f,Change.Local.Position.X,4);
        Assert.Equal(Key,Request.SceneKey);
        Assert.Equal(7,Request.DocumentVersion);
        Assert.Equal(EditTransactionState.Committing,Controller.State);
    }

    [Fact]
    public void PreviewRotation_RotatesWorldAroundSharedPivotAndCarriesCommitMask(){
        SceneDocumentSnapshot Snapshot=CreateSnapshot(DocumentVersion:8);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(2f,0f,0f),Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);

        IReadOnlyList<SceneTransformPreview>Preview=Controller.PreviewRotation(
            Vector3.Zero,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,(float)(Math.PI/2d)));
        Assert.True(Controller.TryCreateCommit(
            "Rotate Cube",SceneTransformMemberMask.Position|SceneTransformMemberMask.Rotation,
            out SceneEditCommitRequest?Request));

        Assert.Equal(0f,Assert.Single(Preview).World.Translation.X,4);
        Assert.Equal(2f,Assert.Single(Preview).World.Translation.Y,4);
        Assert.Equal(SceneTransformMemberMask.Position|SceneTransformMemberMask.Rotation,Request!.Members);
    }

    [Fact]
    public void PreviewScale_ScalesWorldAroundSharedPivot(){
        SceneDocumentSnapshot Snapshot=CreateSnapshot(DocumentVersion:9);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(2f,0f,0f),Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);

        IReadOnlyList<SceneTransformPreview>Preview=Controller.PreviewScale(
            Vector3.Zero,Quaternion.Identity,new Vector3(2f,1f,1f));

        Assert.Equal(4f,Assert.Single(Preview).World.Translation.X,4);
        Assert.Equal(2f,Assert.Single(Preview).World.M11,4);
    }

    [Fact]
    public void PreviewScale_LocalBasisScalesAlongTheRotatedPrimaryAxis(){
        SceneDocumentSnapshot Snapshot=CreateSnapshot(DocumentVersion:10);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.CreateTranslation(0f,1f,0f),Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);

        IReadOnlyList<SceneTransformPreview>Preview=Controller.PreviewScale(
            Vector3.Zero,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,(float)(Math.PI/2d)),
            new Vector3(2f,1f,1f));

        Assert.Equal(0f,Assert.Single(Preview).World.Translation.X,4);
        Assert.Equal(2f,Assert.Single(Preview).World.Translation.Y,4);
    }

    [Fact]
    public void PreviewSurfaceSnap_MovesPivotAndAlignsPrimaryUpToSurfaceNormal(){
        SceneDocumentSnapshot Snapshot=CreateSnapshot(DocumentVersion:11);
        SceneTransformTarget Target=new(new EditorNodeId(2),NodePath.Root.Append(0),
            Matrix4x4.Identity,Matrix4x4.Identity);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[Target]);

        IReadOnlyList<SceneTransformPreview>Preview=Controller.PreviewSurfaceSnap(
            Pivot:Vector3.Zero,TargetPosition:new Vector3(3f,4f,5f),
            PrimaryRotation:Quaternion.Identity,SurfaceNormal:Vector3.UnitZ);

        SceneTransformPreview Value=Assert.Single(Preview);
        Assert.Equal(new Vector3(3f,4f,5f),Value.World.Translation);
        Vector3 AlignedUp=Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY,Value.World));
        Assert.Equal(0f,AlignedUp.X,5);
        Assert.Equal(0f,AlignedUp.Y,5);
        Assert.Equal(1f,AlignedUp.Z,5);
    }

    [Fact]
    public void CreateDuplicateCommit_CarriesPredictedResultPathsInTargetOrder(){
        SceneDocumentSnapshot Snapshot=CreateSnapshot(DocumentVersion:12);
        EditTransactionController Controller=new();
        Controller.Begin(Snapshot,[new SceneTransformTarget(
            new EditorNodeId(2),NodePath.Root.Append(0),Matrix4x4.Identity,Matrix4x4.Identity)]);
        Controller.PreviewTranslation(Vector3.UnitX);
        NodePath ResultPath=NodePath.Root.Append(1);

        Assert.True(Controller.TryCreateDuplicateCommit(
            "Duplicate Cube",SceneTransformMemberMask.Position,[ResultPath],out SceneEditCommitRequest?Request));

        Assert.True(Request!.IsDuplicate);
        Assert.Equal([ResultPath],Request.DuplicateResultPaths);
        DuplicateNodeOperation Operation=Assert.IsType<DuplicateNodeOperation>(
            Assert.Single(SceneEditorSessionService.CreateOperations(Request)));
        Assert.Equal(new EditorNodeId(2),Operation.NodeId);
        Assert.Equal(SceneTransformMemberMask.Position,Operation.Members);
    }

    [Fact]
    public void DirectPropertyCommit_CarriesSourceOperationsThroughTheSameTransaction(){
        SceneDocumentSnapshot Snapshot=CreateSnapshot(DocumentVersion:13);
        EditorNodeId Cube=Assert.Single(Snapshot.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SetComponentMemberOperation Operation=new(
            Key,Cube,13,"global::CSharpGameEngine.Runtime.Windows.Camera","NearZ","0.5f");
        EditTransactionController Controller=new();
        Controller.BeginDirect(Snapshot);

        Assert.True(Controller.TryCreateDirectCommit(
            "Edit Camera Near",[Operation],out SceneEditCommitRequest?Request));

        Assert.Same(Operation,Assert.Single(Request!.DirectOperations));
        Assert.Same(Operation,Assert.Single(SceneEditorSessionService.CreateOperations(Request)));
        Assert.Equal(EditTransactionState.Committing,Controller.State);
    }

    private static SceneDocumentSnapshot CreateSnapshot(long DocumentVersion){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
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
