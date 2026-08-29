using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class FeatureStageExecutorTests {
    [Fact]
    public async Task ExecuteAsync_ConfiguredExitCodeBecomesInfrastructureError() {
        var Stage=new FeatureStageDefinition(
            "test.infrastructure","Infrastructure classification",FeatureStagePack.Foundation,FeatureStageMode.Headless,
            FeatureStageAvailability.Executable,FeatureAdapterRequirement.None,["F-TEST"],["test"],1,1,30,"exit classification",
            [new FeatureStageCommand("cmd",["/c","exit","2"],[],[],[2])],["tests"]);
        var Options=new FeatureStageExecutionOptions(Directory.GetCurrentDirectory(),"Debug","test",FeatureAdapter.Warp,1,1);
        var Result=await new FeatureStageExecutor().ExecuteAsync(Stage,Options,CancellationToken.None);
        Assert.Equal(FeatureStageRunStatus.InfrastructureError,Result.Status);
        Assert.Contains(Result.Assertions,Assertion=>!Assertion.Passed&&Assertion.Details=="Exit code 2");
    }
}
