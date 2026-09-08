using System.IO.Pipes;
using System.Collections.Concurrent;
using CSharpGameEngine.Contracts;

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
/// Stops accepting after <paramref name="idleTimeout"/> with no new connections and drains accepted
/// requests before exiting. The client distinguishes unavailability before request submission from
/// timeout or crash after submission; only the former may start a fresh one-shot request.
/// </summary>
public static class BridgeServerHost {
    public static Task RunAsync(
        string PipeName,
        Func<string,string,(int ExitCode,string Output)>Handler,
        TimeSpan IdleTimeout,
        CancellationToken CancellationToken=default)=>RunArgumentsAsync(
            PipeName,
            (Arguments,_)=>Handler(
                Arguments.Length>0?Arguments[0]:string.Empty,
                Arguments.Length>1?Arguments[1]:string.Empty),
            IdleTimeout,CancellationToken);

    public static async Task RunArgumentsAsync(
        string PipeName,
        Func<string[],CancellationToken,(int ExitCode,string Output)>Handler,
        TimeSpan IdleTimeout,
        CancellationToken CancellationToken=default){
        using var IdleCts=CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        IdleCts.CancelAfter(IdleTimeout);
        ConcurrentDictionary<Task,byte> ActiveRequests=new();

        while(true){
            NamedPipeServerStream Pipe=new(
                PipeName,PipeDirection.InOut,NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,PipeOptions.Asynchronous);

            try {
                await Pipe.WaitForConnectionAsync(IdleCts.Token);
            } catch(OperationCanceledException) when (IdleCts.IsCancellationRequested){
                await Pipe.DisposeAsync();
                await Task.WhenAll(ActiveRequests.Keys);
                return;
            }

            // Reset the idle window now that a connection came in, before handing it off - a burst
            // of requests should keep the server alive for idleTimeout past the *last* one, not the
            // first.
            IdleCts.CancelAfter(IdleTimeout);

            var RequestTask=HandleConnectionAsync(Pipe,Handler);
            ActiveRequests.TryAdd(RequestTask,0);
            _=RequestTask.ContinueWith(
                Completed=>ActiveRequests.TryRemove(Completed,out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static async Task HandleConnectionAsync(
        NamedPipeServerStream Pipe,
        Func<string[],CancellationToken,(int ExitCode,string Output)>Handler){
        await using(Pipe){
            try {
                var Request=await BridgeWireMessage.ReadAsync<AuthoringHostRequest>(Pipe,CancellationToken.None);
                using CancellationTokenSource ClientConnection=new();
                _=CancelWhenClientDisconnectsAsync(Pipe,ClientConnection);
                var Response=await AuthoringHostSession.RunAsync(
                    Request,
                    async(ValidatedRequest,_,ExecutionToken)=>{
                        (var ExitCode,var Output)=await Task.Run(
                            ()=>Handler(ValidatedRequest.Arguments,ExecutionToken),ExecutionToken);
                        return new AuthoringHostExecutionResult(ExitCode,Output);
                    },ClientConnection.Token);
                await BridgeWireMessage.WriteAsync(Pipe,Response,CancellationToken.None);
            } catch(Exception Ex){
                // A handler exception (bad input file etc.) must not take the resident process down -
                // report it as a normal failure response instead, exactly like the one-shot bridge's
                // non-zero exit code + stderr text.
                try {
                    await BridgeWireMessage.WriteAsync(Pipe,new AuthoringHostResult {
                        State=AuthoringHostTerminalState.Failed,
                        ExitCode=1,
                        Output=Ex.Message,
                        Diagnostics=[new AuthoringHostDiagnostic {
                            Severity=AuthoringHostDiagnosticSeverity.Error,
                            Code="HOST_PROTOCOL_FAILURE",
                            Message=Ex.Message,
                        }],
                    },CancellationToken.None);
                } catch {
                    // Client already gone / pipe broken - nothing more to do for this connection.
                }
            }
        }
    }

    private static async Task CancelWhenClientDisconnectsAsync(
        NamedPipeServerStream Pipe,CancellationTokenSource Cancellation){
        byte[] Probe=new byte[1];
        try {
            int Read=await Pipe.ReadAsync(Probe,0,1,CancellationToken.None);
            if(Read==0){ TryCancel(Cancellation); }
        } catch(Exception Exception)when(
            Exception is IOException or ObjectDisposedException or InvalidOperationException){
            TryCancel(Cancellation);
        }
    }

    private static void TryCancel(CancellationTokenSource Cancellation){
        try { Cancellation.Cancel(); }
        catch(ObjectDisposedException){ }
    }
}
