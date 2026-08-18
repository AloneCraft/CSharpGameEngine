using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0047: セッション中の正本(<see cref="SceneEditDocument"/>)。差分やインバース操作を持たず
/// スナップショット列だけでUndo/Redoが成立すること、<see cref="SceneEditDocument.IsDirty"/>が
/// 参照比較で保存点を正しく判定することを固定する。
/// </summary>
public sealed class SceneEditDocumentTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }
        """;

    private const string Source="""
        using CSharpGameEngine.Entities;
        using CSharpGameEngine.Runtime.Windows;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() { Children=[
                new Node { Name="Cube", Components=[new Transform { PositionX=1f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }] },
            ] };
        }
        """;

    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");

    [Fact]
    public void TryApply_PublishesImmediatelyAndIsNotDirtyBeforeAnyEdit(){
        SceneEditDocument Document=CreateDocument();
        Assert.False(Document.IsDirty);
        Assert.False(Document.CanUndo);
        Assert.False(Document.CanRedo);
    }

    [Fact]
    public void TryApply_MakesTheDocumentDirtyAndUndoable(){
        SceneEditDocument Document=CreateDocument();

        Assert.True(Document.TryApply([Move(Document,5f)],"Move"));

        Assert.True(Document.IsDirty);
        Assert.True(Document.CanUndo);
        Assert.False(Document.CanRedo);
        Assert.Equal(5f,ReadX(Document));
    }

    [Fact]
    public void TryUndo_MultipleSteps_UndoesEachOneAtATime(){
        SceneEditDocument Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Document.TryApply([Move(Document,2f)],"Move2");
        Document.TryApply([Move(Document,3f)],"Move3");
        Assert.Equal(3f,ReadX(Document));

        Assert.True(Document.TryUndo());
        Assert.Equal(2f,ReadX(Document));
        Assert.True(Document.TryUndo());
        Assert.Equal(1f,ReadX(Document));
        Assert.True(Document.TryUndo());
        Assert.Equal(1f,ReadX(Document)); // 初期値(Sourceの1f)まで戻る
        Assert.False(Document.CanUndo);
        Assert.False(Document.TryUndo());
    }

    [Fact]
    public void TryRedo_ReappliesUndoneSteps(){
        SceneEditDocument Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Document.TryApply([Move(Document,2f)],"Move2");
        Document.TryUndo();
        Document.TryUndo();

        Assert.True(Document.TryRedo());
        Assert.Equal(1f,ReadX(Document));
        Assert.True(Document.TryRedo());
        Assert.Equal(2f,ReadX(Document));
        Assert.False(Document.CanRedo);
    }

    [Fact]
    public void TryApply_AfterUndo_DiscardsTheRedoBranch(){
        SceneEditDocument Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Document.TryApply([Move(Document,2f)],"Move2");
        Document.TryUndo();
        Assert.True(Document.CanRedo);

        Assert.True(Document.TryApply([Move(Document,9f)],"Move9"));

        Assert.False(Document.CanRedo);
        Assert.False(Document.TryRedo());
        Assert.Equal(9f,ReadX(Document));
    }

    [Fact]
    public void IsDirty_ByReferenceComparison_BecomesFalseWhenUndoneBackToTheSavedModel(){
        SceneEditDocument Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Assert.True(Document.IsDirty);
        Document.MarkSaved();
        Assert.False(Document.IsDirty);

        Document.TryApply([Move(Document,2f)],"Move2");
        Assert.True(Document.IsDirty);

        Assert.True(Document.TryUndo()); // ちょうど保存時点まで戻る
        Assert.False(Document.IsDirty);
    }

    [Fact]
    public void IsDirty_StaysTrueWhenUndoingPastTheSavedPoint(){
        SceneEditDocument Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Document.TryApply([Move(Document,2f)],"Move2");
        Document.MarkSaved(); // 2f時点で保存

        Document.TryUndo(); // 1fまで戻る = 保存時点より前 = ディスク(2f)と食い違う
        Assert.True(Document.IsDirty);
    }

    [Fact]
    public void History_ReflectsOnlyEntriesReachableFromCurrent_NotUndoneOnes(){
        SceneEditDocument Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Document.TryApply([Move(Document,2f)],"Move2");
        Document.TryUndo();

        Assert.Single(Document.History);
        Assert.Equal("Move1",Document.History[0].Label);
    }

    [Fact]
    public void TryApply_UnsupportedOperation_LeavesDocumentUnchanged(){
        SceneEditDocument Document=CreateDocument();
        EditorNodeId Cube=FindId(Document,"Cube");

        bool Applied=Document.TryApply(
            [new AddComponentOperation(Key,Cube,DocumentVersion:1,"global::Game.Foo","new Foo()")],"Bad");

        Assert.False(Applied);
        Assert.False(Document.IsDirty);
        Assert.False(Document.CanUndo);
    }

    private static SceneEditDocument CreateDocument(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze([TransformTypes,Source]);
        SceneProjectionReconciler Reconciler=new();
        SceneProjection Projection=Reconciler.Reconcile(Key,Model,Previous:null);
        return new SceneEditDocument(Key,new SceneEditSnapshot(Model,Projection),Reconciler);
    }

    private static SetTransformOperation Move(SceneEditDocument Document,float X)=>
        new(Key,FindId(Document,"Cube"),DocumentVersion:1,
            new SceneTransformValue(new Vector3(X,0f,0f),Quaternion.Identity,Vector3.One));

    private static EditorNodeId FindId(SceneEditDocument Document,string NodeName)=>
        Assert.Single(Document.Projection.Nodes,Node=>Node.Node.Name==NodeName).Id;

    private static float ReadX(SceneEditDocument Document){
        SceneNode Cube=Assert.Single(Document.Model.EnumerateNodes(),static Node=>Node.Name=="Cube");
        Assert.True(SceneTransformBinding.TryRead(Cube,out SceneTransformValue Value));
        return Value.Position.X;
    }
}
