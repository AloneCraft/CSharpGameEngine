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

    [Fact]
    public async Task NamedPipe_StopsWhileAClientIsMidRequest(){
        // Audit AUD-003: after the connection the request read used no token, so a client that sent part of a request
        // and kept the pipe open kept the host from stopping.
        string PipeName=$"CSharpGameEngine.Preview.Test.{Guid.NewGuid():N}";
        using CancellationTokenSource Stop=new();
        Task Server=RenderPreviewHostServer.RunAsync(PipeName,_=>new FakeSession(),Stop.Token);
        using NamedPipeClientStream Stalled=await ConnectAndSendPartOfARequestAsync(PipeName);

        Stop.Cancel();

        await Server.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task NamedPipe_DropsAClientThatDoesNotFinishItsRequestInTime(){
        // The host has one pipe instance, so a stalled client must not hold it past the receive deadline.
        string PipeName=$"CSharpGameEngine.Preview.Test.{Guid.NewGuid():N}";
        using CancellationTokenSource Stop=new();
        Task Server=RenderPreviewHostServer.RunAsync(PipeName,_=>new FakeSession(),TimeSpan.FromMilliseconds(200),Stop.Token);
        using NamedPipeClientStream Stalled=await ConnectAndSendPartOfARequestAsync(PipeName);

        RenderPreviewHostRequest Open=RenderPreviewHostSessionTableTests.CreateOpenForPipe();
        RenderPreviewHostResponse Opened=await SendAsync(PipeName,Open).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(RenderPreviewHostResultState.Succeeded,Opened.State);
        Stop.Cancel();
        await Server.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task<NamedPipeClientStream>ConnectAndSendPartOfARequestAsync(string PipeName){
        NamedPipeClientStream Pipe=new(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        await Pipe.ConnectAsync(5_000);
        await Pipe.WriteAsync(new byte[]{1});
        await Pipe.FlushAsync();
        await Task.Delay(100);
        return Pipe;
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
