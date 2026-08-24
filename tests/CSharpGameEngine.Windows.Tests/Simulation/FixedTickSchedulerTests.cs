using CSharpGameEngine.Runtime.Simulation;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class FixedTickSchedulerTests {
    [Fact]
    public void Advance_AccumulatesWholeTicksAndKeepsRemainder(){
        FixedTickScheduler Scheduler=new(60,4);

        Assert.Equal(0,Scheduler.Advance(8_000,1_000_000));
        Assert.Equal(1,Scheduler.Advance(9_000,1_000_000));
        Assert.Equal(1UL,Scheduler.NextTick);
        Assert.Equal(4,Scheduler.Advance(100_000,1_000_000));
        Assert.True(Scheduler.IsClamped);
        Assert.Equal(5UL,Scheduler.NextTick);
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
