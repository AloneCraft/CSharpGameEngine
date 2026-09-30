using System.IO.Pipes;
using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.EditorPreview.Host;

/// <summary>
/// One length-prefixed request per pipe connection. The process owns all sessions and releases
/// them when cancellation, parent shutdown, or an unhandled transport exit ends the accept loop.
/// </summary>
public static class RenderPreviewHostServer {
    public static Task RunAsync(string PipeName,CancellationToken CancellationToken=default)=>
        RunAsync(PipeName,Open,CancellationToken);

    /// <summary>A runtime view (ADR-0070 B1) when the Open request asks for one, otherwise the scene preview.</summary>
    public static IRenderPreviewSession Open(RenderPreviewHostRequest Request)=>
        Request.RuntimeView is null
            ?RenderPreviewRenderSession.Open(Request)
            :RenderPreviewRuntimeSession.Open(Request);

    public static async Task RunAsync(
        string PipeName,Func<RenderPreviewHostRequest,IRenderPreviewSession>Factory,
        CancellationToken CancellationToken=default){
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
                RenderPreviewHostRequest Request=
                    await BridgeWireMessage.ReadAsync<RenderPreviewHostRequest>(Pipe,CancellationToken.None);
                RenderPreviewHostResponse Response=Sessions.Handle(Request);
                await BridgeWireMessage.WriteAsync(Pipe,Response,CancellationToken.None);
                if(Request.Command==RenderPreviewHostCommand.Close
                    &&Response.State==RenderPreviewHostResultState.Succeeded){
                    return;
                }
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
