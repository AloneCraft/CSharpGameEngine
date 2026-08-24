namespace CSharpGameEngine.Runtime.Simulation;

/// <summary>Integer-only fixed-step accumulator. Wall-clock values never enter a simulation domain.</summary>
public sealed class FixedTickScheduler {
    private readonly int TickRate;
    private readonly int MaximumStepsPerFrame;
    private Int128 AccumulatedNumerator;

    public FixedTickScheduler(int TickRate=60,int MaximumStepsPerFrame=8){
        if(TickRate<=0){
            throw new ArgumentOutOfRangeException(nameof(TickRate));
        }
        if(MaximumStepsPerFrame<=0){
            throw new ArgumentOutOfRangeException(nameof(MaximumStepsPerFrame));
        }
        this.TickRate=TickRate;
        this.MaximumStepsPerFrame=MaximumStepsPerFrame;
    }

    public ulong NextTick { get;private set; }
    public bool IsClamped { get;private set; }

    /// <summary>Consumes elapsed host counter units and returns the number of fixed ticks to run.</summary>
    public int Advance(long ElapsedCounter,long CounterFrequency){
        if(ElapsedCounter<0){
            throw new ArgumentOutOfRangeException(nameof(ElapsedCounter));
        }
        if(CounterFrequency<=0){
            throw new ArgumentOutOfRangeException(nameof(CounterFrequency));
        }
        this.AccumulatedNumerator+=(Int128)ElapsedCounter*this.TickRate;
        Int128 Available=this.AccumulatedNumerator/CounterFrequency;
        int Steps=Available>this.MaximumStepsPerFrame?this.MaximumStepsPerFrame:(int)Available;
        this.IsClamped=Available>this.MaximumStepsPerFrame;
        this.AccumulatedNumerator-=(Int128)Steps*CounterFrequency;
        this.NextTick+=checked((ulong)Steps);
        return Steps;
    }

    public void Reset(ulong NextTick=0){
        this.NextTick=NextTick;
        this.AccumulatedNumerator=0;
        this.IsClamped=false;
    }
}
