namespace CSharpGameEngine.Runtime.Simulation;

public enum FixedTickOverrunPolicy:byte {
    RetainBacklog,
}

public readonly record struct FixedTickAdvanceResult(
    int Steps,
    Int128 AttemptedSteps,
    bool IsClamped,
    bool ShouldReportOverrun,
    Int128 BacklogSteps,
    double InterpolationAlpha,
    FixedTickOverrunPolicy OverrunPolicy);

/// <summary>Integer-only fixed-step accumulator. Wall-clock values never enter a simulation domain.</summary>
public sealed class FixedTickScheduler {
    private readonly int ConfiguredTickRate;
    private readonly int ConfiguredMaximumStepsPerFrame;
    private Int128 AccumulatedNumerator;
    private bool WasClamped;

    public FixedTickScheduler(int TickRate=60,int MaximumStepsPerFrame=8){
        if(TickRate<=0){
            throw new ArgumentOutOfRangeException(nameof(TickRate));
        }
        if(MaximumStepsPerFrame<=0){
            throw new ArgumentOutOfRangeException(nameof(MaximumStepsPerFrame));
        }
        this.ConfiguredTickRate=TickRate;
        this.ConfiguredMaximumStepsPerFrame=MaximumStepsPerFrame;
    }

    public bool IsClamped { get;private set; }
    public double InterpolationAlpha { get;private set; }
    public int TickRate=>this.ConfiguredTickRate;
    public int MaximumStepsPerFrame=>this.ConfiguredMaximumStepsPerFrame;

    /// <summary>Consumes elapsed host counter units and returns the fixed ticks available for this host frame.</summary>
    public FixedTickAdvanceResult Advance(long ElapsedCounter,long CounterFrequency){
        if(ElapsedCounter<0){
            throw new ArgumentOutOfRangeException(nameof(ElapsedCounter));
        }
        if(CounterFrequency<=0){
            throw new ArgumentOutOfRangeException(nameof(CounterFrequency));
        }
        this.AccumulatedNumerator+=(Int128)ElapsedCounter*this.ConfiguredTickRate;
        var AttemptedSteps=this.AccumulatedNumerator/CounterFrequency;
        var Steps=AttemptedSteps>this.ConfiguredMaximumStepsPerFrame?this.ConfiguredMaximumStepsPerFrame:(int)AttemptedSteps;
        this.AccumulatedNumerator-=(Int128)Steps*CounterFrequency;
        this.IsClamped=AttemptedSteps>this.ConfiguredMaximumStepsPerFrame;
        var ShouldReportOverrun=this.IsClamped&&!this.WasClamped;
        this.WasClamped=this.IsClamped;
        this.InterpolationAlpha=this.AccumulatedNumerator>=CounterFrequency?1d:(double)this.AccumulatedNumerator/CounterFrequency;
        return new FixedTickAdvanceResult(Steps,AttemptedSteps,this.IsClamped,ShouldReportOverrun,this.AccumulatedNumerator/CounterFrequency,this.InterpolationAlpha,FixedTickOverrunPolicy.RetainBacklog);
    }

    public void Reset(){
        this.AccumulatedNumerator=0;
        this.IsClamped=false;
        this.WasClamped=false;
        this.InterpolationAlpha=0d;
    }
}
