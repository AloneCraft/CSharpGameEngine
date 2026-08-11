using CSharpGameEngine.Bridge.Hosting;

namespace CSharpGameEngine.Bridge.Hosting.Tests;

/// <summary>Exercises the length-prefixed JSON framing against a <see cref="MemoryStream"/> - no real pipe or process involved (ADR-0023).</summary>
public class BridgeWireMessageTests {
    [Fact]
    public async Task WriteThenRead_RoundTripsRequest(){
        using MemoryStream stream=new();
        BridgeServerRequest Original=new(){ Args=["input.glb","output.json"]};

        await BridgeWireMessage.WriteAsync(stream,Original,CancellationToken.None);
        stream.Position=0;
        BridgeServerRequest RoundTripped= await BridgeWireMessage.ReadAsync<BridgeServerRequest>(stream,CancellationToken.None);

        Assert.Equal(Original.Args,RoundTripped.Args);
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsResponse(){
        using MemoryStream stream=new();
        BridgeServerResponse Original=new(){ ExitCode=1,Output="Failed to parse FBX file 'bad.fbx': corrupt header" };

        await BridgeWireMessage.WriteAsync(stream,Original,CancellationToken.None);
        stream.Position=0;
        BridgeServerResponse RoundTripped= await BridgeWireMessage.ReadAsync<BridgeServerResponse>(stream,CancellationToken.None);

        Assert.Equal(Original.ExitCode,RoundTripped.ExitCode);
        Assert.Equal(Original.Output,RoundTripped.Output);
    }

    [Fact]
    public async Task WriteThenRead_MultipleMessagesOnSameStream_ReadBackInOrder(){
        using MemoryStream stream=new();
        await BridgeWireMessage.WriteAsync(stream,new BridgeServerResponse { ExitCode=0,Output="first" },CancellationToken.None);
        await BridgeWireMessage.WriteAsync(stream,new BridgeServerResponse { ExitCode=0,Output="second" },CancellationToken.None);
        stream.Position=0;

        BridgeServerResponse First= await BridgeWireMessage.ReadAsync<BridgeServerResponse>(stream,CancellationToken.None);
        BridgeServerResponse Second= await BridgeWireMessage.ReadAsync<BridgeServerResponse>(stream,CancellationToken.None);

        Assert.Equal("first",First.Output);
        Assert.Equal("second",Second.Output);
    }

    [Fact]
    public async Task Read_StreamEndsBeforeLengthPrefixComplete_ThrowsEndOfStream(){
        using MemoryStream stream=new([0x01,0x00]);

        await Assert.ThrowsAsync<EndOfStreamException>(
            ()=>BridgeWireMessage.ReadAsync<BridgeServerResponse>(stream,CancellationToken.None));
    }
}
