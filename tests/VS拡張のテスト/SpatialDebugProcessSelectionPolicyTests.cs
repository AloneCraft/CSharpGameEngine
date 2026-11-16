using System.Collections.Generic;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class SpatialDebugProcessSelectionPolicyTests {
    [Fact]
    public void Resolve_SelectsTheOnlyRunningProcess(){
        int?ProcessId=SpatialDebugProcessSelectionPolicy.Resolve([1234],SelectedProcessId:null);
        Assert.Equal(1234,ProcessId);
        Assert.False(SpatialDebugProcessSelectionPolicy.ShouldShowPicker([1234]));
    }
    [Fact]
    public void Resolve_RequiresExplicitSelectionWhenSeveralProcessesRun(){
        int?ProcessId=SpatialDebugProcessSelectionPolicy.Resolve([1234,5678],SelectedProcessId:null);
        Assert.Null(ProcessId);
        Assert.True(SpatialDebugProcessSelectionPolicy.ShouldShowPicker([1234,5678]));
    }
    [Fact]
    public void Resolve_ClearsEndedSelectionAndKeepsValidSelection(){
        IReadOnlyList<int>CandidateIds=[1234,5678];
        Assert.Equal(5678,SpatialDebugProcessSelectionPolicy.Resolve(CandidateIds,5678));
        Assert.Null(SpatialDebugProcessSelectionPolicy.Resolve(CandidateIds,9999));
    }
}