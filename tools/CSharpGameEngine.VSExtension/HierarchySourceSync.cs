using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// The Hierarchy's selection model is deliberately independent from WPF's single-selected-item
/// implementation.  That lets Ctrl/Shift selection survive a live Roslyn rebuild.
/// </summary>
internal sealed class HierarchyNodeSelection {
    internal static readonly HierarchyNodeSelection Empty=new([],PrimaryPath:null,RangeAnchor:null);

    internal HierarchyNodeSelection(
        IReadOnlyList<NodePath> Paths,NodePath? PrimaryPath,NodePath? RangeAnchor){
        this.Paths=Paths.ToArray();
        this.PrimaryPath=PrimaryPath;
        this.RangeAnchor=RangeAnchor;
    }

    internal IReadOnlyList<NodePath> Paths { get; }

    internal NodePath? PrimaryPath { get; }

    internal NodePath? RangeAnchor { get; }
}

/// <summary>
/// M0046: <c>TreeView.SelectedItemChanged</c>は、再解析による<c>ItemsSource</c>差し替えの後に
/// WPFが<b>遅延生成</b>した<c>TreeViewItem</c>へスタイルSetter経由で<c>IsSelected</c>が入った
/// タイミングでも発火する。これは<c>Populate</c>の同期ブロックが終わった後のレイアウトパスなので、
/// 同期スコープのフラグでは覆えない。そのため「ツリーが<b>既に</b>キーボードフォーカスを
/// 持っているか」を選択発行の必要条件にする。
///
/// <para>
/// これにより、ユーザーがコードエディタで入力している最中に再解析が着地しても、Hierarchyが
/// フォーカスを奪わない(ADR-0041のツールウィンドウ フォーカスモデル)。矢印キーでのツリー内
/// 移動は、その時点でツリーがフォーカスを持っているので従来どおり発行される。
/// </para>
/// </summary>
internal static class HierarchyFocusPolicy {
    internal static bool ShouldPublishSelectionChange(
        bool SuppressSelectionPublish,bool TreeHasKeyboardFocus)=>
        !SuppressSelectionPublish&&TreeHasKeyboardFocus;
}

/// <summary>Pure selection policy for the Hierarchy's declaration-order rows.</summary>
internal static class HierarchySelectionPlanner {
    internal static HierarchyNodeSelection Select(
        HierarchyNodeSelection Existing,IReadOnlyList<NodePath> OrderedPaths,NodePath Clicked,
        bool ControlPressed,bool ShiftPressed){
        if(!OrderedPaths.Contains(Clicked)){
            return Existing;
        }

        var Ordered=OrderedPaths.ToList();

        if(ShiftPressed){
            var Anchor=Existing.RangeAnchor is { } ExistingAnchor&&OrderedPaths.Contains(ExistingAnchor)
                           ?ExistingAnchor
                           :Clicked;
            var First=Math.Min(Ordered.IndexOf(Anchor),Ordered.IndexOf(Clicked));
            var Last=Math.Max(Ordered.IndexOf(Anchor),Ordered.IndexOf(Clicked));
            return new HierarchyNodeSelection(OrderedPaths.Skip(First).Take(Last-First+1).ToArray(),Clicked,Anchor);
        }

        if(!ControlPressed){
            return new HierarchyNodeSelection([Clicked],Clicked,Clicked);
        }

        var Updated=Existing.Paths.Where(Path=>OrderedPaths.Contains(Path)).ToList();
        if(!Updated.Remove(Clicked)){
            Updated.Add(Clicked);
        }

        // Keep presentation and downstream SceneSelection deterministic: declaration order, not
        // the order in which Ctrl clicks happened.
        Updated=OrderedPaths.Where(Updated.Contains).ToList();
        NodePath?Primary=Updated.Contains(Clicked)
            ?Clicked
            :Existing.PrimaryPath is { } Previous&&Updated.Contains(Previous)
                ?Previous
                :Updated.Count>0?Updated[^1]:null;
        return new HierarchyNodeSelection(Updated,Primary,Clicked);
    }
}

/// <summary>Source range to decorate without changing the editor's native text selection.</summary>
internal readonly struct SceneSourceMarkerRange : IEquatable<SceneSourceMarkerRange> {
    internal SceneSourceMarkerRange(TextSpan Span)=>this.Span=Span;

    internal TextSpan Span { get; }

    public bool Equals(SceneSourceMarkerRange Other)=>this.Span.Equals(Other.Span);

    public override bool Equals(object? Object)=>Object is SceneSourceMarkerRange Other&&this.Equals(Other);

    public override int GetHashCode()=>this.Span.GetHashCode();
}

/// <summary>Maps selected hierarchy paths to the declaration spans owned by the Scene model.</summary>
internal static class SceneSourceMarkerPlanner {
    /// <param name="JsonText">
    /// The live text of a JSON scene (ADR-0070), whose node locations are looked up in the JSON rather
    /// than in the model's designer view; <c>null</c> for a C# scene.
    /// </param>
    internal static IReadOnlyList<SceneSourceMarkerRange> Create(SceneModel? Model,IReadOnlyList<NodePath> Paths,string? JsonText=null){
        if(Model?.Root is null||Paths.Count==0){
            return [];
        }

        var Spans=Paths
                 .Select(Path=>TryGetSpan(Model,Path,JsonText,out var Span)?Span:default)
                 .Where(static Span=>Span.Length>0)
                 .OrderBy(static Span=>Span.Start)
                 .ThenByDescending(static Span=>Span.Length)
                 .ToList();
        if(Spans.Count==0){
            return [];
        }

        List<SceneSourceMarkerRange> Result=[];
        var Current=Spans[0];
        for(var Index=1;Index<Spans.Count;Index++){
            var Next=Spans[Index];
            if(Next.Start<=Current.End){
                Current=TextSpan.FromBounds(Current.Start,Math.Max(Current.End,Next.End));
                continue;
            }

            Result.Add(new SceneSourceMarkerRange(Current));
            Current=Next;
        }

        Result.Add(new SceneSourceMarkerRange(Current));
        return Result;
    }

    internal static bool TryCreateNavigation(
        SceneModel? Model,NodePath? PrimaryPath,bool IsDocumentOpen,
        out string FilePath,out int Position,string? JsonText=null){
        FilePath=string.Empty;
        Position=0;
        if(!IsDocumentOpen||Model is null||PrimaryPath is not { } Path||Model.FindNode(Path) is not { } Node
            ||!TryGetSpan(Model,Path,JsonText,out var Span)){
            return false;
        }

        FilePath=Node.Location.FilePath;
        Position=Span.Start;
        return true;
    }

    private static bool TryGetSpan(SceneModel Model,NodePath Path,string? JsonText,out TextSpan Span){
        Span=default;
        if(JsonText is not null){
            return CSharpGameEngine.SceneAnalysis.Json.SceneJsonNodeLocator.TryFind(JsonText,Path,out Span);
        }
        if(Model.FindNode(Path) is not { } Node){
            return false;
        }
        Span=Node.Location.Span;
        return true;
    }
}

/// <summary>
/// M0040: 「今の選択」からソースマーカー用の<see cref="NodePath"/>集合を解決する。VS SDKへ
/// 一切触れない純粋関数なのでユニットテスト可能 — <see cref="CSharpGameEngineViewerPackage"/>の
/// 複数の呼び出し元(Viewport/Inspector起点の選択変更、Roslyn再解析着地の両方)がここへ委譲する
/// ことで、「どの経路が『今の選択』をどこから読むか」がずれて再解析のたびに無関係な位置へ
/// 再スクロールしてしまう不具合(Hierarchyパネル専用の<c>SceneEditorHub.HierarchySelection</c>を
/// 参照していた箇所が、Viewport起点の選択更新では一切更新されない静的フィールドを読んでいたために
/// 発生していた)を作らないようにする。
/// </summary>
internal static class SceneSelectionPathResolver {
    internal static IReadOnlyList<NodePath>Resolve(
        SceneSelection?Selection,SceneKey PinnedKey,SceneProjection?Projection){
        if(Selection is not { }Current||Projection is null||!Current.Key.Equals(PinnedKey)){
            return [];
        }

        List<NodePath>Paths=[];
        foreach(EditorNodeId Id in Current.NodeIds){
            if(Projection.TryGetNode(Id,out var Node)){
                Paths.Add(Node.Path);
            }
        }
        return Paths;
    }
}
