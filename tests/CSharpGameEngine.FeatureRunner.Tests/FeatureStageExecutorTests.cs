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
        Assert.Single(Result.Assertions,Assertion=>Assertion.Name=="retry:cmd");
    }
    [Fact]
    public async Task ExecuteAsync_OneInfrastructureFailureMayRecoverExactlyOnce(){
        string Root=Path.Combine(Path.GetTempPath(),"cge-feature-retry-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        string Marker=Path.Combine(Root,"attempted.marker");
        string Script=$"$p='{Marker.Replace("'","''",StringComparison.Ordinal)}'; if(Test-Path -LiteralPath $p){{ exit 0 }}; Set-Content -LiteralPath $p -Value attempted; exit 2";
        try {
            var Stage=new FeatureStageDefinition(
                "test.retry","Infrastructure retry",FeatureStagePack.Foundation,FeatureStageMode.Headless,
                FeatureStageAvailability.Executable,FeatureAdapterRequirement.None,["F-TEST"],["test"],1,1,30,"one retry",
                [new FeatureStageCommand("powershell",["-NoProfile","-Command",Script],[],[],[2])],["tests"]);
            var Options=new FeatureStageExecutionOptions(Directory.GetCurrentDirectory(),"Debug","test",FeatureAdapter.Warp,1,1);

            FeatureStageRunResult Result=await new FeatureStageExecutor().ExecuteAsync(Stage,Options,CancellationToken.None);

            Assert.Equal(FeatureStageRunStatus.Passed,Result.Status);
            Assert.Single(Result.Assertions,Assertion=>Assertion.Name=="retry:powershell"&&Assertion.Passed);
            Assert.Contains(Result.Assertions,Assertion=>Assertion.Name=="command:powershell:attempt:2"&&Assertion.Passed);
        } finally { Directory.Delete(Root,true); }
    }
    [Fact]
    public async Task ExecuteAsync_OracleFailureNeverRetries(){
        var Stage=new FeatureStageDefinition(
            "test.failure","Oracle failure",FeatureStagePack.Foundation,FeatureStageMode.Headless,
            FeatureStageAvailability.Executable,FeatureAdapterRequirement.None,["F-TEST"],["test"],1,1,30,"no retry",
            [new FeatureStageCommand("cmd",["/c","exit","1"],[],[],[2])],["tests"]);
        var Options=new FeatureStageExecutionOptions(Directory.GetCurrentDirectory(),"Debug","test",FeatureAdapter.Warp,1,1);

        FeatureStageRunResult Result=await new FeatureStageExecutor().ExecuteAsync(Stage,Options,CancellationToken.None);

        Assert.Equal(FeatureStageRunStatus.Failed,Result.Status);
        Assert.DoesNotContain(Result.Assertions,Assertion=>Assertion.Name.StartsWith("retry:",StringComparison.Ordinal));
        Assert.Contains(Result.Assertions,Assertion=>Assertion.Name=="command:cmd:attempt:1"&&!Assertion.Passed);
    }
    [Fact]
    public async Task ExecuteAsync_TimeoutNeverRetries(){
        var Stage=new FeatureStageDefinition(
            "test.timeout","Timeout",FeatureStagePack.Foundation,FeatureStageMode.Headless,
            FeatureStageAvailability.Executable,FeatureAdapterRequirement.None,["F-TEST"],["test"],1,1,1,"no timeout retry",
            [new FeatureStageCommand("powershell",["-NoProfile","-Command","Start-Sleep -Seconds 3; exit 0"],[],[],[2])],["tests"]);
        var Options=new FeatureStageExecutionOptions(Directory.GetCurrentDirectory(),"Debug","test",FeatureAdapter.Warp,1,1);

        FeatureStageRunResult Result=await new FeatureStageExecutor().ExecuteAsync(Stage,Options,CancellationToken.None);

        Assert.Equal(FeatureStageRunStatus.TimedOut,Result.Status);
        Assert.DoesNotContain(Result.Assertions,Assertion=>Assertion.Name.StartsWith("retry:",StringComparison.Ordinal));
        Assert.Single(Result.Assertions,Assertion=>Assertion.Name=="command:powershell:attempt:1"&&!Assertion.Passed&&Assertion.Details.Contains("never retried",StringComparison.Ordinal));
    }
}
