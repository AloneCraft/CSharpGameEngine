using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0051 Step 1-4: <b>構造編集(Remove/Reparent/Reorder/AddNode)を履歴再生経路
/// (<see cref="SceneSourceEditor.ApplyForSave"/>)で通す</b>テスト。
///
/// <para>
/// 実機で「ヒエラルキーで編集してもCtrl+Sで取り消される」と報告された際の調査で、
/// <b>既存テストは構造編集を履歴再生経路で1本もカバーしていなかった</b>ことが判明した
/// (`SceneSourceEditorApplyForSaveTests`はSetTransform/Rename/AddNodeのみ、
/// `SceneSourceEditorTests`の構造編集は単発`Apply`/`ApplyBatch`であって履歴再生ではない)。
/// このクラスがその穴を埋める。
/// </para>
///
/// <para>
/// 特に重要なのは<b>履歴2件以上</b>のケース。1件目の編集でテキスト長が変わると、Spanベースで
/// 構文を探している箇所が2件目以降で必ず壊れる。実機報告はまさにこの形だった。
/// </para>
/// </summary>
public sealed class SceneSourceEditorStructuralSaveTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Vector3 { public float R; public float U; public float F; public Vector3(float R,float U,float F){ this.R=R;this.U=U;this.F=F; } }
            public struct Transform {
                public Vector3 Position; public Vector3 Rotation; public Vector3 Scale;
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }
        """;

    /// <summary>Root直下に3つの兄弟、うち1つは子を1つ持つ。構造編集の対象として十分な形。</summary>
    private const string Source="""
        using CSharpGameEngine.Entities;
        using CSharpGameEngine.Runtime.Windows;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() { Children=[
                new Node {
                    Name="Alpha",
                    Components=[new Transform { PositionX=1f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }],
                },
                new Node { Name="Beta" },
                new Node {
                    Name="Gamma",
                    Children=[new Node { Name="GammaChild" }],
                },
            ] };
        }
        """;

    private static readonly SceneKey Key=new("project-a","Game.MainScene");

    // ---- 単発の構造編集(履歴1件) ----

    [Fact]
    public void Remove_SingleEntry_Succeeds(){
        Fixture F=Fixture.Create();
        F.Apply([new RemoveNodeOperation(Key,F.Id("Beta"),1)],"削除");

        string Text=F.SaveAndVerify();

        Assert.DoesNotContain("\"Beta\"",Text);
        Assert.Contains("\"Alpha\"",Text);
        Assert.Contains("\"Gamma\"",Text);
    }

    [Fact]
    public void Reparent_SingleEntry_Succeeds(){
        Fixture F=Fixture.Create();
        F.Apply([new ReparentNodeOperation(Key,F.Id("Beta"),1,F.Id("Gamma"),0)],"親変更");

        string Text=F.SaveAndVerify();

        Assert.Contains("\"Beta\"",Text);
    }

    [Fact]
    public void Reorder_SingleEntry_Succeeds(){
        Fixture F=Fixture.Create();
        F.Apply([new ReorderNodeOperation(Key,F.Id("Alpha"),1,2)],"並べ替え");

        F.SaveAndVerify();
    }

    // ---- 履歴2件以上(実機報告の本命) ----

    /// <summary>
    /// 実機報告の再現ケース。兄弟をRenameしてから別の兄弟をDeleteすると、Renameでテキスト長が
    /// 変わって<b>親</b>のSpanがずれ、削除対象の親を特定できずに保存が中止されていた
    /// (`ApplyForSaveSteps`の事前Annotateが操作対象の親を注釈していなかったため)。
    /// </summary>
    [Fact]
    public void RenameThenRemoveSibling_Succeeds(){
        Fixture F=Fixture.Create();
        F.Apply([new RenameNodeOperation(Key,F.Id("Alpha"),1,"AlphaRenamedMuchLonger")],"名前の変更");
        F.Apply([new RemoveNodeOperation(Key,F.Id("Beta"),2)],"削除");

        string Text=F.SaveAndVerify();

        Assert.Contains("\"AlphaRenamedMuchLonger\"",Text);
        Assert.DoesNotContain("\"Beta\"",Text);
    }

    [Fact]
    public void RenameThenReparentSibling_Succeeds(){
        Fixture F=Fixture.Create();
        F.Apply([new RenameNodeOperation(Key,F.Id("Alpha"),1,"AlphaRenamedMuchLonger")],"名前の変更");
        F.Apply([new ReparentNodeOperation(Key,F.Id("Beta"),2,F.Id("Gamma"),0)],"親変更");

        string Text=F.SaveAndVerify();

        Assert.Contains("\"AlphaRenamedMuchLonger\"",Text);
    }

    [Fact]
    public void RemoveTwoSiblings_Succeeds(){
        Fixture F=Fixture.Create();
        F.Apply([new RemoveNodeOperation(Key,F.Id("Alpha"),1)],"削除1");
        F.Apply([new RemoveNodeOperation(Key,F.Id("Beta"),2)],"削除2");

        string Text=F.SaveAndVerify();

        Assert.DoesNotContain("\"Alpha\"",Text);
        Assert.DoesNotContain("\"Beta\"",Text);
        Assert.Contains("\"Gamma\"",Text);
    }

    [Fact]
    public void MoveThenRemoveSibling_Succeeds(){
        Fixture F=Fixture.Create();
        F.Apply(
            [new SetTransformOperation(Key,F.Id("Alpha"),1,
                new SceneTransformValue(new Vector3(9f,0f,0f),Quaternion.Identity,Vector3.One))],
            "移動");
        F.Apply([new RemoveNodeOperation(Key,F.Id("Beta"),2)],"削除");

        string Text=F.SaveAndVerify();

        Assert.Contains("Position=new global::CSharpGameEngine.Runtime.Windows.Vector3(9f,0f,0f)",Text);
        Assert.DoesNotContain("\"Beta\"",Text);
    }

    /// <summary>子を持つノードを削除しても、その子ごと消える(親の特定が2段で必要なケース)。</summary>
    [Fact]
    public void RenameThenRemoveParentWithChild_Succeeds(){
        Fixture F=Fixture.Create();
        F.Apply([new RenameNodeOperation(Key,F.Id("Alpha"),1,"AlphaRenamedMuchLonger")],"名前の変更");
        F.Apply([new RemoveNodeOperation(Key,F.Id("Gamma"),2)],"削除");

        string Text=F.SaveAndVerify();

        Assert.DoesNotContain("\"Gamma\"",Text);
        Assert.DoesNotContain("\"GammaChild\"",Text);
    }

    // ---- AddNode(原因E/F) ----

    /// <summary>
    /// 1セッションで同じ親へ2回AddChildすると、ソース側の一意名採番が古い投影を見ていたため
    /// Documentは`Node`/`Node (1)`、ソースは`Node`/`Node`となり往復検証が必ず不一致になっていた。
    /// </summary>
    [Fact]
    public void AddChildTwiceToSameParent_Succeeds(){
        Fixture F=Fixture.Create();
        F.Apply([new AddNodeOperation(Key,F.Id("Gamma"),1,"Node")],"追加1");
        F.Apply([new AddNodeOperation(Key,F.Id("Gamma"),2,"Node")],"追加2");

        F.SaveAndVerify();
    }

    /// <summary>削除してから追加すると、採番が削除済み兄弟を数えてPath重複を起こしうる。</summary>
    [Fact]
    public void RemoveThenAddChild_Succeeds(){
        Fixture F=Fixture.Create();
        F.Apply([new RemoveNodeOperation(Key,F.Id("Beta"),1)],"削除");
        F.Apply([new AddNodeOperation(Key,F.Id("Gamma"),2,"Node")],"追加");

        F.SaveAndVerify();
    }

    /// <summary>
    /// テスト用の土台。実Compilationから解析したModelを基点にDocumentを作り、
    /// 保存経路(`ApplyForSave`)と往復検証(`SceneSaveVerification`)の両方を通す。
    /// </summary>
    private sealed class Fixture {
        private Fixture(
            CSharpCompilation Compilation,SyntaxNode Root,SceneProjection Projection,SceneEditDocument Document){
            this.Compilation=Compilation;
            this.Root=Root;
            this.Projection=Projection;
            this.Document=Document;
        }

        private CSharpCompilation Compilation { get; }

        private SyntaxNode Root { get; }

        private SceneProjection Projection { get; }

        private SceneEditDocument Document { get; }

        internal static Fixture Create(){
            var Compilation=SceneAnalysisTestHelper.CreateCompilation(TransformTypes,Source);
            var Root=Compilation.SyntaxTrees
                                .Single(Tree=>Tree.GetRoot().ToFullString().Contains("class MainScene"))
                                .GetRoot();
            var SceneClass=SceneAnalysisTestHelper.FindSceneClass(Compilation,"MainScene")!;
            var Model=SceneDeclarationAnalyzer.Analyze(SceneClass,Compilation,CancellationToken.None);
            SceneProjectionReconciler Reconciler=new();
            var Projection=Reconciler.Reconcile(Key,Model,Previous:null);
            return new Fixture(
                Compilation,Root,Projection,new SceneEditDocument(Key,new SceneEditSnapshot(Model,Projection),Reconciler));
        }

        internal EditorNodeId Id(string NodeName)=>
            Assert.Single(this.Document.Projection.Nodes,Node=>Node.Node.Name==NodeName).Id;

        internal void Apply(SceneEditOperation[]Operations,string Label)=>
            Assert.True(this.Document.TryApply(Operations,Label),$"Documentへの適用に失敗: {Label}");

        /// <summary>保存経路を通し、往復検証も通ることを確認して結果テキストを返す。</summary>
        internal string SaveAndVerify(){
            var Result=SceneSourceEditor.ApplyForSave(
                                                      this.Root,Key,this.Projection,this.Document.History);
            Assert.True(Result.Succeeded,Result.ErrorMessage);

            Assert.True(SceneSaveVerification.Verify(
                Result.Root!,this.Root,this.Compilation,this.Document.Model,Key.ClassFullyQualifiedName,
                CancellationToken.None,out var Error),Error);

            return Result.Root!.ToFullString();
        }
    }
}
