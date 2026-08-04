using CSharpGameEngine.Bridge.Hosting;

namespace CSharpGameEngine.Bridge.Hosting.Tests;

/// <summary>Exercises the length-prefixed JSON framing against a <see cref="MemoryStream"/> - no real pipe or process involved (ADR-0023).</summary>
public class BridgeWireMessageTests
{
    [Fact]
    public async Task WriteThenRead_RoundTripsRequest()
    {
        using MemoryStream stream = new();
        BridgeServerRequest original = new() { Args = ["input.glb", "output.json"] };

        await BridgeWireMessage.WriteAsync(stream, original, CancellationToken.None);
        stream.Position = 0;
        BridgeServerRequest roundTripped = await BridgeWireMessage.ReadAsync<BridgeServerRequest>(stream, CancellationToken.None);

        Assert.Equal(original.Args, roundTripped.Args);
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsResponse()
    {
        using MemoryStream stream = new();
        BridgeServerResponse original = new() { ExitCode = 1, Output = "Failed to parse FBX file 'bad.fbx': corrupt header" };

        await BridgeWireMessage.WriteAsync(stream, original, CancellationToken.None);
        stream.Position = 0;
        BridgeServerResponse roundTripped = await BridgeWireMessage.ReadAsync<BridgeServerResponse>(stream, CancellationToken.None);

        Assert.Equal(original.ExitCode, roundTripped.ExitCode);
        Assert.Equal(original.Output, roundTripped.Output);
    }

    [Fact]
    public async Task WriteThenRead_MultipleMessagesOnSameStream_ReadBackInOrder()
    {
        using MemoryStream stream = new();
        await BridgeWireMessage.WriteAsync(stream, new BridgeServerResponse { ExitCode = 0, Output = "first" }, CancellationToken.None);
        await BridgeWireMessage.WriteAsync(stream, new BridgeServerResponse { ExitCode = 0, Output = "second" }, CancellationToken.None);
        stream.Position = 0;

        BridgeServerResponse first = await BridgeWireMessage.ReadAsync<BridgeServerResponse>(stream, CancellationToken.None);
        BridgeServerResponse second = await BridgeWireMessage.ReadAsync<BridgeServerResponse>(stream, CancellationToken.None);

        Assert.Equal("first", first.Output);
        Assert.Equal("second", second.Output);
    }

    [Fact]
    public async Task Read_StreamEndsBeforeLengthPrefixComplete_ThrowsEndOfStream()
    {
        using MemoryStream stream = new([0x01, 0x00]);

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => BridgeWireMessage.ReadAsync<BridgeServerResponse>(stream, CancellationToken.None));
    }
}
