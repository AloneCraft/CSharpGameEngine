using System.Collections.Generic;
namespace CSharpGameEngine.VSExtension;
internal static class SpatialDebugProcessSelectionPolicy {
    internal static int?Resolve(IReadOnlyList<int>CandidateIds,int?SelectedProcessId){
        if(CandidateIds.Count==1){
            return CandidateIds[0];
        }
        if(SelectedProcessId is not { }Selected){
            return null;
        }
        foreach(int CandidateId in CandidateIds){
            if(CandidateId==Selected){
                return Selected;
            }
        }
        return null;
    }
    internal static bool ShouldShowPicker(IReadOnlyList<int>CandidateIds)=>CandidateIds.Count>1;
}