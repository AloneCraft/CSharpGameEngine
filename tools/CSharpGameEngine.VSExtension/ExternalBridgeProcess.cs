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
internal static class ExternalBridgeProcess {
    public static string RunAndReadJson(string BridgeProjectRelativePath,string InputPath,string OutputJsonPath,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        RunProcess(BridgeProjectRelativePath,InputPath,OutputJsonPath,RepoRoot,Timeout,UseResidentServer);

        if(!File.Exists(OutputJsonPath)){
            throw new InvalidOperationException($"インポートブリッジ({BridgeProjectRelativePath})が出力ファイルを生成しませんでした。");
        }

        return File.ReadAllText(OutputJsonPath);
    }

    /// <summary>Like <see cref="RunAndReadJson"/> but for a bridge that writes a binary file (e.g. a <c>.glb</c>) the caller will load separately rather than read back as text here.</summary>
    public static void RunProducingFile(string BridgeProjectRelativePath,string FirstArg,string OutputPath,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        RunProcess(BridgeProjectRelativePath,FirstArg,OutputPath,RepoRoot,Timeout,UseResidentServer);

        if(!File.Exists(OutputPath)){
            throw new InvalidOperationException($"生成ブリッジ({BridgeProjectRelativePath})が出力ファイルを生成しませんでした。");
        }
    }

    private static void RunProcess(string BridgeProjectRelativePath,string FirstArg,string OutputPath,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        var ExecutablePath=BridgeExecutableLocator.FindExecutable(RepoRoot,BridgeProjectRelativePath);

        if(ExecutablePath is not null&&UseResidentServer&&
            ResidentBridgeClient.TryRun(ExecutablePath,RepoRoot,BridgeProjectRelativePath,FirstArg,OutputPath,Timeout,out var ResidentExitCode,out var ResidentOutput)){
            if(ResidentExitCode!=0){
                throw new InvalidOperationException(
                    $"インポートブリッジ({BridgeProjectRelativePath})がエラー終了しました(exit code {ResidentExitCode})。\n{ResidentOutput}");
            }

            return;
        }

        RunOneShotProcess(BridgeProjectRelativePath,FirstArg,OutputPath,RepoRoot,Timeout,ExecutablePath);
    }

    private static void RunOneShotProcess(string BridgeProjectRelativePath,string FirstArg,string OutputPath,string RepoRoot,TimeSpan Timeout,string?ExecutablePath){
        ProcessStartInfo StartInfo=ExecutablePath is not null
            ?new ProcessStartInfo(ExecutablePath, $"\"{FirstArg}\" \"{OutputPath}\"")
            :new ProcessStartInfo(
                "dotnet",
                $"run --project \"{BridgeProjectRelativePath}\" -- \"{FirstArg}\" \"{OutputPath}\"");

        StartInfo.WorkingDirectory=RepoRoot;
        StartInfo.UseShellExecute=false;
        StartInfo.RedirectStandardOutput=true;
        StartInfo.RedirectStandardError=true;
        StartInfo.CreateNoWindow=true;

        using Process Process=new(){ StartInfo=StartInfo };

        // Read both streams via the event-based API (BeginOutputReadLine/BeginErrorReadLine)
        // before WaitForExit: reading them synchronously and sequentially here would deadlock if
        // the child fills the OS pipe buffer for the stream being read second while blocked
        // writing to the (unread) first stream.
        StringBuilder Stdout=new();
        StringBuilder Stderr=new();
        Process.OutputDataReceived+=(_,E)=>{ if(E.Data is not null)Stdout.AppendLine(E.Data);};
        Process.ErrorDataReceived+=(_,E)=>{ if(E.Data is not null)Stderr.AppendLine(E.Data);};

        Process.Start();
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();

        var Exited=Process.WaitForExit((int)Timeout.TotalMilliseconds);
        if(!Exited){
            try { Process.Kill();} catch(InvalidOperationException){ }
            throw new TimeoutException($"インポートブリッジ({BridgeProjectRelativePath})が{Timeout.TotalSeconds}秒以内に終了しませんでした。");
        }

        if(Process.ExitCode!=0){
            throw new InvalidOperationException(
                $"インポートブリッジ({BridgeProjectRelativePath})がエラー終了しました(exit code {Process.ExitCode})。\n{Stdout}\n{Stderr}");
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
internal static class BridgeExecutableLocator {
    private static readonly string[]Configurations=["Release","Debug"];

    public static string?FindExecutable(string RepoRoot,string BridgeProjectRelativePath){
        var ExeFileName=Path.GetFileName(BridgeProjectRelativePath)+".exe";
        var BinDirectory=Path.Combine(RepoRoot,BridgeProjectRelativePath,"bin");

        string?Best=null;
        DateTime BestWriteTimeUtc=DateTime.MinValue;

        foreach(var Configuration in Configurations){
            string[]Candidates=[
                Path.Combine(BinDirectory,Configuration,"net10.0",ExeFileName),
                Path.Combine(BinDirectory,Configuration,"net10.0","win-x64",ExeFileName),
            ];

            foreach(var Candidate in Candidates){
                // An apphost without its runtimeconfig is not runnable. It can be left behind
                // by an incomplete build; launching it produces a misleading hostpolicy.dll error.
                // Fall back to dotnet run until the portable output is complete.
                string RuntimeConfigPath=Path.ChangeExtension(Candidate,".runtimeconfig.json");
                if(!File.Exists(Candidate)||!File.Exists(RuntimeConfigPath)){
                    continue;
                }

                DateTime WriteTimeUtc=File.GetLastWriteTimeUtc(Candidate);
                if(Best is null||WriteTimeUtc>BestWriteTimeUtc){
                    Best=Candidate;
                    BestWriteTimeUtc=WriteTimeUtc;
                }
            }
        }

        return Best;
    }
}
