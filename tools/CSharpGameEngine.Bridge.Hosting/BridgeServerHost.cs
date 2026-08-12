using System.IO.Pipes;

namespace CSharpGameEngine.Bridge.Hosting;

/// <summary>
/// Generic named-pipe accept loop for a resident bridge process (ADR-0023). One connection per
/// request (never a single multiplexed connection with request-ID correlation) - mirrors the
/// "one process per request" mental model the one-shot bridge already used, and lets the VSIX's own
/// already-parallel requests (e.g. <c>SceneHwndHost</c> warming several distinct meshes at once) be
/// handled concurrently here too, with no correlation layer needed on either side. Each connection
/// is dispatched to its own fire-and-forget handler task rather than serviced in the accept loop
/// itself, so slow/concurrent requests never block new connections from being accepted.
///
/// Exits after <paramref name="idleTimeout"/> with no new connections. There is deliberately no
/// separate crash-detection/restart logic: the client (<c>ResidentBridgeClient</c>) treats "can't
/// connect" and "server crashed mid-session" identically - a failed connection attempt just
/// triggers the same spawn-a-new-server path used for a cold start.
/// </summary>
public static class BridgeServerHost {
    public static async Task RunAsync(
        string PipeName,
        Func<string,string,(int ExitCode,string Output)>Handler,
        TimeSpan IdleTimeout,
        CancellationToken CancellationToken=default){
        using CancellationTokenSource IdleCts=CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        IdleCts.CancelAfter(IdleTimeout);

        while(true){
            NamedPipeServerStream Pipe=new(
                PipeName,PipeDirection.InOut,NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,PipeOptions.Asynchronous);

            try {
                await Pipe.WaitForConnectionAsync(IdleCts.Token);
            } catch(OperationCanceledException) when (IdleCts.IsCancellationRequested){
                await Pipe.DisposeAsync();
                return;
            }

            // Reset the idle window now that a connection came in, before handing it off - a burst
            // of requests should keep the server alive for idleTimeout past the *last* one, not the
            // first.
            IdleCts.CancelAfter(IdleTimeout);

            _=HandleConnectionAsync(Pipe,Handler);
        }
    }

    private static async Task HandleConnectionAsync(NamedPipeServerStream Pipe,Func<string,string,(int ExitCode,string Output)>Handler){
        await using(Pipe){
            try {
                BridgeServerRequest Request= await BridgeWireMessage.ReadAsync<BridgeServerRequest>(Pipe,CancellationToken.None);
                var FirstArg=Request.Args.Length>0?Request.Args[0]:string.Empty;
                var OutputPath=Request.Args.Length>1?Request.Args[1]:string.Empty;

                (var ExitCode,var Output)=Handler(FirstArg,OutputPath);

                await BridgeWireMessage.WriteAsync(Pipe,new BridgeServerResponse { ExitCode=ExitCode,Output=Output },CancellationToken.None);
            } catch(Exception Ex){
                // A handler exception (bad input file etc.) must not take the resident process down -
                // report it as a normal failure response instead, exactly like the one-shot bridge's
                // non-zero exit code + stderr text.
                try {
                    await BridgeWireMessage.WriteAsync(Pipe,new BridgeServerResponse { ExitCode=1,Output=Ex.Message },CancellationToken.None);
                } catch {
                    // Client already gone / pipe broken - nothing more to do for this connection.
                }
            }
        }
    }
}
