using System.Collections.Immutable;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.World;
public readonly record struct FrameLocalBounds(LogicalVector3 Center,LogicalVector3 HalfExtents) {
    internal bool IsValid=>this.HalfExtents.R>LogicalScalar.Zero&&this.HalfExtents.U>LogicalScalar.Zero&&this.HalfExtents.F>LogicalScalar.Zero&&this.TryGetMinMax(out _,out _);
    public bool TryGetMinMax(out LogicalVector3 Minimum,out LogicalVector3 Maximum){
        Minimum=default;
        Maximum=default;
        if(!LogicalScalar.TrySubtract(this.Center.R,this.HalfExtents.R,out var MinR)
            ||!LogicalScalar.TrySubtract(this.Center.U,this.HalfExtents.U,out var MinU)
            ||!LogicalScalar.TrySubtract(this.Center.F,this.HalfExtents.F,out var MinF)
            ||!LogicalScalar.TryAdd(this.Center.R,this.HalfExtents.R,out var MaxR)
            ||!LogicalScalar.TryAdd(this.Center.U,this.HalfExtents.U,out var MaxU)
            ||!LogicalScalar.TryAdd(this.Center.F,this.HalfExtents.F,out var MaxF)){ return false; }
        Minimum=new LogicalVector3(MinR,MinU,MinF);
        Maximum=new LogicalVector3(MaxR,MaxU,MaxF);
        return true;
    }
}
public sealed class WorldCellDefinition {
    public WorldCellDefinition(WorldCellId Id,ReferenceFrameId Frame,FrameLocalBounds Bounds,AssetKey EntityPayload,int LevelOfDetail=0,IEnumerable<WorldCellId>?Dependencies=null,IEnumerable<AssetArtifactReference>?ArtifactClosure=null){
        this.Id=Id;
        this.Frame=Frame;
        this.Bounds=Bounds;
        this.EntityPayload=EntityPayload;
        this.LevelOfDetail=LevelOfDetail;
        this.Dependencies=Dependencies?.ToImmutableArray()??[];
        this.ArtifactClosure=ArtifactClosure?.ToImmutableArray()??[];
    }
    public WorldCellId Id{get;}
    public ReferenceFrameId Frame{get;}
    public FrameLocalBounds Bounds{get;}
    public AssetKey EntityPayload{get;}
    public int LevelOfDetail{get;}
    public ImmutableArray<WorldCellId> Dependencies{get;}
    public ImmutableArray<AssetArtifactReference> ArtifactClosure{get;}
}
public enum WorldPartitionErrorCode { None,NullDefinition,InvalidCellId,InvalidFrameId,InvalidBounds,InvalidEntityPayload,InvalidLevelOfDetail,DuplicateCell,InvalidDependency,DuplicateDependency,SelfDependency,MissingDependency,DependencyCycle,InvalidArtifact,DuplicateArtifact,MissingEntityPayloadArtifact }
public readonly record struct WorldPartitionError(WorldPartitionErrorCode Code,WorldCellId Cell,WorldCellId RelatedCell,string Message) {
    public static WorldPartitionError None=>new(WorldPartitionErrorCode.None,default,default,string.Empty);
}
public sealed class WorldPartitionManifest {
    // Artifact/topology order is an established external contract. Keep the original
    // canonical textual ordering even though hot-path selection uses allocation-free IDs.
    private static readonly IComparer<WorldCellId>IdComparer=Comparer<WorldCellId>.Create(static (Left,Right)=>StringComparer.Ordinal.Compare(Left.ToString(),Right.ToString()));
    private readonly ImmutableDictionary<WorldCellId,WorldCellDefinition>Definitions;
    private readonly ImmutableDictionary<WorldCellId,int>TopologicalRanks;
    private WorldPartitionManifest(ImmutableDictionary<WorldCellId,WorldCellDefinition>Definitions,ImmutableArray<WorldCellId>TopologicalOrder){
        this.Definitions=Definitions;
        this.TopologicalOrder=TopologicalOrder;
        this.TopologicalRanks=TopologicalOrder.Select(static(Id,Rank)=>(Id,Rank)).ToImmutableDictionary(static Pair=>Pair.Id,static Pair=>Pair.Rank);
    }
    public IReadOnlyDictionary<WorldCellId,WorldCellDefinition> Cells=>this.Definitions;
    public ImmutableArray<WorldCellId> TopologicalOrder{get;}
    public WorldCellDefinition this[WorldCellId Cell]=>this.Definitions[Cell];
    public bool Contains(WorldCellId Cell)=>this.Definitions.ContainsKey(Cell);
    public static bool TryCreate(IEnumerable<WorldCellDefinition>?Definitions,out WorldPartitionManifest Manifest,out WorldPartitionError Error){
        Manifest=null!;
        if(Definitions is null){ Error=CreateError(WorldPartitionErrorCode.NullDefinition,default,default,"World cell definitions are required.");return false; }
        var Builder=ImmutableDictionary.CreateBuilder<WorldCellId,WorldCellDefinition>();
        foreach(var Definition in Definitions){
            if(Definition is null){ Error=CreateError(WorldPartitionErrorCode.NullDefinition,default,default,"World cell definitions cannot contain null.");return false; }
            if(!Definition.Id.IsValid){ Error=CreateError(WorldPartitionErrorCode.InvalidCellId,Definition.Id,default,"World cell ID must be valid.");return false; }
            if(!Definition.Frame.IsValid){ Error=CreateError(WorldPartitionErrorCode.InvalidFrameId,Definition.Id,default,"World cell reference frame ID must be valid.");return false; }
            if(!Definition.Bounds.IsValid){ Error=CreateError(WorldPartitionErrorCode.InvalidBounds,Definition.Id,default,"World cell half extents must be positive.");return false; }
            if(!Definition.EntityPayload.IsValid){ Error=CreateError(WorldPartitionErrorCode.InvalidEntityPayload,Definition.Id,default,"World cell entity payload identity must be valid.");return false; }
            if(Definition.LevelOfDetail<0){ Error=CreateError(WorldPartitionErrorCode.InvalidLevelOfDetail,Definition.Id,default,"World cell level of detail cannot be negative.");return false; }
            if(!Builder.TryAdd(Definition.Id,Definition)){ Error=CreateError(WorldPartitionErrorCode.DuplicateCell,Definition.Id,default,"World cell ID is defined more than once.");return false; }
            var Dependencies=new HashSet<WorldCellId>();
            foreach(var Dependency in Definition.Dependencies){
                if(!Dependency.IsValid){ Error=CreateError(WorldPartitionErrorCode.InvalidDependency,Definition.Id,Dependency,"World cell dependency ID must be valid.");return false; }
                if(Dependency==Definition.Id){ Error=CreateError(WorldPartitionErrorCode.SelfDependency,Definition.Id,Dependency,"World cell cannot depend on itself.");return false; }
                if(!Dependencies.Add(Dependency)){ Error=CreateError(WorldPartitionErrorCode.DuplicateDependency,Definition.Id,Dependency,"World cell dependency is listed more than once.");return false; }
            }
            var Artifacts=new HashSet<AssetKey>();
            foreach(var Artifact in Definition.ArtifactClosure){
                if(!Artifact.IsValid){ Error=CreateError(WorldPartitionErrorCode.InvalidArtifact,Definition.Id,default,"World cell artifact reference and content hash must be valid.");return false; }
                if(!Artifacts.Add(Artifact.Key)){ Error=CreateError(WorldPartitionErrorCode.DuplicateArtifact,Definition.Id,default,"World cell artifact is listed more than once.");return false; }
            }
            if(!Artifacts.Contains(Definition.EntityPayload)){ Error=CreateError(WorldPartitionErrorCode.MissingEntityPayloadArtifact,Definition.Id,default,"World cell artifact closure must contain its entity payload.");return false; }
        }
        foreach(var Definition in Builder.Values){
            foreach(var Dependency in Definition.Dependencies){
                if(!Builder.ContainsKey(Dependency)){ Error=CreateError(WorldPartitionErrorCode.MissingDependency,Definition.Id,Dependency,"World cell dependency is not present in the manifest.");return false; }
            }
        }
        if(!TryCreateTopologicalOrder(Builder,out var Order,out Error)){ return false; }
        Manifest=new WorldPartitionManifest(Builder.ToImmutable(),Order);
        Error=WorldPartitionError.None;
        return true;
    }
    public bool TryGetActivationOrder(WorldCellId Cell,out ImmutableArray<WorldCellId>Order){
        Order=[];
        if(!this.Definitions.ContainsKey(Cell)){ return false; }
        var Required=new HashSet<WorldCellId>();
        var Pending=new Stack<WorldCellId>();
        Pending.Push(Cell);
        while(Pending.Count!=0){
            var Current=Pending.Pop();
            if(!Required.Add(Current)){ continue; }
            foreach(var Dependency in this.Definitions[Current].Dependencies){ Pending.Push(Dependency); }
        }
        Order=this.OrderSubset(Required,false);
        return true;
    }
    internal ImmutableArray<WorldCellId> OrderSubset(IReadOnlySet<WorldCellId>Cells,bool Reverse){
        var Ordered=Cells.OrderBy(Cell=>this.TopologicalRanks[Cell]).ToArray();
        if(Reverse){ Array.Reverse(Ordered); }
        return [.. Ordered];
    }
    private static bool TryCreateTopologicalOrder(ImmutableDictionary<WorldCellId,WorldCellDefinition>.Builder Definitions,out ImmutableArray<WorldCellId>Order,out WorldPartitionError Error){
        var InDegree=Definitions.Keys.ToDictionary(static Cell=>Cell,static _=>0);
        var Dependents=Definitions.Keys.ToDictionary(static Cell=>Cell,static _=>new List<WorldCellId>());
        foreach(var Definition in Definitions.Values){
            InDegree[Definition.Id]=Definition.Dependencies.Length;
            foreach(var Dependency in Definition.Dependencies){ Dependents[Dependency].Add(Definition.Id); }
        }
        var Ready=new SortedSet<WorldCellId>(IdComparer);
        foreach(var Pair in InDegree){ if(Pair.Value==0){ Ready.Add(Pair.Key); } }
        var Result=ImmutableArray.CreateBuilder<WorldCellId>(Definitions.Count);
        while(Ready.Count!=0){
            var Cell=Ready.Min;
            Ready.Remove(Cell);
            Result.Add(Cell);
            foreach(var Dependent in Dependents[Cell]){
                var Remaining=--InDegree[Dependent];
                if(Remaining==0){ Ready.Add(Dependent); }
            }
        }
        if(Result.Count!=Definitions.Count){
            var Cell=InDegree.Where(static Pair=>Pair.Value!=0).Select(static Pair=>Pair.Key).OrderBy(static Cell=>Cell,IdComparer).First();
            Order=[];
            Error=CreateError(WorldPartitionErrorCode.DependencyCycle,Cell,default,"World cell dependency graph contains a cycle.");
            return false;
        }
        Order=Result.MoveToImmutable();
        Error=WorldPartitionError.None;
        return true;
    }
    private static WorldPartitionError CreateError(WorldPartitionErrorCode Code,WorldCellId Cell,WorldCellId RelatedCell,string Message)=>new(Code,Cell,RelatedCell,Message);
}
public enum WorldCellState { Unloaded,Loading,Loaded,Activating,Active,Deactivating,Unloading,Faulted }
public enum WorldCellFaultCode { None,Cancelled,CorruptArtifact,LoadFailed,ActivationFailed,MissingReferenceFrame,UnloadFailed }
public readonly record struct WorldCellFault(WorldCellFaultCode Code,string Message) {
    public static WorldCellFault None=>new(WorldCellFaultCode.None,string.Empty);
}
internal readonly record struct WorldCellLoadCompletion(WorldCellId Cell,WorldCellFault Fault);
public readonly record struct WorldCellStatus(WorldCellId Cell,WorldCellState State,WorldCellFault Fault);
public enum WorldCellOperationErrorCode { None,UnknownCell,InvalidState,TransitionInProgress,NoTransition,StaleTransition,StaleFrameSnapshot,DuplicateDesiredCell,DependencyNotDesired,CellNotLoaded,MissingReferenceFrame,InvalidFault }
public readonly record struct WorldCellOperationError(WorldCellOperationErrorCode Code,WorldCellId Cell,string Message) {
    public static WorldCellOperationError None=>new(WorldCellOperationErrorCode.None,default,string.Empty);
}
public sealed class WorldCellSnapshot {
    private readonly ImmutableHashSet<WorldCellId>ActiveSet;
    internal WorldCellSnapshot(ulong Version,ulong FrameVersion,ulong Tick,ImmutableArray<WorldCellId>ActiveCells){
        this.Version=Version;
        this.FrameVersion=FrameVersion;
        this.Tick=Tick;
        this.ActiveCells=ActiveCells;
        this.ActiveSet=ActiveCells.ToImmutableHashSet();
    }
    public ulong Version{get;}
    public ulong FrameVersion{get;}
    public ulong Tick{get;}
    public ImmutableArray<WorldCellId> ActiveCells{get;}
    public bool Contains(WorldCellId Cell)=>this.ActiveSet.Contains(Cell);
}
public sealed class WorldCellTransition {
    internal WorldCellTransition(ulong BaseVersion,ulong MinimumFrameTick,ImmutableArray<WorldCellId>DesiredActive,ImmutableArray<WorldCellId>ToActivate,ImmutableArray<WorldCellId>ToDeactivate){
        this.BaseVersion=BaseVersion;
        this.MinimumFrameTick=MinimumFrameTick;
        this.DesiredActive=DesiredActive;
        this.ToActivate=ToActivate;
        this.ToDeactivate=ToDeactivate;
    }
    public ulong BaseVersion{get;}
    public ulong MinimumFrameTick{get;}
    public ImmutableArray<WorldCellId> DesiredActive{get;}
    public ImmutableArray<WorldCellId> ToActivate{get;}
    public ImmutableArray<WorldCellId> ToDeactivate{get;}
}
public sealed class WorldCellRuntime {
    private readonly object Gate=new();
    private readonly Dictionary<WorldCellId,WorldCellStatus>Statuses=[];
    private WorldCellSnapshot CurrentSnapshot;
    private WorldCellTransition?PendingTransition;
    public WorldCellRuntime(WorldPartitionManifest Manifest){
        this.Manifest=Manifest??throw new ArgumentNullException(nameof(Manifest));
        foreach(var Cell in Manifest.TopologicalOrder){ this.Statuses.Add(Cell,new WorldCellStatus(Cell,WorldCellState.Unloaded,WorldCellFault.None)); }
        this.CurrentSnapshot=new WorldCellSnapshot(0,0,0,[]);
    }
    public WorldPartitionManifest Manifest{get;}
    public WorldCellSnapshot Current=>Volatile.Read(ref this.CurrentSnapshot);
    public bool HasPendingTransition{get { lock(this.Gate){ return this.PendingTransition is not null; } }}
    public WorldCellStatus GetStatus(WorldCellId Cell){
        lock(this.Gate){ return this.Statuses.TryGetValue(Cell,out var Status)?Status:throw new KeyNotFoundException($"Unknown world cell: {Cell}"); }
    }
    internal bool TryBeginLoad(WorldCellId Cell,out WorldCellOperationError Error){
        lock(this.Gate){
            if(!this.TryGetMutableStatus(Cell,out var Status,out Error)){ return false; }
            if(Status.State!=WorldCellState.Unloaded){ Error=CreateError(WorldCellOperationErrorCode.InvalidState,Cell,$"World cell must be Unloaded, but is {Status.State}.");return false; }
            this.SetStatus(Cell,WorldCellState.Loading,WorldCellFault.None);
            Error=WorldCellOperationError.None;
            return true;
        }
    }
    internal bool TryBeginLoads(IReadOnlyList<WorldCellId>Cells,out WorldCellOperationError Error){
        ArgumentNullException.ThrowIfNull(Cells);
        lock(this.Gate){
            var Unique=new HashSet<WorldCellId>();
            foreach(var Cell in Cells){
                if(!Unique.Add(Cell)){ Error=CreateError(WorldCellOperationErrorCode.DuplicateDesiredCell,Cell,"Load batch contains a duplicate world cell.");return false; }
                if(!this.TryGetMutableStatus(Cell,out var Status,out Error)){ return false; }
                if(Status.State!=WorldCellState.Unloaded){ Error=CreateError(WorldCellOperationErrorCode.InvalidState,Cell,$"World cell must be Unloaded, but is {Status.State}.");return false; }
            }
            foreach(var Cell in Cells){ this.SetStatus(Cell,WorldCellState.Loading,WorldCellFault.None); }
            Error=WorldCellOperationError.None;
            return true;
        }
    }
    internal bool TryCompleteLoad(WorldCellId Cell,out WorldCellOperationError Error){
        lock(this.Gate){
            if(!this.TryGetMutableStatus(Cell,out var Status,out Error)){ return false; }
            if(Status.State!=WorldCellState.Loading){ Error=CreateError(WorldCellOperationErrorCode.InvalidState,Cell,$"World cell must be Loading, but is {Status.State}.");return false; }
            this.SetStatus(Cell,WorldCellState.Loaded,WorldCellFault.None);
            Error=WorldCellOperationError.None;
            return true;
        }
    }
    internal bool TryFailLoad(WorldCellId Cell,WorldCellFaultCode Code,string Message,out WorldCellOperationError Error){
        lock(this.Gate){
            if(!ValidateFault(Code,Cell,out Error)){ return false; }
            if(!this.TryGetMutableStatus(Cell,out var Status,out Error)){ return false; }
            if(Status.State!=WorldCellState.Loading){ Error=CreateError(WorldCellOperationErrorCode.InvalidState,Cell,$"World cell must be Loading, but is {Status.State}.");return false; }
            this.SetStatus(Cell,WorldCellState.Faulted,new WorldCellFault(Code,Message??string.Empty));
            Error=WorldCellOperationError.None;
            return true;
        }
    }
    internal bool TryFinishLoads(IReadOnlyList<WorldCellLoadCompletion>Completions,out WorldCellOperationError Error){
        ArgumentNullException.ThrowIfNull(Completions);
        lock(this.Gate){
            var Unique=new HashSet<WorldCellId>();
            foreach(var Completion in Completions){
                if(!Unique.Add(Completion.Cell)){ Error=CreateError(WorldCellOperationErrorCode.DuplicateDesiredCell,Completion.Cell,"Load completion batch contains a duplicate world cell.");return false; }
                if(!this.TryGetMutableStatus(Completion.Cell,out var Status,out Error)){ return false; }
                if(Status.State!=WorldCellState.Loading){ Error=CreateError(WorldCellOperationErrorCode.InvalidState,Completion.Cell,$"World cell must be Loading, but is {Status.State}.");return false; }
                if(Completion.Fault.Code!=WorldCellFaultCode.None&&!ValidateFault(Completion.Fault.Code,Completion.Cell,out Error)){ return false; }
            }
            foreach(var Completion in Completions){
                this.SetStatus(Completion.Cell,
                    Completion.Fault.Code==WorldCellFaultCode.None?WorldCellState.Loaded:WorldCellState.Faulted,
                    Completion.Fault);
            }
            Error=WorldCellOperationError.None;
            return true;
        }
    }
    public bool TryResetFault(WorldCellId Cell,out WorldCellOperationError Error){
        lock(this.Gate){
            if(!this.TryGetMutableStatus(Cell,out var Status,out Error)){ return false; }
            if(Status.State!=WorldCellState.Faulted){ Error=CreateError(WorldCellOperationErrorCode.InvalidState,Cell,$"World cell must be Faulted, but is {Status.State}.");return false; }
            this.SetStatus(Cell,WorldCellState.Unloaded,WorldCellFault.None);
            Error=WorldCellOperationError.None;
            return true;
        }
    }
    internal bool TryBeginTransition(IEnumerable<WorldCellId>DesiredActive,ReferenceFrameSnapshot Frames,out WorldCellTransition Transition,out WorldCellOperationError Error){
        ArgumentNullException.ThrowIfNull(DesiredActive);
        ArgumentNullException.ThrowIfNull(Frames);
        lock(this.Gate){
            Transition=null!;
            if(this.PendingTransition is not null){ Error=CreateError(WorldCellOperationErrorCode.TransitionInProgress,default,"A world cell transition is already in progress.");return false; }
            if(Frames.Tick<this.CurrentSnapshot.Tick){ Error=CreateError(WorldCellOperationErrorCode.StaleFrameSnapshot,default,"Reference frame snapshot is older than the published active set.");return false; }
            var Desired=new HashSet<WorldCellId>();
            foreach(var Cell in DesiredActive){
                if(!this.Statuses.ContainsKey(Cell)){ Error=CreateError(WorldCellOperationErrorCode.UnknownCell,Cell,"Desired active set contains an unknown world cell.");return false; }
                if(!Desired.Add(Cell)){ Error=CreateError(WorldCellOperationErrorCode.DuplicateDesiredCell,Cell,"Desired active set contains a world cell more than once.");return false; }
            }
            foreach(var Cell in Desired){
                var Definition=this.Manifest[Cell];
                if(!Frames.Contains(Definition.Frame)){ Error=CreateError(WorldCellOperationErrorCode.MissingReferenceFrame,Cell,"World cell reference frame is not present in the frame snapshot.");return false; }
                foreach(var Dependency in Definition.Dependencies){
                    if(!Desired.Contains(Dependency)){ Error=CreateError(WorldCellOperationErrorCode.DependencyNotDesired,Cell,$"Required dependency {Dependency} is absent from the desired active set.");return false; }
                }
            }
            var Current=this.CurrentSnapshot;
            var CurrentSet=Current.ActiveCells.ToHashSet();
            foreach(var Cell in Desired){
                var Status=this.Statuses[Cell];
                var RequiredState=CurrentSet.Contains(Cell)?WorldCellState.Active:WorldCellState.Loaded;
                if(Status.State!=RequiredState){ Error=CreateError(WorldCellOperationErrorCode.CellNotLoaded,Cell,$"World cell must be {RequiredState}, but is {Status.State}.");return false; }
            }
            foreach(var Cell in CurrentSet){
                if(this.Statuses[Cell].State!=WorldCellState.Active){ Error=CreateError(WorldCellOperationErrorCode.InvalidState,Cell,"Published active cell is not in Active state.");return false; }
            }
            var ToActivateSet=Desired.Where(Cell=>!CurrentSet.Contains(Cell)).ToHashSet();
            var ToDeactivateSet=CurrentSet.Where(Cell=>!Desired.Contains(Cell)).ToHashSet();
            if(ToActivateSet.Count==0&&ToDeactivateSet.Count==0){ Error=CreateError(WorldCellOperationErrorCode.NoTransition,default,"Desired active set is already published.");return false; }
            var DesiredOrdered=this.Manifest.OrderSubset(Desired,false);
            var ToActivate=this.Manifest.OrderSubset(ToActivateSet,false);
            var ToDeactivate=this.Manifest.OrderSubset(ToDeactivateSet,true);
            Transition=new WorldCellTransition(Current.Version,Frames.Tick,DesiredOrdered,ToActivate,ToDeactivate);
            foreach(var Cell in ToActivate){ this.SetStatus(Cell,WorldCellState.Activating,WorldCellFault.None); }
            foreach(var Cell in ToDeactivate){ this.SetStatus(Cell,WorldCellState.Deactivating,WorldCellFault.None); }
            this.PendingTransition=Transition;
            Error=WorldCellOperationError.None;
            return true;
        }
    }
    internal bool TryCommitTransition(WorldCellTransition Transition,ReferenceFrameSnapshot Frames,out WorldCellSnapshot Snapshot,out WorldCellOperationError Error){
        ArgumentNullException.ThrowIfNull(Transition);
        ArgumentNullException.ThrowIfNull(Frames);
        lock(this.Gate){
            if(!this.TryValidateTransition(Transition,out Error)){ Snapshot=this.CurrentSnapshot;return false; }
            if(Frames.Tick<Transition.MinimumFrameTick||Frames.Tick<this.CurrentSnapshot.Tick){ Snapshot=this.CurrentSnapshot;Error=CreateError(WorldCellOperationErrorCode.StaleFrameSnapshot,default,"Reference frame snapshot is older than the pending transition.");return false; }
            foreach(var Cell in Transition.DesiredActive){
                if(!Frames.Contains(this.Manifest[Cell].Frame)){
                    this.FailPending(new WorldCellFault(WorldCellFaultCode.MissingReferenceFrame,"World cell reference frame disappeared before activation commit."));
                    Snapshot=this.CurrentSnapshot;
                    Error=CreateError(WorldCellOperationErrorCode.MissingReferenceFrame,Cell,"World cell reference frame disappeared before activation commit.");
                    return false;
                }
            }
            var Candidate=new WorldCellSnapshot(this.CurrentSnapshot.Version+1,Frames.Version,Frames.Tick,Transition.DesiredActive);
            foreach(var Cell in Transition.ToActivate){ this.SetStatus(Cell,WorldCellState.Active,WorldCellFault.None); }
            foreach(var Cell in Transition.ToDeactivate){ this.SetStatus(Cell,WorldCellState.Unloading,WorldCellFault.None); }
            Volatile.Write(ref this.CurrentSnapshot,Candidate);
            this.PendingTransition=null;
            Snapshot=Candidate;
            Error=WorldCellOperationError.None;
            return true;
        }
    }
    internal bool TryFailTransition(WorldCellTransition Transition,WorldCellFaultCode Code,string Message,out WorldCellOperationError Error){
        ArgumentNullException.ThrowIfNull(Transition);
        lock(this.Gate){
            if(!ValidateFault(Code,default,out Error)||!this.TryValidateTransition(Transition,out Error)){ return false; }
            this.FailPending(new WorldCellFault(Code,Message??string.Empty));
            Error=WorldCellOperationError.None;
            return true;
        }
    }
    internal bool TryCompleteUnload(WorldCellId Cell,out WorldCellOperationError Error){
        lock(this.Gate){
            if(!this.TryGetMutableStatus(Cell,out var Status,out Error)){ return false; }
            if(Status.State!=WorldCellState.Unloading){ Error=CreateError(WorldCellOperationErrorCode.InvalidState,Cell,$"World cell must be Unloading, but is {Status.State}.");return false; }
            this.SetStatus(Cell,WorldCellState.Unloaded,WorldCellFault.None);
            Error=WorldCellOperationError.None;
            return true;
        }
    }
    internal bool TryBeginUnload(WorldCellId Cell,out WorldCellOperationError Error){
        lock(this.Gate){
            if(!this.TryGetMutableStatus(Cell,out var Status,out Error)){ return false; }
            if(Status.State!=WorldCellState.Loaded){ Error=CreateError(WorldCellOperationErrorCode.InvalidState,Cell,$"World cell must be Loaded, but is {Status.State}.");return false; }
            this.SetStatus(Cell,WorldCellState.Unloading,WorldCellFault.None);
            Error=WorldCellOperationError.None;
            return true;
        }
    }
    internal bool TryFailUnload(WorldCellId Cell,string Message,out WorldCellOperationError Error){
        lock(this.Gate){
            if(!this.TryGetMutableStatus(Cell,out var Status,out Error)){ return false; }
            if(Status.State!=WorldCellState.Unloading){ Error=CreateError(WorldCellOperationErrorCode.InvalidState,Cell,$"World cell must be Unloading, but is {Status.State}.");return false; }
            this.SetStatus(Cell,WorldCellState.Faulted,new WorldCellFault(WorldCellFaultCode.UnloadFailed,Message??string.Empty));
            Error=WorldCellOperationError.None;
            return true;
        }
    }
    private bool TryGetMutableStatus(WorldCellId Cell,out WorldCellStatus Status,out WorldCellOperationError Error){
        if(!this.Statuses.TryGetValue(Cell,out Status)){ Error=CreateError(WorldCellOperationErrorCode.UnknownCell,Cell,"World cell is not present in the manifest.");return false; }
        Error=WorldCellOperationError.None;
        return true;
    }
    private bool TryValidateTransition(WorldCellTransition Transition,out WorldCellOperationError Error){
        if(this.PendingTransition is null){ Error=CreateError(WorldCellOperationErrorCode.NoTransition,default,"No world cell transition is pending.");return false; }
        if(!ReferenceEquals(this.PendingTransition,Transition)||Transition.BaseVersion!=this.CurrentSnapshot.Version){ Error=CreateError(WorldCellOperationErrorCode.StaleTransition,default,"World cell transition token is stale or belongs to another runtime.");return false; }
        Error=WorldCellOperationError.None;
        return true;
    }
    private void FailPending(WorldCellFault Fault){
        var Transition=this.PendingTransition!;
        foreach(var Cell in Transition.ToActivate){ this.SetStatus(Cell,WorldCellState.Faulted,Fault); }
        foreach(var Cell in Transition.ToDeactivate){ this.SetStatus(Cell,WorldCellState.Active,WorldCellFault.None); }
        this.PendingTransition=null;
    }
    private void SetStatus(WorldCellId Cell,WorldCellState State,WorldCellFault Fault)=>this.Statuses[Cell]=new WorldCellStatus(Cell,State,Fault);
    private static bool ValidateFault(WorldCellFaultCode Code,WorldCellId Cell,out WorldCellOperationError Error){
        if(Code==WorldCellFaultCode.None){ Error=CreateError(WorldCellOperationErrorCode.InvalidFault,Cell,"A non-empty world cell fault code is required.");return false; }
        Error=WorldCellOperationError.None;
        return true;
    }
    private static WorldCellOperationError CreateError(WorldCellOperationErrorCode Code,WorldCellId Cell,string Message)=>new(Code,Cell,Message);
}
