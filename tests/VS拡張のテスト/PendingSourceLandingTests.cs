using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class PendingSourceLandingTests {
    private static readonly SceneKey Key=new("project-a","Game.MainScene");

    [Fact]
    public void LandingMatchesOnlyItsExpectedCanonicalSemantics(){
        SceneModel Expected=Analyze("Expected");
        SceneModel SameSemantics=Analyze("Expected",Comment:"// external formatting");
        SceneModel SupersedingEdit=Analyze("Changed");
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(
            Key,Expected,Previous:null);

        PendingSourceLanding Landing=PendingSourceLanding.Begin(Projection,Expected);

        Assert.Same(Projection,Landing.Projection);
        Assert.False(Landing.WriteCompleted);
        Assert.True(Landing.Matches(SameSemantics));
        Assert.False(Landing.Matches(SupersedingEdit));
    }

    [Fact]
    public void CompletingWritePreservesExpectedIdentityAndAdvancesState(){
        SceneModel Expected=Analyze("Expected");
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(
            Key,Expected,Previous:null);
        PendingSourceLanding Pending=PendingSourceLanding.Begin(Projection,Expected);

        PendingSourceLanding Completed=Pending.CompleteWrite();

        Assert.False(Pending.WriteCompleted);
        Assert.True(Completed.WriteCompleted);
        Assert.Same(Projection,Completed.Projection);
        Assert.True(Completed.Matches(Expected));
    }

    private static SceneModel Analyze(string NodeName,string Comment=""){
        string Source=$$"""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Children=[new Node { Name="{{NodeName}}" }] {{Comment}}
                };
            }
            """;
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(Source);
        var SceneClass=SceneAnalysisTestHelper.FindSceneClass(Compilation,"MainScene")!;
        return SceneDeclarationAnalyzer.Analyze(
            SceneClass,Compilation,CancellationToken.None);
    }
}
