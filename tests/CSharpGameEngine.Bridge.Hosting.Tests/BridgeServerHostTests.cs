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
public class BridgeServerHostTests
{
    private static string UniquePipeName() => $"csge-bridgehost-tests-{Guid.NewGuid():N}";

    [Fact]
    public async Task SingleRequest_ReturnsHandlerResult()
    {
        string pipeName = UniquePipeName();
        _ = BridgeServerHost.RunAsync(pipeName, (firstArg, outputPath) => (0, $"{firstArg}|{outputPath}"), TimeSpan.FromSeconds(5));

        BridgeServerResponse response = await SendRequestAsync(pipeName, "input.glb", "output.json");

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("input.glb|output.json", response.Output);
    }

    [Fact]
    public async Task ConcurrentRequests_DoNotCrossTalk()
    {
        string pipeName = UniquePipeName();
        _ = BridgeServerHost.RunAsync(
            pipeName,
            (firstArg, _) =>
            {
                Thread.Sleep(50);
                return (0, $"handled:{firstArg}");
            },
            TimeSpan.FromSeconds(5));

        Task<BridgeServerResponse>[] requests =
        [
            .. Enumerable.Range(0, 8).Select(i => SendRequestAsync(pipeName, $"mesh-{i}.glb", "out.json"))
        ];
        BridgeServerResponse[] responses = await Task.WhenAll(requests);

        for (int i = 0; i < responses.Length; i++)
        {
            Assert.Equal($"handled:mesh-{i}.glb", responses[i].Output);
        }
    }

    [Fact]
    public async Task HandlerThrows_ReturnsFailureResponse_AndServerStaysUsable()
    {
        string pipeName = UniquePipeName();
        _ = BridgeServerHost.RunAsync(
            pipeName,
            (firstArg, _) => firstArg == "bad.fbx"
                ? throw new InvalidOperationException("corrupt file")
                : (0, "ok"),
            TimeSpan.FromSeconds(5));

        BridgeServerResponse failure = await SendRequestAsync(pipeName, "bad.fbx", "out.json");
        Assert.Equal(1, failure.ExitCode);
        Assert.Contains("corrupt file", failure.Output);

        BridgeServerResponse success = await SendRequestAsync(pipeName, "good.fbx", "out.json");
        Assert.Equal(0, success.ExitCode);
        Assert.Equal("ok", success.Output);
    }

    [Fact]
    public async Task IdleTimeout_ExitsAcceptLoop()
    {
        string pipeName = UniquePipeName();
        Task serverTask = BridgeServerHost.RunAsync(pipeName, (_, _) => (0, ""), TimeSpan.FromMilliseconds(200));

        await serverTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task<BridgeServerResponse> SendRequestAsync(string pipeName, string firstArg, string outputPath)
    {
        using NamedPipeClientStream client = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync((int)TimeSpan.FromSeconds(5).TotalMilliseconds);

        await BridgeWireMessage.WriteAsync(client, new BridgeServerRequest { Args = [firstArg, outputPath] }, CancellationToken.None);
        return await BridgeWireMessage.ReadAsync<BridgeServerResponse>(client, CancellationToken.None);
    }
}
