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
        var Document=CreateDocument();
        Assert.False(Document.IsDirty);
        Assert.False(Document.CanUndo);
        Assert.False(Document.CanRedo);
    }

    [Fact]
    public void TryApply_MakesTheDocumentDirtyAndUndoable(){
        var Document=CreateDocument();

        Assert.True(Document.TryApply([Move(Document,5f)],"Move"));

        Assert.True(Document.IsDirty);
        Assert.True(Document.CanUndo);
        Assert.False(Document.CanRedo);
        Assert.Equal(5f,ReadX(Document));
    }

    [Fact]
    public void TryUndo_MultipleSteps_UndoesEachOneAtATime(){
        var Document=CreateDocument();
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
        var Document=CreateDocument();
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
        var Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Document.TryApply([Move(Document,2f)],"Move2");
        Document.TryUndo();
        Assert.True(Document.CanRedo);

        Assert.True(Document.TryApply([Move(Document,9f)],"Move9"));

        Assert.False(Document.CanRedo);
        Assert.False(Document.TryRedo());
        Assert.Equal(9f,ReadX(Document));
    }

    /// <summary>
    /// M0051 Step 1-6: IsDirty は参照比較ではなくウォーターマークとの距離で決まる。
    /// 「ちょうど書き出し済みの位置に居るときはdirtyでない」というM0047の設計意図はそのまま。
    /// </summary>
    [Fact]
    public void IsDirty_BecomesFalseWhenUndoneBackToTheWatermark(){
        var Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Assert.True(Document.IsDirty);
        Document.MarkWritten();
        Assert.False(Document.IsDirty);

        Document.TryApply([Move(Document,2f)],"Move2");
        Assert.True(Document.IsDirty);

        Assert.True(Document.TryUndo()); // ちょうど書き出し済みの位置まで戻る
        Assert.False(Document.IsDirty);
    }

    /// <summary>
    /// 書き出し済みの位置より下へ潜ったらdirty。ただしこのときテキストも一緒に戻すので
    /// (SceneUndoAction.InMemoryWithText)、実運用ではウォーターマークも同時に下がり
    /// dirtyにはならない — RollbackWatermark を呼ばない場合の素の挙動を固定する。
    /// </summary>
    [Fact]
    public void IsDirty_StaysTrueWhenUndoingPastTheWatermarkWithoutRollingItBack(){
        var Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Document.TryApply([Move(Document,2f)],"Move2");
        Document.MarkWritten();

        Document.TryUndo();

        Assert.True(Document.IsDirty);
    }

    /// <summary>
    /// 実運用の形。テキストごと戻すときはウォーターマークも一緒に下げるので、
    /// テキストとDocumentは同じ位置に居続ける = dirtyでない。
    /// </summary>
    [Fact]
    public void RollbackWatermark_KeepsDocumentAndTextAtTheSamePosition(){
        var Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Document.TryApply([Move(Document,2f)],"Move2");
        Document.MarkWritten();
        Assert.Equal(2,Document.TextWatermark);

        Document.TryUndo();
        Document.RollbackWatermark();

        Assert.Equal(1,Document.TextWatermark);
        Assert.False(Document.IsDirty);
        Assert.Empty(Document.UnwrittenHistory);
    }

    /// <summary>Redoで進めた分もウォーターマークが追いつき、保存時点へ戻れば dirty が消える。</summary>
    [Fact]
    public void AdvanceWatermark_RestoresTheSavedPosition(){
        var Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Document.MarkWritten();
        Document.TryUndo();
        Document.RollbackWatermark();

        Assert.True(Document.TryRedo());
        Document.AdvanceWatermark();

        Assert.Equal(1,Document.TextWatermark);
        Assert.False(Document.IsDirty);
    }

    /// <summary>
    /// 保存が二重適用にならないための本命。書き出し済みの分は再生対象から外れる。
    /// これが崩れると、2回目の保存が1回目の編集をもう一度C#へ適用してしまう。
    /// </summary>
    [Fact]
    public void UnwrittenHistory_ExcludesEntriesAlreadyWrittenToText(){
        var Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Document.TryApply([Move(Document,2f)],"Move2");
        Document.MarkWritten();
        Document.TryApply([Move(Document,3f)],"Move3");

        Assert.Equal(3,Document.History.Count);
        Assert.Single(Document.UnwrittenHistory);
        Assert.Equal("Move3",Document.UnwrittenHistory[0].Label);
    }

    /// <summary>
    /// M0051 Step 1-6の中心。保存してもUndo履歴は残る — これが「保存後もCtrl+Zが即時」の土台。
    /// Step 1-5まではここで履歴が捨てられ、保存後のCtrl+Zがテキスト履歴への委譲(再解析待ち)に
    /// なっていた。
    /// </summary>
    [Fact]
    public void MarkWritten_KeepsTheUndoHistoryForTheWholeSession(){
        var Document=CreateDocument();
        Assert.True(Document.TryApply([Move(Document,2f)],"Move"));

        Document.MarkWritten();

        Assert.False(Document.IsDirty);
        Assert.True(Document.CanUndo);
        Assert.Single(Document.History);
    }

    [Fact]
    public void History_ReflectsOnlyEntriesReachableFromCurrent_NotUndoneOnes(){
        var Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Document.TryApply([Move(Document,2f)],"Move2");
        Document.TryUndo();

        Assert.Single(Document.History);
        Assert.Equal("Move1",Document.History[0].Label);
    }

    /// <summary>
    /// M0051: AddComponentOperationは対応済みになったが、同じ型を既に持つNodeへの追加は
    /// ドメイン上の理由で失敗する(SceneModelEditApplierTests参照)。TryApplyが失敗したときに
    /// Documentが一切変化しないことは、失敗理由が「型として未対応」でも「ドメイン上失敗」でも
    /// 変わらず保たれるべき不変条件。
    /// </summary>
    [Fact]
    public void TryApply_FailingOperation_LeavesDocumentUnchanged(){
        var Document=CreateDocument();
        var Cube=FindId(Document,"Cube");

        // Cubeは既にTransformを持つので、同じ型を追加しようとすると失敗する。
        var Applied=Document.TryApply(
                                      [new AddComponentOperation(
                                                                 Key,Cube,DocumentVersion:1,
                                                                 "global::CSharpGameEngine.Runtime.Windows.Transform",
                                                                 "new global::CSharpGameEngine.Runtime.Windows.Transform()")],
                                      "Bad");

        Assert.False(Applied);
        Assert.False(Document.IsDirty);
        Assert.False(Document.CanUndo);
    }

    /// <summary>
    /// ResetTo は「実C#へ完全に追随した」ことを表すので、Redo側もウォーターマークも残してはいけない
    /// (残すと、次の保存が古い部分列を再生する)。M0051 Step 1-6では、リネージが食い違ったときの
    /// 縮退経路としてだけ通る。
    /// </summary>
    [Fact]
    public void ResetTo_ClearsBothStacksAndTheWatermark(){
        var Document=CreateDocument();
        Assert.True(Document.TryApply([Move(Document,2f)],"Move"));
        Document.MarkWritten();
        Assert.True(Document.TryApply([Move(Document,3f)],"Move2"));
        Assert.True(Document.TryUndo());
        Assert.True(Document.CanRedo);

        Document.ResetTo(new SceneEditSnapshot(Document.Model,Document.Projection));

        Assert.False(Document.CanUndo);
        Assert.False(Document.CanRedo);
        Assert.Equal(0,Document.TextWatermark);
        Assert.False(Document.IsDirty);
        Assert.Empty(Document.UnwrittenHistory);
    }

    /// <summary>
    /// 潜ってから新しい編集をすると、Redoスタックは捨てられ、未書き出しが1件になる。
    /// ウォーターマークの不変条件(0 <= W <= Count)がここで崩れないことを固定する。
    /// </summary>
    [Fact]
    public void NewEditAfterSteppingBelowTheWatermark_KeepsTheInvariant(){
        var Document=CreateDocument();
        Document.TryApply([Move(Document,1f)],"Move1");
        Document.TryApply([Move(Document,2f)],"Move2");
        Document.MarkWritten();
        Document.TryUndo();
        Document.RollbackWatermark();

        Assert.True(Document.TryApply([Move(Document,5f)],"Move3"));

        Assert.Equal(2,Document.History.Count);
        Assert.Equal(1,Document.TextWatermark);
        Assert.False(Document.CanRedo);
        Assert.Single(Document.UnwrittenHistory);
        Assert.True(Document.IsDirty);
    }

    private static SceneEditDocument CreateDocument(){
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,Source]);
        SceneProjectionReconciler Reconciler=new();
        var Projection=Reconciler.Reconcile(Key,Model,Previous:null);
        return new SceneEditDocument(Key,new SceneEditSnapshot(Model,Projection),Reconciler);
    }

    private static SetTransformOperation Move(SceneEditDocument Document,float X)=>
        new(Key,FindId(Document,"Cube"),DocumentVersion:1,
            new SceneTransformValue(new Vector3(X,0f,0f),Quaternion.Identity,Vector3.One));

    private static EditorNodeId FindId(SceneEditDocument Document,string NodeName)=>
        Assert.Single(Document.Projection.Nodes,Node=>Node.Node.Name==NodeName).Id;

    private static float ReadX(SceneEditDocument Document){
        var Cube=Assert.Single(Document.Model.EnumerateNodes(),static Node=>Node.Name=="Cube");
        Assert.True(SceneTransformBinding.TryRead(Cube,out var Value));
        return Value.Position.X;
    }
}
