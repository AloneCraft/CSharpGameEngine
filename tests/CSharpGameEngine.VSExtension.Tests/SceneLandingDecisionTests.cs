using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0051 Step 1-7: 自分の書き込みの着地でDocumentを維持してよいかの判定を、
/// <b>予測モデルと実解析モデルを実際に突き合わせて</b>固定する。
///
/// <para>
/// <b>なぜ結合テストなのか</b>: Step 1-6は判定自体
/// (<c>Decide(IsOwnLanding:true,ProjectionsMatch:false)→Rebase</c>)は正しく、
/// <b>第2引数を実機でどう作るかが誤っていた</b>。<c>SceneDiffEngine.Compare(...).IsEmpty</c>を
/// 使ったため、Componentの<c>EmitExpressionText</c>が予測モデル側で古いまま残る仕様
/// (<c>SceneModelEditApplierTests</c>が明文化している)により<b>実機で一度もKeepDocumentにならなかった</b>。
/// 引数の作り方がテストの外にある限り、ポリシーのユニットテストでは永久に捕まらない。
/// ここでは実Compilationから解析したモデルを使い、判定材料の生成ごと検証する。
/// </para>
/// </summary>
public sealed class SceneLandingDecisionTests {
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

    /// <summary>
    /// <b>今回の不具合を確実に捕まえる1本。</b> ギズモ移動を保存して再解析した着地で
    /// <c>KeepDocument</c>にならなければ、Stage Bの高速経路は実機で死んでいる。
    /// Step 1-6の実装はここで必ず<c>Rebase</c>を返した。
    /// </summary>
    [Fact]
    public void GizmoMove_LandsAsKeepDocument(){
        Fixture F=Fixture.Create();
        F.Move("Alpha",9f);

        Assert.Equal(SceneLandingAction.KeepDocument,F.SaveAndLand());
    }

    [Fact]
    public void Rename_LandsAsKeepDocument(){
        Fixture F=Fixture.Create();
        F.Apply([new RenameNodeOperation(Key,F.Id("Alpha"),1,"AlphaRenamed")],"Rename node");

        Assert.Equal(SceneLandingAction.KeepDocument,F.SaveAndLand());
    }

    [Fact]
    public void AddNode_LandsAsKeepDocument(){
        Fixture F=Fixture.Create();
        F.Apply([new AddNodeOperation(Key,F.Id("Gamma"),1,"Added")],"Add node");

        Assert.Equal(SceneLandingAction.KeepDocument,F.SaveAndLand());
    }

    [Fact]
    public void Remove_LandsAsKeepDocument(){
        Fixture F=Fixture.Create();
        F.Apply([new RemoveNodeOperation(Key,F.Id("Beta"),1)],"Remove node");

        Assert.Equal(SceneLandingAction.KeepDocument,F.SaveAndLand());
    }

    [Fact]
    public void Reparent_LandsAsKeepDocument(){
        Fixture F=Fixture.Create();
        F.Apply([new ReparentNodeOperation(Key,F.Id("Beta"),1,F.Id("Gamma"),0)],"Reparent node");

        Assert.Equal(SceneLandingAction.KeepDocument,F.SaveAndLand());
    }

    /// <summary>複数操作を溜めてから保存しても維持できること(実運用の形)。</summary>
    [Fact]
    public void MultipleEdits_LandAsKeepDocument(){
        Fixture F=Fixture.Create();
        F.Apply([new RenameNodeOperation(Key,F.Id("Alpha"),1,"AlphaRenamed")],"Rename node");
        F.Move("AlphaRenamed",7f);
        F.Apply([new RemoveNodeOperation(Key,F.Id("Beta"),3)],"Remove node");

        Assert.Equal(SceneLandingAction.KeepDocument,F.SaveAndLand());
    }

    /// <summary>
    /// 着地までの窓でユーザーがC#を直接編集して値だけ変えたケース。ID対応は一致してしまうので
    /// 構造署名の比較が無いと見逃し、その編集が表示から永久に落ちる。
    /// </summary>
    [Fact]
    public void ForeignValueEdit_LandsAsRebase(){
        Fixture F=Fixture.Create();
        F.Move("Alpha",9f);

        Assert.Equal(SceneLandingAction.Rebase,F.SaveAndLand(TamperPositionX:42f));
    }

    /// <summary>
    /// 着地までの窓でユーザーがノードを足したケース。構造署名で検知する。
    /// </summary>
    [Fact]
    public void ForeignStructuralEdit_LandsAsRebase(){
        Fixture F=Fixture.Create();
        F.Move("Alpha",9f);

        Assert.Equal(SceneLandingAction.Rebase,F.SaveAndLand(ExtraNodeName:"Injected"));
    }

    /// <summary>解析エラー中は判断材料が無い。触らずに次の正常な解析を待つ。</summary>
    [Fact]
    public void AnalysisWithoutRoot_LandsAsDefer(){
        Fixture F=Fixture.Create();

        Assert.Equal(
            SceneLandingAction.Defer,
            SceneLandingDecision.Evaluate(
                RealModel:null,RealProjection:null,
                F.Document.Model,F.Document.Projection,IsOwnLanding:true));
    }

    /// <summary>
    /// 解析エラーで直前の良い投影が引き継がれてくる形。ここで<c>LastKnownGoodModel</c>へ
    /// フォールバックすると「一致」と誤判定してエラー状態を見逃す。
    /// </summary>
    [Fact]
    public void AnalysisWithErrors_LandsAsDefer(){
        Fixture F=Fixture.Create();

        Assert.Equal(
            SceneLandingAction.Defer,
            SceneLandingDecision.Evaluate(
                F.BrokenModel(),F.Document.Projection,
                F.Document.Model,F.Document.Projection,IsOwnLanding:true));
    }

    /// <summary>自分の書き込みでない着地は、内容が一致していてもDocumentを維持する根拠にならない。</summary>
    [Fact]
    public void ForeignLanding_AlwaysRebases(){
        Fixture F=Fixture.Create();

        Assert.Equal(
            SceneLandingAction.Rebase,
            SceneLandingDecision.Evaluate(
                F.Document.Model,F.Document.Projection,
                F.Document.Model,F.Document.Projection,IsOwnLanding:false));
    }

    // ---- ID対応の全単射 ----

    [Fact]
    public void Identity_MatchesForTheSameProjection(){
        Fixture F=Fixture.Create();

        Assert.True(SceneProjectionIdentityComparer.Matches(F.Document.Projection,F.Document.Projection));
    }

    /// <summary>
    /// 区別できない兄弟はIDが払い直される(`SceneProjectionIdentityTests`が固定している既知の限界)。
    /// そのケースを維持してしまうと、次の保存が部分列のNodeIdを解決できず保存不能になる。
    /// </summary>
    [Fact]
    public void Identity_DoesNotMatchWhenIdsAreReallocated(){
        SceneProjectionReconciler Reconciler=new();
        SceneModel Model=Analyze();
        SceneProjection Left=Reconciler.Reconcile(Key,Model,Previous:null);
        SceneProjection Right=Reconciler.Reconcile(Key,Model,Previous:null);

        Assert.False(SceneProjectionIdentityComparer.Matches(Left,Right));
        Assert.NotNull(SceneProjectionIdentityComparer.FindFirstMismatch(Left,Right));
    }

    /// <summary>ノード数が違えば即座に不一致(片方向比較だと取りこぼす形)。</summary>
    [Fact]
    public void Identity_DoesNotMatchWhenNodeCountsDiffer(){
        Fixture F=Fixture.Create();
        SceneProjection Before=F.Document.Projection;
        F.Apply([new RemoveNodeOperation(Key,F.Id("Beta"),1)],"Remove node");

        Assert.False(SceneProjectionIdentityComparer.Matches(Before,F.Document.Projection));
        Assert.Contains("ノード数",SceneProjectionIdentityComparer.FindFirstMismatch(Before,F.Document.Projection)!);
    }

    private static SceneModel Analyze(string?ExtraNodeName=null,float?TamperPositionX=null){
        string Body=Source;
        if(TamperPositionX is { }X){
            Body=Body.Replace("PositionX=1f",$"PositionX={X}f");
        }
        if(ExtraNodeName is { }Extra){
            Body=Body.Replace("new Node { Name=\"Beta\" },",$"new Node {{ Name=\"Beta\" }},new Node {{ Name=\"{Extra}\" }},");
        }

        CSharpCompilation Compilation=SceneAnalysisTestHelper.CreateCompilation(TransformTypes,Body);
        INamedTypeSymbol SceneClass=SceneAnalysisTestHelper.FindSceneClass(Compilation,"MainScene")!;
        return SceneDeclarationAnalyzer.Analyze(SceneClass,Compilation,CancellationToken.None);
    }

    /// <summary>
    /// 「Documentで編集 → 保存で書き戻し → 書き戻した結果を解析し直して着地」を実際に通す土台。
    /// 判定材料(実解析Model/Projection)を本番と同じ作り方で用意するのが目的。
    /// </summary>
    private sealed class Fixture {
        private Fixture(
            CSharpCompilation Compilation,SyntaxNode Root,SceneProjection Projection,
            SceneEditDocument Document,SceneProjectionReconciler Reconciler){
            this.Compilation=Compilation;
            this.Root=Root;
            this.Projection=Projection;
            this.Document=Document;
            this.Reconciler=Reconciler;
        }

        private CSharpCompilation Compilation { get; }

        private SyntaxNode Root { get; }

        private SceneProjection Projection { get; }

        private SceneProjectionReconciler Reconciler { get; }

        internal SceneEditDocument Document { get; }

        internal static Fixture Create(){
            CSharpCompilation Compilation=SceneAnalysisTestHelper.CreateCompilation(TransformTypes,Source);
            SyntaxNode Root=Compilation.SyntaxTrees
                .Single(Tree=>Tree.GetRoot().ToFullString().Contains("class MainScene"))
                .GetRoot();
            INamedTypeSymbol SceneClass=SceneAnalysisTestHelper.FindSceneClass(Compilation,"MainScene")!;
            SceneModel Model=SceneDeclarationAnalyzer.Analyze(SceneClass,Compilation,CancellationToken.None);
            SceneProjectionReconciler Reconciler=new();
            SceneProjection Projection=Reconciler.Reconcile(Key,Model,Previous:null);
            return new Fixture(
                Compilation,Root,Projection,
                new SceneEditDocument(Key,new SceneEditSnapshot(Model,Projection),Reconciler),Reconciler);
        }

        internal EditorNodeId Id(string NodeName)=>
            Assert.Single(this.Document.Projection.Nodes,Node=>Node.Node.Name==NodeName).Id;

        internal void Apply(SceneEditOperation[]Operations,string Label)=>
            Assert.True(this.Document.TryApply(Operations,Label),$"Documentへの適用に失敗: {Label}");

        internal void Move(string NodeName,float X)=>
            this.Apply(
                [new SetTransformOperation(Key,this.Id(NodeName),1,
                    new SceneTransformValue(new Vector3(X,0f,0f),Quaternion.Identity,Vector3.One))],
                "Move node");

        /// <summary>解析は通るがRootが取れないModel(構文エラー中の形)。</summary>
        internal SceneModel BrokenModel(){
            CSharpCompilation Broken=SceneAnalysisTestHelper.CreateCompilation(
                TransformTypes,
                """
                using CSharpGameEngine.Entities;
                namespace Game;
                public partial class MainScene:IScene {
                    public Node Root=>null!;
                }
                """);
            INamedTypeSymbol SceneClass=SceneAnalysisTestHelper.FindSceneClass(Broken,"MainScene")!;
            return SceneDeclarationAnalyzer.Analyze(SceneClass,Broken,CancellationToken.None);
        }

        /// <summary>
        /// Documentの未書き出し履歴を書き戻し、その結果を解析し直して着地させ、判定結果を返す。
        /// <paramref name="TamperPositionX"/>/<paramref name="ExtraNodeName"/>は、書き戻しから着地までの
        /// 窓でユーザーがC#を直接編集したケースを模す。
        /// </summary>
        internal SceneLandingAction SaveAndLand(float?TamperPositionX=null,string?ExtraNodeName=null){
            SceneSourceEditStepsResult Written=SceneSourceEditor.ApplyForSaveSteps(
                this.Root,Key,this.Projection,this.Document.UnwrittenHistory);
            Assert.True(Written.Succeeded,Written.ErrorMessage);
            this.Document.MarkWritten();

            SyntaxNode LandedRoot=Written.FinalRoot??this.Root;
            if(TamperPositionX is { }X){
                LandedRoot=CSharpSyntaxTree
                    .ParseText(LandedRoot.ToFullString().Replace("PositionX=9f",$"PositionX={X}f")).GetRoot();
            }
            if(ExtraNodeName is { }Extra){
                LandedRoot=CSharpSyntaxTree.ParseText(LandedRoot.ToFullString()
                    .Replace("new Node { Name=\"Beta\" },",$"new Node {{ Name=\"Beta\" }},new Node {{ Name=\"{Extra}\" }},"))
                    .GetRoot();
            }

            CSharpCompilation Landed=this.Compilation.ReplaceSyntaxTree(
                this.Root.SyntaxTree,LandedRoot.SyntaxTree);
            INamedTypeSymbol SceneClass=SceneAnalysisTestHelper.FindSceneClass(Landed,"MainScene")!;
            SceneModel RealModel=SceneDeclarationAnalyzer.Analyze(SceneClass,Landed,CancellationToken.None);

            // 本番の Publish と同じく、自分の着地では Document の投影を引き継ぎ元にする。
            SceneProjection RealProjection=this.Reconciler.Reconcile(Key,RealModel,this.Document.Projection);

            return SceneLandingDecision.Evaluate(
                RealModel,RealProjection,this.Document.Model,this.Document.Projection,IsOwnLanding:true);
        }
    }
}
