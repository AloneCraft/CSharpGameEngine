using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CSharpGameEngine.VSExtension;

/// <summary>M0097：Diagnostic Tools コマンドが実行中のゲームに対して呼び出す Visual Studio 診断ツール。</summary>
internal enum GameDiagnosticsKind:byte {
    Debugger,
    CpuUsage,
    DotNetAllocations,
    GpuUsage,
}

/// <summary>コマンド一覧に表示する、実行中のプロセス1つ。</summary>
internal readonly record struct GameProcessInfo(int ProcessId,string Name,DateTime?StartTime,string?Path);

/// <summary>
/// Diagnostic Tools コマンドが候補として表示する実行中プロセス。対象はソリューション内のゲームアセンブリ名に似た名前のプロセスです。ビルドから起動したゲームにはこの Visual Studio のデバッガーがすでに接続されているため、アタッチ候補からデバッグ中のプロセスを除外します。
/// </summary>
internal static class GameProcessCandidates {
    internal static IReadOnlyList<GameProcessInfo>Select(
        IEnumerable<GameProcessInfo>Running,IEnumerable<string>GameAssemblyNames,IReadOnlyCollection<int>DebuggedProcessIds,
        GameDiagnosticsKind Kind,out int AlreadyDebugged){
        HashSet<string>Names=new(GameAssemblyNames,StringComparer.OrdinalIgnoreCase);
        List<GameProcessInfo>Games=[..Running
            .Where(Process=>Names.Contains(Process.Name))
            .OrderBy(static Process=>Process.Name,StringComparer.OrdinalIgnoreCase)
            .ThenBy(static Process=>Process.ProcessId)];
        AlreadyDebugged=0;
        if(Kind!=GameDiagnosticsKind.Debugger){ return Games; }
        List<GameProcessInfo>Attachable=[..Games.Where(Process=>!DebuggedProcessIds.Contains(Process.ProcessId))];
        AlreadyDebugged=Games.Count-Attachable.Count;
        return Attachable;
    }
}

/// <summary>
/// Visual Studio 標準の collector（<c>VSDiagnostics.exe</c>）。実行中のプロセスに profiler の CPU または .NET allocation agent を接続し、Visual Studio でレポートとして開く <c>.diagsession</c> を書き出します。
/// </summary>
internal static class VsDiagnosticsCollector {
    internal const string RelativePath=@"Team Tools\DiagnosticsHub\Collector\VSDiagnostics.exe";

    /// <summary><paramref name="DevenvPath"/> が指す <c>Common7\IDE\devenv.exe</c> を持つ Visual Studio の collector。</summary>
    internal static string?Find(string DevenvPath,Func<string,bool>Exists){
        string?Root=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(DevenvPath)));
        if(Root is null){ return null; }
        string Candidate=Path.Combine(Root,RelativePath);
        return Exists(Candidate)?Candidate:null;
    }

    internal static string StartArguments(Guid Session,int ProcessId,string Collector,GameDiagnosticsKind Kind){
        string Config=Path.Combine(Path.GetDirectoryName(Collector)!,"AgentConfigs",ConfigFileName(Kind));
        return "start "+Session.ToString("D")+" /attach:"+ProcessId.ToString(CultureInfo.InvariantCulture)+" /loadConfig:\""+Config+"\"";
    }

    internal static string StopArguments(Guid Session,string Output)=>"stop "+Session.ToString("D")+" /output:\""+Output+"\"";

    internal static string OutputPath(string Root,string GameName,GameDiagnosticsKind Kind,DateTime Now)=>
        Path.Combine(Root,".artifacts","Diagnostics",
            GameName+"-"+Slug(Kind)+"-"+Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+".diagsession");

    private static string ConfigFileName(GameDiagnosticsKind Kind)=>Kind switch {
        GameDiagnosticsKind.CpuUsage=>"CpuUsageBase.json",
        GameDiagnosticsKind.DotNetAllocations=>"DotNetObjectAllocBase.json",
        _=>throw new ArgumentOutOfRangeException(nameof(Kind),Kind,"The collector has no agent configuration for this kind."),
    };

    private static string Slug(GameDiagnosticsKind Kind)=>Kind switch {
        GameDiagnosticsKind.CpuUsage=>"cpu",
        GameDiagnosticsKind.DotNetAllocations=>"dotnet-alloc",
        _=>throw new ArgumentOutOfRangeException(nameof(Kind),Kind,"The collector does not record this kind."),
    };
}
