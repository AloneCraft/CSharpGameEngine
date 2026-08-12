using System.Text.Json;

namespace CSharpGameEngine.Bridge.Hosting;

/// <summary>Request shape for a resident bridge server (ADR-0023) - mirrors a one-shot bridge's <c>args[]</c>.</summary>
public sealed class BridgeServerRequest {
    public string[]Args { get; set;}=[];
}

/// <summary>Response shape for a resident bridge server (ADR-0023) - mirrors a one-shot bridge's exit code + combined stdout/stderr text.</summary>
public sealed class BridgeServerResponse {
    public int ExitCode { get; set;}

    public string Output { get; set;}="";
}

/// <summary>
/// Length-prefixed UTF-8 JSON framing shared by <see cref="BridgeServerHost"/> (the resident bridge
/// server side) and independently hand-mirrored by the VSIX's <c>ResidentBridgeClient</c> (net472
/// can't <c>ProjectReference</c> this net10.0 library, so the wire shape - not the types - is what's
/// shared; same "duplicate now, consolidate later" approach ADR-0019/ADR-0021 already use across the
/// net472/net10.0 boundary). Operates on a plain <see cref="Stream"/> rather than
/// <see cref="System.IO.Pipes.PipeStream"/> specifically so tests can exercise it against a
/// <see cref="MemoryStream"/> without spawning a real pipe or process.
/// </summary>
public static class BridgeWireMessage {
    public static async Task WriteAsync<T>(Stream Stream,T Value,CancellationToken CancellationToken){
        var Payload=JsonSerializer.SerializeToUtf8Bytes(Value);
        var LengthPrefix=BitConverter.GetBytes(Payload.Length);
        await Stream.WriteAsync(LengthPrefix,CancellationToken);
        await Stream.WriteAsync(Payload,CancellationToken);
        await Stream.FlushAsync(CancellationToken);
    }

    public static async Task<T>ReadAsync<T>(Stream Stream,CancellationToken CancellationToken){
        var LengthPrefix=new byte[4];
        await Stream.ReadExactlyAsync(LengthPrefix,CancellationToken);
        var Length=BitConverter.ToInt32(LengthPrefix,0);

        var Payload=new byte[Length];
        await Stream.ReadExactlyAsync(Payload,CancellationToken);

        return JsonSerializer.Deserialize<T>(Payload)
            ??throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from the bridge wire protocol.");
    }
}
