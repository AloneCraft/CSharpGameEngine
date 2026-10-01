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
        public bool Disposed{get;private set;}
        public void ReplaceScene(RenderPreviewScene Scene,string SceneJson){ }
        public void UpdateFrame(RenderPreviewFrame Frame){ }
        public void Resize(int Width,int Height){ }
        public void Render(bool CaptureForVerification){ this.RenderCalls++; }
        public void Dispose(){ this.Disposed=true; }
    }
}
