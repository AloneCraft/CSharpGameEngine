namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetViewportRecoveryTests {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefaultIsReportedOnlyAfterSuccessfulLoading(bool LoadSucceeded){
        var Recovery=new AssetViewportRecovery();
        var Events=new List<string>();
        var Recovered=Recovery.TryRecover(false,false,
            ()=>throw new InvalidOperationException("not a transport failure"),
            ()=>{ Events.Add("load");return LoadSucceeded; },
            ()=>Events.Add("applied"));
        Assert.Equal(LoadSucceeded,Recovered);
        Assert.Equal(LoadSucceeded?new[] { "load","applied" }:new[] { "load" },Events);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATransportFailureGetsOneFreshSurfaceAttemptAndNeverChangesTheTemplate(bool LoadSucceeded){
        var Recovery=new AssetViewportRecovery();
        var Attempts=0;
        Func<bool>Reload=()=>{ Attempts++;return LoadSucceeded; };
        Func<bool>WrongFallback=()=>throw new InvalidOperationException("must keep the template");
        Action WrongNotification=()=>throw new InvalidOperationException("no fallback was applied");
        Assert.Equal(LoadSucceeded,Recovery.TryRecover(true,false,Reload,WrongFallback,WrongNotification));
        Assert.False(Recovery.TryRecover(true,false,Reload,WrongFallback,WrongNotification));
        Assert.Equal(1,Attempts);
        Recovery.Reset();
        Assert.Equal(LoadSucceeded,Recovery.TryRecover(true,false,Reload,WrongFallback,WrongNotification));
        Assert.Equal(2,Attempts);
    }
    [Fact]
    public void FailureInDefaultDoesNotRetryTheSameTemplate(){
        var Recovery=new AssetViewportRecovery();
        Assert.False(Recovery.TryRecover(false,true,
            ()=>throw new InvalidOperationException(),()=>throw new InvalidOperationException(),()=>throw new InvalidOperationException()));
    }
}
