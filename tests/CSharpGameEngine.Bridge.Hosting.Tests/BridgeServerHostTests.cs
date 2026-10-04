using System.IO.Pipes;
using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Bridge.Hosting.Tests;

/// <summary>
/// Exercises <see cref="BridgeServerHost"/> against real <see cref="System.IO.Pipes"/> objects, both
/// ends running in-process in the same test (no child process spawned) - the same "real OS
/// primitive, fake process topology" approach <c>BridgeExecutableLocatorTests</c> already uses for
/// the filesystem (ADR-0023). Tests that don't specifically exercise the idle timeout use a long
/// (5s) one and never await the server's <see cref="Task"/> to completion - it self-terminates in
/// the background harmlessly once the test process moves on, keeping these tests fast.
/// </summary>
public class BridgeServerHostTests {
    private static string UniquePipeName()=> $"csge-bridgehost-tests-{Guid.NewGuid():N}";

    [Fact]
    public async Task SingleRequest_ReturnsHandlerResult(){
        var PipeName=UniquePipeName();
        _=BridgeServerHost.RunAsync(PipeName,(FirstArg,OutputPath)=>(0, $"{FirstArg}|{OutputPath}"),TimeSpan.FromSeconds(5));

        AuthoringHostResult Response=await SendRequestAsync(PipeName,"input.glb","output.json");

        Assert.Equal(AuthoringHostTerminalState.Succeeded,Response.State);
        Assert.Equal(0,Response.ExitCode);
        Assert.Equal("input.glb|output.json",Response.Output);
    }

    [Fact]
    public async Task VariableArgumentRequest_PreservesEveryArgument(){
        var PipeName=UniquePipeName();
        _=BridgeServerHost.RunArgumentsAsync(
            PipeName,(Arguments,_)=>(0,string.Join("|",Arguments)),TimeSpan.FromSeconds(5));
        AuthoringHostRequest Request=CreateRequest("bake","Scene.Type");
        Request.Operation=AuthoringHostOperation.Bake;
        Request.Arguments=["bake","Scene.Type","output.cgephyscache","120","HASH"];

        AuthoringHostResult Response=await SendRequestAsync(PipeName,Request);

        Assert.Equal(AuthoringHostTerminalState.Succeeded,Response.State);
        Assert.Equal("bake|Scene.Type|output.cgephyscache|120|HASH",Response.Output);
    }

    [Fact]
    public async Task VariableArgumentHandler_ReceivesTimeoutCancellation(){
        var PipeName=UniquePipeName();
        _=BridgeServerHost.RunArgumentsAsync(
            PipeName,
            (Arguments,Cancellation)=>{
                Assert.Equal("wait",Arguments[0]);
                Cancellation.WaitHandle.WaitOne();
                Cancellation.ThrowIfCancellationRequested();
                return(0,"unexpected");
            },
            TimeSpan.FromSeconds(5));
        AuthoringHostRequest Request=CreateRequest("wait","unused");
        Request.TimeoutMilliseconds=50;

        AuthoringHostResult Response=await SendRequestAsync(PipeName,Request);

        Assert.Equal(AuthoringHostTerminalState.TimedOut,Response.State);
    }

    [Fact]
    public async Task ClientDisconnect_CancelsSubmittedHandler(){
        string PipeName=UniquePipeName();
        using ManualResetEventSlim Started=new();
        using ManualResetEventSlim Cancelled=new();
        _=BridgeServerHost.RunArgumentsAsync(
            PipeName,
            (_,Cancellation)=>{
                Started.Set();
                Cancellation.WaitHandle.WaitOne();
                Cancelled.Set();
                Cancellation.ThrowIfCancellationRequested();
                return(0,"unexpected");
            },
            TimeSpan.FromSeconds(5));

        using(NamedPipeClientStream Client=new(
            ".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous)){
            await Client.ConnectAsync((int)TimeSpan.FromSeconds(5).TotalMilliseconds);
            await BridgeWireMessage.WriteAsync(
                Client,CreateRequest("wait","unused"),CancellationToken.None);
            Assert.True(Started.Wait(TimeSpan.FromSeconds(5)));
        }

        Assert.True(Cancelled.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ConcurrentRequests_DoNotCrossTalk(){
        var PipeName=UniquePipeName();
        _=BridgeServerHost.RunAsync(
            PipeName,
            (FirstArg,_)=>{
                Thread.Sleep(50);
                return(0, $"handled:{FirstArg}");
            },
            TimeSpan.FromSeconds(5));

        Task<AuthoringHostResult>[]Requests=[
            .. Enumerable.Range(0,8).Select(I=>SendRequestAsync(PipeName, $"mesh-{I}.glb","out.json"))
        ];
        AuthoringHostResult[]Responses=await Task.WhenAll(Requests);

        for(var I=0;I<Responses.Length;I++){
            Assert.Equal($"handled:mesh-{I}.glb",Responses[I].Output);
        }
    }

    [Fact]
    public async Task HandlerThrows_ReturnsFailureResponse_AndServerStaysUsable(){
        var PipeName=UniquePipeName();
        _=BridgeServerHost.RunAsync(
            PipeName,
            (FirstArg,_)=>FirstArg=="bad.fbx"
                ?throw new InvalidOperationException("corrupt file")
                :(0,"ok"),
            TimeSpan.FromSeconds(5));

        AuthoringHostResult Failure=await SendRequestAsync(PipeName,"bad.fbx","out.json");
        Assert.Equal(AuthoringHostTerminalState.Failed,Failure.State);
        Assert.Equal(1,Failure.ExitCode);
        Assert.Contains("corrupt file",Failure.Output);

        AuthoringHostResult Success=await SendRequestAsync(PipeName,"good.fbx","out.json");
        Assert.Equal(0,Success.ExitCode);
        Assert.Equal("ok",Success.Output);
    }

    [Fact]
    public async Task Stop_EndsAConnectionThatStoppedMidRequest(){
        // Audit AUD-003: the server drains accepted connections before it exits, so a client that sent part of a
        // request and kept the pipe open used to keep the bridge (and the extension's locked files) alive.
        var PipeName=UniquePipeName();
        using var Stop=new CancellationTokenSource();
        Task ServerTask=BridgeServerHost.RunAsync(PipeName,(_,_)=>(0,""),TimeSpan.FromSeconds(30),Stop.Token);
        using var Client=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        await Client.ConnectAsync((int)TimeSpan.FromSeconds(5).TotalMilliseconds);
        await Client.WriteAsync(new byte[]{1});
        await Client.FlushAsync();
        await Task.Delay(100);

        Stop.Cancel();

        await ServerTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IdleTimeout_ExitsAcceptLoop(){
        var PipeName=UniquePipeName();
        Task ServerTask=BridgeServerHost.RunAsync(PipeName,(_,_)=>(0,""),TimeSpan.FromMilliseconds(200));

        await ServerTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task OwnerExit_ExitsAcceptLoopLongBeforeTheIdleTimeout(){
        using var Owner=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe","/c ping -n 60 127.0.0.1 >nul"){ UseShellExecute=false,CreateNoWindow=true })!;
        Task ServerTask=BridgeServerHost.RunAsync(
            UniquePipeName(),(_,_)=>(0,""),TimeSpan.FromMinutes(15),OwnerProcessId:Owner.Id);
        await Task.Delay(200);
        Assert.False(ServerTask.IsCompleted);

        Owner.Kill(entireProcessTree:true);

        await ServerTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task OwnerAlreadyGone_ExitsAtOnce(){
        using var Owner=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe","/c exit 0"){ UseShellExecute=false,CreateNoWindow=true })!;
        Owner.WaitForExit();

        await BridgeServerHost.RunAsync(
            UniquePipeName(),(_,_)=>(0,""),TimeSpan.FromMinutes(15),OwnerProcessId:Owner.Id)
            .WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(new[]{ "--serve","Pipe" },"Pipe",null)]
    [InlineData(new[]{ "--serve","Pipe","--parent-process-id","4242" },"Pipe",4242)]
    public void ServeCommand_ReadsThePipeAndTheOwner(string[]Arguments,string Pipe,int?Owner){
        Assert.True(BridgeServeCommand.TryParse(Arguments,out var PipeName,out var OwnerProcessId));
        Assert.Equal(Pipe,PipeName);
        Assert.Equal(Owner,OwnerProcessId);
    }

    [Theory]
    [InlineData("input.glb output.json")]
    [InlineData("--serve")]
    [InlineData("--serve Pipe --parent-process-id -1")]
    [InlineData("--serve Pipe --parent-process-id x")]
    public void ServeCommand_RejectsOtherCommandLines(string CommandLine){
        Assert.False(BridgeServeCommand.TryParse(CommandLine.Split(' '),out _,out _));
    }

    [Fact]
    public async Task IdleTimeout_DrainsAcceptedRequestBeforeExit(){
        var PipeName=UniquePipeName();
        using ManualResetEventSlim Started=new();
        using ManualResetEventSlim Release=new();
        Task ServerTask=BridgeServerHost.RunAsync(
            PipeName,
            (_,_)=>{
                Started.Set();
                Release.Wait(TimeSpan.FromSeconds(5));
                return(0,"done");
            },
            TimeSpan.FromMilliseconds(50));
        Task<AuthoringHostResult>RequestTask=SendRequestAsync(PipeName,"input","output");

        Assert.True(Started.Wait(TimeSpan.FromSeconds(5)));
        await Task.Delay(150);
        Assert.False(ServerTask.IsCompleted);
        Release.Set();

        Assert.Equal(AuthoringHostTerminalState.Succeeded,(await RequestTask).State);
        await ServerTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// The idle window ends the accept loop, not a connection already accepted: a request that arrives after the window
    /// (a slow or busy client) is still answered, within the receive deadline. Ending the read with the idle window
    /// dropped such a request without a response.
    /// </summary>
    [Fact]
    public async Task IdleTimeout_DoesNotDropARequestStillArriving(){
        var PipeName=UniquePipeName();
        Task ServerTask=BridgeServerHost.RunAsync(PipeName,(_,_)=>(0,"done"),TimeSpan.FromMilliseconds(50));
        using NamedPipeClientStream Client=new(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        await Client.ConnectAsync((int)TimeSpan.FromSeconds(5).TotalMilliseconds);
        await Task.Delay(300);

        await BridgeWireMessage.WriteAsync(Client,CreateRequest("input","output"),CancellationToken.None);
        var Result=await BridgeWireMessage.ReadAsync<AuthoringHostResult>(Client,CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(AuthoringHostTerminalState.Succeeded,Result.State);
        await ServerTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task VersionMismatch_DoesNotInvokeHandler(){
        var PipeName=UniquePipeName();
        var HandlerCalls=0;
        _=BridgeServerHost.RunAsync(PipeName,(_,_)=>{ HandlerCalls++;return(0,"unexpected");},TimeSpan.FromSeconds(5));

        var Request=CreateRequest("input.glb","output.json");
        Request.ProtocolVersion=AuthoringHostProtocol.CurrentVersion+1;
        var Response=await SendRequestAsync(PipeName,Request);

        Assert.Equal(AuthoringHostTerminalState.VersionMismatch,Response.State);
        Assert.Equal(0,HandlerCalls);
    }

    private static Task<AuthoringHostResult>SendRequestAsync(string PipeName,string FirstArg,string OutputPath)=>
        SendRequestAsync(PipeName,CreateRequest(FirstArg,OutputPath));

    private static async Task<AuthoringHostResult>SendRequestAsync(string PipeName,AuthoringHostRequest Request){
        using NamedPipeClientStream Client=new(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        await Client.ConnectAsync((int)TimeSpan.FromSeconds(5).TotalMilliseconds);

        await BridgeWireMessage.WriteAsync(Client,Request,CancellationToken.None);
        return await BridgeWireMessage.ReadAsync<AuthoringHostResult>(Client,CancellationToken.None);
    }

    private static AuthoringHostRequest CreateRequest(string FirstArg,string OutputPath)=>new(){
        RequestId=Guid.NewGuid(),
        Operation=AuthoringHostOperation.Import,
        TimeoutMilliseconds=5_000,
        Arguments=[FirstArg,OutputPath],
    };
}
