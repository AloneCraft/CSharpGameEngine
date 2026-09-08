using CSharpGameEngine.Core;

namespace CSharpGameEngine.Entities;

public enum DeterministicJobExecutionMode:byte {
    SingleThread,
    Scheduled,
}

public readonly record struct DeterministicJobOptions(int PartitionSize,int MaximumWorkers) {
    public static DeterministicJobOptions Default=>new(256,Math.Max(1,Environment.ProcessorCount));
    internal void Validate(){
        if(this.PartitionSize<=0){ throw new ArgumentOutOfRangeException(nameof(this.PartitionSize)); }
        if(this.MaximumWorkers<=0){ throw new ArgumentOutOfRangeException(nameof(this.MaximumWorkers)); }
    }
}

public readonly record struct DeterministicJobKey(
    StableId System,int PartitionId,int WorkerLocalSequence,int CommandSequence):IComparable<DeterministicJobKey> {
    public int CompareTo(DeterministicJobKey Other){
        var Result=this.System.CompareTo(Other.System);
        if(Result!=0){ return Result; }
        Result=this.PartitionId.CompareTo(Other.PartitionId);
        if(Result!=0){ return Result; }
        Result=this.WorkerLocalSequence.CompareTo(Other.WorkerLocalSequence);
        return Result!=0?Result:this.CommandSequence.CompareTo(Other.CommandSequence);
    }
}

public readonly record struct DeterministicJobEvent<TEvent>(DeterministicJobKey Key,TEvent Value)where TEvent:unmanaged;

public struct DeterministicJobContext<TEvent>where TEvent:unmanaged {
    private readonly StableId System;
    private readonly int PartitionId;
    private readonly int WorkerLocalSequence;
    private readonly List<DeterministicJobEvent<TEvent>>Events;
    private int CommandSequence;
    internal DeterministicJobContext(
        StableId System,int PartitionId,int WorkerLocalSequence,List<DeterministicJobEvent<TEvent>>Events){
        this.System=System;
        this.PartitionId=PartitionId;
        this.WorkerLocalSequence=WorkerLocalSequence;
        this.Events=Events;
        this.CommandSequence=0;
    }
    public void Emit(TEvent Value){
        this.Events.Add(new(
            new DeterministicJobKey(this.System,this.PartitionId,this.WorkerLocalSequence,this.CommandSequence++),
            Value));
    }
}

public interface IDeterministicJob<TState,TEvent>
    where TState:unmanaged
    where TEvent:unmanaged {
    TState Execute(int Index,TState State,ref DeterministicJobContext<TEvent>Context);
}

public sealed class DeterministicJobException:Exception {
    internal DeterministicJobException(int PartitionId,int Index,Exception InnerException)
        :base($"Deterministic job failed in partition {PartitionId} at index {Index}.",InnerException){
        this.PartitionId=PartitionId;
        this.Index=Index;
    }
    public int PartitionId{get;}
    public int Index{get;}
}

public sealed class DeterministicJobResult<TState,TEvent>
    where TState:unmanaged
    where TEvent:unmanaged {
    internal DeterministicJobResult(TState[]States,DeterministicJobEvent<TEvent>[]Events,int MaximumConcurrency){
        this.States=States;
        this.Events=Events;
        this.MaximumConcurrency=MaximumConcurrency;
    }
    public TState[]States{get;}
    public DeterministicJobEvent<TEvent>[]Events{get;}
    public int MaximumConcurrency{get;}
}

/// <summary>Runs fixed partitions concurrently and exposes results only after a stable-key merge succeeds.</summary>
public static class DeterministicJobRuntime {
    public static async ValueTask<DeterministicJobResult<TState,TEvent>>ExecuteAsync<TJob,TState,TEvent>(
        StableId System,TJob Job,ReadOnlyMemory<TState>Source,
        DeterministicJobExecutionMode Mode,DeterministicJobOptions Options,
        CancellationToken CancellationToken=default)
        where TJob:unmanaged,IDeterministicJob<TState,TEvent>
        where TState:unmanaged
        where TEvent:unmanaged {
        if(!System.IsValid){ throw new ArgumentException("A stable system ID is required.",nameof(System)); }
        Options.Validate();
        if(Mode is not (DeterministicJobExecutionMode.SingleThread or DeterministicJobExecutionMode.Scheduled)){
            throw new ArgumentOutOfRangeException(nameof(Mode));
        }
        CancellationToken.ThrowIfCancellationRequested();
        var Output=Source.ToArray();
        if(Output.Length==0){ return new(Output,[],0); }
        var PartitionCount=checked((Output.Length+Options.PartitionSize-1)/Options.PartitionSize);
        var Partitions=new PartitionResult<TEvent>[PartitionCount];
        var ActiveWorkers=0;
        var MaximumConcurrency=Mode==DeterministicJobExecutionMode.SingleThread?1:0;
        if(Mode==DeterministicJobExecutionMode.SingleThread){
            for(var PartitionId=0;PartitionId<PartitionCount;PartitionId++){
                CancellationToken.ThrowIfCancellationRequested();
                Partitions[PartitionId]=RunPartition<TJob,TState,TEvent>(System,Job,Output,PartitionId,Options.PartitionSize,CancellationToken);
            }
        } else if(Mode==DeterministicJobExecutionMode.Scheduled){
            var WorkerCount=Math.Min(Options.MaximumWorkers,PartitionCount);
            var NextPartition=-1;
            var Tasks=new Task[WorkerCount];
            for(var WorkerIndex=0;WorkerIndex<WorkerCount;WorkerIndex++){
                Tasks[WorkerIndex]=Task.Run(RunWorker,CancellationToken);
            }
            await Task.WhenAll(Tasks).ConfigureAwait(false);

            void RunWorker(){
                var Current=Interlocked.Increment(ref ActiveWorkers);
                UpdateMaximum(ref MaximumConcurrency,Current);
                try {
                    while(true){
                        CancellationToken.ThrowIfCancellationRequested();
                        var PartitionId=Interlocked.Increment(ref NextPartition);
                        if(PartitionId>=PartitionCount){ return; }
                        Partitions[PartitionId]=RunPartition<TJob,TState,TEvent>(
                            System,Job,Output,PartitionId,Options.PartitionSize,CancellationToken);
                    }
                } finally { Interlocked.Decrement(ref ActiveWorkers); }
            }
        }
        CancellationToken.ThrowIfCancellationRequested();
        for(var PartitionId=0;PartitionId<Partitions.Length;PartitionId++){
            if(Partitions[PartitionId].Failure is { } Failure){
                throw new DeterministicJobException(PartitionId,Failure.Index,Failure.Exception);
            }
        }
        var EventCount=Partitions.Sum(static Partition=>Partition.Events.Count);
        var Events=new DeterministicJobEvent<TEvent>[EventCount];
        var EventIndex=0;
        foreach(var Partition in Partitions){
            Partition.Events.CopyTo(Events,EventIndex);
            EventIndex+=Partition.Events.Count;
        }
        return new(Output,Events,MaximumConcurrency);
    }

    private static void UpdateMaximum(ref int Maximum,int Candidate){
        int Existing;
        while(Candidate>(Existing=Volatile.Read(ref Maximum))
            &&Interlocked.CompareExchange(ref Maximum,Candidate,Existing)!=Existing){ }
    }

    private static PartitionResult<TEvent>RunPartition<TJob,TState,TEvent>(
        StableId System,TJob Job,TState[]Output,int PartitionId,int PartitionSize,
        CancellationToken CancellationToken)
        where TJob:unmanaged,IDeterministicJob<TState,TEvent>
        where TState:unmanaged
        where TEvent:unmanaged {
        var Start=checked(PartitionId*PartitionSize);
        var End=Math.Min(Output.Length,checked(Start+PartitionSize));
        var Events=new List<DeterministicJobEvent<TEvent>>();
        for(var Index=Start;Index<End;Index++){
            try {
                CancellationToken.ThrowIfCancellationRequested();
                var Context=new DeterministicJobContext<TEvent>(System,PartitionId,Index-Start,Events);
                Output[Index]=Job.Execute(Index,Output[Index],ref Context);
            }catch(OperationCanceledException){ throw; }
            catch(Exception Exception){ return new(Events,new PartitionFailure(Index,Exception)); }
        }
        return new(Events,null);
    }

    private sealed record PartitionFailure(int Index,Exception Exception);
    private sealed record PartitionResult<TEvent>(
        List<DeterministicJobEvent<TEvent>>Events,PartitionFailure?Failure)where TEvent:unmanaged;
}
