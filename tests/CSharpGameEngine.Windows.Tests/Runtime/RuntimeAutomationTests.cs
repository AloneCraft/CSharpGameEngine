using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>The scripted key schedule that automation runs use to drive a game without a user.</summary>
public sealed class RuntimeAutomationTests {
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
