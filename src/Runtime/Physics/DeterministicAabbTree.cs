namespace CSharpGameEngine.Runtime.Physics;

/// <summary>単一書き込みの動的 AABB ツリー。同じコストになる挿入先は安定 ID で決定する。</summary>
internal sealed class DeterministicAabbTree {
    private sealed class Node {
        internal Node? Parent;
        internal Node? Left;
        internal Node? Right;
        internal PhysicsAabb Bounds;
        internal ulong StableId;
        internal ulong MinimumStableId;
        internal bool IsLeaf=>this.Left is null;
    }

    private readonly Dictionary<ulong,Node> Leaves=[];
    private readonly Stack<Node> QueryStack=[];
    private readonly Stack<(Node Node,int Depth)>DebugStack=[];
    private readonly Stack<Node> FreeBranches=[];
    private Node? Root;

    internal int Count=>this.Leaves.Count;

    internal bool Upsert(ulong StableId,in PhysicsAabb Bounds){
        if(this.Leaves.TryGetValue(StableId,out Node? Existing)){
            if(Existing.Bounds==Bounds){ return false; }
            this.RemoveLeaf(Existing);
            Existing.Bounds=Bounds;
            Existing.MinimumStableId=StableId;
            this.InsertLeaf(Existing);
            return true;
        }
        Node Leaf=new() { Bounds=Bounds,StableId=StableId,MinimumStableId=StableId };
        this.Leaves.Add(StableId,Leaf);
        this.InsertLeaf(Leaf);
        return true;
    }

    internal bool Remove(ulong StableId){
        if(!this.Leaves.Remove(StableId,out Node? Leaf)){ return false; }
        this.RemoveLeaf(Leaf);
        return true;
    }

    internal void Clear(){
        this.Root=null;
        this.Leaves.Clear();
        this.QueryStack.Clear();
        this.DebugStack.Clear();
        this.FreeBranches.Clear();
    }

    internal int CollectDebugNodes(List<PhysicsDebugTreeNode>Results,int MaximumCount){
        ArgumentNullException.ThrowIfNull(Results);
        if(MaximumCount<0){ throw new ArgumentOutOfRangeException(nameof(MaximumCount)); }
        int Start=Results.Count;
        if(this.Root is null||MaximumCount==0){ return 0; }
        this.DebugStack.Clear();
        this.DebugStack.Push((this.Root,0));
        while(this.DebugStack.Count>0&&Results.Count-Start<MaximumCount){
            (Node Node,int Depth)=this.DebugStack.Pop();
            Results.Add(new PhysicsDebugTreeNode(Node.Bounds,Depth,Node.IsLeaf,Node.StableId));
            if(Node.IsLeaf){ continue; }
            Node Left=Node.Left!;
            Node Right=Node.Right!;
            if(Left.MinimumStableId<=Right.MinimumStableId){
                this.DebugStack.Push((Right,checked(Depth+1)));
                this.DebugStack.Push((Left,checked(Depth+1)));
            }
            else {
                this.DebugStack.Push((Left,checked(Depth+1)));
                this.DebugStack.Push((Right,checked(Depth+1)));
            }
        }
        this.DebugStack.Clear();
        return Results.Count-Start;
    }

    internal int Query(in PhysicsAabb Bounds,List<ulong> Results){
        int Start=Results.Count;
        if(this.Root is null){ return 0; }
        this.QueryStack.Clear();
        this.QueryStack.Push(this.Root);
        while(this.QueryStack.Count>0){
            Node Node=this.QueryStack.Pop();
            if(!Node.Bounds.Intersects(Bounds)){ continue; }
            if(Node.IsLeaf){
                Results.Add(Node.StableId);
                continue;
            }
            Node Left=Node.Left!;
            Node Right=Node.Right!;
            if(Left.MinimumStableId<Right.MinimumStableId){
                this.QueryStack.Push(Right);
                this.QueryStack.Push(Left);
            }
            else {
                this.QueryStack.Push(Left);
                this.QueryStack.Push(Right);
            }
        }
        int Count=Results.Count-Start;
        if(Count>1){ SortResults(Results,Start,Count); }
        return Count;
    }

    private static void SortResults(List<ulong>Results,int Start,int Count){
        Span<ulong>Values=System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Results).Slice(Start,Count);
        for(int Root=Count/2-1;Root>=0;Root--){ SiftDown(Values,Root,Count); }
        for(int End=Count-1;End>0;End--){
            (Values[0],Values[End])=(Values[End],Values[0]);
            SiftDown(Values,0,End);
        }
    }

    private static void SiftDown(Span<ulong>Values,int Root,int Count){
        while(true){
            int Child=checked(Root*2+1);
            if(Child>=Count){ return; }
            if(Child+1<Count&&Values[Child]<Values[Child+1]){ Child++; }
            if(Values[Root]>=Values[Child]){ return; }
            (Values[Root],Values[Child])=(Values[Child],Values[Root]);
            Root=Child;
        }
    }

    private void InsertLeaf(Node Leaf){
        if(this.Root is null){
            this.Root=Leaf;
            Leaf.Parent=null;
            return;
        }
        Node Sibling=this.Root;
        while(!Sibling.IsLeaf){
            Node Left=Sibling.Left!;
            Node Right=Sibling.Right!;
            PhysicsAabb LeftCombined=Combine(Left.Bounds,Leaf.Bounds);
            PhysicsAabb RightCombined=Combine(Right.Bounds,Leaf.Bounds);
            LogicalScalar LeftCost=SurfaceArea(LeftCombined)-SurfaceArea(Left.Bounds);
            LogicalScalar RightCost=SurfaceArea(RightCombined)-SurfaceArea(Right.Bounds);
            if(LeftCost<RightCost){
                Sibling=Left;
            }
            else if(RightCost<LeftCost){
                Sibling=Right;
            }
            else {
                Sibling=Left.MinimumStableId<=Right.MinimumStableId?Left:Right;
            }
        }
        Node? OldParent=Sibling.Parent;
        Node Parent=this.FreeBranches.TryPop(out Node? Reused)?Reused:new Node();
        Parent.Parent=OldParent;
        Parent.Left=Sibling;
        Parent.Right=Leaf;
        Parent.Bounds=Combine(Sibling.Bounds,Leaf.Bounds);
        Parent.StableId=0;
        Parent.MinimumStableId=Math.Min(Sibling.MinimumStableId,Leaf.StableId);
        Sibling.Parent=Parent;
        Leaf.Parent=Parent;
        if(OldParent is null){
            this.Root=Parent;
        }
        else if(ReferenceEquals(OldParent.Left,Sibling)){
            OldParent.Left=Parent;
        }
        else {
            OldParent.Right=Parent;
        }
        Refit(Parent);
    }

    private void RemoveLeaf(Node Leaf){
        if(ReferenceEquals(Leaf,this.Root)){
            this.Root=null;
            Leaf.Parent=null;
            return;
        }
        Node Parent=Leaf.Parent!;
        Node Sibling=ReferenceEquals(Parent.Left,Leaf)?Parent.Right!:Parent.Left!;
        Node? GrandParent=Parent.Parent;
        if(GrandParent is null){
            this.Root=Sibling;
            Sibling.Parent=null;
        }
        else {
            if(ReferenceEquals(GrandParent.Left,Parent)){ GrandParent.Left=Sibling; }
            else { GrandParent.Right=Sibling; }
            Sibling.Parent=GrandParent;
            Refit(GrandParent);
        }
        Leaf.Parent=null;
        Parent.Parent=null;
        Parent.Left=null;
        Parent.Right=null;
        Parent.StableId=0;
        Parent.MinimumStableId=0;
        this.FreeBranches.Push(Parent);
    }

    private static void Refit(Node Start){
        for(Node? Current=Start;Current is not null;Current=Current.Parent){
            if(Current.IsLeaf){
                Current.MinimumStableId=Current.StableId;
            }
            else {
                Current.Bounds=Combine(Current.Left!.Bounds,Current.Right!.Bounds);
                Current.MinimumStableId=Math.Min(Current.Left.MinimumStableId,Current.Right.MinimumStableId);
            }
        }
    }

    private static PhysicsAabb Combine(in PhysicsAabb Left,in PhysicsAabb Right)=>new(
        new LogicalVector3(Min(Left.Min.R,Right.Min.R),Min(Left.Min.U,Right.Min.U),Min(Left.Min.F,Right.Min.F)),
        new LogicalVector3(Max(Left.Max.R,Right.Max.R),Max(Left.Max.U,Right.Max.U),Max(Left.Max.F,Right.Max.F)));

    private static LogicalScalar SurfaceArea(in PhysicsAabb Bounds){
        LogicalVector3 Size=Bounds.Max-Bounds.Min;
        return 2*(Size.R*Size.U+Size.R*Size.F+Size.U*Size.F);
    }
    private static LogicalScalar Min(LogicalScalar Left,LogicalScalar Right)=>Left<Right?Left:Right;
    private static LogicalScalar Max(LogicalScalar Left,LogicalScalar Right)=>Left>Right?Left:Right;
}
