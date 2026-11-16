using CSharpGameEngine.Core;

namespace CSharpGameEngine.Entities.Tests;

public sealed class DeterministicJobRuntimeSpec {
    private static readonly StableId SystemId=StableId.Parse("4a7578ca-9d2e-5935-9378-e0c927435720");

    [Fact]
    public async Task SingleAndScheduledExecutionProduceIdenticalStateAndEventOrder(){
        var Source=Enumerable.Range(0,4097).Select(static Value=>new JobState(Value,0)).ToArray();
        var Options=new DeterministicJobOptions(73,4);

        var Single=await DeterministicJobRuntime.ExecuteAsync<TransformJob,JobState,JobEvent>(
            SystemId,new TransformJob(),Source,DeterministicJobExecutionMode.SingleThread,Options);
        var Scheduled=await DeterministicJobRuntime.ExecuteAsync<TransformJob,JobState,JobEvent>(
            SystemId,new TransformJob(),Source,DeterministicJobExecutionMode.Scheduled,Options);

        Assert.Equal(Single.States,Scheduled.States);
        Assert.Equal(Single.Events,Scheduled.Events);
        Assert.Equal(Hash(Single.States,Single.Events),Hash(Scheduled.States,Scheduled.Events));
        Assert.True(Scheduled.Events.Zip(Scheduled.Events.Skip(1),(Left,Right)=>Left.Key.CompareTo(Right.Key)<0).All(static Ordered=>Ordered));
        Assert.All(Source,static State=>Assert.Equal(0,State.Accumulated));
    }

    [Fact]
    public async Task ScheduledFailureIsReportedFromTheLowestPartitionAndDoesNotMutateInput(){
        var Source=Enumerable.Range(0,400).Select(static Value=>new JobState(Value,0)).ToArray();
        var Snapshot=Source.ToArray();

        var Exception=await Assert.ThrowsAsync<DeterministicJobException>(async()=>await DeterministicJobRuntime.ExecuteAsync<FailingJob,JobState,JobEvent>(
            SystemId,new FailingJob(),Source,DeterministicJobExecutionMode.Scheduled,new DeterministicJobOptions(50,4)).AsTask());

        Assert.Equal(1,Exception.PartitionId);
        Assert.Equal(75,Exception.Index);
        Assert.Equal(Snapshot,Source);
    }

    [Fact]
    public async Task ScheduledExecutionRespectsTheWorkerBound(){
        var Source=new JobState[32];

        var Result=await DeterministicJobRuntime.ExecuteAsync<ProbedJob,JobState,JobEvent>(
            SystemId,new ProbedJob(),Source,DeterministicJobExecutionMode.Scheduled,new DeterministicJobOptions(1,3));

        Assert.InRange(Result.MaximumConcurrency,1,3);
    }

    [Fact]
    public async Task InvalidModeIsRejectedForAnEmptyInput(){
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async()=>await DeterministicJobRuntime.ExecuteAsync<TransformJob,JobState,JobEvent>(
            SystemId,new TransformJob(),ReadOnlyMemory<JobState>.Empty,(DeterministicJobExecutionMode)byte.MaxValue,
            DeterministicJobOptions.Default).AsTask());
    }

    [Fact]
    public async Task CancellationDoesNotMutateTheInput(){
        var Source=Enumerable.Range(0,32).Select(static Value=>new JobState(Value,0)).ToArray();
        var Snapshot=Source.ToArray();
        using var Cancellation=new CancellationTokenSource();
        Cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await DeterministicJobRuntime.ExecuteAsync<TransformJob,JobState,JobEvent>(
            SystemId,new TransformJob(),Source,DeterministicJobExecutionMode.Scheduled,
            new DeterministicJobOptions(1,2),Cancellation.Token).AsTask());

        Assert.Equal(Snapshot,Source);
    }

    private static uint Hash(
        IReadOnlyList<JobState>States,IReadOnlyList<DeterministicJobEvent<JobEvent>>Events){
        var Hash=2166136261u;
        foreach(var State in States){ Hash=unchecked((Hash^(uint)State.Value)*16777619u);Hash=unchecked((Hash^(uint)State.Accumulated)*16777619u); }
        foreach(var Item in Events){ Hash=unchecked((Hash^(uint)Item.Value.Value)*16777619u);Hash=unchecked((Hash^(uint)Item.Key.PartitionId)*16777619u); }
        return Hash;
    }

    private readonly record struct JobState(int Value,int Accumulated);
    private readonly record struct JobEvent(int Value);
    private struct TransformJob:IDeterministicJob<JobState,JobEvent> {
        public JobState Execute(int Index,JobState State,ref DeterministicJobContext<JobEvent>Context){
            var Result=State with { Accumulated=unchecked(State.Value*31+Index) };
            if((Index&7)==0){ Context.Emit(new JobEvent(Result.Accumulated)); }
            return Result;
        }
    }
    private struct FailingJob:IDeterministicJob<JobState,JobEvent> {
        public JobState Execute(int Index,JobState State,ref DeterministicJobContext<JobEvent>Context){
            if(Index is 75 or 275){ throw new InvalidOperationException("injected"); }
            return State with { Accumulated=Index };
        }
    }
    private readonly struct ProbedJob:IDeterministicJob<JobState,JobEvent> {
        public JobState Execute(int Index,JobState State,ref DeterministicJobContext<JobEvent>Context){
            Thread.SpinWait(100_000);
            return State;
        }
    }
}
