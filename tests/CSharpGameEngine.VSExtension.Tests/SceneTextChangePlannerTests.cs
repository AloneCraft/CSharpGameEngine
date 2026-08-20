using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneTextChangePlannerTests {
    [Fact]
    public void Plan_OrdersIndependentChangesFromEndToStart(){
        var Before=SourceText.From("alpha beta gamma");
        var After=SourceText.From("ALPHA beta GAMMA!");

        var Changes=SceneTextChangePlanner.Plan(Before,After);

        Assert.NotEmpty(Changes);
        Assert.True(Changes.Zip(Changes.Skip(1),(Left,Right)=>Left.Span.Start>=Right.Span.Start).All(static Ordered=>Ordered));
    }

    [Fact]
    public void Plan_ReturnsNoChangeForIdenticalSource(){
        var Source=SourceText.From("unchanged");

        Assert.Empty(SceneTextChangePlanner.Plan(Source,Source));
    }

    [Fact]
    public void ApplyGuard_ExactLiveText_AllowsAtomicWrite(){
        Assert.True(SceneTextBufferApplyGuard.Matches(
            SourceText.From("before"),LiveText:"before"));
    }

    [Fact]
    public void ApplyGuard_LiveTextChangedAfterRoslynRead_RejectsWrite(){
        Assert.False(SceneTextBufferApplyGuard.Matches(
            SourceText.From("before"),LiveText:"externally changed"));
    }

    /// <summary>
    /// M0044: SourceText版の粗さを実測値として固定する。血縁の無いSourceText同士では、
    /// 1リテラルしか違わなくても<b>ファイル全体を覆う1個の変更</b>が返る。これがギズモ編集の
    /// たびにエディタを全再レイアウトさせ、表示が一度最上部へ飛んでいた原因だった。
    /// この特性が将来変わったら気付けるようにテストで留めておく。
    /// </summary>
    [Fact]
    public void Plan_SourceTextOverload_RewritesTheWholeFileEvenForOneLiteral(){
        var Before=SourceText.From(BuildSource("18.9862971f","1f"));
        var After=SourceText.From(BuildSource("30.98745f","1f"));

        var Changes=SceneTextChangePlanner.Plan(Before,After);

        var Single=Assert.Single(Changes);
        Assert.Equal(0,Single.Span.Start);
        Assert.Equal(Before.Length,Single.Span.Length);
    }

    /// <summary>
    /// M0044: ツリー版は実際に変わった領域だけを返す。SourceText版がファイル全体を書き換えるのに
    /// 対し、こちらは数値リテラルの長さぶんしか触らない。
    /// </summary>
    [Fact]
    public void Plan_SyntaxTreeOverload_TouchesOnlyTheChangedLiteral(){
        var Before=CSharpSyntaxTree.ParseText(BuildSource("18.9862971f","1f"));
        var After=ReplaceLiterals(Before.GetRoot(),("18.9862971f","30.98745f"));

        var Changes=SceneTextChangePlanner.Plan(Before,After.SyntaxTree);

        var Single=Assert.Single(Changes);
        Assert.Equal("30.98745",Single.NewText);
        Assert.True(
            Single.Span.Length<50,
            $"変更範囲がファイル全体({Before.GetText().Length}文字)に対して十分小さいこと。実測{Single.Span.Length}");
    }

    /// <summary>
    /// M0044: 複数選択ドラッグ相当。離れた2箇所を変えたとき、SourceText版は間の数千行を含む
    /// 1個の変更にしてしまうが、ツリー版は独立した2個の小さな変更として返す。
    /// </summary>
    [Fact]
    public void Plan_SyntaxTreeOverload_ReturnsSeparateChangesForDistantEdits(){
        var Before=CSharpSyntaxTree.ParseText(BuildSource("18.9862971f","1f"));
        var After=ReplaceLiterals(Before.GetRoot(),("18.9862971f","30.98745f"),("1f","2f"));

        var Changes=SceneTextChangePlanner.Plan(Before,After.SyntaxTree);

        Assert.Equal(2,Changes.Count);
        Assert.True(
            Changes.Sum(static Change=>Change.Span.Length)<50,
            "離れた2箇所でも書き換え総量は小さいままであること。");
        Assert.True(
            Changes.Zip(Changes.Skip(1),static (Left,Right)=>Left.Span.Start>=Right.Span.Start).All(static Ordered=>Ordered),
            "適用順序(開始位置の降順)はSourceText版と同じであること。");
    }

    /// <summary>
    /// M0044でツリー版へ切り替えるにあたっての<b>最重要の契約</b>。差分の出所を変えても、
    /// 適用結果が編集後ツリーの全文と完全一致しなければソースを壊す。
    /// </summary>
    [Fact]
    public void Plan_SyntaxTreeOverload_AppliedChangesReproduceTheEditedTreeExactly(){
        var Before=CSharpSyntaxTree.ParseText(BuildSource("18.9862971f","1f"));
        var After=ReplaceLiterals(Before.GetRoot(),("18.9862971f","30.98745f"),("1f","2f"));

        var Changes=SceneTextChangePlanner.Plan(Before,After.SyntaxTree);
        var Applied=Before.GetText().WithChanges(Changes);

        Assert.Equal(After.ToFullString(),Applied.ToString());
    }

    /// <summary>SceneSourceEditorと同じく、元rootへ<c>ReplaceNode</c>して新rootを得る。</summary>
    private static SyntaxNode ReplaceLiterals(SyntaxNode Root,params (string Old,string New)[]Edits){
        var Current=Root;
        foreach(var Edit in Edits){
            var Target=Current.DescendantNodes()
                              .OfType<LiteralExpressionSyntax>()
                              .First(Node=>Node.ToString()==Edit.Old);
            Current=Current.ReplaceNode(Target,SyntaxFactory.ParseExpression(Edit.New).WithTriviaFrom(Target));
        }
        return Current;
    }

    /// <summary>2つのリテラルの間に数百行を挟んだ、実シーンに近い規模のソース。</summary>
    private static string BuildSource(string First,string Second){
        var Text=new System.Text.StringBuilder();
        Text.AppendLine("class S {");
        Text.AppendLine("    void M(){");
        for(var Index=0;Index<200;Index++){ Text.AppendLine("        // filler "+Index); }
        Text.AppendLine("        var A="+First+";");
        for(var Index=0;Index<400;Index++){ Text.AppendLine("        // middle "+Index); }
        Text.AppendLine("        var B="+Second+";");
        for(var Index=0;Index<200;Index++){ Text.AppendLine("        // tail "+Index); }
        Text.AppendLine("    }");
        Text.AppendLine("}");
        return Text.ToString();
    }
}
