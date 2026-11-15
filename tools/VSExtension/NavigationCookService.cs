using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
namespace CSharpGameEngine.VSExtension;
/// <summary>ナビメッシュを焼いた結果(ADR-0079/M0106)。<see cref="OutputPaths"/>は作られた<c>.cgenav</c>、<see cref="Diagnostics"/>はビルドの警告とエラーの行。</summary>
internal readonly record struct NavigationCookResult(bool Succeeded,string Message,IReadOnlyList<string>OutputPaths,IReadOnlyList<string>Diagnostics);
/// <summary>
/// シーンのゲームのナビメッシュを、ビルドと同じ手順で焼く(ADR-0079/M0106)。Navigation プラグインのターゲット <c>CgeCookNavigation</c> を、そのプロジェクトで
/// <c>dotnet msbuild</c> に実行させる。Cook 本体(<c>CSharpGameEngine.Cook.Cli</c>とプラグインの Cook モジュール)は別のプロセスで動き、VS のプロセスはユーザーの DLL を読まない
/// (ADR-0019/0020)。物理のベイクのようにエンジンのソースの場所に頼らないので、エンジンのリポジトリのプロジェクトでも、パッケージから作ったプロジェクトでも使える。
/// </summary>
internal static class NavigationCookService {
    internal const string TargetName="CgeCookNavigation";
    internal const string OutputItemName="CgeNavigationOutput";
    internal const string BoundsTypeName="CSharpGameEngine.Navigation.NavMeshBounds";
    private static readonly TimeSpan CookTimeout=new(0,10,0);
    /// <summary>シーンのファイルが、ナビメッシュを作る領域(<c>NavMeshBounds</c>)を持つか。</summary>
    internal static bool HasNavigationVolume(string SceneFilePath){
        try {
            return File.Exists(SceneFilePath)&&File.ReadAllText(SceneFilePath).IndexOf(BoundsTypeName,StringComparison.Ordinal)>=0;
        }
        catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException){
            return false;
        }
    }
    /// <summary>シーンの上に向かって探す、プロジェクトのファイルが 1 つだけあるフォルダの<c>.csproj</c>。無ければ null。</summary>
    internal static string?FindProjectFile(string SceneFilePath){
        var Directory=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(SceneFilePath))??".");
        while(Directory is not null){
            var Projects=Directory.Exists?Directory.EnumerateFiles("*.csproj",SearchOption.TopDirectoryOnly).Select(static File=>File.FullName).ToArray():[];
            if(Projects.Length==1){ return Projects[0]; }
            if(Projects.Length>1){ return null; }
            Directory=Directory.Parent;
        }
        return null;
    }
    /// <summary><c>dotnet</c>に渡す引数。完成したビルドの項目 <see cref="OutputItemName"/> を JSON で書かせる。</summary>
    internal static string BuildArguments(string ProjectFile)=>
        $"msbuild \"{ProjectFile}\" -t:{TargetName} -nologo -v:minimal -getItem:{OutputItemName}";
    /// <summary>
    /// MSBuild の出力から、作られたファイルと診断を取り出す。<c>-getItem</c> は出力の最後に JSON を書くので、最後の <c>{</c> から行頭の <c>}</c> までを読む。
    /// 診断は、<c>error</c> か <c>warning</c> を含む行。
    /// </summary>
    internal static NavigationCookResult ParseOutput(int ExitCode,string Output){
        var Diagnostics=Output.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries)
            .Select(static Line=>Line.Trim())
            .Where(static Line=>Line.Length>0&&Line[0]!='{'&&Line[0]!='}'&&Line[0]!='"'&&Line[0]!='['&&Line[0]!=']'&&(Line.IndexOf(" error ",StringComparison.OrdinalIgnoreCase)>=0||Line.IndexOf(" warning ",StringComparison.OrdinalIgnoreCase)>=0||Line.IndexOf(": error",StringComparison.OrdinalIgnoreCase)>=0||Line.IndexOf(": warning",StringComparison.OrdinalIgnoreCase)>=0))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var Outputs=new List<string>();
        var Start=Output.IndexOf("{\n",StringComparison.Ordinal);
        if(Start<0){ Start=Output.IndexOf("{\r\n",StringComparison.Ordinal); }
        if(Start>=0){
            try {
                using var Document=JsonDocument.Parse(Output.Substring(Start));
                if(Document.RootElement.TryGetProperty("Items",out var Items)&&Items.TryGetProperty(OutputItemName,out var Entries)&&Entries.ValueKind==JsonValueKind.Array){
                    foreach(var Entry in Entries.EnumerateArray()){
                        if(Entry.TryGetProperty("FullPath",out var FullPath)&&FullPath.GetString() is { Length:>0 }Path_){ Outputs.Add(Path_); }
                        else if(Entry.TryGetProperty("Identity",out var Identity)&&Identity.GetString() is { Length:>0 }Fallback){ Outputs.Add(Fallback); }
                    }
                }
            }
            catch(JsonException){
                // JSON の後ろに別の出力が続いたときは、ここでは読まない(ファイルの一覧が空になるだけ)。
            }
        }
        var Succeeded=ExitCode==0&&!Diagnostics.Any(static Line=>Line.IndexOf("error",StringComparison.OrdinalIgnoreCase)>=0&&Line.IndexOf("warning",StringComparison.OrdinalIgnoreCase)<0);
        var Message=Succeeded
            ?$"{Outputs.Count} navigation mesh file(s)."
            :Diagnostics.FirstOrDefault(static Line=>Line.IndexOf("error",StringComparison.OrdinalIgnoreCase)>=0)??$"dotnet msbuild exited with code {ExitCode}.";
        return new NavigationCookResult(Succeeded,Message,Outputs,Diagnostics);
    }
    /// <summary>
    /// <paramref name="SceneFilePath"/>のプロジェクトのナビメッシュを焼く。<paramref name="RunDotnet"/>は引数・作業フォルダ・取り消しを受けて(終了コード, 出力)を返す
    /// (既定は<c>dotnet</c>を実際に動かす。テストが差し替える)。
    /// </summary>
    internal static NavigationCookResult Cook(string SceneFilePath,CancellationToken CancellationToken,Func<string,string,CancellationToken,(int ExitCode,string Output)>?RunDotnet=null){
        var ProjectFile=FindProjectFile(SceneFilePath);
        if(ProjectFile is null){
            return new NavigationCookResult(false,$"The game project of the scene was not found: {SceneFilePath}",[],[]);
        }
        RunDotnet??=RunProcess;
        var (ExitCode,Output)=RunDotnet(BuildArguments(ProjectFile),Path.GetDirectoryName(ProjectFile)!,CancellationToken);
        return ParseOutput(ExitCode,Output);
    }
    private static (int ExitCode,string Output)RunProcess(string Arguments,string WorkingDirectory,CancellationToken CancellationToken){
        var Output=new StringBuilder();
        var StartInfo=new ProcessStartInfo("dotnet",Arguments){
            WorkingDirectory=WorkingDirectory,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,
        };
        using var Process=new Process { StartInfo=StartInfo };
        Process.OutputDataReceived+=(_,Event)=>{ if(Event.Data is not null){ lock(Output){ Output.AppendLine(Event.Data); } } };
        Process.ErrorDataReceived+=(_,Event)=>{ if(Event.Data is not null){ lock(Output){ Output.AppendLine(Event.Data); } } };
        Process.Start();
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
        var Timer=Stopwatch.StartNew();
        while(!Process.WaitForExit(100)){
            if(CancellationToken.IsCancellationRequested){
                try { Process.Kill(); }
                catch(InvalidOperationException){ }
                CancellationToken.ThrowIfCancellationRequested();
            }
            if(Timer.Elapsed<=CookTimeout){ continue; }
            try { Process.Kill(); }
            catch(InvalidOperationException){ }
            throw new TimeoutException("The navigation mesh bake timed out.");
        }
        Process.WaitForExit();
        lock(Output){ return (Process.ExitCode,Output.ToString()); }
    }
}
