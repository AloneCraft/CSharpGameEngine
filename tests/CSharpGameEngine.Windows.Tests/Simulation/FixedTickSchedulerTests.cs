using CSharpGameEngine.Runtime.Simulation;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class FixedTickSchedulerTests {
    [Fact]
    public void Advance_AccumulatesWholeTicksAndKeepsRemainder(){
        var Scheduler=new FixedTickScheduler(60,4);
        var BeforeTick=Scheduler.Advance(8_000,1_000_000);
        var OneTick=Scheduler.Advance(8_667,1_000_000);
        Assert.Equal(0,BeforeTick.Steps);
        Assert.Equal(1,OneTick.Steps);
        Assert.Equal((Int128)1,OneTick.AttemptedSteps);
        Assert.False(OneTick.IsClamped);
        Assert.InRange(OneTick.InterpolationAlpha,0d,1d);
    }

    [Fact]
    public void Advance_ClampsAtEightAndRetainsBacklogUntilItCatchesUp(){
        var Scheduler=new FixedTickScheduler();
        var Result=Scheduler.Advance(1_000_000,1_000_000);
        Assert.Equal(8,Result.Steps);
        Assert.Equal((Int128)60,Result.AttemptedSteps);
        Assert.True(Result.IsClamped);
        Assert.True(Result.ShouldReportOverrun);
        Assert.Equal(FixedTickOverrunPolicy.RetainBacklog,Result.OverrunPolicy);
        var SameClamp=Scheduler.Advance(0,1_000_000);
        Assert.Equal(8,SameClamp.Steps);
        Assert.False(SameClamp.ShouldReportOverrun);
        Assert.Equal((Int128)52,SameClamp.AttemptedSteps);
        Assert.True(SameClamp.IsClamped);
        Assert.True(Scheduler.InterpolationAlpha>0d);
    }

    [Fact]
    public void Advance_ReportsClampAgainAfterBacklogReturnsToNormal(){
        var Scheduler=new FixedTickScheduler();
        Assert.True(Scheduler.Advance(1_000_000,1_000_000).ShouldReportOverrun);
        while(Scheduler.IsClamped){
            Scheduler.Advance(0,1_000_000);
        }
        var Result=Scheduler.Advance(1_000_000,60_000_000);
        Assert.False(Result.IsClamped);
        Assert.False(Result.ShouldReportOverrun);
        var Overrun=Scheduler.Advance(1_000_000,1_000_000);
        Assert.True(Overrun.IsClamped);
        Assert.True(Overrun.ShouldReportOverrun);
    }

    [Fact]
    public void Reset_ClearsAccumulator(){
        var Scheduler=new FixedTickScheduler();
        Scheduler.Advance(1_000_000,1_000_000);
        Scheduler.Reset();
        var Result=Scheduler.Advance(1_000,60_000);
        Assert.Equal(1,Result.Steps);
        Assert.False(Result.IsClamped);
    }

    [Fact]
    public void Commands_AreCommittedInStableDeterministicOrder(){
        SimulationCommandBuffer<string> Commands=new();
        Commands.Enqueue(new SimulationCommand<string>(2,9,0,"late"));
        Commands.Enqueue(new SimulationCommand<string>(1,8,1,"second"));
        Commands.Enqueue(new SimulationCommand<string>(1,8,0,"first"));

        List<SimulationCommand<string>> Committed=[];
        Assert.Equal(2,Commands.Commit(1,Committed));
        Assert.Equal(["first","second"],Committed.Select(Command=>Command.Payload));
        Assert.Single(Commands.Pending);
    }
}
