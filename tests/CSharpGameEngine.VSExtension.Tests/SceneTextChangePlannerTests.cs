using System.Linq;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneTextChangePlannerTests {
    [Fact]
    public void Plan_OrdersIndependentChangesFromEndToStart(){
        SourceText Before=SourceText.From("alpha beta gamma");
        SourceText After=SourceText.From("ALPHA beta GAMMA!");

        var Changes=SceneTextChangePlanner.Plan(Before,After);

        Assert.NotEmpty(Changes);
        Assert.True(Changes.Zip(Changes.Skip(1),(Left,Right)=>Left.Span.Start>=Right.Span.Start).All(static Ordered=>Ordered));
    }

    [Fact]
    public void Plan_ReturnsNoChangeForIdenticalSource(){
        SourceText Source=SourceText.From("unchanged");

        Assert.Empty(SceneTextChangePlanner.Plan(Source,Source));
    }
}
