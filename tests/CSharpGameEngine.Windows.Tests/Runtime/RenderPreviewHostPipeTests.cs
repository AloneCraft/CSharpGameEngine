using System.IO.Pipes;
using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.EditorPreview.Host;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Windows.Tests.Runtime;

public sealed class RenderPreviewHostPipeTests {
    [Fact]
    public async Task NamedPipe_CorrelatesVersionedRequestAndDisposesOnShutdown(){
        string PipeName=$"CSharpGameEngine.Preview.Test.{Guid.NewGuid():N}";
        FakeSession?Owned=null;
        using CancellationTokenSource Stop=new();
        Task Server=RenderPreviewHostServer.RunAsync(
            PipeName,_=>Owned=new FakeSession(),Stop.Token);
        RenderPreviewHostRequest Open=RenderPreviewHostSessionTableTests.CreateOpenForPipe();

        RenderPreviewHostResponse Opened=await SendAsync(PipeName,Open);
        Assert.Equal(Open.RequestId,Opened.RequestId);
        Assert.Equal(Open.SessionId,Opened.SessionId);
        Assert.Equal(RenderPreviewHostResultState.Succeeded,Opened.State);

        RenderPreviewHostRequest Render=new(){
            RequestId=Guid.NewGuid(),
            SessionId=Open.SessionId,
            Sequence=2,
            Command=RenderPreviewHostCommand.Render,
        };
        Assert.Equal(RenderPreviewHostResultState.Succeeded,
            (await SendAsync(PipeName,Render)).State);
        Assert.Equal(1,Owned!.RenderCalls);

        RenderPreviewHostRequest SnapshotRequest=new(){
            RequestId=Guid.NewGuid(),SessionId=Open.SessionId,Sequence=3,
            Command=RenderPreviewHostCommand.Snapshot,
        };
        RenderPreviewHostResponse SnapshotResponse=
            await SendAsync(PipeName,SnapshotRequest);
        Assert.Equal(RenderPreviewHostResultState.Succeeded,SnapshotResponse.State);
        Assert.Null(RenderPreviewHostProtocol.ValidateDebugSnapshot(
            SnapshotResponse.DebugSnapshot));
        Assert.Equal(1,Owned.SnapshotCalls);
        Assert.Equal(1,Owned.RenderCalls);

        Stop.Cancel();
        await Server.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(Owned.Disposed);
    }

    private static async Task<RenderPreviewHostResponse>SendAsync(
        string PipeName,RenderPreviewHostRequest Request){
        using NamedPipeClientStream Pipe=new(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        await Pipe.ConnectAsync(5_000);
        await BridgeWireMessage.WriteAsync(Pipe,Request,CancellationToken.None);
        return await BridgeWireMessage.ReadAsync<RenderPreviewHostResponse>(Pipe,CancellationToken.None);
    }

    private sealed class FakeSession:IRenderPreviewSession {
        public uint DeviceGeneration=>3;
        public ulong CompletedFence=>5;
        public string LastFrameHash=>string.Empty;
        public RenderPreviewCameraDiagnostics[]CameraPreviewDiagnostics{get;}=[];
        public int RenderCalls{get;private set;}
        public int SnapshotCalls{get;private set;}
        public bool Disposed{get;private set;}
        public void ReplaceScene(RenderPreviewScene Scene,string SceneJson){ }
        public void UpdateFrame(RenderPreviewFrame Frame){ }
        public void Resize(int Width,int Height){ }
        public void Render(bool CaptureForVerification){ this.RenderCalls++; }
        public RenderPreviewDebugSnapshot CaptureDebugSnapshot(){
            this.SnapshotCalls++;
            return new RenderPreviewDebugSnapshot {
                DeviceState=RenderPreviewDebugDeviceState.Ready,
                DeviceGeneration=3,CompletedFence=5,
                SurfaceWidth=64,SurfaceHeight=64,
                AdapterDescription="Fake",QualityProfile="Compatibility",
                Passes=[new RenderPreviewDebugPass {
                    Index=0,Name="Present",Queue=RenderPreviewDebugQueue.Graphics,
                    ExecutionIndex=0,
                }],
                Resources=[new RenderPreviewDebugResource {
                    Index=0,Name="BackBuffer",
                    Kind=RenderPreviewDebugResourceKind.Texture,
                    Width=64,Height=64,Format="rgba8",Imported=true,
                    FirstUse=0,LastUse=0,AliasSlot=-1,
                }],
                Platform=PlatformDebugSnapshotCapture.Capture(
                    PlatformDebugSnapshotCapture.NotOwnedInput("Test host owns no input."),
                    PlatformDebugSnapshotCapture.NotOwnedAudio("Test host owns no audio.")),
            };
        }
        public void Dispose(){ this.Disposed=true; }
    }
}
