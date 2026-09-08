using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Bridge.Hosting.Tests;

public class AuthoringHostSessionTests {
    [Fact]
    public async Task Run_Success_CorrelatesProgressAndResult(){
        var Request=CreateRequest();

        var Result=await AuthoringHostSession.RunAsync(
            Request,
            (Current,Progress,_)=>{
                Progress(new AuthoringHostProgress {
                    RequestId=Current.RequestId,Sequence=1,Fraction=0.5f,Phase="Importing",
                });
                return Task.FromResult(new AuthoringHostExecutionResult(0,"ok"));
            });

        Assert.Equal(AuthoringHostTerminalState.Succeeded,Result.State);
        Assert.Equal(Request.RequestId,Result.RequestId);
        Assert.Equal(1f,Result.Progress!.Fraction);
        Assert.Equal(2UL,Result.Progress.Sequence);
    }

    [Fact]
    public async Task Run_VersionMismatch_DoesNotExecute(){
        var Request=CreateRequest();
        Request.ProtocolVersion++;
        var Calls=0;

        var Result=await AuthoringHostSession.RunAsync(Request,(_,_,_)=>{ Calls++;return Task.FromResult(new AuthoringHostExecutionResult());});

        Assert.Equal(AuthoringHostTerminalState.VersionMismatch,Result.State);
        Assert.Equal(0,Calls);
    }

    [Fact]
    public async Task Run_Timeout_IsTerminalAndExecutesOnce(){
        var Request=CreateRequest();
        Request.TimeoutMilliseconds=25;
        var Calls=0;

        var Result=await AuthoringHostSession.RunAsync(
            Request,
            async(_,_,CancellationToken)=>{
                Calls++;
                await Task.Delay(Timeout.InfiniteTimeSpan,CancellationToken);
                return new AuthoringHostExecutionResult();
            });

        Assert.Equal(AuthoringHostTerminalState.TimedOut,Result.State);
        Assert.Equal(1,Calls);
    }

    [Fact]
    public async Task Run_ExternalCancellation_IsDistinctFromTimeout(){
        var Request=CreateRequest();
        using CancellationTokenSource Cancellation=new();
        Cancellation.Cancel();

        var Result=await AuthoringHostSession.RunAsync(
            Request,
            async(_,_,Token)=>{
                await Task.Delay(Timeout.InfiniteTimeSpan,Token);
                return new AuthoringHostExecutionResult();
            },Cancellation.Token);

        Assert.Equal(AuthoringHostTerminalState.Cancelled,Result.State);
    }

    [Fact]
    public async Task Run_Crash_IsTypedTerminalState(){
        var Result=await AuthoringHostSession.RunAsync(
            CreateRequest(),
            (_,_,_)=>throw new AuthoringHostCrashedException("host process exited"));

        Assert.Equal(AuthoringHostTerminalState.HostCrashed,Result.State);
        Assert.Equal("HOST_CRASHED",Assert.Single(Result.Diagnostics).Code);
    }

    [Fact]
    public async Task Run_DecreasingProgress_FailsRequest(){
        var Request=CreateRequest();
        var Result=await AuthoringHostSession.RunAsync(
            Request,
            (Current,Progress,_)=>{
                Progress(new AuthoringHostProgress { RequestId=Current.RequestId,Sequence=1,Fraction=0.75f,Phase="Bake" });
                Progress(new AuthoringHostProgress { RequestId=Current.RequestId,Sequence=2,Fraction=0.5f,Phase="Bake" });
                return Task.FromResult(new AuthoringHostExecutionResult());
            });

        Assert.Equal(AuthoringHostTerminalState.Failed,Result.State);
        Assert.Equal("HOST_PROGRESS_INVALID",Assert.Single(Result.Diagnostics).Code);
    }

    private static AuthoringHostRequest CreateRequest()=>new(){
        RequestId=Guid.NewGuid(),
        Operation=AuthoringHostOperation.Bake,
        TimeoutMilliseconds=5_000,
        Arguments=["input","output"],
    };
}
