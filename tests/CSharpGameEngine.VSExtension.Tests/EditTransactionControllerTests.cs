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
