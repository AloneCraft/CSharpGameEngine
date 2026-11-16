using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneSubsystemStartupRetryTests {
    [Fact]
    public async Task WorkspaceUnavailableAtPackageLoad_RetriesUntilReady(){
        var Attempts=0;
        var Delays=0;

        var Ready=await SceneSubsystemStartupRetry.WaitAsync(
            ()=>Task.FromResult(++Attempts==3),
            MaximumAttempts:5,
            Delay:TimeSpan.FromSeconds(1),
            (Duration,CancellationToken)=>{
                Delays++;
                Assert.Equal(TimeSpan.FromSeconds(1),Duration);
                Assert.False(CancellationToken.IsCancellationRequested);
                return Task.CompletedTask;
            },CancellationToken.None);

        Assert.True(Ready);
        Assert.Equal(3,Attempts);
        Assert.Equal(2,Delays);
    }

    [Fact]
    public async Task WorkspaceNeverAvailable_StopsAtTheBound(){
        var Attempts=0;
        var Delays=0;

        var Ready=await SceneSubsystemStartupRetry.WaitAsync(
            ()=>{
                Attempts++;
                return Task.FromResult(false);
            },
            MaximumAttempts:4,
            Delay:TimeSpan.Zero,
            (Duration,CancellationToken)=>{
                Delays++;
                return Task.CompletedTask;
            },CancellationToken.None);

        Assert.False(Ready);
        Assert.Equal(4,Attempts);
        Assert.Equal(3,Delays);
    }
}
