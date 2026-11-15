using System.IO.Pipes;
using System.Collections.Concurrent;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Bridge.Hosting;

/// <summary>
/// 常駐ブリッジプロセス用の汎用名前付きパイプ受付ループ (ADR-0023)。要求ごとに 1 接続を使い、要求 ID で相関させる単一の多重化接続は使わない。既存の 1 回限りのブリッジが採用していた「要求ごとに 1 プロセス」という考え方に沿い、VSIX 側ですでに並列化された複数の要求（例：<c>SceneHwndHost</c> が複数の別メッシュを同時に準備する処理）も、どちら側にも相関レイヤーを設けず並行処理できる。各接続は受付ループ内で処理せず、専用の fire-and-forget ハンドラータスクへ振り分けるため、遅い要求や並列要求が新しい接続の受付を妨げない。
///
/// <paramref name="idleTimeout"/> の間、新しい接続がなければ受付を停止し、受付済み要求を処理してから終了する。クライアントは要求送信前の利用不能と、送信後のタイムアウトまたはクラッシュを区別する。新しい 1 回限りの要求を開始できるのは前者だけである。
///
/// 所有者プロセス（ブリッジを起動した Visual Studio）がある場合、その終了時にも同じように停止する。Visual Studio 終了後もブリッジが残ると、次回の配置で拡張機能のファイルがロックされたままになるためである。
/// </summary>
public static class BridgeServerHost {
    public static Task RunAsync(
        string PipeName,
        Func<string,string,(int ExitCode,string Output)>Handler,
        TimeSpan IdleTimeout,
        CancellationToken CancellationToken=default,
        int?OwnerProcessId=null)=>RunArgumentsAsync(
            PipeName,
            (Arguments,_)=>Handler(
                Arguments.Length>0?Arguments[0]:string.Empty,
                Arguments.Length>1?Arguments[1]:string.Empty),
            IdleTimeout,CancellationToken,OwnerProcessId);

    public static async Task RunArgumentsAsync(
        string PipeName,
        Func<string[],CancellationToken,(int ExitCode,string Output)>Handler,
        TimeSpan IdleTimeout,
        CancellationToken CancellationToken=default,
        int?OwnerProcessId=null){
        // 呼び出し元のトークンと所有者プロセスが終了すると、受信中の要求も含め、すべての処理が終了する。
        // idle の時間枠で受付ループだけが終了し、その後、受付済み接続の処理を完了する。
        using var LifetimeCts=CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        using var Owner=ParentProcessLifetime.Attach(OwnerProcessId,LifetimeCts);
        using var IdleCts=CancellationTokenSource.CreateLinkedTokenSource(LifetimeCts.Token);
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

            // 接続が到着したら、引き渡す前に idle の時間枠をリセットする。要求が連続して届く場合、最後の要求から idleTimeout が経過するまで
            // サーバーを稼働させるべきであり、最初の要求からではない。
            // 最初に行う。
            IdleCts.CancelAfter(IdleTimeout);

            var RequestTask=HandleConnectionAsync(Pipe,Handler,LifetimeCts.Token);
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
        Func<string[],CancellationToken,(int ExitCode,string Output)>Handler,
        CancellationToken Lifetime){
        await using(Pipe){
            AuthoringHostRequest Request;
            try {
                // 受付ループは終了前に受付済み接続を処理する。そのため受信処理はサーバーの
                // 寿命が終わるか期限に達した時点で終了しなければならない (audit AUD-003)。その時点で未完了の要求には応答者がいない。
                // idle の時間枠では受信を終了しない。その時間枠の後に到着した要求にも応答する。
                Request=await BridgeWireMessage.ReadRequestAsync<AuthoringHostRequest>(
                    Pipe,BridgeWireMessage.RequestReceiveTimeout,Lifetime);
            } catch(OperationCanceledException){
                return;
            } catch(Exception Ex){
                await TryWriteFailureAsync(Pipe,Ex.Message);
                return;
            }
            try {
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
                // ハンドラーの例外（入力ファイルの不備など）で常駐プロセスを終了させてはならない。
                // 代わりに、1 回限りのブリッジと同じように、通常の失敗応答として
                // 0 以外の終了コードと標準エラーのテキストを返す。
                await TryWriteFailureAsync(Pipe,Ex.Message);
            }
        }
    }

    private static async Task TryWriteFailureAsync(NamedPipeServerStream Pipe,string Message){
        try {
            await BridgeWireMessage.WriteAsync(Pipe,new AuthoringHostResult {
                State=AuthoringHostTerminalState.Failed,
                ExitCode=1,
                Output=Message,
                Diagnostics=[new AuthoringHostDiagnostic {
                    Severity=AuthoringHostDiagnosticSeverity.Error,
                    Code="HOST_PROTOCOL_FAILURE",
                    Message=Message,
                }],
            },CancellationToken.None);
        } catch {
            // クライアントがすでに終了した、またはパイプが切断された場合：この接続に対してこれ以上行うことはない。
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
