using System.Buffers.Binary;
using System.Text.Json;

namespace CSharpGameEngine.Bridge.Hosting;

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
    public const int MaximumPayloadBytes=16*1024*1024;

    public static async Task WriteAsync<T>(Stream Stream,T Value,CancellationToken CancellationToken){
        var Payload=JsonSerializer.SerializeToUtf8Bytes(Value);
        if(Payload.Length<1||Payload.Length>MaximumPayloadBytes){
            throw new InvalidDataException($"Bridge payload length {Payload.Length} is outside the supported range.");
        }
        var LengthPrefix=new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(LengthPrefix,Payload.Length);
        await Stream.WriteAsync(LengthPrefix,CancellationToken);
        await Stream.WriteAsync(Payload,CancellationToken);
        await Stream.FlushAsync(CancellationToken);
    }

    /// <summary>The longest a server waits for a connected client to send its whole request (audit AUD-003).</summary>
    public static readonly TimeSpan RequestReceiveTimeout=TimeSpan.FromSeconds(30);

    /// <summary>
    /// Reads one request on a server. It ends with <see cref="OperationCanceledException"/> when the server stops
    /// (<paramref name="Lifetime"/>) or the client has not sent the whole request within <paramref name="Timeout"/>, so a
    /// client that stops mid-request cannot keep the server running or hold its pipe.
    /// </summary>
    public static async Task<T>ReadRequestAsync<T>(Stream Stream,TimeSpan Timeout,CancellationToken Lifetime){
        using var Receive=CancellationTokenSource.CreateLinkedTokenSource(Lifetime);
        Receive.CancelAfter(Timeout);
        return await ReadAsync<T>(Stream,Receive.Token).ConfigureAwait(false);
    }

    public static async Task<T>ReadAsync<T>(Stream Stream,CancellationToken CancellationToken){
        var LengthPrefix=new byte[4];
        await Stream.ReadExactlyAsync(LengthPrefix,CancellationToken);
        var Length=BinaryPrimitives.ReadInt32LittleEndian(LengthPrefix);
        if(Length<1||Length>MaximumPayloadBytes){
            throw new InvalidDataException($"Bridge payload length {Length} is outside the supported range.");
        }

        var Payload=new byte[Length];
        await Stream.ReadExactlyAsync(Payload,CancellationToken);

        return JsonSerializer.Deserialize<T>(Payload)
            ??throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from the bridge wire protocol.");
    }
}
