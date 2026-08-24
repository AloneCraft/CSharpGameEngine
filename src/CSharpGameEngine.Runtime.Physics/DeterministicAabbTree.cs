namespace CSharpGameEngine.Runtime.Physics;

/// <summary>Single-writer dynamic AABB tree. Stable IDs resolve every equal-cost insertion tie.</summary>
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
        Results.Sort(Start,Count,Comparer<ulong>.Default);
        return Count;
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
            PhysicsScalar LeftCost=SurfaceArea(LeftCombined)-SurfaceArea(Left.Bounds);
            PhysicsScalar RightCost=SurfaceArea(RightCombined)-SurfaceArea(Right.Bounds);
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
        Node Parent=new() {
            Parent=OldParent,
            Left=Sibling,
            Right=Leaf,
            Bounds=Combine(Sibling.Bounds,Leaf.Bounds),
            MinimumStableId=Math.Min(Sibling.MinimumStableId,Leaf.StableId),
        };
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
        new PhysicsVector3(Min(Left.Min.R,Right.Min.R),Min(Left.Min.U,Right.Min.U),Min(Left.Min.F,Right.Min.F)),
        new PhysicsVector3(Max(Left.Max.R,Right.Max.R),Max(Left.Max.U,Right.Max.U),Max(Left.Max.F,Right.Max.F)));

    private static PhysicsScalar SurfaceArea(in PhysicsAabb Bounds){
        PhysicsVector3 Size=Bounds.Max-Bounds.Min;
        return 2*(Size.R*Size.U+Size.R*Size.F+Size.U*Size.F);
    }
    private static PhysicsScalar Min(PhysicsScalar Left,PhysicsScalar Right)=>Left<Right?Left:Right;
    private static PhysicsScalar Max(PhysicsScalar Left,PhysicsScalar Right)=>Left>Right?Left:Right;
}
