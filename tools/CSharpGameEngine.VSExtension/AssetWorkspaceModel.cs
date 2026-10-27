using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
internal sealed class AssetWorkspaceQuery {
    internal string ProjectPath{get;set;}=string.Empty;
    internal string Folder{get;set;}=string.Empty;
    internal string Search{get;set;}=string.Empty;
    internal string Kind{get;set;}=string.Empty;
    internal string Provider{get;set;}=string.Empty;
    internal string State{get;set;}=string.Empty;
    internal bool WholeProject{get;set;}
    internal bool Locked{get;set;}
    internal bool Details{get;set;}
    internal void Reveal(AssetWorkspaceItem Item){
        this.ProjectPath=Item.Source.ProjectPath;
        this.Folder=Path.GetDirectoryName(Item.Source.SourcePath)??string.Empty;
        this.WholeProject=false;
        this.Search=string.Empty;
        this.Kind=string.Empty;
        this.Provider=string.Empty;
        this.State=string.Empty;
    }
    internal AssetWorkspaceItem[]Apply(IEnumerable<AssetWorkspaceItem>Items,CancellationToken CancellationToken){
        var Result=new List<AssetWorkspaceItem>();
        foreach(var Item in Items){
            CancellationToken.ThrowIfCancellationRequested();
            if(!string.IsNullOrEmpty(this.ProjectPath)&&!string.Equals(Item.Source.ProjectPath,this.ProjectPath,StringComparison.OrdinalIgnoreCase)){ continue; }
            if(!this.WholeProject&&!string.IsNullOrEmpty(this.Folder)&&!InFolder(Item,this.Folder)){ continue; }
            if(!string.IsNullOrEmpty(this.Search)&&Item.Source.RelativePath.IndexOf(this.Search,StringComparison.OrdinalIgnoreCase)<0){ continue; }
            if(!string.IsNullOrEmpty(this.Kind)&&Item.Kind.ToString()!=this.Kind){ continue; }
            if(!string.IsNullOrEmpty(this.Provider)&&Item.Source.Provider!=this.Provider){ continue; }
            if(!string.IsNullOrEmpty(this.State)&&Item.State.ToString()!=this.State){ continue; }
            Result.Add(Item);
        }
        return Result.OrderBy(static Item=>Item.Source.RelativePath,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static bool IsWithin(string PathValue,string Folder)=>PathValue.StartsWith(
        Folder.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    internal static bool InFolder(AssetWorkspaceItem Item,string Folder)=>IsWithin(Item.Source.SourcePath,Folder)
        ||!string.IsNullOrEmpty(Item.Source.ProjectRoot)&&!Path.IsPathRooted(Item.Source.RelativePath)
            &&IsWithin(Path.Combine(Item.Source.ProjectRoot,Item.Source.RelativePath.Replace('/',Path.DirectorySeparatorChar)),Folder);
}
internal static class AssetWorkspaceSelection {
    internal static event Action<IReadOnlyList<AssetWorkspaceItem>>?Changed;
    internal static event Action<string>?RevealRequested;
    internal static IReadOnlyList<AssetWorkspaceItem>Current{get;private set;}=Array.Empty<AssetWorkspaceItem>();
    internal static void Select(IReadOnlyList<AssetWorkspaceItem>Items){ Current=Items;Changed?.Invoke(Items); }
    internal static void Reveal(string SourcePath)=>RevealRequested?.Invoke(SourcePath);
    internal static void Clear()=>Select(Array.Empty<AssetWorkspaceItem>());
}
