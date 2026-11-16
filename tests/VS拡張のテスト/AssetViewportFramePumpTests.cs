using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.VSExtension;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetViewportFramePumpTests {
    [Fact]
    public void SlowFrameDoesNotBlockSubmitAndOnlyTheLatestWaitingFrameIsRendered(){
        using var FirstEntered=new ManualResetEventSlim();
        using var ReleaseFirst=new ManualResetEventSlim();
        using var TwoRenders=new ManualResetEventSlim();
        var Times=new List<float>();
        var RenderCount=0;
        using var Client=ReadyClient(Request=>{
            if(Request.Command==RenderPreviewHostCommand.UpdateFrame){
                lock(Times){ Times.Add(Request.Frame!.Animation!.TimeSeconds); }
                if(Request.Frame!.Animation!.TimeSeconds==1f){
                    FirstEntered.Set();
                    Assert.True(ReleaseFirst.Wait(TimeSpan.FromSeconds(10)));
                }
            }
            if(Request.Command==RenderPreviewHostCommand.Render&&Interlocked.Increment(ref RenderCount)==2){ TwoRenders.Set(); }
        });
        using var Pump=new AssetViewportFramePump(Client,_=>throw new InvalidOperationException("Unexpected failure."));
        Assert.True(Pump.Submit(Frame(1f)));
        Assert.True(FirstEntered.Wait(TimeSpan.FromSeconds(10)));
        var Started=System.Diagnostics.Stopwatch.StartNew();
        Assert.True(Pump.Submit(Frame(2f)));
        Assert.True(Pump.Submit(Frame(3f)));
        Assert.True(Started.Elapsed<TimeSpan.FromSeconds(5));
        ReleaseFirst.Set();
        Assert.True(TwoRenders.Wait(TimeSpan.FromSeconds(10)));
        lock(Times){ Assert.Equal([1f,3f],Times); }
        Assert.Equal(2,Volatile.Read(ref RenderCount));
    }
    [Fact]
    public void SceneABAChangesRemainOrderedWithTheirFrames(){
        using var FirstReplaceEntered=new ManualResetEventSlim();
        using var ReleaseFirstReplace=new ManualResetEventSlim();
        using var TwoRenders=new ManualResetEventSlim();
        var Requests=new List<string>();
        var RenderCount=0;
        using var Client=ReadyClient(Request=>{
            switch(Request.Command){
                case RenderPreviewHostCommand.ReplaceScene:
                    var Name=Request.Scene!.Objects[0].ObjectId;
                    lock(Requests){ Requests.Add("Scene:"+Name); }
                    if(Name=="B"){
                        FirstReplaceEntered.Set();
                        Assert.True(ReleaseFirstReplace.Wait(TimeSpan.FromSeconds(10)));
                    }
                    break;
                case RenderPreviewHostCommand.UpdateFrame:
                    lock(Requests){ Requests.Add("Frame:"+Request.Frame!.Animation!.TimeSeconds); }
                    break;
                case RenderPreviewHostCommand.Render:
                    if(Interlocked.Increment(ref RenderCount)==2){ TwoRenders.Set(); }
                    break;
            }
        });
        using var Pump=new AssetViewportFramePump(Client,_=>throw new InvalidOperationException("Unexpected failure."));
        Pump.SetScene(Scene("B"));
        Assert.True(Pump.Submit(Frame(1f)));
        Assert.True(FirstReplaceEntered.Wait(TimeSpan.FromSeconds(10)));
        Pump.SetScene(Scene("A"));
        Assert.True(Pump.Submit(Frame(2f)));
        ReleaseFirstReplace.Set();
        Assert.True(TwoRenders.Wait(TimeSpan.FromSeconds(10)));
        lock(Requests){ Assert.Equal(["Scene:B","Frame:1","Scene:A","Frame:2"],Requests); }
    }
    [Fact]
    public void ASceneChangedAfterSubmitCannotBePairedWithTheOlderFrame(){
        using var FirstEntered=new ManualResetEventSlim();
        using var ReleaseFirst=new ManualResetEventSlim();
        using var LaterRendered=new ManualResetEventSlim();
        var Requests=new List<string>();
        using var Client=ReadyClient(Request=>{
            if(Request.Command==RenderPreviewHostCommand.UpdateFrame){
                var Time=Request.Frame!.Animation!.TimeSeconds;
                if(Time==0f){
                    FirstEntered.Set();
                    Assert.True(ReleaseFirst.Wait(TimeSpan.FromSeconds(10)));
                } else { lock(Requests){ Requests.Add("Frame:"+Time); } }
            } else if(Request.Command==RenderPreviewHostCommand.ReplaceScene){
                lock(Requests){ Requests.Add("Scene:"+Request.Scene!.Objects[0].ObjectId); }
            } else if(Request.Command==RenderPreviewHostCommand.Render){
                lock(Requests){ if(Requests.Count>=2){ LaterRendered.Set(); } }
            }
        });
        using var Pump=new AssetViewportFramePump(Client,_=>throw new InvalidOperationException("Unexpected failure."));
        Assert.True(Pump.Submit(Frame(0f)));
        Assert.True(FirstEntered.Wait(TimeSpan.FromSeconds(10)));
        Pump.SetScene(Scene("B"));
        Assert.True(Pump.Submit(Frame(1f)));
        Pump.SetScene(Scene("C"));
        ReleaseFirst.Set();
        Assert.True(LaterRendered.Wait(TimeSpan.FromSeconds(10)));
        lock(Requests){ Assert.Equal(["Scene:B","Frame:1"],Requests); }
        Assert.True(Pump.Submit(Frame(2f)));
        Assert.True(SpinWait.SpinUntil(()=>{
            lock(Requests){ return Requests.Count==4; }
        },TimeSpan.FromSeconds(10)));
        lock(Requests){ Assert.Equal(["Scene:B","Frame:1","Scene:C","Frame:2"],Requests); }
    }
    [Fact]
    public void ResizeSceneAndLatestFrameAreAppliedInThatOrder(){
        using var FirstEntered=new ManualResetEventSlim();
        using var ReleaseFirst=new ManualResetEventSlim();
        using var LastRendered=new ManualResetEventSlim();
        var Requests=new List<string>();
        using var Client=ReadyClient(Request=>{
            if(Request.Command==RenderPreviewHostCommand.UpdateFrame&&Request.Frame!.Animation!.TimeSeconds==0f){
                FirstEntered.Set();
                Assert.True(ReleaseFirst.Wait(TimeSpan.FromSeconds(10)));
                return;
            }
            lock(Requests){
                switch(Request.Command){
                    case RenderPreviewHostCommand.Resize:
                        Requests.Add($"Resize:{Request.Width}x{Request.Height}");
                        break;
                    case RenderPreviewHostCommand.ReplaceScene:
                        Requests.Add("Scene:"+Request.Scene!.Objects[0].ObjectId);
                        break;
                    case RenderPreviewHostCommand.UpdateFrame:
                        Requests.Add("Frame:"+Request.Frame!.Animation!.TimeSeconds);
                        break;
                    case RenderPreviewHostCommand.Render when Requests.Count>=3:
                        LastRendered.Set();
                        break;
                }
            }
        });
        using var Pump=new AssetViewportFramePump(Client,_=>throw new InvalidOperationException("Unexpected failure."));
        Assert.True(Pump.Submit(Frame(0f)));
        Assert.True(FirstEntered.Wait(TimeSpan.FromSeconds(10)));
        Pump.SetScene(Scene("B"));
        Assert.True(Pump.Submit(Frame(1f),(100,100)));
        Assert.True(Pump.Submit(Frame(2f),(200,150)));
        ReleaseFirst.Set();
        Assert.True(LastRendered.Wait(TimeSpan.FromSeconds(10)));
        lock(Requests){ Assert.Equal(["Resize:200x150","Scene:B","Frame:2"],Requests); }
    }
    [Fact]
    public void DisposalDuringAnInFlightFrameAbortsTheOwnedTransportOnce(){
        using var Entered=new ManualResetEventSlim();
        using var Released=new ManualResetEventSlim();
        using var WorkerDone=new ManualResetEventSlim();
        var Aborts=0;
        var Closes=0;
        var Failures=0;
        using var Client=ReadyClient(Request=>{
            if(Request.Command!=RenderPreviewHostCommand.UpdateFrame){ return; }
            Entered.Set();
            Assert.True(Released.Wait(TimeSpan.FromSeconds(10)));
            WorkerDone.Set();
            throw new IOException("owned host stopped");
        },()=>Interlocked.Increment(ref Closes),()=>{
            Interlocked.Increment(ref Aborts);
            Released.Set();
        });
        using var Pump=new AssetViewportFramePump(Client,_=>Interlocked.Increment(ref Failures));
        Assert.True(Pump.Submit(Frame(1f)));
        Assert.True(Entered.Wait(TimeSpan.FromSeconds(10)));
        Pump.Dispose();
        Assert.True(WorkerDone.Wait(TimeSpan.FromSeconds(10)));
        Assert.False(Pump.Submit(Frame(2f)));
        Assert.Equal(1,Volatile.Read(ref Aborts));
        Assert.Equal(0,Volatile.Read(ref Closes));
        Assert.Equal(0,Volatile.Read(ref Failures));
    }
    [Fact]
    public void DisposalWaitsForAnActiveFailureNotification(){
        using var FailureEntered=new ManualResetEventSlim();
        using var ReleaseFailure=new ManualResetEventSlim();
        using var DisposeStarted=new ManualResetEventSlim();
        using var DisposeEnded=new ManualResetEventSlim();
        using var Client=ReadyClient(Request=>{
            if(Request.Command==RenderPreviewHostCommand.UpdateFrame){ throw new IOException("preview frame failed"); }
        });
        using var Pump=new AssetViewportFramePump(Client,_=>{
            FailureEntered.Set();
            Assert.True(ReleaseFailure.Wait(TimeSpan.FromSeconds(10)));
        });
        Assert.True(Pump.Submit(Frame(1f)));
        Assert.True(FailureEntered.Wait(TimeSpan.FromSeconds(10)));
        var Disposer=new Thread(()=>{
            DisposeStarted.Set();
            Pump.Dispose();
            DisposeEnded.Set();
        });
        Disposer.Start();
        try {
            Assert.True(DisposeStarted.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(SpinWait.SpinUntil(()=>DisposeEnded.IsSet||(Disposer.ThreadState&ThreadState.WaitSleepJoin)!=0,TimeSpan.FromSeconds(10)));
            Assert.False(DisposeEnded.IsSet);
        } finally { ReleaseFailure.Set(); }
        Assert.True(DisposeEnded.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(Disposer.Join(TimeSpan.FromSeconds(10)));
    }
    private static RenderPreviewHostClient ReadyClient(Action<RenderPreviewHostRequest> OnRequest,Action? Dispose=null,Action? Abort=null){
        var Client=new RenderPreviewHostClient(Guid.NewGuid(),Request=>{
            OnRequest(Request);
            return new RenderPreviewHostResponse {
                RequestId=Request.RequestId,SessionId=Request.SessionId,Sequence=Request.Sequence,
                State=RenderPreviewHostResultState.Succeeded,
            };
        },Dispose??(()=>{}),AbortTransport:Abort);
        Client.Open(123,64,48,"Stage.cgeshaderartifact",Scene("A"));
        return Client;
    }
    private static RenderPreviewFrame Frame(float Time)=>new(){ Animation=new RenderPreviewAnimation { ClipIndex=0,TimeSeconds=Time } };
    private static RenderPreviewScene Scene(string Name)=>new(){ Objects=[new RenderPreviewObject { ObjectId=Name }] };
}
