using System.Buffers.Binary;
using System.Text.Json;

namespace CSharpGameEngine.Bridge.Hosting;

/// <summary>
/// <see cref="BridgeServerHost"/>（常駐ブリッジのサーバー側）と、VSIX の <c>ResidentBridgeClient</c> が独立して同じ形式を実装する、長さプレフィックス付き UTF-8 JSON フレーミング。net472 はこの net10.0 ライブラリを <c>ProjectReference</c> できないため、共有するのは型ではなく通信形式である。ADR-0019/ADR-0021 でも net472/net10.0 の境界で「今は複製し、後で統合」の方式を使っている。特定の <see cref="System.IO.Pipes.PipeStream"/> ではなく通常の <see cref="Stream"/> を扱うため、実際のパイプやプロセスを起動せずに <see cref="MemoryStream"/> でテストできる。
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

    /// <summary>サーバーが、接続済みクライアントから要求全体を受信するまで待つ最長時間 (audit AUD-003)。</summary>
    public static readonly TimeSpan RequestReceiveTimeout=TimeSpan.FromSeconds(30);

    /// <summary>
    /// サーバーで 1 つの要求を読み取る。サーバーの停止時（<paramref name="Lifetime"/>）またはクライアントが <paramref name="Timeout"/> 以内に要求全体を送信しない場合に <see cref="OperationCanceledException"/> で終了する。要求の途中で停止したクライアントがサーバーを稼働させ続けたり、パイプを占有したりすることを防ぐ。
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
