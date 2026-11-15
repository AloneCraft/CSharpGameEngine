using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// 起点アセットから参照元（Referencers）と依存先（Dependencies）をたどって <see cref="AssetReferenceGraph"/> を作る。
/// 参照元の検出は呼び出し側から渡す（実機では <see cref="AssetReferencePlanner"/> の Inspect 計画）ので、Visual Studio なしで単体テストできる。
/// 依存先は Scene / Data / ShaderGraph の本文に、同じプロジェクトの別アセットの相対パスが現れるかで判定する（型付きの Assets.* 参照は C# 側の参照元として表れる）。
/// </summary>
internal static class AssetReferenceGraphBuilder {
    internal const int MaximumDepth=3;
    internal static async Task<AssetReferenceGraph> BuildAsync(AssetWorkspaceItem Focus,IReadOnlyList<AssetWorkspaceItem> Items,int ReferencerDepth,int DependencyDepth,
        Func<AssetWorkspaceItem,CancellationToken,Task<IReadOnlyList<string>>> FindReferencers,Func<string,string?> ReadText,CancellationToken Token){
        ReferencerDepth=Math.Max(0,Math.Min(MaximumDepth,ReferencerDepth));
        DependencyDepth=Math.Max(0,Math.Min(MaximumDepth,DependencyDepth));
        var ByPath=new Dictionary<string,AssetWorkspaceItem>(StringComparer.OrdinalIgnoreCase);
        foreach(var Item in Items.Where(Candidate=>Candidate.Source.ProjectPath==Focus.Source.ProjectPath)){ ByPath[Item.Source.SourcePath]=Item; }
        ByPath[Focus.Source.SourcePath]=Focus;
        var Nodes=new Dictionary<string,AssetReferenceNode>(StringComparer.OrdinalIgnoreCase);
        var Edges=new List<AssetReferenceEdge>();
        Nodes[Focus.Source.SourcePath]=NodeFor(Focus,AssetReferenceNodeRole.Focus,0);
        var Frontier=new List<AssetWorkspaceItem>{Focus};
        for(var Depth=1;Depth<=ReferencerDepth&&Frontier.Count>0;Depth++){
            var Next=new List<AssetWorkspaceItem>();
            foreach(var Target in Frontier){
                Token.ThrowIfCancellationRequested();
                foreach(var Path in await FindReferencers(Target,Token)){
                    if(string.Equals(Path,Target.Source.SourcePath,StringComparison.OrdinalIgnoreCase)){ continue; }
                    AddEdge(Edges,Path,Target.Source.SourcePath);
                    if(Nodes.ContainsKey(Path)){ continue; }
                    if(ByPath.TryGetValue(Path,out var Referencer)){ Nodes[Path]=NodeFor(Referencer,AssetReferenceNodeRole.Referencer,-Depth);Next.Add(Referencer); }
                    else { Nodes[Path]=new AssetReferenceNode(Path,RelativeTo(Focus,Path),AssetWorkspaceClassification.Classify(Path),AssetReferenceNodeRole.Referencer,-Depth,false); }
                }
            }
            Frontier=Next;
        }
        Frontier=[Focus];
        for(var Depth=1;Depth<=DependencyDepth&&Frontier.Count>0;Depth++){
            var Next=new List<AssetWorkspaceItem>();
            foreach(var Source in Frontier){
                Token.ThrowIfCancellationRequested();
                foreach(var Dependency in FindDependencies(Source,ByPath.Values.ToArray(),ReadText)){
                    AddEdge(Edges,Source.Source.SourcePath,Dependency.Source.SourcePath);
                    if(Nodes.ContainsKey(Dependency.Source.SourcePath)){ continue; }
                    Nodes[Dependency.Source.SourcePath]=NodeFor(Dependency,AssetReferenceNodeRole.Dependency,Depth);
                    Next.Add(Dependency);
                }
            }
            Frontier=Next;
        }
        var Known=Edges.Where(Edge=>Nodes.ContainsKey(Edge.FromPath)&&Nodes.ContainsKey(Edge.ToPath)).ToArray();
        return new AssetReferenceGraph(Nodes[Focus.Source.SourcePath],Nodes.Values.ToArray(),Known);
    }
    /// <summary>テキストを持つ種類（Scene / Data / ShaderGraph）の本文に、別アセットの相対パスが書かれているものを依存先とする。</summary>
    internal static IReadOnlyList<AssetWorkspaceItem> FindDependencies(AssetWorkspaceItem Source,IReadOnlyList<AssetWorkspaceItem> Candidates,Func<string,string?> ReadText){
        if(Source.Kind is not(AssetWorkspaceKind.Scene or AssetWorkspaceKind.Data or AssetWorkspaceKind.ShaderGraph)){ return []; }
        var Text=ReadText(Source.Source.SourcePath);
        if(string.IsNullOrEmpty(Text)){ return []; }
        return Candidates.Where(Candidate=>Candidate.Source.ProjectPath==Source.Source.ProjectPath
            &&!string.Equals(Candidate.Source.SourcePath,Source.Source.SourcePath,StringComparison.OrdinalIgnoreCase)
            &&Mentions(Text!,Candidate.Source.RelativePath)).ToArray();
    }
    private static bool Mentions(string Text,string RelativePath){
        if(string.IsNullOrEmpty(RelativePath)){ return false; }
        var Forward=RelativePath.Replace('\\','/');
        return Text.IndexOf(Forward,StringComparison.OrdinalIgnoreCase)>=0
            ||Text.IndexOf(Forward.Replace("/","\\\\"),StringComparison.OrdinalIgnoreCase)>=0;
    }
    private static void AddEdge(List<AssetReferenceEdge> Edges,string From,string To){
        if(!Edges.Any(Edge=>string.Equals(Edge.FromPath,From,StringComparison.OrdinalIgnoreCase)&&string.Equals(Edge.ToPath,To,StringComparison.OrdinalIgnoreCase))){ Edges.Add(new AssetReferenceEdge(From,To)); }
    }
    private static AssetReferenceNode NodeFor(AssetWorkspaceItem Item,AssetReferenceNodeRole Role,int Depth)=>new(Item.Source.SourcePath,Item.Source.RelativePath,Item.Kind,Role,Depth,true);
    private static string RelativeTo(AssetWorkspaceItem Focus,string Path){
        var Root=Focus.Source.ProjectRoot;
        if(string.IsNullOrEmpty(Root)){ return Path; }
        var Full=System.IO.Path.GetFullPath(Path);
        var Prefix=System.IO.Path.GetFullPath(Root).TrimEnd('\\','/')+System.IO.Path.DirectorySeparatorChar;
        return Full.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)?Full.Substring(Prefix.Length).Replace('\\','/'):Path;
    }
}
