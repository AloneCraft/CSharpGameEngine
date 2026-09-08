using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Bridge.Hosting.Tests;

/// <summary>Exercises the length-prefixed JSON framing against a <see cref="MemoryStream"/> - no real pipe or process involved (ADR-0023).</summary>
public class BridgeWireMessageTests {
    [Fact]
    public async Task WriteThenRead_RoundTripsRequest(){
        using MemoryStream Stream=new();
        AuthoringHostRequest Original=new(){
            RequestId=Guid.NewGuid(),Operation=AuthoringHostOperation.Import,Arguments=["input.glb","output.json"]
        };

        await BridgeWireMessage.WriteAsync(Stream,Original,CancellationToken.None);
        Stream.Position=0;
        var RoundTripped=await BridgeWireMessage.ReadAsync<AuthoringHostRequest>(Stream,CancellationToken.None);

        Assert.Equal(Original.RequestId,RoundTripped.RequestId);
        Assert.Equal(Original.Arguments,RoundTripped.Arguments);
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsResponse(){
        using MemoryStream Stream=new();
        AuthoringHostResult Original=new(){
            RequestId=Guid.NewGuid(),State=AuthoringHostTerminalState.Failed,ExitCode=1,
            Output="Failed to parse FBX file 'bad.fbx': corrupt header"
        };

        await BridgeWireMessage.WriteAsync(Stream,Original,CancellationToken.None);
        Stream.Position=0;
        var RoundTripped=await BridgeWireMessage.ReadAsync<AuthoringHostResult>(Stream,CancellationToken.None);

        Assert.Equal(Original.ExitCode,RoundTripped.ExitCode);
        Assert.Equal(Original.Output,RoundTripped.Output);
    }

    [Fact]
    public async Task WriteThenRead_MultipleMessagesOnSameStream_ReadBackInOrder(){
        using MemoryStream Stream=new();
        await BridgeWireMessage.WriteAsync(Stream,new AuthoringHostResult { ExitCode=0,Output="first" },CancellationToken.None);
        await BridgeWireMessage.WriteAsync(Stream,new AuthoringHostResult { ExitCode=0,Output="second" },CancellationToken.None);
        Stream.Position=0;

        var First=await BridgeWireMessage.ReadAsync<AuthoringHostResult>(Stream,CancellationToken.None);
        var Second=await BridgeWireMessage.ReadAsync<AuthoringHostResult>(Stream,CancellationToken.None);

        Assert.Equal("first",First.Output);
        Assert.Equal("second",Second.Output);
    }

    [Fact]
    public async Task Read_StreamEndsBeforeLengthPrefixComplete_ThrowsEndOfStream(){
        using MemoryStream Stream=new([0x01,0x00]);

        await Assert.ThrowsAsync<EndOfStreamException>(
            ()=>BridgeWireMessage.ReadAsync<AuthoringHostResult>(Stream,CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(16*1024*1024+1)]
    public async Task Read_InvalidPayloadLength_RejectsBeforeAllocation(int Length){
        using MemoryStream Stream=new(BitConverter.GetBytes(Length));

        await Assert.ThrowsAsync<InvalidDataException>(
            ()=>BridgeWireMessage.ReadAsync<AuthoringHostResult>(Stream,CancellationToken.None));
    }
}
