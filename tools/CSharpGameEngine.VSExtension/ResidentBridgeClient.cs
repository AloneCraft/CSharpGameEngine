using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 常駐 bridge server の client 側（ADR-0023）。呼び出しごとに OS process の作成と .NET runtime の起動コストを負担する代わりに、named pipe 経由で実行中の bridge process を再利用します。リクエストと結果の DTO は net10 host と共有する netstandard2.0 Contracts assembly にあります。送信前の失敗なら新しい単発リクエストを使えますが、リクエストの byte を1つでも送信した後の timeout/crash/protocol failure は終端エラーとし、再試行しません。
/// </summary>
internal static class ResidentBridgeClient {
    private const int MaximumPayloadBytes=16*1024*1024;
    private static readonly TimeSpan ConnectTimeout=TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SpawnMutexTimeout=TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SpawnRetryTimeout=TimeSpan.FromSeconds(10);

    /// <summary>
    /// リポジトリ、bridge、所有者ごとに決まる pipe 名。所有者は Visual Studio プロセスです。bridge はその Visual Studio 自身の extension install から起動され、その終了時に終了するため、別の Visual Studio（別 install、場合によっては別バージョン）は接続しません。<see cref="object.GetHashCode"/>/<see cref="string.GetHashCode()"/> は modern .NET でプロセスごとに値がランダム化されるため、使用してはいけません。
    /// </summary>
    internal static string ComputePipeName(string RepoRoot,string BridgeProjectRelativePath,int OwnerProcessId){
        var Seed= $"{Path.GetFullPath(RepoRoot)}|{BridgeProjectRelativePath}|{OwnerProcessId}";
        using var Sha256=SHA256.Create();
        var Hash=Sha256.ComputeHash(Encoding.UTF8.GetBytes(Seed));
        var Hex=BitConverter.ToString(Hash).Replace("-","");
        return $"CSharpGameEngine.Bridge.{Hex}";
    }

    internal static ResidentBridgeAttemptStatus TryRun(string ExecutablePath,string RepoRoot,string BridgeProjectRelativePath,string FirstArg,string OutputPath,TimeSpan RequestTimeout,out int ExitCode,out string Output){
        return TryRun(ExecutablePath,RepoRoot,BridgeProjectRelativePath,[FirstArg,OutputPath],RequestTimeout,out ExitCode,out Output);
    }

    internal static ResidentBridgeAttemptStatus TryRun(string ExecutablePath,string RepoRoot,string BridgeProjectRelativePath,string[]Arguments,TimeSpan RequestTimeout,out int ExitCode,out string Output){
        return TryRun(
            ExecutablePath,RepoRoot,BridgeProjectRelativePath,Arguments,RequestTimeout,
            CancellationToken.None,out ExitCode,out Output);
    }

    internal static ResidentBridgeAttemptStatus TryRun(
        string ExecutablePath,string RepoRoot,string BridgeProjectRelativePath,string[]Arguments,
        TimeSpan RequestTimeout,CancellationToken CancellationToken,out int ExitCode,out string Output){
        ResidentBridgeAttemptStatus Status=TryRunDetailed(
            ExecutablePath,RepoRoot,BridgeProjectRelativePath,Arguments,RequestTimeout,
            CancellationToken,out AuthoringHostResult?Result);
        ExitCode=Result is null
            ?0
            :Result.State==AuthoringHostTerminalState.Succeeded
                ?Result.ExitCode
                :Math.Max(1,Result.ExitCode);
        Output=Result is null?string.Empty:FormatOutput(Result);
        return Status;
    }

    internal static ResidentBridgeAttemptStatus TryRunDetailed(
        string ExecutablePath,string RepoRoot,string BridgeProjectRelativePath,string[]Arguments,
        TimeSpan RequestTimeout,CancellationToken CancellationToken,
        out AuthoringHostResult?Result){
        if(Arguments is null){ throw new ArgumentNullException(nameof(Arguments)); }
        CancellationToken.ThrowIfCancellationRequested();
        var PipeName=ComputePipeName(RepoRoot,BridgeProjectRelativePath,OwnerProcessId);
        var Request=new AuthoringHostRequest {
            RequestId=Guid.NewGuid(),
            Operation=ClassifyOperation(BridgeProjectRelativePath),
            TimeoutMilliseconds=Math.Min(900_000,Math.Max(1,GetTimeoutMilliseconds(RequestTimeout))),
            Arguments=Arguments,
        };

        var Status=TryConnectAndRun(
            PipeName,Request,RequestTimeout,CancellationToken,out Result);
        if(Status!=ResidentBridgeAttemptStatus.Unavailable){
            return Status;
        }

        if(!TrySpawnServer(ExecutablePath,RepoRoot,PipeName)){
            return ResidentBridgeAttemptStatus.Unavailable;
        }

        var Deadline=DateTime.UtcNow+SpawnRetryTimeout;
        while(DateTime.UtcNow<Deadline){
            CancellationToken.ThrowIfCancellationRequested();
            Status=TryConnectAndRun(
                PipeName,Request,RequestTimeout,CancellationToken,out Result);
            if(Status!=ResidentBridgeAttemptStatus.Unavailable){
                return Status;
            }

            if(CancellationToken.WaitHandle.WaitOne(100)){
                CancellationToken.ThrowIfCancellationRequested();
            }
        }

        return ResidentBridgeAttemptStatus.Unavailable;
    }

    internal static AuthoringHostOperation ClassifyOperation(string BridgeProjectRelativePath){
        if(BridgeProjectRelativePath.IndexOf(".Import.",StringComparison.OrdinalIgnoreCase)>=0){
            return AuthoringHostOperation.Import;
        }
        if(BridgeProjectRelativePath.IndexOf("Bake",StringComparison.OrdinalIgnoreCase)>=0||
            BridgeProjectRelativePath.IndexOf("Physics.Cli",StringComparison.OrdinalIgnoreCase)>=0){
            return AuthoringHostOperation.Bake;
        }
        return AuthoringHostOperation.Preview;
    }

    internal static ResidentBridgeAttemptStatus ClassifyTransportFailure(bool Submitted,Exception Error){
        if(!Submitted){ return ResidentBridgeAttemptStatus.Unavailable; }
        if(Error is TimeoutException){ return ResidentBridgeAttemptStatus.TimedOut; }
        if(Error is IOException or UnauthorizedAccessException){ return ResidentBridgeAttemptStatus.HostCrashed; }
        return ResidentBridgeAttemptStatus.ProtocolMismatch;
    }

    private static ResidentBridgeAttemptStatus TryConnectAndRun(
        string PipeName,AuthoringHostRequest Request,TimeSpan RequestTimeout,
        CancellationToken CancellationToken,out AuthoringHostResult?Result){
        Result=null;
        var Submitted=false;

        try {
            using NamedPipeClientStream Pipe=new(".",PipeName,PipeDirection.InOut,PipeOptions.None);
            Pipe.Connect(GetTimeoutMilliseconds(ConnectTimeout));

            Submitted=true;
            WriteMessage(Pipe,Request);
            AuthoringHostResult Response=ReadMessage<AuthoringHostResult>(
                Pipe,RequestTimeout,CancellationToken);
            Result=Response;

            if(Response.ProtocolVersion!=AuthoringHostProtocol.CurrentVersion||Response.RequestId!=Request.RequestId){
                return ResidentBridgeAttemptStatus.ProtocolMismatch;
            }

            return Response.State switch {
                AuthoringHostTerminalState.Succeeded or AuthoringHostTerminalState.Failed or AuthoringHostTerminalState.Cancelled=>ResidentBridgeAttemptStatus.Completed,
                AuthoringHostTerminalState.TimedOut=>ResidentBridgeAttemptStatus.TimedOut,
                AuthoringHostTerminalState.HostCrashed=>ResidentBridgeAttemptStatus.HostCrashed,
                AuthoringHostTerminalState.VersionMismatch=>ResidentBridgeAttemptStatus.ProtocolMismatch,
                _=>ResidentBridgeAttemptStatus.ProtocolMismatch,
            };
        } catch(Exception Ex) when (Ex is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException){
            return ClassifyTransportFailure(Submitted,Ex);
        }
    }

    private static string FormatOutput(AuthoringHostResult Response){
        StringBuilder Builder=new(Response.Output??string.Empty);
        foreach(var Diagnostic in Response.Diagnostics??Array.Empty<AuthoringHostDiagnostic>()){
            if(Builder.Length>0){ Builder.AppendLine(); }
            Builder.Append(Diagnostic.Code).Append(": ").Append(Diagnostic.Message);
        }
        return Builder.ToString();
    }

    private static readonly int OwnerProcessId=CurrentProcessId();

    private static int CurrentProcessId(){
        using Process Current=Process.GetCurrentProcess();
        return Current.Id;
    }

    /// <summary>このプロセスが終了するまで（または idle timeout になるまで）<paramref name="PipeName"/> を提供する bridge のコマンドライン。</summary>
    internal static string ServeArguments(string PipeName,int OwnerProcessId)=>
        $"--serve \"{PipeName}\" --parent-process-id {OwnerProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static bool TryConnectOnly(string PipeName,TimeSpan ConnectTimeout){
        try {
            using NamedPipeClientStream Pipe=new(".",PipeName,PipeDirection.InOut,PipeOptions.None);
            Pipe.Connect(GetTimeoutMilliseconds(ConnectTimeout));
            return true;
        } catch(Exception Ex) when (Ex is TimeoutException or IOException or UnauthorizedAccessException){
            return false;
        }
    }

    /// <summary>
    /// <see cref="BridgeExecutableLocator"/> がすでに特定した同じビルド済み exe である <paramref name="ExecutablePath"/> を、<c>--serve</c> mode で起動します。「server が起動していない」と同時に判定する N 個の呼び出し元（例：SceneHwndHost が複数の異なる mesh を並列に準備する場合）が N 個のプロセスを起動しないよう、<see cref="Mutex"/> で制御します。競合に負けた呼び出し元は勝者を待ち、個別に起動してから余分なインスタンスを片付けることはありません。
    /// </summary>
    private static bool TrySpawnServer(string ExecutablePath,string RepoRoot,string PipeName){
        using Mutex Mutex=new(initiallyOwned:false, $@"Global\{PipeName}.SpawnMutex");

        bool Acquired;
        try {
            Acquired=Mutex.WaitOne(SpawnMutexTimeout);
        } catch(AbandonedMutexException){
            // 以前の holder（spawn 中にクラッシュした devenv.exe など）が mutex を解放しなかった場合は、
            // 後続の呼び出しが毎回失敗し続けないよう、取得済みとして扱います。
            Acquired=true;
        }

        if(!Acquired){
            // ほかのプロセスが server を起動中と思われます。TryRun 自身の再試行ループが、
            // ここで追加処理をしなくても、準備完了後に接続します。
            return true;
        }

        try {
            if(TryConnectOnly(PipeName,ConnectTimeout)){
                // mutex の前の holder がすでに server の起動を終えているため、何もする必要はありません。
                return true;
            }

            ProcessStartInfo StartInfo=new(ExecutablePath,ServeArguments(PipeName,OwnerProcessId)){
                WorkingDirectory=RepoRoot,
                UseShellExecute=false,
                CreateNoWindow=true,
                // stdout/stderr は意図的にリダイレクトしません。このプロセスの15分間の idle timeout 中、
                // それらの pipe を読み取る処理はありません。未読の OS pipe buffer が満杯になると、
                // server 自身の書き込みがブロックされ、停止してしまうためです。
            };
            Process.Start(StartInfo);
            return true;
        } catch(Exception Ex) when (Ex is InvalidOperationException or Win32Exception){
            return false;
        } finally {
            Mutex.ReleaseMutex();
        }
    }

    internal static void WriteMessage<T>(Stream Stream,T Value){
        var Payload=JsonSerializer.SerializeToUtf8Bytes(Value);
        if(Payload.Length<1||Payload.Length>MaximumPayloadBytes){
            throw new InvalidOperationException(VsixText.Get("ResidentBridge_EmptyResponse"));
        }
        var LengthPrefix=BitConverter.GetBytes(Payload.Length);
        Stream.Write(LengthPrefix,0,LengthPrefix.Length);
        Stream.Write(Payload,0,Payload.Length);
        Stream.Flush();
    }

    /// <summary>
    /// 単純な非 overlapped <see cref="NamedPipeClientStream"/>（<see cref="TryConnectAndRun"/> を参照）では <see cref="PipeStream.ReadTimeout"/> が機能しないため、読み取りをバックグラウンド <see cref="Thread"/> で実行し、ここでは timeout を設定して待機します。<c>Task.Run(...).Wait()</c>（VSTHRD002 の対象）ではなく、通常の <see cref="Thread"/>/<see cref="ManualResetEventSlim"/> を意図的に使います。このクラスは UI thread（例：AssetPreviewHwndHost の単発プレビュー）とバックグラウンド thread（SceneHwndHost の並列 warm-up）の両方から呼び出されます。Task を同期的に待つ場合に VS が推奨する <c>ThreadHelper.JoinableTaskFactory.Run</c> は、ここでは機能しないことが分かりました。実行中の VS host 外（例：xunit）で直接測定すると、このメソッド自身の timeout と同じ約10秒を毎回要しました。つまり失敗が見えないまま常駐経路が単発 fallback に隠れ、server を実際には再利用できていませんでした。通常の <see cref="Thread"/> は VS/JoinableTaskContext の ambient state に依存せず、そもそも UI thread affinity を保つ必要もありません（待機する処理は純粋なバックグラウンド I/O）。そのため、analyzer suppression を追加せずに VSTHRD002 が対象とするデッドロックの種類と JoinableTaskFactory 依存の両方を回避できます。
    /// </summary>
    internal static T ReadMessage<T>(
        Stream Stream,TimeSpan Timeout,CancellationToken CancellationToken){
        T?Result=default;
        Exception?Error=null;
        using ManualResetEventSlim Completed=new(initialState:false);

        Thread ReaderThread=new(()=>{
            try {
                var LengthPrefix=ReadExactly(Stream,4);
                var Length=BitConverter.ToInt32(LengthPrefix,0);
                if(Length<1||Length>MaximumPayloadBytes){
                    throw new InvalidOperationException(VsixText.Get("ResidentBridge_EmptyResponse"));
                }
                var Payload=ReadExactly(Stream,Length);

                Result=JsonSerializer.Deserialize<T>(Payload)
                    ??throw new InvalidOperationException(VsixText.Get("ResidentBridge_EmptyResponse"));
            } catch(Exception Ex){
                Error=Ex;
            } finally {
                Completed.Set();
            }
        }){ IsBackground=true };
        ReaderThread.Start();

        if(!Completed.Wait(Timeout,CancellationToken)){
            throw new TimeoutException(VsixText.Get("ResidentBridge_ResponseTimedOut"));
        }

        if(Error is not null){
            // 素の `throw error;` と異なり、元の stack trace と型を維持します。この箇所では、
            // TryConnectAndRun の catch filter が例外の元の型に一致するため重要です。
            ExceptionDispatchInfo.Capture(Error).Throw();
        }

        return Result!;
    }

    private static byte[]ReadExactly(Stream Stream,int Count){
        var Buffer=new byte[Count];
        var Offset=0;
        while(Offset<Count){
            var Read=Stream.Read(Buffer,Offset,Count-Offset);
            if(Read==0){
                throw new IOException(VsixText.Get("ResidentBridge_Disconnected"));
            }

            Offset+=Read;
        }

        return Buffer;
    }
    private static int GetTimeoutMilliseconds(TimeSpan Timeout){
        var Milliseconds=Math.Min(int.MaxValue,Math.Max(0L,Timeout.Ticks/TimeSpan.TicksPerMillisecond));
        return(int)Milliseconds;
    }
}

internal enum ResidentBridgeAttemptStatus:byte {
    Unavailable,
    Completed,
    TimedOut,
    HostCrashed,
    ProtocolMismatch,
}
