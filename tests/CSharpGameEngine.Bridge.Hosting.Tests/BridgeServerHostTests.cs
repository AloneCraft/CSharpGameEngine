using System.IO.Pipes;
using CSharpGameEngine.Bridge.Hosting;

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

        BridgeServerResponse Response= await SendRequestAsync(PipeName,"input.glb","output.json");

        Assert.Equal(0,Response.ExitCode);
        Assert.Equal("input.glb|output.json",Response.Output);
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

        Task<BridgeServerResponse>[]Requests=[
            .. Enumerable.Range(0,8).Select(I=>SendRequestAsync(PipeName, $"mesh-{I}.glb","out.json"))
        ];
        BridgeServerResponse[]Responses= await Task.WhenAll(Requests);

        for(var i=0;i<Responses.Length;i++){
            Assert.Equal($"handled:mesh-{i}.glb",Responses[i].Output);
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

        BridgeServerResponse Failure= await SendRequestAsync(PipeName,"bad.fbx","out.json");
        Assert.Equal(1,Failure.ExitCode);
        Assert.Contains("corrupt file",Failure.Output);

        BridgeServerResponse Success= await SendRequestAsync(PipeName,"good.fbx","out.json");
        Assert.Equal(0,Success.ExitCode);
        Assert.Equal("ok",Success.Output);
    }

    [Fact]
    public async Task IdleTimeout_ExitsAcceptLoop(){
        var PipeName=UniquePipeName();
        Task ServerTask=BridgeServerHost.RunAsync(PipeName,(_,_)=>(0,""),TimeSpan.FromMilliseconds(200));

        await ServerTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task<BridgeServerResponse>SendRequestAsync(string PipeName,string FirstArg,string OutputPath){
        using NamedPipeClientStream Client=new(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        await Client.ConnectAsync((int)TimeSpan.FromSeconds(5).TotalMilliseconds);

        await BridgeWireMessage.WriteAsync(Client,new BridgeServerRequest { Args=[FirstArg,OutputPath]},CancellationToken.None);
        return await BridgeWireMessage.ReadAsync<BridgeServerResponse>(Client,CancellationToken.None);
    }
}
