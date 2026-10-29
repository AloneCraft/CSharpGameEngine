using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
/// <summary>参照ビューアーのノードが起点に対してどの位置にあるか。</summary>
internal enum AssetReferenceNodeRole { Focus,Referencer,Dependency }
/// <summary>参照グラフの 1 ノード。<see cref="Depth"/> は起点が 0、参照元が負、依存先が正。</summary>
internal sealed class AssetReferenceNode(string Path,string RelativePath,AssetWorkspaceKind? Kind,AssetReferenceNodeRole Role,int Depth,bool IsAsset) {
    internal string Path { get; }=Path;
    internal string RelativePath { get; }=RelativePath;
    /// <summary>アセットとして分類できないファイル（C# ソースなど）は null。</summary>
    internal AssetWorkspaceKind? Kind { get; }=Kind;
    internal AssetReferenceNodeRole Role { get; }=Role;
    internal int Depth { get; }=Depth;
    /// <summary>Assets ウィンドウの項目として存在するか。起点の切り替えができるのはアセットだけ。</summary>
    internal bool IsAsset { get; }=IsAsset;
    internal string Name=>System.IO.Path.GetFileName(this.Path);
}
/// <summary><see cref="FromPath"/> のファイルが <see cref="ToPath"/> のファイルを参照している。</summary>
internal sealed class AssetReferenceEdge(string FromPath,string ToPath) {
    internal string FromPath { get; }=FromPath;
    internal string ToPath { get; }=ToPath;
}
internal sealed class AssetReferenceGraph(AssetReferenceNode Focus,IReadOnlyList<AssetReferenceNode> Nodes,IReadOnlyList<AssetReferenceEdge> Edges) {
    internal AssetReferenceNode Focus { get; }=Focus;
    internal IReadOnlyList<AssetReferenceNode> Nodes { get; }=Nodes;
    internal IReadOnlyList<AssetReferenceEdge> Edges { get; }=Edges;
}
/// <summary>列（<see cref="AssetReferenceNode.Depth"/>）ごとに縦へ積むだけの単純なレイアウト。WPF に依存しないので単体テストできる。</summary>
internal static class AssetReferenceGraphLayout {
    internal const float NodeWidth=200;
    internal const float NodeHeight=120;
    internal const float ColumnSpacing=140;
    internal const float RowSpacing=24;
    /// <summary>各ノードの左上座標。座標の原点は最も左の列・最も上の行。</summary>
    internal static IReadOnlyDictionary<string,(float X,float Y)> Compute(AssetReferenceGraph Graph){
        var Result=new Dictionary<string,(float X,float Y)>(StringComparer.OrdinalIgnoreCase);
        var Columns=Graph.Nodes.GroupBy(static Node=>Node.Depth).OrderBy(static Group=>Group.Key).ToArray();
        if(Columns.Length==0){ return Result; }
        var Tallest=Columns.Max(static Group=>Group.Count());
        var TotalHeight=Tallest*NodeHeight+(Tallest-1)*RowSpacing;
        var Left=Columns[0].Key;
        foreach(var Column in Columns){
            var Nodes=Column.OrderBy(static Node=>Node.Name,StringComparer.OrdinalIgnoreCase).ToArray();
            var Height=Nodes.Length*NodeHeight+(Nodes.Length-1)*RowSpacing;
            var Top=(TotalHeight-Height)/2;
            for(var Index=0;Index<Nodes.Length;Index++){
                Result[Nodes[Index].Path]=((Column.Key-Left)*(NodeWidth+ColumnSpacing),Top+Index*(NodeHeight+RowSpacing));
            }
        }
        return Result;
    }
}
