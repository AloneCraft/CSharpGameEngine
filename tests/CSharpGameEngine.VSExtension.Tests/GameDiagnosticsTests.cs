using System;
using System.IO;
using System.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0097: the Diagnostic Tools commands offer the running games of the solution. A game started from a build
/// already has this Visual Studio's debugger, so attaching skips it; the profilers measure it all the same.
/// </summary>
public sealed class GameDiagnosticsTests {
    private static readonly GameProcessInfo[]Running=[
        new(10,"CgeThirdPerson",null,@"C:\Games\CgeThirdPerson.exe"),
        new(11,"UserEcsProject",null,null),
        new(12,"devenv",null,null),
        new(13,"cgethirdperson",null,null),
    ];

    [Fact]
    public void Candidates_AreTheSolutionsGamesByAssemblyName(){
        var Candidates=GameProcessCandidates.Select(Running,["CgeThirdPerson"],[],GameDiagnosticsKind.CpuUsage,out int AlreadyDebugged);
        Assert.Equal([10,13],Candidates.Select(static Candidate=>Candidate.ProcessId));
        Assert.Equal(0,AlreadyDebugged);
    }

    [Fact]
    public void AttachSkipsAGameThisVisualStudioAlreadyDebugs(){
        var Attach=GameProcessCandidates.Select(Running,["CgeThirdPerson","UserEcsProject"],[11],GameDiagnosticsKind.Debugger,out int AlreadyDebugged);
        Assert.Equal([10,13],Attach.Select(static Candidate=>Candidate.ProcessId));
        Assert.Equal(1,AlreadyDebugged);
        var Profile=GameProcessCandidates.Select(Running,["CgeThirdPerson","UserEcsProject"],[11],GameDiagnosticsKind.DotNetAllocations,out _);
        Assert.Contains(Profile,static Candidate=>Candidate.ProcessId==11);
    }

    [Fact]
    public void Collector_IsFoundBesideTheRunningVisualStudio(){
        string Devenv=@"C:\VS\18\Community\Common7\IDE\devenv.exe";
        string Expected=@"C:\VS\18\Community\Team Tools\DiagnosticsHub\Collector\VSDiagnostics.exe";
        Assert.Equal(Expected,VsDiagnosticsCollector.Find(Devenv,Path=>Path==Expected));
        Assert.Null(VsDiagnosticsCollector.Find(Devenv,static _=>false));
    }

    [Fact]
    public void Collector_StartsWithTheKindsAgentConfigAndStopsIntoTheOutput(){
        string Collector=@"C:\VS\Team Tools\DiagnosticsHub\Collector\VSDiagnostics.exe";
        Guid Session=Guid.Parse("6197e42f-003d-4f91-a845-6404cf289e84");
        Assert.Equal(
            "start 6197e42f-003d-4f91-a845-6404cf289e84 /attach:42 /loadConfig:\"C:\\VS\\Team Tools\\DiagnosticsHub\\Collector\\AgentConfigs\\CpuUsageBase.json\"",
            VsDiagnosticsCollector.StartArguments(Session,42,Collector,GameDiagnosticsKind.CpuUsage));
        Assert.EndsWith("DotNetObjectAllocBase.json\"",VsDiagnosticsCollector.StartArguments(Session,42,Collector,GameDiagnosticsKind.DotNetAllocations),StringComparison.Ordinal);
        Assert.Equal("stop 6197e42f-003d-4f91-a845-6404cf289e84 /output:\"C:\\out\\a.diagsession\"",
            VsDiagnosticsCollector.StopArguments(Session,@"C:\out\a.diagsession"));
        Assert.Throws<ArgumentOutOfRangeException>(()=>VsDiagnosticsCollector.StartArguments(Session,42,Collector,GameDiagnosticsKind.GpuUsage));
    }

    [Fact]
    public void Collector_WritesTheResultUnderTheSolutionsArtifacts(){
        string Path=VsDiagnosticsCollector.OutputPath(@"C:\Repo","CgeThirdPerson",GameDiagnosticsKind.CpuUsage,new DateTime(2026,10,1,22,55,3));
        Assert.Equal(@"C:\Repo\.artifacts\Diagnostics\CgeThirdPerson-cpu-20261001-225503.diagsession",Path);
    }
}
