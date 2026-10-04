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
/// Client half of the resident bridge server (ADR-0023): tries to reuse an already-running bridge
/// process over a named pipe instead of paying OS-process-creation + .NET runtime startup cost on
/// every call. Request and result DTOs come from the netstandard2.0 Contracts assembly shared with
/// the net10 host. A failure before submission may use a fresh one-shot request; after any request
/// byte can have been submitted, timeout/crash/protocol failure is terminal and is never retried.
/// </summary>
internal static class ResidentBridgeClient {
    private const int MaximumPayloadBytes=16*1024*1024;
    private static readonly TimeSpan ConnectTimeout=TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SpawnMutexTimeout=TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SpawnRetryTimeout=TimeSpan.FromSeconds(10);

    /// <summary>
    /// Deterministic per-repo, per-bridge, per-owner pipe name. The owner is the Visual Studio
    /// process: its bridges run from its own extension install and end with it, so another Visual
    /// Studio (another install, maybe another version) never connects to them. Must not use
    /// <see cref="object.GetHashCode"/>/<see cref="string.GetHashCode()"/> - both are randomized
    /// per-process since modern .NET.
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

    /// <summary>The bridge's command line: it serves <paramref name="PipeName"/> until this process exits (or it idles out).</summary>
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

            ProcessStartInfo StartInfo=new(ExecutablePath,ServeArguments(PipeName,OwnerProcessId)){
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
