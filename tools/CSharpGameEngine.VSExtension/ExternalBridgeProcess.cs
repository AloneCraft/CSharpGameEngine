using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Shared out-of-process bridge launcher (ADR-0010, generalized in ADR-0019/ADR-0021/ADR-0023).
/// Three-tier fallback, each tier strictly a robustness net under the previous, so correctness never
/// depends on the fast paths working:
/// <list type="number">
/// <item>Resident named-pipe server (<see cref="ResidentBridgeClient"/>, ADR-0023) - reuses an
/// already-running bridge process instead of paying OS-process-creation + .NET runtime startup cost
/// per call. Only attempted when <c>useResidentServer</c> is <see langword="true"/> and a prebuilt
/// exe was found.</item>
/// <item>Launching the bridge project's already-built apphost <c>.exe</c> directly
/// (<see cref="BridgeExecutableLocator"/>).</item>
/// <item><c>dotnet run --project &lt;bridgeProjectRelativePath&gt; -- &lt;firstArg&gt; &lt;secondArg&gt;</c>
/// - only when no built executable exists yet (e.g. a fresh clone). This matters because `dotnet
/// run` pays MSBuild evaluation cost on effectively every invocation, not just the first - ADR-0010
/// flagged this as a known, deliberately-deferred cost ("体感速度の最適化...は今回のスコープ外とした"),
/// and it became the dominant cost once the シーン tab started loading several meshes (one bridge
/// process per distinct mesh) per open.</item>
/// </list>
/// Used by <see cref="ExternalFbxLoader"/>/<see cref="ExternalGltfLoader"/> (second arg is a JSON
/// path the caller reads back via <see cref="RunAndReadJson"/>) and by <see cref="PrimitiveSpawner"/>
/// (second arg is a <c>.glb</c> path the caller only needs to know exists afterward, via
/// <see cref="RunProducingFile"/>) - so no model/mesh data ever needs to be generated in-process
/// inside <c>devenv.exe</c>.
/// </summary>
internal static class ExternalBridgeProcess
{
    public static string RunAndReadJson(string bridgeProjectRelativePath, string inputPath, string outputJsonPath, string repoRoot, TimeSpan timeout, bool useResidentServer)
    {
        RunProcess(bridgeProjectRelativePath, inputPath, outputJsonPath, repoRoot, timeout, useResidentServer);

        if (!File.Exists(outputJsonPath))
        {
            throw new InvalidOperationException($"インポートブリッジ({bridgeProjectRelativePath})が出力ファイルを生成しませんでした。");
        }

        return File.ReadAllText(outputJsonPath);
    }

    /// <summary>Like <see cref="RunAndReadJson"/> but for a bridge that writes a binary file (e.g. a <c>.glb</c>) the caller will load separately rather than read back as text here.</summary>
    public static void RunProducingFile(string bridgeProjectRelativePath, string firstArg, string outputPath, string repoRoot, TimeSpan timeout, bool useResidentServer)
    {
        RunProcess(bridgeProjectRelativePath, firstArg, outputPath, repoRoot, timeout, useResidentServer);

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException($"生成ブリッジ({bridgeProjectRelativePath})が出力ファイルを生成しませんでした。");
        }
    }

    private static void RunProcess(string bridgeProjectRelativePath, string firstArg, string outputPath, string repoRoot, TimeSpan timeout, bool useResidentServer)
    {
        string? executablePath = BridgeExecutableLocator.FindExecutable(repoRoot, bridgeProjectRelativePath);

        if (executablePath is not null && useResidentServer &&
            ResidentBridgeClient.TryRun(executablePath, repoRoot, bridgeProjectRelativePath, firstArg, outputPath, timeout, out int residentExitCode, out string residentOutput))
        {
            if (residentExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"インポートブリッジ({bridgeProjectRelativePath})がエラー終了しました(exit code {residentExitCode})。\n{residentOutput}");
            }

            return;
        }

        RunOneShotProcess(bridgeProjectRelativePath, firstArg, outputPath, repoRoot, timeout, executablePath);
    }

    private static void RunOneShotProcess(string bridgeProjectRelativePath, string firstArg, string outputPath, string repoRoot, TimeSpan timeout, string? executablePath)
    {
        ProcessStartInfo startInfo = executablePath is not null
            ? new ProcessStartInfo(executablePath, $"\"{firstArg}\" \"{outputPath}\"")
            : new ProcessStartInfo(
                "dotnet",
                $"run --project \"{bridgeProjectRelativePath}\" -- \"{firstArg}\" \"{outputPath}\"");

        startInfo.WorkingDirectory = repoRoot;
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;

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

/// <summary>
/// Finds the newest already-built apphost <c>.exe</c> for a bridge project, if one exists, so
/// <see cref="ExternalBridgeProcess"/> can skip `dotnet run`'s MSBuild evaluation entirely. None of
/// the bridge projects (<c>Import.Fbx.Cli</c>/<c>Import.Gltf.Cli</c>/<c>Primitive.Cli</c>) set an
/// explicit <c>&lt;AssemblyName&gt;</c>, so the output <c>.exe</c> name always matches the project
/// folder name - the last segment of <c>bridgeProjectRelativePath</c>. Only <c>Import.Fbx.Cli</c>
/// sets a <c>RuntimeIdentifier</c> (AssimpNet's native dependency), so both the RID and non-RID
/// output layouts are probed rather than special-casing per project. A top-level (not nested)
/// <c>internal</c> class so <c>CSharpGameEngine.VSExtension.Tests</c> can unit-test the path-
/// selection logic directly via <c>InternalsVisibleTo</c>, without spawning real processes.
/// </summary>
internal static class BridgeExecutableLocator
{
    private static readonly string[] Configurations = ["Release", "Debug"];

    public static string? FindExecutable(string repoRoot, string bridgeProjectRelativePath)
    {
        string exeFileName = Path.GetFileName(bridgeProjectRelativePath) + ".exe";
        string binDirectory = Path.Combine(repoRoot, bridgeProjectRelativePath, "bin");

        string? best = null;
        DateTime bestWriteTimeUtc = DateTime.MinValue;

        foreach (string configuration in Configurations)
        {
            string[] candidates =
            [
                Path.Combine(binDirectory, configuration, "net10.0", exeFileName),
                Path.Combine(binDirectory, configuration, "net10.0", "win-x64", exeFileName),
            ];

            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                DateTime writeTimeUtc = File.GetLastWriteTimeUtc(candidate);
                if (best is null || writeTimeUtc > bestWriteTimeUtc)
                {
                    best = candidate;
                    bestWriteTimeUtc = writeTimeUtc;
                }
            }
        }

        return best;
    }
}
