using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Shared out-of-process bridge launcher (ADR-0010, generalized in ADR-0019): runs
/// <c>dotnet run --project &lt;bridgeProjectRelativePath&gt; -- &lt;inputPath&gt; &lt;outputJsonPath&gt;</c>
/// and returns the JSON the bridge wrote. Used by both <see cref="ExternalFbxLoader"/> and
/// <see cref="ExternalGltfLoader"/> so the two model formats aren't loaded via two different
/// strategies (one in-process, one out-of-process) inside the same VSIX.
/// </summary>
internal static class ExternalBridgeProcess
{
    public static string RunAndReadJson(string bridgeProjectRelativePath, string inputPath, string outputJsonPath, string repoRoot, TimeSpan timeout)
    {
        RunProcess(bridgeProjectRelativePath, inputPath, outputJsonPath, repoRoot, timeout);

        if (!File.Exists(outputJsonPath))
        {
            throw new InvalidOperationException($"インポートブリッジ({bridgeProjectRelativePath})が出力ファイルを生成しませんでした。");
        }

        return File.ReadAllText(outputJsonPath);
    }

    private static void RunProcess(string bridgeProjectRelativePath, string inputPath, string outputJsonPath, string repoRoot, TimeSpan timeout)
    {
        ProcessStartInfo startInfo = new(
            "dotnet",
            $"run --project \"{bridgeProjectRelativePath}\" -- \"{inputPath}\" \"{outputJsonPath}\"")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using Process process = new() { StartInfo = startInfo };

        // Read both streams via the event-based API (BeginOutputReadLine/BeginErrorReadLine)
        // before WaitForExit: reading them synchronously and sequentially here would deadlock if
        // the child fills the OS pipe buffer for the stream being read second while blocked
        // writing to the (unread) first stream.
        StringBuilder stdout = new();
        StringBuilder stderr = new();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool exited = process.WaitForExit((int)timeout.TotalMilliseconds);
        if (!exited)
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            throw new TimeoutException($"インポートブリッジ({bridgeProjectRelativePath})が{timeout.TotalSeconds}秒以内に終了しませんでした。");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"インポートブリッジ({bridgeProjectRelativePath})がエラー終了しました(exit code {process.ExitCode})。\n{stdout}\n{stderr}");
        }
    }
}
