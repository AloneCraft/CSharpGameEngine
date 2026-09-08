using System.Security.Cryptography;
using System.Text;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class StressExecutionFrameworkTests {
    [Fact]
    public void PlanBoundsSeedTicksAndEightHourMaximum(){
        Assert.Equal(28800,StressExecutionFramework.MaximumTimeoutSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(()=>StressExecutionFramework.ValidatePlan(CreatePlan(Seed:0)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>StressExecutionFramework.ValidatePlan(CreatePlan(TimeoutSeconds:28801)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>StressExecutionFramework.ValidatePlan(CreatePlan(Ticks:0)));
        StressPlan MissingFaultMetadata=CreatePlan()with{Workload=new Dictionary<string,string>{{"workload","synthetic"}}};
        Assert.Throws<ArgumentException>(()=>StressExecutionFramework.ValidatePlan(MissingFaultMetadata));
    }

    [Fact]
    public async Task SameSeedTicksAndWorkloadProduceSameAggregateHashes(){
        StressPlan Plan=CreatePlan();

        StressExecutionResult First=await StressExecutionFramework.RunAsync(Plan,SuccessTick,CancellationToken.None);
        StressExecutionResult Second=await StressExecutionFramework.RunAsync(Plan,SuccessTick,CancellationToken.None);

        Assert.True(First.Passed);
        Assert.Equal(Plan.TickCount,First.CompletedTicks);
        Assert.Equal(First.StateHash,Second.StateHash);
        Assert.Equal(First.EventHash,Second.EventHash);
    }

    [Fact]
    public async Task FirstFailureStopsAndProducesAReproducibleEnvelope(){
        StressPlan Plan=CreatePlan();
        static ValueTask<StressTickResult>Fault(int Tick,uint Seed,CancellationToken _)=>ValueTask.FromResult(Tick==7?StressTickResult.Failure(Hash($"s:{Seed}:{Tick}"),Hash($"e:{Seed}:{Tick}"),"fault-7","Injected fault at tick 7."):StressTickResult.Success(Hash($"s:{Seed}:{Tick}"),Hash($"e:{Seed}:{Tick}")));

        StressExecutionResult First=await StressExecutionFramework.RunAsync(Plan,Fault,CancellationToken.None);
        StressExecutionResult Second=await StressExecutionFramework.RunAsync(Plan,Fault,CancellationToken.None);

        Assert.Equal(StressOutcome.Failed,First.Outcome);
        Assert.Equal(8,First.CompletedTicks);
        Assert.Equal(new StressFailure(7,"fault-7","Injected fault at tick 7."),First.Failure);
        Assert.Equal(First,Second);
    }

    [Fact]
    public async Task InvalidWorkloadResultBecomesTheFirstFailureEnvelope(){
        StressPlan Plan=CreatePlan(Ticks:3);
        static ValueTask<StressTickResult>Invalid(int _,uint __,CancellationToken ___)=>ValueTask.FromResult(StressTickResult.Success("not-a-hash","not-a-hash"));

        StressExecutionResult Result=await StressExecutionFramework.RunAsync(Plan,Invalid,CancellationToken.None);

        Assert.Equal(StressOutcome.Failed,Result.Outcome);
        Assert.Equal(0,Result.CompletedTicks);
        Assert.Equal(new StressFailure(0,"workload-contract","Stress tick 0 must return canonical SHA-256 state and event hashes."),Result.Failure);
    }

    [Fact]
    public async Task DeadlineProducesTimeoutEnvelopeInsteadOfSuccess(){
        StressPlan Plan=CreatePlan(Ticks:1,TimeoutSeconds:1);
        static async ValueTask<StressTickResult>Slow(int Tick,uint Seed,CancellationToken CancellationToken){
            await Task.Delay(TimeSpan.FromSeconds(3),CancellationToken);
            return StressTickResult.Success(Hash($"s:{Seed}:{Tick}"),Hash($"e:{Seed}:{Tick}"));
        }

        StressExecutionResult Result=await StressExecutionFramework.RunAsync(Plan,Slow,CancellationToken.None);

        Assert.Equal(StressOutcome.TimedOut,Result.Outcome);
        Assert.False(Result.Passed);
        Assert.Equal("timeout",Result.Failure!.Signature);
    }

    [Fact]
    public async Task DeadlineWinsWhenAWorkloadIgnoresCancellationButReturns(){
        StressPlan Plan=CreatePlan(Ticks:1,TimeoutSeconds:1);
        static async ValueTask<StressTickResult>IgnoresCancellation(int Tick,uint Seed,CancellationToken _){
            await Task.Delay(TimeSpan.FromMilliseconds(1200));
            return StressTickResult.Success(Hash($"s:{Seed}:{Tick}"),Hash($"e:{Seed}:{Tick}"));
        }

        StressExecutionResult Result=await StressExecutionFramework.RunAsync(Plan,IgnoresCancellation,CancellationToken.None);

        Assert.Equal(StressOutcome.TimedOut,Result.Outcome);
        Assert.Equal(0,Result.CompletedTicks);
        Assert.Equal(new StressFailure(0,"timeout","Stress workload exceeded 1 seconds."),Result.Failure);
    }

    [Fact]
    public async Task VersionedReportRejectsTampering(){
        string Root=Path.Combine(Path.GetTempPath(),"cge-stress-report-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        try {
            StressPlan Plan=CreatePlan();
            StressExecutionResult Result=await StressExecutionFramework.RunAsync(Plan,SuccessTick,CancellationToken.None);
            var Payload=new StressReportPayload(StressReportStore.Schema,DateTimeOffset.Parse("2026-08-31T00:00:00Z"),Plan,Result,PerformanceMeasurementFramework.CaptureMachineFingerprint(),"contract",true);
            string ReportPath=await StressReportStore.SaveAsync(Root,Payload,CancellationToken.None);

            Assert.Empty(StressReportStore.Validate(ReportPath));
            string Json=await File.ReadAllTextAsync(ReportPath);
            await File.WriteAllTextAsync(ReportPath,Json.Replace("\"completedTicks\": 30","\"completedTicks\": 29",StringComparison.Ordinal));

            IReadOnlyList<string>Errors=StressReportStore.Validate(ReportPath);
            Assert.Contains(Errors,Error=>Error.Contains("SHA-256 mismatch",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("passed verdict contradicts",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    private static StressPlan CreatePlan(uint Seed=5489,int Ticks=30,int TimeoutSeconds=30)=>new("contract","Release","None",Seed,Ticks,TimeoutSeconds,new Dictionary<string,string>{{"workload","synthetic"},{"faultInjection","none"}},$"run --seed {Seed} --ticks {Ticks}");
    private static ValueTask<StressTickResult>SuccessTick(int Tick,uint Seed,CancellationToken _)=>ValueTask.FromResult(StressTickResult.Success(Hash($"s:{Seed}:{Tick}"),Hash($"e:{Seed}:{Tick}")));
    private static string Hash(string Value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Value))).ToLowerInvariant();
}
