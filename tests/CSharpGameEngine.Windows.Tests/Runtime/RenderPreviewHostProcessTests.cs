using System.Diagnostics;
using System.IO.Pipes;
using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Platform.Windows;

namespace CSharpGameEngine.Windows.Tests.Runtime;

[Collection(CSharpGameEngine.Graphics.D3D12.Tests.D3D12GpuCollection.Name)]
public sealed class RenderPreviewHostProcessTests {
    [Fact]
    public void ParentExit_CancelsOwnedPreviewHostWithoutAClientConnection(){
        if(!OperatingSystem.IsWindows()){ return; }
        Process?Parent=null;
        Process?Owned=null;
        try {
            Parent=StartHost($"CSharpGameEngine.Preview.Parent.{Guid.NewGuid():N}");
            Owned=StartHost(
                $"CSharpGameEngine.Preview.Owned.{Guid.NewGuid():N}",Parent.Id);
            Assert.False(Parent.WaitForExit(500));
            Assert.False(Owned.WaitForExit(500));

            Parent.Kill(entireProcessTree:false);
            Assert.True(Parent.WaitForExit(5_000));
            Assert.True(Owned.WaitForExit(5_000));
            Assert.Equal(0,Owned.ExitCode);
        } finally {
            StopOwnedProcess(Owned);
            StopOwnedProcess(Parent);
        }
    }

    [Fact]
    public async Task HostCrash_NewProcessAndSurfaceReopenSameSceneWithIdenticalFrameHash(){
        if(!OperatingSystem.IsWindows()){ return; }
        Process?First=null;
        Process?Second=null;
        try {
            RenderPreviewHostResponse FirstFrame;
            using(Win32Window FirstWindow=CreateWindow("First")){
                string FirstPipe=$"CSharpGameEngine.Preview.Process.{Guid.NewGuid():N}";
                First=StartHost(FirstPipe);
                RenderPreviewHostRequest FirstOpen=
                    RenderPreviewRenderSessionTests.CreateOpenForHost(FirstWindow);
                Assert.Equal(RenderPreviewHostResultState.Succeeded,
                    (await SendWithConnectRetryAsync(FirstPipe,FirstOpen)).State);
                RenderPreviewHostRequest FirstUpdate=
                    Command(FirstOpen,RenderPreviewHostCommand.UpdateFrame,2);
                FirstUpdate.Frame=CameraFrame(FirstOpen);
                Assert.Equal(RenderPreviewHostResultState.Succeeded,
                    (await SendAsync(FirstPipe,FirstUpdate)).State);
                FirstFrame=await SendAsync(
                    FirstPipe,Command(FirstOpen,RenderPreviewHostCommand.Render,3,Capture:true));
                Assert.Equal(RenderPreviewHostResultState.Succeeded,FirstFrame.State);
                Assert.Equal(64,FirstFrame.FrameHash.Length);
                RenderPreviewCameraDiagnostics FirstCamera=
                    Assert.Single(FirstFrame.CameraPreviews);
                Assert.Equal(1,FirstCamera.SceneObjectCount);
                Assert.Equal(0,FirstCamera.DrawCallCount);

                First.Kill(entireProcessTree:true);
                Assert.True(First.WaitForExit(5_000));
            }

            // DXGI can retain an abruptly-terminated process's HWND association. Recovery owns a
            // fresh preview-surface generation as well as a fresh host/device generation.
            using Win32Window SecondWindow=CreateWindow("Recovered");
            string SecondPipe=$"CSharpGameEngine.Preview.Process.{Guid.NewGuid():N}";
            Second=StartHost(SecondPipe);
            RenderPreviewHostRequest SecondOpen=
                RenderPreviewRenderSessionTests.CreateOpenForHost(SecondWindow);
            RenderPreviewHostResponse SecondOpened=await SendWithConnectRetryAsync(SecondPipe,SecondOpen);
            Assert.True(SecondOpened.State==RenderPreviewHostResultState.Succeeded,
                $"{SecondOpened.DiagnosticCode}: {SecondOpened.Message}");
            RenderPreviewHostRequest SecondUpdate=
                Command(SecondOpen,RenderPreviewHostCommand.UpdateFrame,2);
            SecondUpdate.Frame=CameraFrame(SecondOpen);
            Assert.Equal(RenderPreviewHostResultState.Succeeded,
                (await SendAsync(SecondPipe,SecondUpdate)).State);
            RenderPreviewHostResponse SecondFrame=await SendAsync(
                SecondPipe,Command(SecondOpen,RenderPreviewHostCommand.Render,3,Capture:true));
            Assert.Equal(RenderPreviewHostResultState.Succeeded,SecondFrame.State);
            Assert.Equal(FirstFrame.FrameHash,SecondFrame.FrameHash);
            Assert.Equal(
                FirstFrame.CameraPreviews[0].ViewProjectionHash,
                Assert.Single(SecondFrame.CameraPreviews).ViewProjectionHash);

            RenderPreviewHostResponse Closed=await SendAsync(
                SecondPipe,Command(SecondOpen,RenderPreviewHostCommand.Close,4));
            Assert.Equal(RenderPreviewHostResultState.Succeeded,Closed.State);
            Assert.True(Second.WaitForExit(5_000));
            Assert.Equal(0,Second.ExitCode);
        } finally {
            StopOwnedProcess(First);
            StopOwnedProcess(Second);
        }
    }

    /// <summary>
    /// M0096 (ADR-0047): the Scene view's render server runs the editor world as well, so the Spatial debug overlay
    /// asks that process, and a request without a world id gets the editor world. (Rendering here would also make the
    /// view world publish, but a runtime view render in a new host after HostCrash's abrupt kill fails to create its
    /// swap chain in this test process, a separate open issue recorded in M0096.)
    /// </summary>
    [Fact]
    public async Task RuntimeViewHost_AnswersSpatialDebugWithTheEditorWorld(){
        if(!OperatingSystem.IsWindows()){ return; }
        Process?Host=null;
        try {
            using Win32Window Window=CreateWindow("Runtime");
            string PipeName=$"CSharpGameEngine.Preview.Process.{Guid.NewGuid():N}";
            Host=StartHost(PipeName);
            RenderPreviewHostRequest Open=RenderPreviewRuntimeSessionTests.CreateRuntimeOpen(Window);
            RenderPreviewHostResponse Opened=await SendWithConnectRetryAsync(PipeName,Open);
            Assert.True(Opened.State==RenderPreviewHostResultState.Succeeded,$"{Opened.DiagnosticCode}: {Opened.Message}");
            Assert.NotEqual(Guid.Empty,Opened.EditorSpatialWorldId);
            using(NamedPipeClientStream Spatial=new(".",$"CSharpGameEngine.SpatialDebug.{Host.Id}",PipeDirection.InOut,PipeOptions.None)){
                await Spatial.ConnectAsync(5_000);
                byte[]Payload=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { Command="snapshot",WorldId=(string?)null });
                await Spatial.WriteAsync(BitConverter.GetBytes(Payload.Length));
                await Spatial.WriteAsync(Payload);
                byte[]Length=new byte[4];
                await Spatial.ReadExactlyAsync(Length);
                byte[]Body=new byte[BitConverter.ToInt32(Length,0)];
                await Spatial.ReadExactlyAsync(Body);
                using var Response=System.Text.Json.JsonDocument.Parse(Body);
                Assert.Equal("ok",Response.RootElement.GetProperty("Status").GetString());
                Assert.Equal(Opened.EditorSpatialWorldId,Response.RootElement.GetProperty("WorldId").GetGuid());
            }
            RenderPreviewHostResponse Closed=await SendAsync(PipeName,Command(Open,RenderPreviewHostCommand.Close,2));
            Assert.True(Closed.State==RenderPreviewHostResultState.Succeeded,$"{Closed.DiagnosticCode}: {Closed.Message}");
            Assert.True(Host.WaitForExit(10_000));
        } finally {
            StopOwnedProcess(Host);
        }
    }

    private static Win32Window CreateWindow(string Generation)=>new(new Win32WindowOptions {
        Title=$"CSharpGameEngine Out Of Process Preview Test {Generation}",
        Width=128,
        Height=128,
        Visible=false,
    });

    private static Process StartHost(string PipeName,int?ParentProcessId=null){
        string HostPath=Path.Combine(AppContext.BaseDirectory,"CSharpGameEngine.EditorPreview.Host.dll");
        Assert.True(File.Exists(HostPath),HostPath);
        ProcessStartInfo StartInfo=new("dotnet"){
            UseShellExecute=false,
            CreateNoWindow=true,
            WorkingDirectory=AppContext.BaseDirectory,
        };
        StartInfo.ArgumentList.Add(HostPath);
        StartInfo.ArgumentList.Add("--render-server");
        StartInfo.ArgumentList.Add(PipeName);
        if(ParentProcessId is { }Owner){
            StartInfo.ArgumentList.Add("--parent-process-id");
            StartInfo.ArgumentList.Add(Owner.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }
        StartInfo.Environment["CGE_GRAPHICS_ADAPTER"]="warp";
        StartInfo.Environment["CGE_D3D12_DEBUG_LAYER"]="1";
        return Process.Start(StartInfo)??throw new InvalidOperationException("Preview host did not start.");
    }

    private static void StopOwnedProcess(Process?Process){
        if(Process is null){ return; }
        try {
            if(!Process.HasExited){
                Process.Kill(entireProcessTree:true);
                Process.WaitForExit(5_000);
            }
        } catch(InvalidOperationException){ }
        finally { Process.Dispose(); }
    }

    private static RenderPreviewHostRequest Command(
        RenderPreviewHostRequest Open,RenderPreviewHostCommand Command,ulong Sequence,bool Capture=false)=>new(){
            RequestId=Guid.NewGuid(),
            SessionId=Open.SessionId,
            Sequence=Sequence,
            Command=Command,
            CaptureForVerification=Capture,
        };

    private static RenderPreviewFrame CameraFrame(RenderPreviewHostRequest Open)=>new(){
        Camera=Open.Scene!.Camera,
        Objects=[
            new RenderPreviewObjectTransform {
                ObjectId="Root/Box",
                World=RenderPreviewMatrix4x4.Identity,
            },
        ],
        CameraPreviews=[
            new RenderPreviewCameraPass {
                X=8,Y=8,Width=48,Height=32,
                Camera=Open.Scene.Camera,
                ExcludedObjectId="Root/Box",
            },
        ],
    };

    private static async Task<RenderPreviewHostResponse>SendWithConnectRetryAsync(
        string PipeName,RenderPreviewHostRequest Request){
        long Deadline=Stopwatch.GetTimestamp()+Stopwatch.Frequency*10;
        while(true){
            try { return await SendAsync(PipeName,Request,ConnectTimeoutMilliseconds:200); }
            catch(TimeoutException)when(Stopwatch.GetTimestamp()<Deadline){
                await Task.Delay(50);
            }
        }
    }

    private static async Task<RenderPreviewHostResponse>SendAsync(
        string PipeName,RenderPreviewHostRequest Request,int ConnectTimeoutMilliseconds=5_000){
        using NamedPipeClientStream Pipe=new(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        await Pipe.ConnectAsync(ConnectTimeoutMilliseconds);
        await BridgeWireMessage.WriteAsync(Pipe,Request,CancellationToken.None);
        return await BridgeWireMessage.ReadAsync<RenderPreviewHostResponse>(Pipe,CancellationToken.None);
    }
}
