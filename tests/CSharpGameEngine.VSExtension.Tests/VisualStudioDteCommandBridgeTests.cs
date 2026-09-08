using CSharpGameEngine.Vsix.Automation;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class VisualStudioDteCommandBridgeTests {
    [Theory]
    [InlineData("Open in Scene Designer")]
    [InlineData("シーン デザイナーで開く")]
    public void ToolbarCommandName_AcceptsSupportedLocales(string Name) {
        Assert.True(VsixExperimentalScenario.IsSceneDesignerToolbarButtonName(Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Show Hierarchy")]
    public void ToolbarCommandName_RejectsOtherButtons(string?Name) {
        Assert.False(VsixExperimentalScenario.IsSceneDesignerToolbarButtonName(Name));
    }

    [Theory]
    [InlineData(2,0,100)]
    [InlineData(2,1999,100)]
    [InlineData(2,2000,-1)]
    [InlineData(1,0,-1)]
    [InlineData(0,0,-1)]
    public void OleMessageFilter_RetriesOnlyRetryLaterCalls(
        int RejectType,int ElapsedMilliseconds,int ExpectedDelay) {
        Assert.Equal(ExpectedDelay,OleMessageFilter.GetRetryDelay(
            RejectType,ElapsedMilliseconds));
    }

    [Theory]
    [InlineData("!VisualStudio.DTE.18.0:1234",1234)]
    [InlineData("VisualStudio.DTE.17.0:42",42)]
    public void ProcessMoniker_RequiresExactProcessSuffix(
        string DisplayName,int ProcessId) {
        Assert.True(VisualStudioDteCommandBridge.IsProcessMoniker(
            DisplayName,ProcessId));
        Assert.False(VisualStudioDteCommandBridge.IsProcessMoniker(
            DisplayName,ProcessId+1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Other.Application:1234")]
    [InlineData("VisualStudio.DTE.18.0:12345")]
    public void ProcessMoniker_RejectsNonVisualStudioOrPrefixCollision(
        string?DisplayName) {
        Assert.False(VisualStudioDteCommandBridge.IsProcessMoniker(
            DisplayName,1234));
    }
}
