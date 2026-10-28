using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Solution Explorer で選んだものに対する Assets の動き。アセットなら選択して場所を合わせ、フォルダーなら場所だけ合わせて選択を解除し、
/// アセット以外のファイルなら選択を解除する（場所は変えない）。
/// </summary>
internal static class AssetSelectionPlanner {
    internal enum Kind { Asset, Folder, Other }
    internal readonly record struct Plan(Kind Kind,AssetWorkspaceItem?Item,string ProjectPath,string Folder);
    internal static Plan Decide(IReadOnlyList<AssetWorkspaceItem>Items,IReadOnlyList<(string ProjectFilePath,string ProjectDirectory)>Projects,string PathValue,Func<string,bool>DirectoryExists){
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
