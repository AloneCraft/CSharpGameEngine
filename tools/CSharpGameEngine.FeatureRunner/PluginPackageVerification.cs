using System.Globalization;
using System.Text.RegularExpressions;
using CSharpGameEngine.ProjectTemplates;
namespace CSharpGameEngine.FeatureStages;
/// <summary>
/// リポジトリ外における ADR-0079 のプラグイン配置をエンドツーエンドで検証する。Visual Studio ウィザードと同様に、パッケージ化済み SDK から ThirdPerson ゲームを生成し、2 つのサンプルプラグインを追加する。プロジェクトプラグイン samples/Plugins/Weather は Plugins フォルダーへコピーし、パッケージプラグイン CSharpGameEngine.Samples.Lightning は同じローカルフィードへパックして PackageReference で追加する。ThirdPerson の入力でゲームをビルド・実行し、各プラグインの [EngineService] がシャットダウン時に集計値を出力する。両プラグインに Intensity という名前のコンポーネントがあるため、ジェネレーターが名前を区別することもビルドで証明する。
/// </summary>
public static class PluginPackageVerification {
    public const string ProjectName="My Plugin Game";
    public const string LightningPackageId="CSharpGameEngine.Samples.Lightning";
    public const string LightningVersion="1.0.0";
    private static readonly Regex WeatherLine=new(@"CGE-PLUGIN-SAMPLE Weather updates=(\d+) rainfall=([0-9.]+) gusts=(\d+)",RegexOptions.CultureInvariant);
    private static readonly Regex LightningLine=new(@"CGE-PLUGIN-SAMPLE Lightning bolts=(\d+) energy=([0-9.]+)",RegexOptions.CultureInvariant);

    public static async Task<int>RunAsync(string RepositoryRoot,string Configuration,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        if(TemplatePackageVerification.FindSdkPackage(RepositoryRoot) is not { }Package){
            await Error.WriteLineAsync("No CSharpGameEngine.Sdk package in .artifacts/packages; run dotnet pack packaging/CSharpGameEngine.Sdk first.").ConfigureAwait(false);
            return 2;
        }
        var Version=GameProjectGenerator.VersionOfPackage(Package);
        var Work=Path.Combine(Path.GetTempPath(),"cge-plugin-e2e",Guid.NewGuid().ToString("N"));
        var Feed=Path.Combine(Work,"feed");
        GameProjectGenerator.PublishToFeed(Package,Feed);
        var Pack=await TemplatePackageVerification.RunDotnetAsync(
            ["pack",Path.Combine(RepositoryRoot,"samples","Plugins",LightningPackageId,LightningPackageId+".csproj"),"--configuration",Configuration,"--output",Feed,"--nologo"],RepositoryRoot,CancellationToken).ConfigureAwait(false);
        if(Pack.ExitCode!=0){
            await Output.WriteAsync(Pack.Output).ConfigureAwait(false);
            await Error.WriteLineAsync($"The Lightning sample plugin failed to pack ({Pack.ExitCode}).").ConfigureAwait(false);
            return 1;
        }
        var ProjectDirectory=Path.Combine(Work,ProjectName);
        var ProjectFile=GameProjectGenerator.Generate(Path.Combine(RepositoryRoot,"Templates","CSharpGameEngine.Templates.ThirdPerson"),ProjectDirectory,ProjectName,Version,Feed);
        CopyDirectory(Path.Combine(RepositoryRoot,"samples","Plugins","Weather"),Path.Combine(ProjectDirectory,"Plugins","Weather"));
        File.WriteAllText(ProjectFile,File.ReadAllText(ProjectFile).Replace(
            "  </ItemGroup>\r\n</Project>",
            $"    <PackageReference Include=\"{LightningPackageId}\" Version=\"{LightningVersion}\" />\r\n  </ItemGroup>\r\n</Project>",StringComparison.Ordinal));
        await Output.WriteLineAsync($"CGE-PLUGIN-PACKAGE generated {ProjectFile} from {Path.GetFileName(Package)} with Plugins/Weather and {LightningPackageId} {LightningVersion}").ConfigureAwait(false);
        TemplatePackageVerification.DropCachedPackage(GameProjectGenerator.SdkPackageId,Version);
        TemplatePackageVerification.DropCachedPackage(LightningPackageId,LightningVersion);
        var Build=await TemplatePackageVerification.RunDotnetAsync(["build",ProjectFile,"--configuration",Configuration,"--nologo"],ProjectDirectory,CancellationToken).ConfigureAwait(false);
        await Output.WriteAsync(Build.Output).ConfigureAwait(false);
        if(Build.ExitCode!=0){
            await Error.WriteLineAsync($"The plugin game failed to build ({Build.ExitCode}).").ConfigureAwait(false);
            return 1;
        }
        var Leaks=TemplatePackageVerification.FindRepositoryReferences(ProjectDirectory,RepositoryRoot);
        foreach(var Leak in Leaks){
            await Error.WriteLineAsync("Generated build input refers to the repository: "+Leak).ConfigureAwait(false);
        }
        if(Leaks.Count>0){
            return 1;
        }
        var Executable=Path.Combine(ProjectDirectory,"bin",Configuration,"net10.0-windows10.0.19041.0","win-x64",ProjectName+".exe");
        var Evidence=Path.Combine(RepositoryRoot,".artifacts","feature-evidence","plugin.package",Adapter);
        using var GameOutput=new StringWriter(CultureInfo.InvariantCulture);
        var Result=await ThirdPersonCharacterVerification.RunGameAsync(Executable,Evidence,Adapter,GameOutput,Error,CancellationToken).ConfigureAwait(false);
        await Output.WriteAsync(GameOutput.ToString()).ConfigureAwait(false);
        var GeneratedDirectory=Path.Combine(ProjectDirectory,"obj","Generated");
        var Generated=Directory.Exists(GeneratedDirectory)
            ?string.Concat(Directory.EnumerateFiles(GeneratedDirectory,"*.g.cs",SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(File.ReadAllText))
            :string.Empty;
        var Failures=Evaluate(GameOutput.ToString(),Generated,File.Exists(Path.Combine(ProjectDirectory,"bin",Configuration,"net10.0-windows10.0.19041.0","win-x64","Plugins","Weather","Assets","Storm.cgeasset")),out var Summary);
        await Output.WriteLineAsync("CGE-PLUGIN-PACKAGE "+Summary).ConfigureAwait(false);
        foreach(var Failure in Failures){
            await Error.WriteLineAsync(Failure).ConfigureAwait(false);
        }
        if(Result==0&&Failures.Count==0){
            try {
                Directory.Delete(Work,true);
            }
            catch(IOException){
                // コンパイラーサーバーがまだ動いていると、ファイルを一時的に保持することがある。フォルダーは %TEMP% にある。
            }
            return 0;
        }
        return 1;
    }

    /// <summary>
    /// ゲームの出力を判定する（両方のプラグインが想定どおり動けば空）。各プラグインのシステムは tick ごとに Entity あたり 1 回実行されるため、集計値はカウントと一致しなければならない。降雨量 = updates × 1.5 (Rate) × 1 (Intensity) × 2 (Storm アセットの RateMultiplier)、エネルギー = bolts × 2² × 5。Storm アセットの Gusts 値 (7) でデータアセットがコンパイルされたことも確認する。生成コード (<paramref name="Generated"/>) では 2 つの Intensity コンポーネントが別名で示され、CgePlugins に指定されていない Vehicles エンジンプラグインを含めてはならない。
    /// </summary>
    public static IReadOnlyList<string>Evaluate(string GameOutput,string Generated,bool PluginAssetCopied,out string Summary){
        List<string>Failures=[];
        var Weather=WeatherLine.Match(GameOutput);
        var Lightning=LightningLine.Match(GameOutput);
        var NamedApart=Generated.Contains("Weather_Intensity",StringComparison.Ordinal)&&Generated.Contains("Lightning_Intensity",StringComparison.Ordinal);
        var VehiclesCompiled=Generated.Contains("global::CSharpGameEngine.Vehicles.",StringComparison.Ordinal);
        Summary=$"weather=[{(Weather.Success?Weather.Value:"missing")}] lightning=[{(Lightning.Success?Lightning.Value:"missing")}] pluginAssetCopied={PluginAssetCopied} intensityNamedApart={NamedApart} vehiclesCompiled={VehiclesCompiled}";
        if(!NamedApart){
            Failures.Add("The generated World does not name the two Intensity components Weather_Intensity and Lightning_Intensity.");
        }
        if(VehiclesCompiled){
            Failures.Add("The Vehicles engine plugin was compiled into a game that does not name it in CgePlugins.");
        }
        if(!Weather.Success){
            Failures.Add("The project plugin Weather printed no totals: its system or its [EngineService] did not run, or the service was not disposed.");
        } else {
            var Updates=int.Parse(Weather.Groups[1].Value,CultureInfo.InvariantCulture);
            var Rainfall=float.Parse(Weather.Groups[2].Value,CultureInfo.InvariantCulture);
            if(Updates<=0){ Failures.Add("The Weather system never ran (updates=0)."); }
            if(MathF.Abs(Rainfall-(Updates*3f))>0.05f+(Updates*3e-6f)){ Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Weather rainfall {Rainfall} is not updates × 3 ({Updates*3})." )); }
            if(Weather.Groups[3].Value!="7"){ Failures.Add($"The Storm data asset's Gusts read {Weather.Groups[3].Value}, not 7."); }
        }
        if(!Lightning.Success){
            Failures.Add("The package plugin Lightning printed no totals: its system or its [EngineService] did not run, or the service was not disposed.");
        } else {
            var Bolts=int.Parse(Lightning.Groups[1].Value,CultureInfo.InvariantCulture);
            var Energy=float.Parse(Lightning.Groups[2].Value,CultureInfo.InvariantCulture);
            if(Bolts<=0){ Failures.Add("The Lightning system never ran (bolts=0)."); }
            if(MathF.Abs(Energy-(Bolts*20f))>0.05f+(Bolts*2e-5f)){ Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Lightning energy {Energy} is not bolts × 20 ({Bolts*20})." )); }
        }
        if(!PluginAssetCopied){
            Failures.Add("The project plugin's asset was not copied to Plugins/Weather/Assets next to the game.");
        }
        return Failures;
    }

    private static void CopyDirectory(string Source,string Destination){
        foreach(var File in Directory.EnumerateFiles(Source,"*",SearchOption.AllDirectories)){
            var Relative=Path.GetRelativePath(Source,File);
            if(Relative.Split(Path.DirectorySeparatorChar).Any(static Segment=>Segment is "bin" or "obj")){ continue; }
            var Target=Path.Combine(Destination,Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
            System.IO.File.Copy(File,Target,true);
        }
    }
}
