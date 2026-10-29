using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Solution Explorer で選んだものに対する Assets の動き。アセットなら選択して場所を合わせ、フォルダーなら場所だけ合わせて選択を解除し、
/// プロジェクトとソリューションのルートなら所属する全アセットを表示し、アセット以外のファイルなら選択を解除する（場所は変えない）。
/// </summary>
internal static class AssetSelectionPlanner {
    internal enum Kind { Asset, Folder, Other, Project, Solution }
    internal readonly record struct Plan(Kind Kind,AssetWorkspaceItem?Item,string ProjectPath,string Folder) {
        internal AssetWorkspaceScope Scope=>this.Kind switch{Kind.Project=>AssetWorkspaceScope.Project,Kind.Solution=>AssetWorkspaceScope.Solution,_=>AssetWorkspaceScope.Folder};
    }
    internal static bool IsSolutionRoot(IVsHierarchy?Hierarchy,uint ItemId,bool Multiple,Func<bool>IsSelectedSolution){
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        if(Multiple||ItemId!=VSConstants.VSITEMID_ROOT){ return false; }
        if(Hierarchy is null){ return IsSelectedSolution(); }
        return Hierarchy is IVsSolution
            ||ErrorHandler.Succeeded(Hierarchy.GetProperty(ItemId,(int)__VSHPROPID.VSHPROPID_ExtObject,out var Value))&&Value is EnvDTE.Solution;
    }
    internal static Plan Decide(IReadOnlyList<AssetWorkspaceItem>Items,IReadOnlyList<(string ProjectFilePath,string ProjectDirectory)>Projects,string PathValue,Func<string,bool>DirectoryExists,bool IsSolutionRoot=false){
        if(IsSolutionRoot){ return new Plan(Kind.Solution,null,string.Empty,string.Empty); }
        foreach(var Project in Projects){
            if(string.Equals(Project.ProjectFilePath,PathValue,StringComparison.OrdinalIgnoreCase)){ return new Plan(Kind.Project,null,Project.ProjectFilePath,Project.ProjectDirectory); }
        }
        var Item=Items.FirstOrDefault(Candidate=>string.Equals(Candidate.Source.SourcePath,PathValue,StringComparison.OrdinalIgnoreCase));
        if(Item is not null){ return new Plan(Kind.Asset,Item,Item.Source.ProjectPath,Path.GetDirectoryName(PathValue)??string.Empty); }
        foreach(var Project in Projects){
            var IsFolder=DirectoryExists(PathValue)&&(AssetWorkspaceQuery.IsWithin(PathValue,Project.ProjectDirectory)||string.Equals(PathValue,Project.ProjectDirectory,StringComparison.OrdinalIgnoreCase));
            if(IsFolder||Items.Any(Candidate=>Candidate.Source.ProjectPath==Project.ProjectFilePath&&AssetWorkspaceQuery.InFolder(Candidate,PathValue))){
                return new Plan(Kind.Folder,null,Project.ProjectFilePath,PathValue);
            }
        }
        return new Plan(Kind.Other,null,string.Empty,string.Empty);
    }
}
