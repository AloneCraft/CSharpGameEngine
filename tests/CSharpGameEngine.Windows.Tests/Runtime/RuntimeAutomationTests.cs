using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>The scripted key schedule that automation runs use to drive a game without a user.</summary>
public sealed class RuntimeAutomationTests {
    [Fact]
    public void FixedFrameTime_AdvancesTheCapEveryFrameWithoutBacklog(){
        // An automation run must not depend on how fast the adapter presents: each frame advances exactly the
        // per-frame cap (WARP happened to reach it every frame, a GPU does not).
        var Scheduler=new CSharpGameEngine.Runtime.Simulation.FixedTickScheduler();
        var (Elapsed,Frequency)=RuntimeAutomation.FixedFrameTime(Scheduler);
        for(var Frame=0;Frame<1000;Frame++){
            var Advance=Scheduler.Advance(Elapsed,Frequency);
            Assert.Equal(Scheduler.MaximumStepsPerFrame,Advance.Steps);
            Assert.False(Advance.IsClamped);
            Assert.Equal(0,Advance.BacklogSteps);
            Assert.Equal(0f,Advance.InterpolationAlpha);
        }
    }

    [Fact]
    public void FrameTime_IsTheMeasuredTimeWithoutAutomation(){
        if(Environment.GetEnvironmentVariable(RuntimeAutomation.FramesVariable) is { Length:>0 }){ return; }
        using var Host=new EngineHost();
        Assert.Null(Host.Automation);
        Assert.Equal((12345L,10_000_000L),Host.FrameTime(12345L,10_000_000L));
    }

    [Fact]
    public void TryParseInput_ReadsHoldsWithExclusiveEndFrames(){
        Assert.True(RuntimeAutomation.TryParseInput(" W@10-40 ; SpaceBar@45-46 ;",out var Holds,out var Error),Error);
        Assert.Equal([new RuntimeAutomationKeyHold("W",10,40),new RuntimeAutomationKeyHold("SpaceBar",45,46)],Holds);
        Assert.True(Holds[0].Contains(10));
        Assert.True(Holds[0].Contains(39));
        Assert.False(Holds[0].Contains(40));
    }
    [Fact]
    public void TryParseInput_AcceptsAnEmptySchedule(){
        Assert.True(RuntimeAutomation.TryParseInput(null,out var Holds,out _));
        Assert.Empty(Holds);
    }
    [Theory]
    [InlineData("W@10")]
    [InlineData("W@40-10")]
    [InlineData("W@5-5")]
    [InlineData("@1-2")]
    [InlineData("W@-1-2")]
    [InlineData("Space Bar@1-2")]
    public void TryParseInput_RejectsMalformedEntries(string Text){
        Assert.False(RuntimeAutomation.TryParseInput(Text,out _,out var Error));
        Assert.Contains(Text.Trim(),Error,StringComparison.Ordinal);
    }
}
