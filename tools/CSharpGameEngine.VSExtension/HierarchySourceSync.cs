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

/// <summary>Pure selection policy for the Hierarchy's declaration-order rows.</summary>
internal static class HierarchySelectionPlanner {
    internal static HierarchyNodeSelection Select(
        HierarchyNodeSelection Existing,IReadOnlyList<NodePath> OrderedPaths,NodePath Clicked,
        bool ControlPressed,bool ShiftPressed){
        if(!OrderedPaths.Contains(Clicked)){
            return Existing;
        }

        List<NodePath> Ordered=OrderedPaths.ToList();

        if(ShiftPressed){
            NodePath Anchor=Existing.RangeAnchor is { } ExistingAnchor&&OrderedPaths.Contains(ExistingAnchor)
                ?ExistingAnchor
                :Clicked;
            int First=Math.Min(Ordered.IndexOf(Anchor),Ordered.IndexOf(Clicked));
            int Last=Math.Max(Ordered.IndexOf(Anchor),Ordered.IndexOf(Clicked));
            return new HierarchyNodeSelection(OrderedPaths.Skip(First).Take(Last-First+1).ToArray(),Clicked,Anchor);
        }

        if(!ControlPressed){
            return new HierarchyNodeSelection([Clicked],Clicked,Clicked);
        }

        List<NodePath> Updated=Existing.Paths.Where(Path=>OrderedPaths.Contains(Path)).ToList();
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
    internal static IReadOnlyList<SceneSourceMarkerRange> Create(SceneModel? Model,IReadOnlyList<NodePath> Paths){
        if(Model?.Root is null||Paths.Count==0){
            return [];
        }

        List<TextSpan> Spans=Paths
            .Select(Model.FindNode)
            .Where(static Node=>Node is not null)
            .Select(static Node=>Node!.Location.Span)
            .Where(static Span=>Span.Length>0)
            .OrderBy(static Span=>Span.Start)
            .ThenByDescending(static Span=>Span.Length)
            .ToList();
        if(Spans.Count==0){
            return [];
        }

        List<SceneSourceMarkerRange> Result=[];
        TextSpan Current=Spans[0];
        for(int Index=1;Index<Spans.Count;Index++){
            TextSpan Next=Spans[Index];
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
        out string FilePath,out int Position){
        FilePath=string.Empty;
        Position=0;
        if(!IsDocumentOpen||Model is null||PrimaryPath is not { } Path||Model.FindNode(Path) is not { } Node){
            return false;
        }

        FilePath=Node.Location.FilePath;
        Position=Node.Location.Span.Start;
        return true;
    }
}
