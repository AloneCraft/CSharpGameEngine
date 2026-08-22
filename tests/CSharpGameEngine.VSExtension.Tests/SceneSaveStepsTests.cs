using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0051 Step 1-5: 保存を<b>1操作=1 VS Undo単位</b>で書き出すための土台
/// (<see cref="SceneSourceEditor.ApplyForSaveSteps"/>)を検証する。
///
/// <para>
/// 実機報告「保存した後も取り消しできるようにして」への対応でテキストUndoへ委譲するようにしたが、
/// 保存が1トランザクションのままだと3回編集してから保存したときCtrl+Zが3回分まとめて戻ってしまう
/// (ユーザーの確定仕様は1操作=1単位)。
/// </para>
///
/// <para>
/// <b>ここでの本命は隣接差分の逐次適用</b>。<c>SceneEditorSessionService.BuildWriteSteps</c>が
/// 書き込み前に行うオフライン検算を、そのまま同じ形で再現している — 各ステップの
/// <c>Plan(Steps[i-1], Steps[i])</c>を順に当てて最終形へ辿り着けなければ、実機では保存が
/// 途中で破綻する。
/// </para>
/// </summary>
public sealed class SceneSaveStepsTests {
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

    [Fact]
    public void Steps_AreEmptyWithoutHistory(){
        Fixture F=Fixture.Create();

        SceneSourceEditStepsResult Result=F.BuildSteps();

        Assert.True(Result.Succeeded);
        Assert.Empty(Result.Steps);
        Assert.Null(Result.FinalRoot);
    }

    /// <summary>
    /// M0051 Step 1-6の本命。保存は履歴全体ではなく<b>まだテキストへ書き出していない部分列</b>だけを
    /// 再生する。ここが崩れると2回目の保存が1回目の編集をもう一度C#へ適用してしまう。
    ///
    /// <para>
    /// さらに厳しいのは、部分列が<b>先に書き出したエントリで追加されたノード</b>を参照する場合。
    /// そのノードのIDはDocumentのReconcilerが払い出したもので、実C#の投影がそれを引き継いで
    /// いなければ解決できず「保存時に対象Nodeの構文を特定できません」になる。
    /// (実運用ではSceneDocumentLineagePolicyがこの引き継ぎを保証し、失敗したらResetToへ縮退する。)
    /// </para>
    /// </summary>
    [Fact]
    public void Suffix_ResolvesNodesAddedByAlreadyWrittenEntries(){
        Fixture F=Fixture.Create();
        F.Apply([new AddNodeOperation(Key,F.Id("Gamma"),1,"Added")],"Add node");
        EditorNodeId Added=F.Id("Added");

        // 1件目まで書き出した、という状態を作る。
        SceneSourceEditStepsResult First=F.BuildSteps();
        Assert.True(First.Succeeded,First.ErrorMessage);
        Assert.Single(First.Steps);
        F.MarkWritten();
        Assert.Empty(F.Document.UnwrittenHistory);

        // 2件目は「1件目で追加したノード」を対象にする。
        F.Apply([new RenameNodeOperation(Key,Added,2,"AddedRenamed")],"Rename node");

        Assert.Single(F.Document.UnwrittenHistory);
        Assert.Equal("Rename node",F.Document.UnwrittenHistory[0].Label);

        // 本番の着地を再現する: 書き出したテキストを解析し直し、DocumentのProjectionを
        // 引き継ぎ元(IdentityBasis)としてReconcileする。これで実ソース由来の位置情報を持ちつつ
        // EditorNodeIdはDocument側と一致する投影ができる。これがStage Bの前提そのもの。
        SceneProjection RealProjection=F.ReanalyzeWith(First.FinalRoot!,F.Document.Projection);

        var Second=SceneSourceEditor.ApplyForSaveSteps(
                                                       First.FinalRoot!,Key,RealProjection,F.Document.UnwrittenHistory);

        Assert.True(Second.Succeeded,Second.ErrorMessage);
        Assert.Single(Second.Steps);
        var Text=Second.FinalRoot!.ToFullString();
        Assert.Contains("\"AddedRenamed\"",Text);

        // 1件目の追加が二重適用されていないこと。元は4ノード(Alpha/Beta/Gamma/GammaChild)で、
        // AddNodeが1件だけ効いているなら5ノード。部分列を渡さず全履歴を再生すると6になる。
        Assert.Equal(5,CountOccurrences(Text,"new Node"));
    }

    private static int CountOccurrences(string Text,string Value){
        var Count=0;
        for(var Index=Text.IndexOf(Value,System.StringComparison.Ordinal);Index>=0;
            Index=Text.IndexOf(Value,Index+Value.Length,System.StringComparison.Ordinal)){
            Count++;
        }

        return Count;
    }

    /// <summary>履歴1件につき中間構文木が1つ、ラベルはDocumentへ積んだものがそのまま並ぶ。</summary>
    [Fact]
    public void Steps_AreOnePerHistoryEntryAndCarryTheirLabels(){
        Fixture F=Fixture.Create();
        F.Apply([new RenameNodeOperation(Key,F.Id("Alpha"),1,"AlphaRenamed")],"Rename node");
        F.Apply([new RemoveNodeOperation(Key,F.Id("Beta"),2)],"Remove node");
        F.Apply(
            [new ReparentNodeOperation(Key,F.Id("GammaChild"),3,F.Id("Gamma"),0)],"Reparent node");

        SceneSourceEditStepsResult Result=F.BuildSteps();

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        Assert.Equal(3,Result.Steps.Count);
        Assert.Equal(["Rename node","Remove node","Reparent node"],Result.Labels);
    }

    /// <summary>
    /// 各ステップは1つ前のステップからの差分として適用できなければならない。ここが崩れると
    /// <c>ApplyTextStepsAsync</c>が2段目以降で<c>SceneTextBufferApplyGuard</c>に弾かれる。
    /// </summary>
    [Fact]
    public void Steps_ApplySequentiallyToReachTheFinalText(){
        Fixture F=Fixture.Create();
        F.Apply([new RenameNodeOperation(Key,F.Id("Alpha"),1,"AlphaRenamedMuchLonger")],"Rename node");
        F.Apply(
            [new SetTransformOperation(Key,F.Id("AlphaRenamedMuchLonger"),2,
                new SceneTransformValue(new Vector3(9f,0f,0f),Quaternion.Identity,Vector3.One))],
            "Move node");
        F.Apply([new RemoveNodeOperation(Key,F.Id("Beta"),3)],"Remove node");

        SceneSourceEditStepsResult Result=F.BuildSteps();
        Assert.True(Result.Succeeded,Result.ErrorMessage);

        var Running=SourceText.From(Source);
        SyntaxTree PreviousTree=F.Root.SyntaxTree;
        List<int>WidestSpans=[];
        foreach(var StepRoot in Result.Steps){
            var Changes=SceneTextChangePlanner.Plan(PreviousTree,StepRoot.SyntaxTree);
            Assert.NotEmpty(Changes);
            WidestSpans.Add(Changes.Max(Change=>Change.Span.Length));
            Running=Running.WithChanges(Changes);
            Assert.Equal(StepRoot.ToFullString(),Running.ToString());
            PreviousTree=StepRoot.SyntaxTree;
        }

        Assert.Equal(Result.FinalRoot!.ToFullString(),Running.ToString());

        // 各ステップが最小差分であること。全文置換へ落ちると変更は「1個」のままspanがファイル全体を
        // 覆うので、個数ではなく<b>置換範囲の広さ</b>で見る必要がある(SceneTextChangePlannerの
        // SourceText版は血縁の無いテキスト同士だとファイル全体を1個の変更として返す)。
        // 1操作の書き換えがファイル全体を覆うと、テキストUndoの単位としては正しくても
        // コードエディタのcaret・折り畳み・スクロール位置が毎回リセットされてしまう。
        Assert.All(WidestSpans,Span=>Assert.True(
            Span<Source.Length/4,$"変更範囲が広すぎます({Span}文字 / 全{Source.Length}文字) — 全文差分へ落ちた疑い"));
    }

    /// <summary>
    /// 最終形は<see cref="SceneSourceEditor.ApplyForSave"/>(従来経路)と一致すること。
    /// ステップ分割は書き込み単位を分けるだけで、保存の成果物を変えてはならない。
    /// </summary>
    [Fact]
    public void Steps_FinalRootMatchesApplyForSave(){
        Fixture F=Fixture.Create();
        F.Apply([new RenameNodeOperation(Key,F.Id("Alpha"),1,"AlphaRenamed")],"Rename node");
        F.Apply([new RemoveNodeOperation(Key,F.Id("Beta"),2)],"Remove node");

        SceneSourceEditStepsResult Steps=F.BuildSteps();
        var Whole=SceneSourceEditor.ApplyForSave(
                                                 F.Root,Key,F.Projection,F.Document.History);

        Assert.True(Steps.Succeeded,Steps.ErrorMessage);
        Assert.True(Whole.Succeeded,Whole.ErrorMessage);
        Assert.Equal(Whole.Root!.ToFullString(),Steps.FinalRoot!.ToFullString());
    }

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

        private SceneProjectionReconciler Reconciler { get; }

        internal SyntaxNode Root { get; }

        internal SceneProjection Projection { get; }

        internal SceneEditDocument Document { get; }

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
                Compilation,Root,Projection,
                new SceneEditDocument(Key,new SceneEditSnapshot(Model,Projection),Reconciler),Reconciler);
        }

        /// <summary>
        /// 書き出した構文木を解析し直し、<paramref name="IdentityBasis"/>からIDを引き継いだ実投影を返す
        /// (本番では<c>SceneEditorSessionService.Publish</c>が着地時に行っていること)。
        /// </summary>
        internal SceneProjection ReanalyzeWith(SyntaxNode WrittenRoot,SceneProjection IdentityBasis){
            CSharpCompilation Updated=this.Compilation.ReplaceSyntaxTree(
                this.Root.SyntaxTree,WrittenRoot.SyntaxTree);
            var SceneClass=SceneAnalysisTestHelper.FindSceneClass(Updated,"MainScene")!;
            var RealModel=SceneDeclarationAnalyzer.Analyze(SceneClass,Updated,CancellationToken.None);
            return this.Reconciler.Reconcile(Key,RealModel,IdentityBasis);
        }

        internal EditorNodeId Id(string NodeName)=>
            Assert.Single(this.Document.Projection.Nodes,Node=>Node.Node.Name==NodeName).Id;

        internal void Apply(SceneEditOperation[]Operations,string Label)=>
            Assert.True(this.Document.TryApply(Operations,Label),$"Documentへの適用に失敗: {Label}");

        internal SceneSourceEditStepsResult BuildSteps()=>
            SceneSourceEditor.ApplyForSaveSteps(
                this.Root,Key,this.Projection,this.Document.UnwrittenHistory);

        internal void MarkWritten()=>this.Document.MarkWritten();
    }
}
