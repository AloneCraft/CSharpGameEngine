using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Runtime.Physics;

/// <summary>Host-owned solver state for exactly one reference-frame domain.</summary>
internal sealed class PhysicsDomainRuntime:IDisposable {
    private readonly DeterministicPhysicsWorld WorldStorage=new();
    private readonly HashSet<Entity>SeenEntities=[];
    private readonly List<Entity>RemovalScratch=[];
    private readonly List<PhysicsBodyState>StateScratch=[];
    private readonly Dictionary<Entity,PhysicsFaultType>SyncFailures=[];
    private PhysicsCachePlayer?CachePlayer;
    private bool Disposed;

    internal PhysicsDomainRuntime(ReferenceFrameId Frame){
        if(!Frame.IsValid){ throw new ArgumentException("A valid physics frame is required.",nameof(Frame)); }
        this.Frame=Frame;
    }

    internal ReferenceFrameId Frame { get; }
    internal Guid DebugId { get; }=Guid.NewGuid();
    internal IReadOnlyPhysicsWorld World=>this.WorldStorage;
    internal PhysicsCommands Commands=>this.WorldStorage.Commands;
    internal DeterministicPhysicsWorld MutableWorld=>this.WorldStorage;
    /// <summary>Cooked StaticMeshCollider triangles; kept for the domain's lifetime so replays reuse them.</summary>
    internal StaticMeshCollisionCooker StaticMeshes { get; }=new();
    internal PhysicsCachePlaybackState CacheState=>this.CachePlayer?.State??PhysicsCachePlaybackState.Stopped;

    internal bool TryLoadCache(
        Stream Input,in CSharpGameEngine.Runtime.Simulation.SimulationCacheIdentity Identity,
        bool AllowRealtimeHandoff,out string Error){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if(!PhysicsCachePlayer.TryLoad(Input,Identity,AllowRealtimeHandoff,out PhysicsCachePlayer? Candidate,out Error)){ return false; }
        this.CachePlayer?.Dispose();
        this.CachePlayer=Candidate;
        return true;
    }

    internal bool OwnsPlaybackTick(ulong Tick){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if(this.CachePlayer is null||this.CachePlayer.State is PhysicsCachePlaybackState.Stopped or PhysicsCachePlaybackState.HandedOffToRealtime){
            return false;
        }
        if(this.CachePlayer.State==PhysicsCachePlaybackState.Playing){ this.CachePlayer.TryApply(Tick,this.WorldStorage); }
        return true;
    }

    internal bool TryHandoffCache(ulong NextTick)=>
        this.CachePlayer is not null&&this.CachePlayer.TryHandoffToRealtime(NextTick);

    internal bool TryGetSyncFailure(Entity Entity,out PhysicsFaultType Failure)=>
        this.SyncFailures.TryGetValue(Entity,out Failure);

    internal void WriteCacheFrame(PhysicsCacheStreamWriter Writer,ulong Tick){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        ArgumentNullException.ThrowIfNull(Writer);
        this.StateScratch.Clear();
        try {
            this.WorldStorage.CollectBodyStates(this.StateScratch);
            Writer.WriteFrame(new PhysicsCacheFrame(Tick,this.StateScratch,this.WorldStorage.Events,this.WorldStorage.BondBreaks));
        }
        finally { this.StateScratch.Clear(); }
    }

    internal void BeginSynchronization(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.SeenEntities.Clear();
    }

    internal static ulong ResolveStableId(Entity Entity,CSharpGameEngine.Entities.World EcsWorld){
        if(EcsWorld.TryGetComponent(Entity,out PhysicsStableId StableId)&&StableId.Value!=0){ return StableId.Value; }
        return checked((ulong)Entity.Id+1UL);
    }

    internal void ApplyBody(in PhysicsBodyDescription Description){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        PhysicsUpdateStatus Status=this.WorldStorage.Synchronize(Description);
        if(Status==PhysicsUpdateStatus.Success){
            this.SeenEntities.Add(Description.Entity);
            this.SyncFailures.Remove(Description.Entity);
            return;
        }
        this.WorldStorage.Remove(Description.Entity);
        this.SyncFailures[Description.Entity]=Status switch {
            PhysicsUpdateStatus.InvalidMass=>PhysicsFaultType.InvalidMass,
            PhysicsUpdateStatus.NumericOverflow=>PhysicsFaultType.NumericOverflow,
            PhysicsUpdateStatus.InvalidShape=>PhysicsFaultType.InvalidShape,
            _=>PhysicsFaultType.InvalidScale,
        };
    }

    internal void ReportSyncFailure(Entity Entity,PhysicsFaultType Failure){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.WorldStorage.Remove(Entity);
        this.SyncFailures[Entity]=Failure;
    }

    internal void EndSynchronization(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.RemovalScratch.Clear();
        this.StateScratch.Clear();
        this.WorldStorage.CollectBodyStates(this.StateScratch);
        foreach(PhysicsBodyState Body in this.StateScratch){
            if(!this.SeenEntities.Contains(Body.Entity)){ this.RemovalScratch.Add(Body.Entity); }
        }
        foreach(Entity Entity in this.RemovalScratch){
            this.WorldStorage.Remove(Entity);
            this.SyncFailures.Remove(Entity);
        }
        this.RemovalScratch.Clear();
        this.StateScratch.Clear();
    }

    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        PhysicsDebugHub.Remove(this.DebugId);
        this.WorldStorage.Reset(0);
        this.SeenEntities.Clear();
        this.RemovalScratch.Clear();
        this.StateScratch.Clear();
        this.SyncFailures.Clear();
        this.CachePlayer?.Dispose();
        this.CachePlayer=null;
    }
}
