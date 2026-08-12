using System.Collections.Concurrent;
using System.Threading;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class LatestOnlyThreadWorkerTests {
    [Fact]
    public void Request_DoesNotWaitForWorkToFinish(){
        using ManualResetEventSlim WorkStarted=new(initialState:false);
        using ManualResetEventSlim ReleaseWork=new(initialState:false);
        using LatestOnlyThreadWorker<int,string> Worker=new(
            Work:Value=>{
                WorkStarted.Set();
                ReleaseWork.Wait();
                return Value.ToString();
            },
            Dispatch:Callback=>Callback(),
            Completed:(_,_,_)=>{ });

        Worker.Request(1);

        Assert.True(WorkStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(ReleaseWork.IsSet);
        ReleaseWork.Set();
    }

    [Fact]
    public void NewRequestWhileWorking_OnlyCompletesTheLatestRequest(){
        using ManualResetEventSlim FirstStarted=new(initialState:false);
        using ManualResetEventSlim ReleaseFirst=new(initialState:false);
        using ManualResetEventSlim Completed=new(initialState:false);
        ConcurrentQueue<string> Results=new();
        using LatestOnlyThreadWorker<int,string> Worker=new(
            Work:Value=>{
                if(Value==1){
                    FirstStarted.Set();
                    ReleaseFirst.Wait();
                }

                return $"Result{Value}";
            },
            Dispatch:Callback=>Callback(),
            Completed:(_,Result,Error)=>{
                Assert.Null(Error);
                Results.Enqueue(Result!);
                Completed.Set();
            });

        Worker.Request(1);
        Assert.True(FirstStarted.Wait(TimeSpan.FromSeconds(5)));
        Worker.Request(2);
        ReleaseFirst.Set();

        Assert.True(Completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(["Result2"],Results);
    }

    [Fact]
    public void WorkThrows_CompletesWithTheOriginalError(){
        using ManualResetEventSlim Completed=new(initialState:false);
        Exception? Observed=null;
        using LatestOnlyThreadWorker<int,string> Worker=new(
            Work:_=>throw new InvalidOperationException("bridge failed"),
            Dispatch:Callback=>Callback(),
            Completed:(_,Result,Error)=>{
                Assert.Null(Result);
                Observed=Error;
                Completed.Set();
            });

        Worker.Request(1);

        Assert.True(Completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(Observed);
        Assert.Equal("bridge failed",Observed!.Message);
    }

    [Fact]
    public void DisposeWhileWorking_DropsTheCompletion(){
        using ManualResetEventSlim WorkStarted=new(initialState:false);
        using ManualResetEventSlim ReleaseWork=new(initialState:false);
        using ManualResetEventSlim Completed=new(initialState:false);
        LatestOnlyThreadWorker<int,string> Worker=new(
            Work:Value=>{
                WorkStarted.Set();
                ReleaseWork.Wait();
                return Value.ToString();
            },
            Dispatch:Callback=>Callback(),
            Completed:(_,_,_)=>Completed.Set());

        Worker.Request(1);
        Assert.True(WorkStarted.Wait(TimeSpan.FromSeconds(5)));
        Worker.Dispose();
        ReleaseWork.Set();

        Assert.False(Completed.Wait(TimeSpan.FromMilliseconds(250)));
    }
}
