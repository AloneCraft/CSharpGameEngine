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

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Client half of the resident bridge server (ADR-0023): tries to reuse an already-running bridge
/// process over a named pipe instead of paying OS-process-creation + .NET runtime startup cost on
/// every call, the way <see cref="ExternalBridgeProcess"/>'s one-shot mode does. Deliberately hand-
/// mirrors (rather than shares an assembly with) the wire DTOs/framing that
/// <c>CSharpGameEngine.Bridge.Hosting</c> (net10.0, server side) uses - this project can't
/// <c>ProjectReference</c> a net10.0 assembly, matching the same "duplicate now, consolidate later"
/// approach ADR-0019/ADR-0021 already use across the net472/net10.0 boundary. Every public entry
/// point here is a pure speed optimization: any failure just returns <see langword="false"/>, and
/// <see cref="ExternalBridgeProcess.RunProcess"/> falls back to its already-proven one-shot exe
/// launch - correctness never depends on this class working.
/// </summary>
internal static class ResidentBridgeClient {
    private static readonly TimeSpan ConnectTimeout=TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SpawnMutexTimeout=TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SpawnRetryTimeout=TimeSpan.FromSeconds(10);

    /// <summary>
    /// Deterministic per-repo, per-bridge pipe name. Must not use <see cref="object.GetHashCode"/>/
    /// <see cref="string.GetHashCode()"/> - both are randomized per-process since modern .NET, so a
    /// second devenv.exe (or the same one restarted) would compute a different name and never find
    /// an already-running server.
    /// </summary>
    internal static string ComputePipeName(string RepoRoot,string BridgeProjectRelativePath){
        var Seed= $"{Path.GetFullPath(RepoRoot)}|{BridgeProjectRelativePath}";
        using SHA256 Sha256=SHA256.Create();
        var Hash=Sha256.ComputeHash(Encoding.UTF8.GetBytes(Seed));
        var Hex=BitConverter.ToString(Hash).Replace("-","");
        return $"CSharpGameEngine.Bridge.{Hex}";
    }

    internal static bool TryRun(string ExecutablePath,string RepoRoot,string BridgeProjectRelativePath,string FirstArg,string OutputPath,TimeSpan RequestTimeout,out int ExitCode,out string Output){
        var PipeName=ComputePipeName(RepoRoot,BridgeProjectRelativePath);

        if(TryConnectAndRun(PipeName,FirstArg,OutputPath,RequestTimeout,out ExitCode,out Output)){
            return true;
        }

        if(!TrySpawnServer(ExecutablePath,RepoRoot,PipeName)){
            return false;
        }

        DateTime Deadline=DateTime.UtcNow+SpawnRetryTimeout;
        while(DateTime.UtcNow<Deadline){
            if(TryConnectAndRun(PipeName,FirstArg,OutputPath,RequestTimeout,out ExitCode,out Output)){
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    private static bool TryConnectAndRun(string PipeName,string FirstArg,string OutputPath,TimeSpan RequestTimeout,out int ExitCode,out string Output){
        ExitCode=0;
        Output=string.Empty;

        try {
            using NamedPipeClientStream Pipe=new(".",PipeName,PipeDirection.InOut,PipeOptions.None);
            Pipe.Connect((int)ConnectTimeout.TotalMilliseconds);

            WriteMessage(Pipe,new ResidentBridgeRequest { Args=[FirstArg,OutputPath]});
            ResidentBridgeResponse Response=ReadMessage<ResidentBridgeResponse>(Pipe,RequestTimeout);

            ExitCode=Response.ExitCode;
            Output=Response.Output;
            return true;
        } catch(Exception Ex) when (Ex is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException){
            return false;
        }
    }

    private static bool TryConnectOnly(string PipeName,TimeSpan ConnectTimeout){
        try {
            using NamedPipeClientStream Pipe=new(".",PipeName,PipeDirection.InOut,PipeOptions.None);
            Pipe.Connect((int)ConnectTimeout.TotalMilliseconds);
            return true;
        } catch(Exception Ex) when (Ex is TimeoutException or IOException or UnauthorizedAccessException){
            return false;
        }
    }

    /// <summary>
    /// Launches <paramref name="ExecutablePath"/> (the same prebuilt exe <see cref="BridgeExecutableLocator"/>
    /// already found) in <c>--serve</c> mode. Gated by a <see cref="Mutex"/> so N callers racing to
    /// discover "no server running" (e.g. SceneHwndHost warming several distinct meshes in parallel)
    /// spawn at most one process instead of N - losing racers just wait for the winner instead of
    /// each spawning their own, then cleaning up redundant instances afterward.
    /// </summary>
    private static bool TrySpawnServer(string ExecutablePath,string RepoRoot,string PipeName){
        using Mutex Mutex=new(initiallyOwned:false, $@"Global\{PipeName}.SpawnMutex");

        bool Acquired;
        try {
            Acquired=Mutex.WaitOne(SpawnMutexTimeout);
        } catch(AbandonedMutexException){
            // A previous holder (e.g. a devenv.exe that crashed mid-spawn) never released this -
            // treat it as acquired rather than failing forever on every future call.
            Acquired=true;
        }

        if(!Acquired){
            // Someone else is presumably spawning right now - TryRun's own retry loop will pick up
            // the connection once it's ready, without us doing anything further here.
            return true;
        }

        try {
            if(TryConnectOnly(PipeName,ConnectTimeout)){
                // The mutex's previous holder already finished starting a server - nothing to do.
                return true;
            }

            ProcessStartInfo StartInfo=new(ExecutablePath, $"--serve \"{PipeName}\""){
                WorkingDirectory=RepoRoot,
                UseShellExecute=false,
                CreateNoWindow=true,
                // Deliberately NOT redirecting stdout/stderr: nobody drains those pipes for this
                // process's 15-minute idle-timeout lifetime, and an unread OS pipe buffer filling up
                // would block the server's own writes and hang it.
            };
            Process.Start(StartInfo);
            return true;
        } catch(Exception Ex) when (Ex is InvalidOperationException or Win32Exception){
            return false;
        } finally {
            Mutex.ReleaseMutex();
        }
    }

    private static void WriteMessage<T>(Stream Stream,T Value){
        var Payload=JsonSerializer.SerializeToUtf8Bytes(Value);
        var LengthPrefix=BitConverter.GetBytes(Payload.Length);
        Stream.Write(LengthPrefix,0,LengthPrefix.Length);
        Stream.Write(Payload,0,Payload.Length);
        Stream.Flush();
    }

    /// <summary>
    /// A plain, non-overlapped <see cref="NamedPipeClientStream"/> (see <see cref="TryConnectAndRun"/>)
    /// has no working <see cref="PipeStream.ReadTimeout"/>, so the read runs on a background
    /// <see cref="Thread"/> and this waits on it with a timeout instead. Deliberately a raw
    /// <see cref="Thread"/>/<see cref="ManualResetEventSlim"/> rather than <c>Task.Run(...).Wait()</c>
    /// (VSTHRD002 territory): this class is called both from the UI thread (e.g.
    /// AssetPreviewHwndHost's one-shot preview) and from background threads (SceneHwndHost's
    /// parallel warm-up), and the VS-blessed fix for a Task-based synchronous wait -
    /// <c>ThreadHelper.JoinableTaskFactory.Run</c> - turned out NOT to work here: measured directly,
    /// every call took ~10s (this method's own timeout) when exercised outside a running VS host
    /// (e.g. from xunit), meaning it was silently failing and masking the resident path entirely
    /// behind the one-shot fallback rather than actually reusing the server. A raw
    /// <see cref="Thread"/> has no dependency on any VS/JoinableTaskContext ambient state and no
    /// UI-thread affinity to protect in the first place (the awaited work is pure background I/O),
    /// so it sidesteps both the deadlock class VSTHRD002 guards against and the JoinableTaskFactory
    /// dependency, without needing an analyzer suppression.
    /// </summary>
    private static T ReadMessage<T>(Stream Stream,TimeSpan Timeout){
        T?Result=default;
        Exception?Error=null;
        using ManualResetEventSlim Completed=new(initialState:false);

        Thread ReaderThread=new(()=>{
            try {
                var LengthPrefix=ReadExactly(Stream,4);
                var Length=BitConverter.ToInt32(LengthPrefix,0);
                var Payload=ReadExactly(Stream,Length);

                Result=JsonSerializer.Deserialize<T>(Payload)
                    ??throw new InvalidOperationException("常駐ブリッジサーバーからの応答が空でした。");
            } catch(Exception Ex){
                Error=Ex;
            } finally {
                Completed.Set();
            }
        }){ IsBackground=true };
        ReaderThread.Start();

        if(!Completed.Wait(Timeout)){
            throw new TimeoutException("常駐ブリッジサーバーからの応答がタイムアウトしました。");
        }

        if(Error is not null){
            // Preserves the original stack trace/type (unlike a bare `throw error;`) - matters here
            // since TryConnectAndRun's catch filter matches on the exception's original type.
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
                throw new IOException("常駐ブリッジサーバーとの接続が予期せず切断されました。");
            }

            Offset+=Read;
        }

        return Buffer;
    }

    private sealed class ResidentBridgeRequest {
        public string[]Args { get; set;}=[];
    }

    private sealed class ResidentBridgeResponse {
        public int ExitCode { get; set;}

        public string Output { get; set;}="";
    }
}
