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
internal static class ResidentBridgeClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SpawnMutexTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SpawnRetryTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Deterministic per-repo, per-bridge pipe name. Must not use <see cref="object.GetHashCode"/>/
    /// <see cref="string.GetHashCode()"/> - both are randomized per-process since modern .NET, so a
    /// second devenv.exe (or the same one restarted) would compute a different name and never find
    /// an already-running server.
    /// </summary>
    internal static string ComputePipeName(string repoRoot, string bridgeProjectRelativePath)
    {
        string seed = $"{Path.GetFullPath(repoRoot)}|{bridgeProjectRelativePath}";
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(seed));
        string hex = BitConverter.ToString(hash).Replace("-", "");
        return $"CSharpGameEngine.Bridge.{hex}";
    }

    internal static bool TryRun(string executablePath, string repoRoot, string bridgeProjectRelativePath, string firstArg, string outputPath, TimeSpan requestTimeout, out int exitCode, out string output)
    {
        string pipeName = ComputePipeName(repoRoot, bridgeProjectRelativePath);

        if (TryConnectAndRun(pipeName, firstArg, outputPath, requestTimeout, out exitCode, out output))
        {
            return true;
        }

        if (!TrySpawnServer(executablePath, repoRoot, pipeName))
        {
            return false;
        }

        DateTime deadline = DateTime.UtcNow + SpawnRetryTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (TryConnectAndRun(pipeName, firstArg, outputPath, requestTimeout, out exitCode, out output))
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    private static bool TryConnectAndRun(string pipeName, string firstArg, string outputPath, TimeSpan requestTimeout, out int exitCode, out string output)
    {
        exitCode = 0;
        output = string.Empty;

        try
        {
            using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect((int)ConnectTimeout.TotalMilliseconds);

            WriteMessage(pipe, new ResidentBridgeRequest { Args = [firstArg, outputPath] });
            ResidentBridgeResponse response = ReadMessage<ResidentBridgeResponse>(pipe, requestTimeout);

            exitCode = response.ExitCode;
            output = response.Output;
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryConnectOnly(string pipeName, TimeSpan connectTimeout)
    {
        try
        {
            using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect((int)connectTimeout.TotalMilliseconds);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Launches <paramref name="executablePath"/> (the same prebuilt exe <see cref="BridgeExecutableLocator"/>
    /// already found) in <c>--serve</c> mode. Gated by a <see cref="Mutex"/> so N callers racing to
    /// discover "no server running" (e.g. SceneHwndHost warming several distinct meshes in parallel)
    /// spawn at most one process instead of N - losing racers just wait for the winner instead of
    /// each spawning their own, then cleaning up redundant instances afterward.
    /// </summary>
    private static bool TrySpawnServer(string executablePath, string repoRoot, string pipeName)
    {
        using Mutex mutex = new(initiallyOwned: false, $@"Global\{pipeName}.SpawnMutex");

        bool acquired;
        try
        {
            acquired = mutex.WaitOne(SpawnMutexTimeout);
        }
        catch (AbandonedMutexException)
        {
            // A previous holder (e.g. a devenv.exe that crashed mid-spawn) never released this -
            // treat it as acquired rather than failing forever on every future call.
            acquired = true;
        }

        if (!acquired)
        {
            // Someone else is presumably spawning right now - TryRun's own retry loop will pick up
            // the connection once it's ready, without us doing anything further here.
            return true;
        }

        try
        {
            if (TryConnectOnly(pipeName, ConnectTimeout))
            {
                // The mutex's previous holder already finished starting a server - nothing to do.
                return true;
            }

            ProcessStartInfo startInfo = new(executablePath, $"--serve \"{pipeName}\"")
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Deliberately NOT redirecting stdout/stderr: nobody drains those pipes for this
                // process's 15-minute idle-timeout lifetime, and an unread OS pipe buffer filling up
                // would block the server's own writes and hang it.
            };
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return false;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void WriteMessage<T>(Stream stream, T value)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value);
        byte[] lengthPrefix = BitConverter.GetBytes(payload.Length);
        stream.Write(lengthPrefix, 0, lengthPrefix.Length);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
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
    private static T ReadMessage<T>(Stream stream, TimeSpan timeout)
    {
        T? result = default;
        Exception? error = null;
        using ManualResetEventSlim completed = new(initialState: false);

        Thread readerThread = new(() =>
        {
            try
            {
                byte[] lengthPrefix = ReadExactly(stream, 4);
                int length = BitConverter.ToInt32(lengthPrefix, 0);
                byte[] payload = ReadExactly(stream, length);

                result = JsonSerializer.Deserialize<T>(payload)
                    ?? throw new InvalidOperationException("常駐ブリッジサーバーからの応答が空でした。");
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                completed.Set();
            }
        })
        { IsBackground = true };
        readerThread.Start();

        if (!completed.Wait(timeout))
        {
            throw new TimeoutException("常駐ブリッジサーバーからの応答がタイムアウトしました。");
        }

        if (error is not null)
        {
            // Preserves the original stack trace/type (unlike a bare `throw error;`) - matters here
            // since TryConnectAndRun's catch filter matches on the exception's original type.
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result!;
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                throw new IOException("常駐ブリッジサーバーとの接続が予期せず切断されました。");
            }

            offset += read;
        }

        return buffer;
    }

    private sealed class ResidentBridgeRequest
    {
        public string[] Args { get; set; } = [];
    }

    private sealed class ResidentBridgeResponse
    {
        public int ExitCode { get; set; }

        public string Output { get; set; } = "";
    }
}
