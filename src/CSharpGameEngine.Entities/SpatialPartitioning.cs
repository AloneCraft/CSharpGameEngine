using System;
using System.Collections.Generic;
namespace CSharpGameEngine.Entities;
/// <summary>Local fixed-point bounds that opt an Entity into World global spatial synchronization.</summary>
public struct SpatialBounds {
    public Logical.Vector3 Center;
    public Logical.Vector3 HalfExtents;
    public SpatialLayer Layer;

    /// <summary>Deterministic unit cube bounds used by generated sample render entities.</summary>
    public static SpatialBounds UnitCube=>new() {
        Center=Logical.Vector3.Zero,
        HalfExtents=new Logical.Vector3(
            Logical.Scalar.FromDouble(0.5),
            Logical.Scalar.FromDouble(0.5),
            Logical.Scalar.FromDouble(0.5)),
        Layer=new SpatialLayer(0),
    };
}
/// <summary>A single spatial layer. The default value is layer zero.</summary>
public readonly record struct SpatialLayer(byte Value);
/// <summary>Bit mask used to select one or more spatial layers during a query.</summary>
public readonly struct SpatialLayerMask(ulong Value) {
    public ulong Value { get; }=Value;
    public static SpatialLayerMask None=>default;
    public static SpatialLayerMask All=>new(ulong.MaxValue);
    public static SpatialLayerMask FromLayer(SpatialLayer Layer)=>new(1UL<<Layer.Value);
    public static SpatialLayerMask FromLayer(int Layer)=>Layer is >=0 and <64?new(1UL<<Layer):throw new ArgumentOutOfRangeException(nameof(Layer));
    public bool Contains(SpatialLayer Layer)=>(this.Value&(1UL<<Layer.Value))!=0;
    public static SpatialLayerMask operator |(SpatialLayerMask Left,SpatialLayerMask Right)=>new(Left.Value|Right.Value);
}
/// <summary>An axis-aligned world-space fixed-point bounding box.</summary>
public readonly struct SpatialAabb(Logical.Vector3 Min,Logical.Vector3 Max) {
    public Logical.Vector3 Min { get; }=Min;
    public Logical.Vector3 Max { get; }=Max;
    public static bool TryCreate(Logical.Vector3 Center,Logical.Vector3 HalfExtents,out SpatialAabb Bounds){
        Bounds=default;
        if(HalfExtents.R<Logical.Scalar.Zero||HalfExtents.U<Logical.Scalar.Zero||HalfExtents.F<Logical.Scalar.Zero
            ||!Logical.Scalar.TrySubtract(Center.R,HalfExtents.R,out Logical.Scalar MinR)
            ||!Logical.Scalar.TrySubtract(Center.U,HalfExtents.U,out Logical.Scalar MinU)
            ||!Logical.Scalar.TrySubtract(Center.F,HalfExtents.F,out Logical.Scalar MinF)
            ||!Logical.Scalar.TryAdd(Center.R,HalfExtents.R,out Logical.Scalar MaxR)
            ||!Logical.Scalar.TryAdd(Center.U,HalfExtents.U,out Logical.Scalar MaxU)
            ||!Logical.Scalar.TryAdd(Center.F,HalfExtents.F,out Logical.Scalar MaxF)){
            return false;
        }
        Bounds=new SpatialAabb(new Logical.Vector3(MinR,MinU,MinF),new Logical.Vector3(MaxR,MaxU,MaxF));
        return true;
    }
    public bool Contains(in SpatialAabb Other)=>this.Min.R<=Other.Min.R&&this.Min.U<=Other.Min.U&&this.Min.F<=Other.Min.F
        &&this.Max.R>=Other.Max.R&&this.Max.U>=Other.Max.U&&this.Max.F>=Other.Max.F;
    public bool Contains(Logical.Vector3 Point)=>Point.R>=this.Min.R&&Point.R<=this.Max.R
        &&Point.U>=this.Min.U&&Point.U<=this.Max.U&&Point.F>=this.Min.F&&Point.F<=this.Max.F;
    public bool Intersects(in SpatialAabb Other)=>this.Min.R<=Other.Max.R&&this.Max.R>=Other.Min.R
        &&this.Min.U<=Other.Max.U&&this.Max.U>=Other.Min.U&&this.Min.F<=Other.Max.F&&this.Max.F>=Other.Min.F;
}
/// <summary>A fixed-point sphere query volume.</summary>
public readonly struct SpatialSphere(Logical.Vector3 Center,Logical.Scalar Radius) {
    public Logical.Vector3 Center { get; }=Center;
    public Logical.Scalar Radius { get; }=Radius;
    public static bool TryCreate(Logical.Vector3 Center,Logical.Scalar Radius,out SpatialSphere Sphere){
        Sphere=default;
        if(Radius<Logical.Scalar.Zero){
            return false;
        }
        Sphere=new SpatialSphere(Center,Radius);
        return true;
    }
}
/// <summary>A normalized fixed-point ray with a non-negative maximum distance.</summary>
public readonly struct SpatialRay(Logical.Vector3 Origin,Logical.Vector3 Direction,Logical.Scalar MaxDistance) {
    public Logical.Vector3 Origin { get; }=Origin;
    public Logical.Vector3 Direction { get; }=Direction;
    public Logical.Scalar MaxDistance { get; }=MaxDistance;
    public static bool TryCreate(Logical.Vector3 Origin,Logical.Vector3 Direction,Logical.Scalar MaxDistance,out SpatialRay Ray){
        Ray=default;
        if(MaxDistance<Logical.Scalar.Zero||!Direction.TryNormalize(out Logical.Vector3 NormalizedDirection)){
            return false;
        }
        Ray=new SpatialRay(Origin,NormalizedDirection,MaxDistance);
        return true;
    }
}
/// <summary>An Entity candidate hit by a spatial ray.</summary>
public readonly record struct SpatialRayHit(Entity Entity,Logical.Scalar Distance);
/// <summary>Read-only description of one octree cell for diagnostics and visualization.</summary>
public readonly record struct SpatialDebugNode(SpatialAabb Bounds,int Depth,int EntryCount,bool IsRoot);
public enum SpatialQueryStatus {
    Success,
    InvalidInput,
    NumericOverflow,
}
public readonly record struct SpatialQueryResult(SpatialQueryStatus Status,int AddedCount);
public enum SpatialUpdateStatus {
    Success,
    InvalidInput,
    NumericOverflow,
}
/// <summary>Read-only contract shared by global and system-owned Entity spatial indices.</summary>
public interface IReadOnlyEntitySpatialIndex {
    int Count { get; }
    bool TryGetBounds(Entity Entity,out SpatialAabb Bounds,out SpatialLayer Layer);
    SpatialQueryResult QueryAabb(in SpatialAabb Bounds,SpatialLayerMask Layers,List<Entity> Results);
    SpatialQueryResult QueryPoint(Logical.Vector3 Point,SpatialLayerMask Layers,List<Entity> Results);
    SpatialQueryResult QuerySphere(in SpatialSphere Sphere,SpatialLayerMask Layers,List<Entity> Results);
    SpatialQueryResult QueryRay(in SpatialRay Ray,SpatialLayerMask Layers,List<SpatialRayHit> Results);
}
/// <summary>Mutable contract for independently-owned Entity spatial indices.</summary>
public interface IEntitySpatialIndex:IReadOnlyEntitySpatialIndex {
    SpatialUpdateStatus Upsert(Entity Entity,in SpatialAabb Bounds,SpatialLayer Layer);
    bool Remove(Entity Entity);
    void Clear();
    void Compact();
}
/// <summary>Validated loose-octree tuning values for a system-owned index.</summary>
public readonly struct SpatialIndexSettings(int NodeCapacity,int MaxDepth,Logical.Scalar Looseness,Logical.Scalar MinimumRootHalfExtent) {
    public int NodeCapacity { get; }=NodeCapacity;
    public int MaxDepth { get; }=MaxDepth;
    public Logical.Scalar Looseness { get; }=Looseness;
    public Logical.Scalar MinimumRootHalfExtent { get; }=MinimumRootHalfExtent;
    public static SpatialIndexSettings Default=>new(16,16,Logical.Scalar.FromDouble(1.5),Logical.Scalar.One);
    internal bool IsValid=>this.NodeCapacity>0&&this.MaxDepth>=0&&this.Looseness>=Logical.Scalar.One&&this.MinimumRootHalfExtent>Logical.Scalar.Zero;
}
/// <summary>
/// Dynamic loose octree with a single proxy for each Entity. It is deliberately single-writer;
/// callers must not query it concurrently with Upsert, Remove, Clear, or Compact.
/// </summary>
public sealed class EntityLooseOctree:IEntitySpatialIndex {
    private sealed class Entry(Entity Entity,SpatialAabb Bounds,SpatialLayer Layer) {
        internal Entity Entity=Entity;
        internal SpatialAabb Bounds=Bounds;
        internal SpatialLayer Layer=Layer;
        internal Node? Node;
    }
    private sealed class Node(Node? Parent,int Depth,Logical.Vector3 Center,Logical.Scalar HalfExtent,SpatialAabb TightBounds,SpatialAabb LooseBounds) {
        internal readonly Node? Parent=Parent;
        internal readonly int Depth=Depth;
        internal readonly Logical.Vector3 Center=Center;
        internal readonly Logical.Scalar HalfExtent=HalfExtent;
        internal readonly SpatialAabb TightBounds=TightBounds;
        internal readonly SpatialAabb LooseBounds=LooseBounds;
        internal readonly List<Entry> Entries=[];
        internal Node[]? Children;
    }
    private readonly SpatialIndexSettings Settings;
    private readonly Dictionary<Entity,Entry>EntriesByEntity=[];
    private readonly Stack<Node>QueryStack=[];
    private Node? Root;
    public EntityLooseOctree():this(SpatialIndexSettings.Default){
    }
    public EntityLooseOctree(SpatialIndexSettings Settings){
        if(!Settings.IsValid){
            throw new ArgumentOutOfRangeException(nameof(Settings));
        }
        this.Settings=Settings;
    }
    public int Count=>this.EntriesByEntity.Count;    /// <summary>Appends the root and every non-empty descendant without allocating.</summary>
    public int CollectDebugNodes(List<SpatialDebugNode>Results){
        if(Results is null){
            throw new ArgumentNullException(nameof(Results));
        }
        int InitialCount=Results.Count;
        if(this.Root is null){
            return 0;
        }
        this.QueryStack.Clear();
        this.QueryStack.Push(this.Root);
        while(this.QueryStack.Count>0){
            Node Node=this.QueryStack.Pop();
            bool IsRoot=ReferenceEquals(Node,this.Root);
            if(IsRoot||Node.Entries.Count>0){
                Results.Add(new SpatialDebugNode(Node.TightBounds,Node.Depth,Node.Entries.Count,IsRoot));
            }
            if(Node.Children is not null){
                for(int Index=Node.Children.Length-1;Index>=0;Index--){
                    this.QueryStack.Push(Node.Children[Index]);
                }
            }
        }
        return Results.Count-InitialCount;
    }
    public bool TryGetBounds(Entity Entity,out SpatialAabb Bounds,out SpatialLayer Layer){
        if(this.EntriesByEntity.TryGetValue(Entity,out Entry? Entry)){
            Bounds=Entry.Bounds;
            Layer=Entry.Layer;
            return true;
        }
        Bounds=default;
        Layer=default;
        return false;
    }
    public SpatialUpdateStatus Upsert(Entity Entity,in SpatialAabb Bounds,SpatialLayer Layer){
        if(Bounds.Min.R>Bounds.Max.R||Bounds.Min.U>Bounds.Max.U||Bounds.Min.F>Bounds.Max.F){
            return SpatialUpdateStatus.InvalidInput;
        }
        if(this.EntriesByEntity.TryGetValue(Entity,out Entry? Existing)){
            Node? ExistingNode=Existing.Node;
            if(ExistingNode is not null&&ExistingNode.LooseBounds.Contains(Bounds)){
                Existing.Bounds=Bounds;
                Existing.Layer=Layer;
                return SpatialUpdateStatus.Success;
            }
            this.RemoveEntry(Existing);
            Existing.Bounds=Bounds;
            Existing.Layer=Layer;
            if(!this.TryEnsureRootContains(Bounds)||!this.TryInsert(Existing,this.Root!)){
                this.EntriesByEntity.Remove(Entity);
                return SpatialUpdateStatus.NumericOverflow;
            }
            return SpatialUpdateStatus.Success;
        }
        Entry Added=new(Entity,Bounds,Layer);
        if(!this.TryEnsureRootContains(Bounds)||!this.TryInsert(Added,this.Root!)){
            return SpatialUpdateStatus.NumericOverflow;
        }
        this.EntriesByEntity.Add(Entity,Added);
        return SpatialUpdateStatus.Success;
    }
    public bool Remove(Entity Entity){
        if(!this.EntriesByEntity.Remove(Entity,out Entry? Existing)){
            return false;
        }
        Node? Parent=this.RemoveEntry(Existing);
        while(Parent is not null){
            this.TryMerge(Parent);
            Parent=Parent.Parent;
        }
        return true;
    }
    public void Clear(){
        this.EntriesByEntity.Clear();
        this.Root=null;
        this.QueryStack.Clear();
    }
    internal void RemoveAllExcept(HashSet<Entity> Seen){
        List<Entity> Removed=[];
        foreach(Entity Entity in this.EntriesByEntity.Keys){
            if(!Seen.Contains(Entity)){
                Removed.Add(Entity);
            }
        }
        foreach(Entity Entity in Removed){
            this.Remove(Entity);
        }
    }
    public void Compact(){
        if(this.EntriesByEntity.Count==0){
            this.Root=null;
            return;
        }
        List<Entry> Entries=[.. this.EntriesByEntity.Values];
        this.Root=null;
        foreach(Entry Entry in Entries){
            Entry.Node=null;
        }
        foreach(Entry Entry in Entries){
            if(!this.TryEnsureRootContains(Entry.Bounds)||!this.TryInsert(Entry,this.Root!)){
                throw new OverflowException("The spatial index cannot represent its live bounds.");
            }
        }
    }
    public SpatialQueryResult QueryAabb(in SpatialAabb Bounds,SpatialLayerMask Layers,List<Entity> Results){
        if(Results is null||Bounds.Min.R>Bounds.Max.R||Bounds.Min.U>Bounds.Max.U||Bounds.Min.F>Bounds.Max.F){
            return new(SpatialQueryStatus.InvalidInput,0);
        }
        SpatialAabb QueryBounds=Bounds;
        return this.QueryEntities(Layers,Results,NodeBounds=>NodeBounds.Intersects(QueryBounds),EntryBounds=>EntryBounds.Intersects(QueryBounds));
    }
    public SpatialQueryResult QueryPoint(Logical.Vector3 Point,SpatialLayerMask Layers,List<Entity> Results){
        if(Results is null){
            return new(SpatialQueryStatus.InvalidInput,0);
        }
        return this.QueryEntities(Layers,Results,NodeBounds=>NodeBounds.Contains(Point),EntryBounds=>EntryBounds.Contains(Point));
    }
    public SpatialQueryResult QuerySphere(in SpatialSphere Sphere,SpatialLayerMask Layers,List<Entity> Results){
        if(Results is null){
            return new(SpatialQueryStatus.InvalidInput,0);
        }
        int InitialCount=Results.Count;
        SpatialSphere QuerySphere=Sphere;
        SpatialQueryStatus Status=this.QueryEntitiesCore(Layers,NodeBounds=>TryIntersectsSphere(NodeBounds,QuerySphere,out bool Intersects)&&Intersects,
            EntryBounds=>TryIntersectsSphere(EntryBounds,QuerySphere,out bool Intersects)&&Intersects,Results);
        return CompleteQuery(Status,Results,InitialCount);
    }
    public SpatialQueryResult QueryRay(in SpatialRay Ray,SpatialLayerMask Layers,List<SpatialRayHit> Results){
        if(Results is null){
            return new(SpatialQueryStatus.InvalidInput,0);
        }
        int InitialCount=Results.Count;
        if(this.Root is null||Layers.Value==0){
            return new(SpatialQueryStatus.Success,0);
        }
        this.QueryStack.Clear();
        this.QueryStack.Push(this.Root);
        while(this.QueryStack.Count>0){
            Node Node=this.QueryStack.Pop();
            if(!TryIntersectRay(Node.LooseBounds,Ray,out bool NodeIntersects,out _)){
                return CompleteQuery(SpatialQueryStatus.NumericOverflow,Results,InitialCount);
            }
            if(!NodeIntersects){
                continue;
            }
            foreach(Entry Entry in Node.Entries){
                if(!Layers.Contains(Entry.Layer)){
                    continue;
                }
                if(!TryIntersectRay(Entry.Bounds,Ray,out bool EntryIntersects,out Logical.Scalar Distance)){
                    return CompleteQuery(SpatialQueryStatus.NumericOverflow,Results,InitialCount);
                }
                if(EntryIntersects){
                    Results.Add(new SpatialRayHit(Entry.Entity,Distance));
                }
            }
            if(Node.Children is not null){
                foreach(Node Child in Node.Children){
                    this.QueryStack.Push(Child);
                }
            }
        }
        return CompleteQuery(SpatialQueryStatus.Success,Results,InitialCount);
    }
    private SpatialQueryResult QueryEntities(SpatialLayerMask Layers,List<Entity> Results,Func<SpatialAabb,bool>NodeFilter,Func<SpatialAabb,bool>EntryFilter){
        int InitialCount=Results.Count;
        SpatialQueryStatus Status=this.QueryEntitiesCore(Layers,NodeFilter,EntryFilter,Results);
        return CompleteQuery(Status,Results,InitialCount);
    }
    private SpatialQueryStatus QueryEntitiesCore(SpatialLayerMask Layers,Func<SpatialAabb,bool>NodeFilter,Func<SpatialAabb,bool>EntryFilter,List<Entity> Results){
        if(this.Root is null||Layers.Value==0){
            return SpatialQueryStatus.Success;
        }
        this.QueryStack.Clear();
        this.QueryStack.Push(this.Root);
        while(this.QueryStack.Count>0){
            Node Node=this.QueryStack.Pop();
            if(!NodeFilter(Node.LooseBounds)){
                continue;
            }
            foreach(Entry Entry in Node.Entries){
                if(Layers.Contains(Entry.Layer)&&EntryFilter(Entry.Bounds)){
                    Results.Add(Entry.Entity);
                }
            }
            if(Node.Children is not null){
                foreach(Node Child in Node.Children){
                    this.QueryStack.Push(Child);
                }
            }
        }
        return SpatialQueryStatus.Success;
    }
    private static SpatialQueryResult CompleteQuery(SpatialQueryStatus Status,List<Entity> Results,int InitialCount){
        if(Status!=SpatialQueryStatus.Success){
            Results.RemoveRange(InitialCount,Results.Count-InitialCount);
            return new(Status,0);
        }
        return new(Status,Results.Count-InitialCount);
    }
    private static SpatialQueryResult CompleteQuery(SpatialQueryStatus Status,List<SpatialRayHit> Results,int InitialCount){
        if(Status!=SpatialQueryStatus.Success){
            Results.RemoveRange(InitialCount,Results.Count-InitialCount);
            return new(Status,0);
        }
        return new(Status,Results.Count-InitialCount);
    }
    private Node? RemoveEntry(Entry Entry){
        Node? Node=Entry.Node;
        if(Node is null){
            return null;
        }
        Node.Entries.Remove(Entry);
        Entry.Node=null;
        return Node;
    }
    private bool TryEnsureRootContains(in SpatialAabb Bounds){
        if(this.Root is null){
            if(!this.TryCreateInitialRoot(Bounds,out Node? NewRoot)||NewRoot is null){
                return false;
            }
            this.Root=NewRoot;
        }
        while(!this.Root.TightBounds.Contains(Bounds)){
            if(!this.TryExpandRoot(Bounds)){
                return false;
            }
        }
        return true;
    }
    private bool TryCreateInitialRoot(in SpatialAabb Bounds,out Node? Root){
        Root=null;
        if(!Logical.Scalar.TryAdd(Bounds.Min.R,Bounds.Max.R,out Logical.Scalar SumR)
            ||!Logical.Scalar.TryAdd(Bounds.Min.U,Bounds.Max.U,out Logical.Scalar SumU)
            ||!Logical.Scalar.TryAdd(Bounds.Min.F,Bounds.Max.F,out Logical.Scalar SumF)
            ||!Logical.Scalar.TryDivide(SumR,2,out Logical.Scalar CenterR)
            ||!Logical.Scalar.TryDivide(SumU,2,out Logical.Scalar CenterU)
            ||!Logical.Scalar.TryDivide(SumF,2,out Logical.Scalar CenterF)
            ||!Logical.Scalar.TrySubtract(Bounds.Max.R,Bounds.Min.R,out Logical.Scalar SizeR)
            ||!Logical.Scalar.TrySubtract(Bounds.Max.U,Bounds.Min.U,out Logical.Scalar SizeU)
            ||!Logical.Scalar.TrySubtract(Bounds.Max.F,Bounds.Min.F,out Logical.Scalar SizeF)
            ||!Logical.Scalar.TryDivide(SizeR,2,out Logical.Scalar HalfR)
            ||!Logical.Scalar.TryDivide(SizeU,2,out Logical.Scalar HalfU)
            ||!Logical.Scalar.TryDivide(SizeF,2,out Logical.Scalar HalfF)){
            return false;
        }
        Logical.Scalar HalfExtent=Max(this.Settings.MinimumRootHalfExtent,Max(HalfR,Max(HalfU,HalfF)));
        return this.TryCreateNode(null,0,new Logical.Vector3(CenterR,CenterU,CenterF),HalfExtent,out Root);
    }
    private bool TryExpandRoot(in SpatialAabb Bounds){
        Node Previous=this.Root!;
        Logical.Scalar ShiftR=Bounds.Min.R<Previous.TightBounds.Min.R?-Previous.HalfExtent:Bounds.Max.R>Previous.TightBounds.Max.R?Previous.HalfExtent:Logical.Scalar.Zero;
        Logical.Scalar ShiftU=Bounds.Min.U<Previous.TightBounds.Min.U?-Previous.HalfExtent:Bounds.Max.U>Previous.TightBounds.Max.U?Previous.HalfExtent:Logical.Scalar.Zero;
        Logical.Scalar ShiftF=Bounds.Min.F<Previous.TightBounds.Min.F?-Previous.HalfExtent:Bounds.Max.F>Previous.TightBounds.Max.F?Previous.HalfExtent:Logical.Scalar.Zero;
        if(!Logical.Scalar.TryAdd(Previous.Center.R,ShiftR,out Logical.Scalar CenterR)
            ||!Logical.Scalar.TryAdd(Previous.Center.U,ShiftU,out Logical.Scalar CenterU)
            ||!Logical.Scalar.TryAdd(Previous.Center.F,ShiftF,out Logical.Scalar CenterF)
            ||!Logical.Scalar.TryAdd(Previous.HalfExtent,Previous.HalfExtent,out Logical.Scalar HalfExtent)
            ||!this.TryCreateNode(null,0,new Logical.Vector3(CenterR,CenterU,CenterF),HalfExtent,out Node? Expanded)||Expanded is null){
            return false;
        }
        this.Root=Expanded;
        foreach(Entry Entry in this.EntriesByEntity.Values){
            Entry.Node=null;
        }
        foreach(Entry Entry in this.EntriesByEntity.Values){
            if(!this.TryInsert(Entry,Expanded)){
                return false;
            }
        }
        return true;
    }
    private bool TryInsert(Entry Entry,Node Node){
        if(Node.Children is not null&&this.TryGetContainingChild(Node,Entry.Bounds,out Node? Child)&&Child is not null){
            return this.TryInsert(Entry,Child);
        }
        Node.Entries.Add(Entry);
        Entry.Node=Node;
        if(Node.Entries.Count>this.Settings.NodeCapacity&&Node.Depth<this.Settings.MaxDepth&&Node.Children is null){
            if(!this.TrySplit(Node)){
                Node.Entries.Remove(Entry);
                Entry.Node=null;
                return false;
            }
        }
        return true;
    }
    private bool TrySplit(Node Node){
        if(!this.TryCreateChildren(Node,out Node[]? Children)){
            return false;
        }
        Node.Children=Children;
        for(int Index=Node.Entries.Count-1;Index>=0;Index--){
            Entry Entry=Node.Entries[Index];
            if(this.TryGetContainingChild(Node,Entry.Bounds,out Node? Child)&&Child is not null){
                Node.Entries.RemoveAt(Index);
                Entry.Node=null;
                if(!this.TryInsert(Entry,Child)){
                    return false;
                }
            }
        }
        return true;
    }
    private bool TryCreateChildren(Node Parent,out Node[]? Children){
        Children=new Node[8];
        if(!Logical.Scalar.TryDivide(Parent.HalfExtent,2,out Logical.Scalar ChildHalf)){
            Children=null;
            return false;
        }
        for(int Index=0;Index<8;Index++){
            Logical.Scalar OffsetR=(Index&1)==0?-ChildHalf:ChildHalf;
            Logical.Scalar OffsetU=(Index&2)==0?-ChildHalf:ChildHalf;
            Logical.Scalar OffsetF=(Index&4)==0?-ChildHalf:ChildHalf;
            if(!Logical.Scalar.TryAdd(Parent.Center.R,OffsetR,out Logical.Scalar CenterR)
                ||!Logical.Scalar.TryAdd(Parent.Center.U,OffsetU,out Logical.Scalar CenterU)
                ||!Logical.Scalar.TryAdd(Parent.Center.F,OffsetF,out Logical.Scalar CenterF)
                ||!this.TryCreateNode(Parent,Parent.Depth+1,new Logical.Vector3(CenterR,CenterU,CenterF),ChildHalf,out Node? Child)){
                Children=null;
                return false;
            }
            Children[Index]=Child!;
        }
        return true;
    }
    private bool TryGetContainingChild(Node Parent,in SpatialAabb Bounds,out Node? Child){
        Child=null;
        if(Parent.Children is null){
            return false;
        }
        foreach(Node Candidate in Parent.Children){
            if(Candidate.LooseBounds.Contains(Bounds)){
                Child=Candidate;
                return true;
            }
        }
        return false;
    }
    private bool TryCreateNode(Node? Parent,int Depth,Logical.Vector3 Center,Logical.Scalar HalfExtent,out Node? Node){
        Node=null;
        if(!SpatialAabb.TryCreate(Center,new Logical.Vector3(HalfExtent,HalfExtent,HalfExtent),out SpatialAabb TightBounds)
            ||!Logical.Scalar.TryMultiply(HalfExtent,this.Settings.Looseness,out Logical.Scalar LooseHalf)
            ||!SpatialAabb.TryCreate(Center,new Logical.Vector3(LooseHalf,LooseHalf,LooseHalf),out SpatialAabb LooseBounds)){
            return false;
        }
        Node=new Node(Parent,Depth,Center,HalfExtent,TightBounds,LooseBounds);
        return true;
    }
    private void TryMerge(Node Node){
        if(Node.Children is null||CountEntries(Node)>this.Settings.NodeCapacity){
            return;
        }
        foreach(Node Child in Node.Children){
            CollectInto(Node,Child);
        }
        Node.Children=null;
    }
    private static int CountEntries(Node Node){
        int Count=Node.Entries.Count;
        if(Node.Children is not null){
            foreach(Node Child in Node.Children){
                Count+=CountEntries(Child);
            }
        }
        return Count;
    }
    private static void CollectInto(Node Target,Node Source){
        foreach(Entry Entry in Source.Entries){
            Target.Entries.Add(Entry);
            Entry.Node=Target;
        }
        if(Source.Children is not null){
            foreach(Node Child in Source.Children){
                CollectInto(Target,Child);
            }
        }
    }
    private static bool TryIntersectsSphere(in SpatialAabb Bounds,in SpatialSphere Sphere,out bool Intersects){
        Intersects=false;
        Logical.Scalar ClosestR=Clamp(Sphere.Center.R,Bounds.Min.R,Bounds.Max.R);
        Logical.Scalar ClosestU=Clamp(Sphere.Center.U,Bounds.Min.U,Bounds.Max.U);
        Logical.Scalar ClosestF=Clamp(Sphere.Center.F,Bounds.Min.F,Bounds.Max.F);
        if(!Logical.Scalar.TrySubtract(Sphere.Center.R,ClosestR,out Logical.Scalar DeltaR)
            ||!Logical.Scalar.TrySubtract(Sphere.Center.U,ClosestU,out Logical.Scalar DeltaU)
            ||!Logical.Scalar.TrySubtract(Sphere.Center.F,ClosestF,out Logical.Scalar DeltaF)
            ||!Logical.Scalar.TryMultiply(DeltaR,DeltaR,out Logical.Scalar DistanceR)
            ||!Logical.Scalar.TryMultiply(DeltaU,DeltaU,out Logical.Scalar DistanceU)
            ||!Logical.Scalar.TryMultiply(DeltaF,DeltaF,out Logical.Scalar DistanceF)
            ||!Logical.Scalar.TryAdd(DistanceR,DistanceU,out Logical.Scalar DistanceSquared)
            ||!Logical.Scalar.TryAdd(DistanceSquared,DistanceF,out DistanceSquared)
            ||!Logical.Scalar.TryMultiply(Sphere.Radius,Sphere.Radius,out Logical.Scalar RadiusSquared)){
            return false;
        }
        Intersects=DistanceSquared<=RadiusSquared;
        return true;
    }
    private static bool TryIntersectRay(in SpatialAabb Bounds,in SpatialRay Ray,out bool Intersects,out Logical.Scalar Distance){
        Intersects=false;
        Distance=default;
        Logical.Scalar Near=Logical.Scalar.Zero;
        Logical.Scalar Far=Ray.MaxDistance;
        if(!TryIntersectRayAxis(Bounds.Min.R,Bounds.Max.R,Ray.Origin.R,Ray.Direction.R,ref Near,ref Far)
            ||!TryIntersectRayAxis(Bounds.Min.U,Bounds.Max.U,Ray.Origin.U,Ray.Direction.U,ref Near,ref Far)
            ||!TryIntersectRayAxis(Bounds.Min.F,Bounds.Max.F,Ray.Origin.F,Ray.Direction.F,ref Near,ref Far)){
            return false;
        }
        Intersects=Far>=Near;
        Distance=Near;
        return true;
    }
    private static bool TryIntersectRayAxis(Logical.Scalar Min,Logical.Scalar Max,Logical.Scalar Origin,Logical.Scalar Direction,ref Logical.Scalar Near,ref Logical.Scalar Far){
        if(Direction.IsZero){
            return Origin>=Min&&Origin<=Max;
        }
        if(!Logical.Scalar.TrySubtract(Min,Origin,out Logical.Scalar MinOffset)
            ||!Logical.Scalar.TrySubtract(Max,Origin,out Logical.Scalar MaxOffset)
            ||!Logical.Scalar.TryDivide(MinOffset,Direction,out Logical.Scalar First)
            ||!Logical.Scalar.TryDivide(MaxOffset,Direction,out Logical.Scalar Second)){
            return false;
        }
        Logical.Scalar AxisNear=First<Second?First:Second;
        Logical.Scalar AxisFar=First<Second?Second:First;
        if(AxisNear>Near){
            Near=AxisNear;
        }
        if(AxisFar<Far){
            Far=AxisFar;
        }
        return Far>=Near;
    }
    private static Logical.Scalar Clamp(Logical.Scalar Value,Logical.Scalar Min,Logical.Scalar Max)=>Value<Min?Min:Value>Max?Max:Value;
    private static Logical.Scalar Max(Logical.Scalar Left,Logical.Scalar Right)=>Left>Right?Left:Right;
}

/// <summary>
/// Shared implementation anchor for generated precision profiles. The public EntityLooseOctree
/// facade remains the stable API while generated consumers select the concrete scalar/vector profile.
/// </summary>
internal sealed class LooseOctreeCore<TEntity,TScalar,TVector,TProfile> where TProfile:struct {
    private readonly EntityLooseOctree Index=new();
    internal int Count => this.Index.Count;
    internal SpatialUpdateStatus Upsert(Entity Entity,SpatialAabb Bounds,byte Layer)=>this.Index.Upsert(Entity,Bounds,new SpatialLayer(Layer));
    internal bool Remove(Entity Entity)=>this.Index.Remove(Entity);
    internal void Clear()=>this.Index.Clear();
    internal void Compact()=>this.Index.Compact();
    internal IReadOnlyEntitySpatialIndex AsReadOnly()=>this.Index;
}
