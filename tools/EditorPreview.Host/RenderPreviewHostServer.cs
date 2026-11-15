using System.IO.Pipes;
using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.EditorPreview.Host;

/// <summary>
/// パイプ接続ごとに長さプレフィックス付きの要求を 1 つ受け付ける。プロセスがすべてのセッションを所有し、キャンセル、親プロセスの終了、または処理されない通信終了で受付ループが終わる際にセッションを解放する。
/// </summary>
public static class RenderPreviewHostServer {
    public static Task RunAsync(string PipeName,CancellationToken CancellationToken=default)=>
        RunAsync(PipeName,Open,CancellationToken);

    /// <summary>Open 要求が求めた場合はランタイムビュー (ADR-0070 B1)、そうでなければシーンプレビュー。</summary>
    public static IRenderPreviewSession Open(RenderPreviewHostRequest Request)=>
        Request.RuntimeView is null
            ?RenderPreviewRenderSession.Open(Request)
            :RenderPreviewRuntimeSession.Open(Request);

    public static Task RunAsync(
        string PipeName,Func<RenderPreviewHostRequest,IRenderPreviewSession>Factory,
        CancellationToken CancellationToken=default)=>
        RunAsync(PipeName,Factory,BridgeWireMessage.RequestReceiveTimeout,CancellationToken);

    internal static async Task RunAsync(
        string PipeName,Func<RenderPreviewHostRequest,IRenderPreviewSession>Factory,
        TimeSpan RequestReceiveTimeout,CancellationToken CancellationToken){
        if(string.IsNullOrWhiteSpace(PipeName)){ throw new ArgumentException("Pipe name is required.",nameof(PipeName)); }
        ArgumentNullException.ThrowIfNull(Factory);
        using RenderPreviewHostSessionTable Sessions=new(Factory);
        while(!CancellationToken.IsCancellationRequested){
            await using NamedPipeServerStream Pipe=new(
                PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
            try {
                await Pipe.WaitForConnectionAsync(CancellationToken);
            } catch(OperationCanceledException)when(CancellationToken.IsCancellationRequested){
                break;
            }

            try {
                // audit AUD-003：ホストが持つパイプインスタンスは 1 つだけなので、要求の途中で停止したクライアントは、
                // ホストが停止するか受信期限に達した時に、そのパイプを解放しなければならない。
                RenderPreviewHostRequest Request=await BridgeWireMessage.ReadRequestAsync<RenderPreviewHostRequest>(
                    Pipe,RequestReceiveTimeout,CancellationToken);
                RenderPreviewHostResponse Response=Sessions.Handle(Request);
                await BridgeWireMessage.WriteAsync(Pipe,Response,CancellationToken);
                if(Request.Command==RenderPreviewHostCommand.Close
                    &&Response.State==RenderPreviewHostResultState.Succeeded){
                    return;
                }
            } catch(OperationCanceledException)when(CancellationToken.IsCancellationRequested){
                break;
            } catch(OperationCanceledException){
                // クライアントが要求全体を期限内に送らなかった場合、その接続を破棄して次の要求を処理する。
                continue;
            } catch(Exception Error)when(
                Error is IOException or InvalidDataException or InvalidOperationException
                    or System.Text.Json.JsonException){
                if(!Pipe.IsConnected){ continue; }
                try {
                    await BridgeWireMessage.WriteAsync(Pipe,new RenderPreviewHostResponse {
                        State=RenderPreviewHostResultState.Failed,
                        DiagnosticCode="PREVIEW_PROTOCOL_FAILURE",
                        Message=Error.Message,
                    },CancellationToken.None);
                } catch(IOException){ }
            }
        }
    }
}
