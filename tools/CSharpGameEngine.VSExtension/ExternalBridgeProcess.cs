using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

internal sealed class AuthoringHostOperationException:InvalidOperationException {
    internal AuthoringHostOperationException(
        string BridgeIdentity,AuthoringHostResult Result)
        :base($"{BridgeIdentity}: {Result?.Output}"){
        this.Result=Result??throw new ArgumentNullException(nameof(Result));
    }

    internal AuthoringHostResult Result { get; }
}

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
/// path the caller reads back via <see cref="RunAndReadJson"/>) and by
/// <see cref="SceneModelRenderObjectBuilder.ResolveViaBridge"/> (second arg is a <c>.glb</c> path the
/// caller only needs to know exists afterward, via <see cref="RunProducingFile"/>) - so no model/mesh
/// data ever needs to be generated in-process inside <c>devenv.exe</c>.
/// </summary>
internal static class ExternalBridgeProcess {
    public static string RunAndReadJson(string BridgeProjectRelativePath,string InputPath,string OutputJsonPath,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        RunProcess(BridgeProjectRelativePath,InputPath,OutputJsonPath,RepoRoot,Timeout,UseResidentServer);

        if(!File.Exists(OutputJsonPath)){
            throw new InvalidOperationException(VsixText.Format("Bridge_OutputNotGenerated",BridgeProjectRelativePath));
        }

        return File.ReadAllText(OutputJsonPath);
    }

    /// <summary>Like <see cref="RunAndReadJson"/> but for a bridge that writes a binary file (e.g. a <c>.glb</c>) the caller will load separately rather than read back as text here.</summary>
    public static void RunProducingFile(string BridgeProjectRelativePath,string FirstArg,string OutputPath,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        RunProcess(BridgeProjectRelativePath,[FirstArg,OutputPath],RepoRoot,Timeout,UseResidentServer);

        if(!File.Exists(OutputPath)){
            throw new InvalidOperationException(VsixText.Format("Bridge_GeneratedOutputNotGenerated",BridgeProjectRelativePath));
        }
    }

    public static void RunCommandProducingFile(string BridgeProjectRelativePath,string[]Arguments,string OutputPath,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        if(Arguments is null){ throw new ArgumentNullException(nameof(Arguments)); }
        RunProcess(BridgeProjectRelativePath,Arguments,RepoRoot,Timeout,UseResidentServer);
        if(!File.Exists(OutputPath)){
            throw new InvalidOperationException(VsixText.Format("Bridge_GeneratedOutputNotGenerated",BridgeProjectRelativePath));
        }
    }

    public static void RunExecutableCommandProducingFile(
        string ExecutablePath,string BridgeIdentity,string[]Arguments,string OutputPath,
        string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        RunExecutableCommandProducingFile(
            ExecutablePath,BridgeIdentity,Arguments,OutputPath,RepoRoot,Timeout,
            UseResidentServer,CancellationToken.None);
    }

    public static void RunExecutableCommandProducingFile(
        string ExecutablePath,string BridgeIdentity,string[]Arguments,string OutputPath,
        string RepoRoot,TimeSpan Timeout,bool UseResidentServer,CancellationToken CancellationToken){
        if(string.IsNullOrWhiteSpace(ExecutablePath)){ throw new ArgumentException(nameof(ExecutablePath)); }
        if(Arguments is null){ throw new ArgumentNullException(nameof(Arguments)); }
        RunProcess(
            BridgeIdentity,Arguments,RepoRoot,Timeout,UseResidentServer,ExecutablePath,
            CancellationToken);
        if(!File.Exists(OutputPath)){
            throw new InvalidOperationException(VsixText.Format("Bridge_GeneratedOutputNotGenerated",BridgeIdentity));
        }
    }

    private static void RunProcess(string BridgeProjectRelativePath,string FirstArg,string OutputPath,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        RunProcess(BridgeProjectRelativePath,[FirstArg,OutputPath],RepoRoot,Timeout,UseResidentServer);
    }

    private static void RunProcess(string BridgeProjectRelativePath,string[]Arguments,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        var ExecutablePath=BridgeExecutableLocator.FindExecutable(RepoRoot,BridgeProjectRelativePath);
        RunProcess(BridgeProjectRelativePath,Arguments,RepoRoot,Timeout,UseResidentServer,ExecutablePath);
    }

    private static void RunProcess(
        string BridgeProjectRelativePath,string[]Arguments,string RepoRoot,TimeSpan Timeout,
        bool UseResidentServer,string?ExecutablePath){
        RunProcess(
            BridgeProjectRelativePath,Arguments,RepoRoot,Timeout,UseResidentServer,
            ExecutablePath,CancellationToken.None);
    }

    private static void RunProcess(
        string BridgeProjectRelativePath,string[]Arguments,string RepoRoot,TimeSpan Timeout,
        bool UseResidentServer,string?ExecutablePath,CancellationToken CancellationToken){

        CancellationToken.ThrowIfCancellationRequested();
        if(ExecutablePath is not null&&UseResidentServer){
            var Attempt=ResidentBridgeClient.TryRunDetailed(
                ExecutablePath,RepoRoot,BridgeProjectRelativePath,Arguments,Timeout,
                CancellationToken,out AuthoringHostResult?ResidentResult);
            if(Attempt==ResidentBridgeAttemptStatus.Completed){
                if(ResidentResult is null){
                    throw new InvalidOperationException(
                        VsixText.Get("ResidentBridge_EmptyResponse"));
                }
                if(!IsSuccessfulResult(ResidentResult)){
                    throw new AuthoringHostOperationException(
                        BridgeProjectRelativePath,ResidentResult);
                }
                return;
            }
            if(Attempt==ResidentBridgeAttemptStatus.TimedOut){
                var TimeoutSeconds=Timeout.Ticks/(float)TimeSpan.TicksPerSecond;
                throw new TimeoutException(VsixText.Format("Bridge_TimedOut",BridgeProjectRelativePath,TimeoutSeconds.ToString(CultureInfo.InvariantCulture)));
            }
            if(Attempt is ResidentBridgeAttemptStatus.HostCrashed or ResidentBridgeAttemptStatus.ProtocolMismatch){
                if(ResidentResult is not null){
                    throw new AuthoringHostOperationException(
                        BridgeProjectRelativePath,ResidentResult);
                }
                throw new InvalidOperationException(
                    VsixText.Format("Bridge_ExitedWithError",BridgeProjectRelativePath,"1",string.Empty));
            }
        }

        RunOneShotProcess(
            BridgeProjectRelativePath,Arguments,RepoRoot,Timeout,ExecutablePath,
            CancellationToken);
    }

    internal static bool IsSuccessfulResult(AuthoringHostResult Result){
        if(Result is null||Result.State!=AuthoringHostTerminalState.Succeeded
            ||Result.ExitCode!=0){ return false; }
        foreach(AuthoringHostDiagnostic?Diagnostic in Result.Diagnostics
            ??Array.Empty<AuthoringHostDiagnostic>()){
            if(Diagnostic is null
                ||Diagnostic.Severity==AuthoringHostDiagnosticSeverity.Error){
                return false;
            }
        }
        return true;
    }

    private static void RunOneShotProcess(
        string BridgeProjectRelativePath,string[]Arguments,string RepoRoot,TimeSpan Timeout,
        string?ExecutablePath,CancellationToken CancellationToken){
        string CommandArguments=string.Join(" ",Array.ConvertAll(Arguments,QuoteArgument));
        var StartInfo=ExecutablePath is not null
                          ?new ProcessStartInfo(ExecutablePath,CommandArguments)
                          :new ProcessStartInfo(
                                                "dotnet",
                                                $"run --project \"{BridgeProjectRelativePath}\" -- {CommandArguments}");

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

        var TimeoutMilliseconds=Math.Min(int.MaxValue,Math.Max(0L,Timeout.Ticks/TimeSpan.TicksPerMillisecond));
        Stopwatch Timer=Stopwatch.StartNew();
        bool Exited=false;
        while(!(Exited=Process.WaitForExit(100))
            &&Timer.ElapsedMilliseconds<TimeoutMilliseconds){
            if(!CancellationToken.IsCancellationRequested){ continue; }
            try { Process.Kill();} catch(InvalidOperationException){ }
            CancellationToken.ThrowIfCancellationRequested();
        }
        if(!Exited){
            try { Process.Kill();} catch(InvalidOperationException){ }
            var TimeoutSeconds=Timeout.Ticks/(float)TimeSpan.TicksPerSecond;
            throw new TimeoutException(VsixText.Format("Bridge_TimedOut",BridgeProjectRelativePath,TimeoutSeconds.ToString(CultureInfo.InvariantCulture)));
        }

        if(Process.ExitCode!=0){
            throw new InvalidOperationException(
                VsixText.Format("Bridge_ExitedWithError",BridgeProjectRelativePath,Process.ExitCode.ToString(CultureInfo.InvariantCulture),Stdout+Environment.NewLine+Stderr));
        }
    }

    /// <summary>
    /// One argument quoted for the Windows command line, read back as itself (CommandLineToArgvW and .NET's parser):
    /// backslashes are doubled where they precede a quote, inside or at the closing one, so <c>C:\Out\</c> does not
    /// escape its closing quote and swallow the next argument.
    /// </summary>
    internal static string QuoteArgument(string Argument){
        if(Argument is null){ throw new ArgumentNullException(nameof(Argument)); }
        var Builder=new System.Text.StringBuilder("\"");
        var Backslashes=0;
        foreach(var Character in Argument){
            if(Character=='\\'){ Backslashes++;continue; }
            Builder.Append('\\',Character=='"'?Backslashes*2+1:Backslashes).Append(Character);
            Backslashes=0;
        }
        return Builder.Append('\\',Backslashes*2).Append('"').ToString();
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

    /// <summary>Folder of this extension, where the VSIX ships each bridge under <c>Bridges/&lt;name&gt;/</c> (ADR-0065).</summary>
    internal static string ExtensionDirectory=>Path.GetDirectoryName(typeof(BridgeExecutableLocator).Assembly.Location)??AppDomain.CurrentDomain.BaseDirectory;

    public static string?FindExecutable(string RepoRoot,string BridgeProjectRelativePath)=>
        FindExecutable(ExtensionDirectory,RepoRoot,BridgeProjectRelativePath);

    /// <summary>
    /// The bridge shipped with the extension when present; otherwise the newest runnable build in the
    /// engine repository (development only). Null when neither exists.
    /// </summary>
    public static string?FindExecutable(string ExtensionDirectory,string RepoRoot,string BridgeProjectRelativePath){
        var ExeFileName=Path.GetFileName(BridgeProjectRelativePath)+".exe";
        var Bundled=Path.Combine(ExtensionDirectory,"Bridges",Path.GetFileName(BridgeProjectRelativePath),ExeFileName);
        if(File.Exists(Bundled)&&File.Exists(Path.ChangeExtension(Bundled,".runtimeconfig.json"))){
            return Bundled;
        }
        var BinDirectory=Path.Combine(RepoRoot,BridgeProjectRelativePath,"bin");

        string?Best=null;
        var BestWriteTimeUtc=DateTime.MinValue;

        foreach(var Configuration in Configurations){
            string[]Candidates=[
                Path.Combine(BinDirectory,Configuration,"net10.0",ExeFileName),
                Path.Combine(BinDirectory,Configuration,"net10.0","win-x64",ExeFileName),
            ];

            foreach(var Candidate in Candidates){
                // An apphost without its runtimeconfig is not runnable. It can be left behind
                // by an incomplete build; launching it produces a misleading hostpolicy.dll error.
                // Fall back to dotnet run until the portable output is complete.
                var RuntimeConfigPath=Path.ChangeExtension(Candidate,".runtimeconfig.json");
                if(!File.Exists(Candidate)||!File.Exists(RuntimeConfigPath)){
                    continue;
                }

                var WriteTimeUtc=File.GetLastWriteTimeUtc(Candidate);
                if(Best is null||WriteTimeUtc>BestWriteTimeUtc){
                    Best=Candidate;
                    BestWriteTimeUtc=WriteTimeUtc;
                }
            }
        }

        return Best;
    }
}
