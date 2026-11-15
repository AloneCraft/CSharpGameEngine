using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CSharpGameEngine.FeatureStages;

public sealed record HermeticBuildCommand(string Phase,IReadOnlyList<string>Arguments);

public sealed record HermeticBuildPlan(
    string RepositoryRoot,string WorkRoot,string ArtifactRoot,string PackageRoot,
    IReadOnlyList<HermeticBuildCommand>Commands){
    /// <summary>
    /// すべてのコマンドに設定する。ビルドは、<c>--packages</c> 引数の対象外となる入れ子の評価で実行ツール（ShaderGraph CLI、ブリッジ、Preview Host）を復元するため、これがないとマシン全体のフォルダーから復元し、ビルド中に隔離された assets ファイルを書き換えてしまう。
    /// </summary>
    public IReadOnlyDictionary<string,string>Environment=>new Dictionary<string,string>{["NUGET_PACKAGES"]=this.PackageRoot};
    public static HermeticBuildPlan Create(string RepositoryRoot,string?RunId=null){
        string Root=Path.GetFullPath(RepositoryRoot);
        string Solution=Path.Combine(Root,"CSharpGameEngine.slnx");
        if(!File.Exists(Solution)){ throw new FileNotFoundException("Canonical solution was not found.",Solution); }
        // 参考：VSIX ビルドでは、ステージ済みブリッジファイルを成果物ルートから約 110 文字下に置いて圧縮する。そのため、
        // 長いパスを有効にしていないマシンでは、VSSDK のパッケージ化が MAX_PATH (260) を超えると失敗する。
        string Id=RunId??Guid.NewGuid().ToString("N")[..12];
        if(Id.Length==0||Id.IndexOfAny(Path.GetInvalidFileNameChars())>=0){
            throw new ArgumentException("Clean-build run ID is invalid.",nameof(RunId));
        }
        string Work=Path.GetFullPath(Path.Combine(Root,".artifacts","CleanBuild","work",Id));
        string Artifacts=Path.Combine(Work,"artifacts");
        string Packages=Path.Combine(Work,"packages");
        string Relative=Path.GetRelativePath(Path.Combine(Root,".artifacts","CleanBuild","work"),Work);
        if(Relative.StartsWith("..",StringComparison.Ordinal)||Path.IsPathRooted(Relative)){
            throw new InvalidOperationException("Clean-build work root escaped its owned directory.");
        }
        var Commands=new List<HermeticBuildCommand>{
            new("restore",[
                "restore",Solution,"--artifacts-path",Artifacts,"--packages",Packages,
                "--disable-build-servers","--force","--nologo",
            ]),
        };
        foreach(string Configuration in new[]{"Debug","Release"}){
            // 通常のビルドでは、所有する成果物ルートが空で始まるため、増分ビルドの比較対象がない。また、
            // Rebuild (--no-incremental) は各プロジェクトが自分用にビルドしたツール（シェーダーコンパイラー、シーン
            // スキーマライター、Feature Runner）まで、各プロジェクトがビルドした後に消去する (audit AUD-004)。
            Commands.Add(new($"build-{Configuration}",[
                "build",Solution,"--configuration",Configuration,
                "--artifacts-path",Artifacts,"--no-restore",
                "--disable-build-servers","--nologo","--verbosity","minimal",
            ]));
            Commands.Add(new($"test-{Configuration}",[
                "test",Solution,"--configuration",Configuration,
                "--artifacts-path",Artifacts,"--no-build","--disable-build-servers",
                "--nologo","--verbosity","minimal",
            ]));
        }
        return new HermeticBuildPlan(Root,Work,Artifacts,Packages,Commands);
    }
}

public static class HermeticBuildVerifier {
    public const string ReportSchema="csharp-game-engine-clean-build-v1";

    public static async Task<int>RunAsync(string RepositoryRoot,TextWriter Output,
        TextWriter Error,CancellationToken CancellationToken){
        HermeticBuildPlan Plan=HermeticBuildPlan.Create(RepositoryRoot);
        Directory.CreateDirectory(Plan.WorkRoot);
        var Results=new List<CleanBuildCommandResult>();
        string?Failure=null;
        string?CleanupFailure=null;
        int ManagedAssemblyCount=0;
        try {
            foreach(HermeticBuildCommand Command in Plan.Commands){
                CleanBuildCommandResult Result=await RunCommandAsync(
                    Plan,Command,CancellationToken).ConfigureAwait(false);
                Results.Add(Result);
                await Output.WriteLineAsync(
                    $"{Command.Phase}: exit={Result.ExitCode} durationMs={Result.DurationMilliseconds}")
                    .ConfigureAwait(false);
                if(Result.ExitCode==0){ continue; }
                Failure=$"{Command.Phase} failed with exit code {Result.ExitCode}.";
                await Error.WriteLineAsync(Failure).ConfigureAwait(false);
                break;
            }
            if(Failure is null){
                ManagedAssemblyCount=Directory.Exists(Plan.ArtifactRoot)
                    ?Directory.EnumerateFiles(Plan.ArtifactRoot,"*.dll",SearchOption.AllDirectories).Count()
                    :0;
                if(ManagedAssemblyCount==0){ Failure="Isolated build produced no managed assemblies."; }
            }
        }
        catch(Exception Exception)when(Exception is not OperationCanceledException){
            Failure=Exception.Message;
            await Error.WriteLineAsync(Exception.ToString()).ConfigureAwait(false);
        }
        finally {
            try {
                if(Directory.Exists(Plan.WorkRoot)){ Directory.Delete(Plan.WorkRoot,true); }
            }
            catch(Exception Exception){ CleanupFailure=Exception.Message; }
        }
        if(CleanupFailure is not null){ Failure=$"Clean-build workspace cleanup failed: {CleanupFailure}"; }
        string ReportDirectory=Path.Combine(Plan.RepositoryRoot,".artifacts","CleanBuild");
        Directory.CreateDirectory(ReportDirectory);
        string ReportPath=Path.Combine(ReportDirectory,
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.clean-build.json");
        var Report=new CleanBuildReport(
            ReportSchema,DateTimeOffset.UtcNow,Plan.RepositoryRoot,
            Results,ManagedAssemblyCount,Failure is null,Failure,CleanupFailure);
        await File.WriteAllTextAsync(ReportPath,
            JsonSerializer.Serialize(Report,new JsonSerializerOptions{WriteIndented=true}),
            CancellationToken).ConfigureAwait(false);
        await Output.WriteLineAsync(ReportPath).ConfigureAwait(false);
        return Failure is null?0:1;
    }

    private static async Task<CleanBuildCommandResult>RunCommandAsync(
        HermeticBuildPlan Plan,HermeticBuildCommand Command,CancellationToken CancellationToken){
        var StartInfo=new ProcessStartInfo(){
            FileName="dotnet",WorkingDirectory=Plan.RepositoryRoot,UseShellExecute=false,
            RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,
        };
        foreach(string Argument in Command.Arguments){ StartInfo.ArgumentList.Add(Argument); }
        foreach(var (Name,Value) in Plan.Environment){ StartInfo.Environment[Name]=Value; }
        using var Process=new Process(){StartInfo=StartInfo};
        Stopwatch Stopwatch=Stopwatch.StartNew();
        if(!Process.Start()){ throw new InvalidOperationException($"Could not start {Command.Phase}."); }
        Task<string>OutputTask=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        Task<string>ErrorTask=Process.StandardError.ReadToEndAsync(CancellationToken);
        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
        string StandardOutput=await OutputTask.ConfigureAwait(false);
        string StandardError=await ErrorTask.ConfigureAwait(false);
        Stopwatch.Stop();
        string Evidence=StandardOutput+"\\n"+StandardError;
        string OutputSha256=Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Evidence))).ToLowerInvariant();
        return new CleanBuildCommandResult(
            Command.Phase,Command.Arguments,Process.ExitCode,
            Stopwatch.ElapsedMilliseconds,OutputSha256,
            Tail(StandardOutput),Tail(StandardError));
    }

    private static string Tail(string Value){
        const int MaximumLength=8000;
        return Value.Length<=MaximumLength?Value:Value[^MaximumLength..];
    }

    private sealed record CleanBuildCommandResult(
        string Phase,IReadOnlyList<string>Arguments,int ExitCode,long DurationMilliseconds,
        string OutputSha256,string StandardOutputTail,string StandardErrorTail);
    private sealed record CleanBuildReport(
        string Schema,DateTimeOffset CreatedUtc,string RepositoryRoot,
        IReadOnlyList<CleanBuildCommandResult>Commands,int ManagedAssemblyCount,bool Passed,
        string?Failure,string?CleanupFailure);
}
