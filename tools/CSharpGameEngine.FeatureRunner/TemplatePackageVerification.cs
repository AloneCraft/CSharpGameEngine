using System.Diagnostics;
using CSharpGameEngine.ProjectTemplates;
namespace CSharpGameEngine.FeatureStages;
/// <summary>
/// ADR-0065/0068 を製品レベルでエンドツーエンド検証する。Visual Studio ウィザードとまったく同じように、ThirdPerson（既定）などのテンプレートをリポジトリ外の一時フォルダーに生成する。パック済みの CSharpGameEngine.Sdk パッケージだけを含むフィードから復元し、rigid.character-controller のスクリプト入力でビルドして実行する（Vehicle テンプレートでは車を操作し、パッケージ内のエンジンプラグインもビルドできることを確認する (ADR-0079)。TopDown テンプレートではクリックで操作し、ビルド時にパッケージの Cook モジュールでナビゲーションメッシュをベイクする (M0106)）。生成されたビルド入力からリポジトリを参照してはならない。
/// </summary>
public static class TemplatePackageVerification {
    public const string ProjectName="My ThirdPerson";
    public static async Task<int>RunAsync(string RepositoryRoot,string Configuration,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken,string Template="ThirdPerson"){
        var ProjectName=Template=="ThirdPerson"?TemplatePackageVerification.ProjectName:"My "+Template;
        var Package=FindSdkPackage(RepositoryRoot);
        if(Package is null){
            await Error.WriteLineAsync("No CSharpGameEngine.Sdk package in .artifacts/packages; run dotnet pack packaging/CSharpGameEngine.Sdk first.").ConfigureAwait(false);
            return 2;
        }
        var Version=GameProjectGenerator.VersionOfPackage(Package);
        var Work=Path.Combine(Path.GetTempPath(),"cge-template-e2e",Guid.NewGuid().ToString("N"));
        var Feed=Path.Combine(Work,"feed");
        GameProjectGenerator.PublishToFeed(Package,Feed);
        var ProjectDirectory=Path.Combine(Work,ProjectName);
        var ProjectFile=GameProjectGenerator.Generate(Path.Combine(RepositoryRoot,"Templates","CSharpGameEngine.Templates."+Template),ProjectDirectory,ProjectName,Version,Feed,Template);
        await Output.WriteLineAsync($"CGE-TEMPLATE-PACKAGE generated {ProjectFile} from {Path.GetFileName(Package)}").ConfigureAwait(false);
        // パッケージの再ビルド後もバージョンは維持されるため、キャッシュされたコピーを削除し、このビルドを確実に復元する。
        DropCachedPackage(GameProjectGenerator.SdkPackageId,Version);
        var Build=await RunDotnetAsync(["build",ProjectFile,"--configuration",Configuration,"--nologo"],ProjectDirectory,CancellationToken).ConfigureAwait(false);
        await Output.WriteAsync(Build.Output).ConfigureAwait(false);
        if(Build.ExitCode!=0){
            await Error.WriteLineAsync($"The generated project failed to build ({Build.ExitCode}).").ConfigureAwait(false);
            return 1;
        }
        var Leaks=FindRepositoryReferences(ProjectDirectory,RepositoryRoot);
        foreach(var Leak in Leaks){
            await Error.WriteLineAsync("Generated build input refers to the repository: "+Leak).ConfigureAwait(false);
        }
        if(Leaks.Count>0){
            return 1;
        }
        var Executable=Path.Combine(ProjectDirectory,"bin",Configuration,"net10.0-windows10.0.19041.0","win-x64",ProjectName+".exe");
        var Evidence=Template=="ThirdPerson"
            ?Path.Combine(RepositoryRoot,".artifacts","feature-evidence","vsix.project-template",Adapter)
            :Path.Combine(RepositoryRoot,".artifacts","feature-evidence","vsix.project-template",Template,Adapter);
        var Result=Template switch{
            // FirstPerson は ThirdPerson と同じように歩き、ジャンプする（同じスクリプト入力と判定。M0112 でパッケージ検査に追加された）。
            "ThirdPerson" or "FirstPerson"=>await ThirdPersonCharacterVerification.RunGameAsync(Executable,Evidence,Adapter,Output,Error,CancellationToken).ConfigureAwait(false),
            "Vehicle"=>await VehicleDriveVerification.RunGameAsync(Executable,Evidence,Adapter,Output,Error,CancellationToken).ConfigureAwait(false),
            // TopDown ゲームはビルド中にパッケージの Navigation プラグインでナビゲーションメッシュをベイクし (ADR-0079/M0106)、その上を歩く。
            "TopDown"=>await ClickToMoveVerification.RunGameAsync(Executable,Evidence,Adapter,Output,Error,CancellationToken).ConfigureAwait(false),
            _=>throw new ArgumentException($"The package check drives ThirdPerson, FirstPerson, TopDown or Vehicle, not {Template}.",nameof(Template)),
        };
        if(Result==0){
            try {
                Directory.Delete(Work,true);
            }
            catch(IOException){
                // コンパイラーサーバーがまだ動いていると、ファイルを一時的に保持することがある。フォルダーは %TEMP% にある。
            }
        }
        return Result;
    }
    /// <summary>リポジトリルートを参照する復元・ビルド出力（生成されたプロジェクトがそのルートに依存してはならない）。</summary>
    public static IReadOnlyList<string>FindRepositoryReferences(string ProjectDirectory,string RepositoryRoot){
        var Root=Path.GetFullPath(RepositoryRoot).TrimEnd(Path.DirectorySeparatorChar);
        string[]Candidates=[.. Directory.EnumerateFiles(Path.Combine(ProjectDirectory,"obj"),"*.nuget.g.*"),Path.Combine(ProjectDirectory,"obj","project.assets.json"),.. Directory.EnumerateFiles(Path.Combine(ProjectDirectory,"bin"),"*.deps.json",SearchOption.AllDirectories)];
        List<string>Leaks=[];
        foreach(var Candidate in Candidates.Where(File.Exists)){
            var Text=File.ReadAllText(Candidate);
            if(Text.Contains(Root,StringComparison.OrdinalIgnoreCase)||Text.Contains(Root.Replace("\\","\\\\",StringComparison.Ordinal),StringComparison.OrdinalIgnoreCase)){
                Leaks.Add(Candidate);
            }
        }
        return Leaks;
    }
    /// <summary>.artifacts/packages にある最新の CSharpGameEngine.Sdk パッケージ。存在しない場合は null。</summary>
    public static string?FindSdkPackage(string RepositoryRoot)=>
        Directory.Exists(Path.Combine(RepositoryRoot,".artifacts","packages"))
            ?Directory.GetFiles(Path.Combine(RepositoryRoot,".artifacts","packages"),GameProjectGenerator.SdkPackageId+".*.nupkg").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            :null;
    /// <summary>NuGet キャッシュからパッケージのバージョンを削除する。再ビルド後もパッケージのバージョンが維持されるため (ADR-0065)。</summary>
    public static void DropCachedPackage(string PackageId,string Version){
        var Cached=Path.Combine(Environment.GetEnvironmentVariable("NUGET_PACKAGES")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".nuget","packages"),PackageId.ToLowerInvariant(),Version);
        if(Directory.Exists(Cached)){
            Directory.Delete(Cached,true);
        }
    }
    internal static async Task<(int ExitCode,string Output)>RunDotnetAsync(IReadOnlyList<string>Arguments,string WorkingDirectory,CancellationToken CancellationToken){
        var Start=new ProcessStartInfo("dotnet") { WorkingDirectory=WorkingDirectory,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var Argument in Arguments){
            Start.ArgumentList.Add(Argument);
        }
        // 生成されたプロジェクトから、リポジトリの MSBuild 環境を見えないようにする。
        Start.Environment.Remove("MSBuildExtensionsPath");
        Start.Environment.Remove("MSBUILD_EXE_PATH");
        using var Process=System.Diagnostics.Process.Start(Start)??throw new InvalidOperationException("Could not start dotnet.");
        var Standard=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var Errors=Process.StandardError.ReadToEndAsync(CancellationToken);
        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
        return (Process.ExitCode,await Standard.ConfigureAwait(false)+await Errors.ConfigureAwait(false));
    }
}
