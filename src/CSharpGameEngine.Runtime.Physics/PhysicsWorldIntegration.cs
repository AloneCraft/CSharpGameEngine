using CSharpGameEngine.Runtime.Physics;

namespace CSharpGameEngine.Entities;

public partial struct World {
    private readonly DeterministicPhysicsWorld PhysicsWorldStorage=new();
    private readonly Guid PhysicsDebugWorldId=Guid.NewGuid();
    private readonly HashSet<Entity> PhysicsSeenEntities=[];
    private readonly List<Entity> PhysicsRemovalScratch=[];
    private readonly List<PhysicsBodyState> PhysicsStateScratch=[];
    private readonly Dictionary<Entity,PhysicsFaultType> PhysicsSyncFailures=[];

    public IReadOnlyPhysicsWorld GlobalPhysicsWorld=>this.PhysicsWorldStorage;
    public PhysicsCommands PhysicsCommands=>this.PhysicsWorldStorage.Commands;
    internal DeterministicPhysicsWorld MutablePhysicsWorld=>this.PhysicsWorldStorage;
    internal Guid PhysicsWorldDebugId=>this.PhysicsDebugWorldId;

    public bool TryGetPhysicsSyncFailure(Entity Entity,out PhysicsFaultType Failure)=>this.PhysicsSyncFailures.TryGetValue(Entity,out Failure);

    internal void BeginPhysicsSynchronization()=>this.PhysicsSeenEntities.Clear();

    internal ulong ResolvePhysicsStableId(Entity Entity){
        if(this.TryGetComponent(Entity,out PhysicsStableId StableId)&&StableId.Value!=0){ return StableId.Value; }
        // Runtime spawn fallback. Baking requires an authored stable ID and rejects this fallback.
        return checked((ulong)Entity.Id+1UL);
    }

    internal void ApplyPhysicsBody(in PhysicsBodyDescription Description){
        PhysicsUpdateStatus Status=this.PhysicsWorldStorage.Synchronize(Description);
        if(Status==PhysicsUpdateStatus.Success){
            this.PhysicsSeenEntities.Add(Description.Entity);
            this.PhysicsSyncFailures.Remove(Description.Entity);
            return;
        }
        this.PhysicsWorldStorage.Remove(Description.Entity);
        this.PhysicsSyncFailures[Description.Entity]=Status switch {
            PhysicsUpdateStatus.InvalidMass=>PhysicsFaultType.InvalidMass,
            PhysicsUpdateStatus.NumericOverflow=>PhysicsFaultType.NumericOverflow,
            _=>PhysicsFaultType.InvalidScale,
        };
    }

    internal void ReportPhysicsSyncFailure(Entity Entity,PhysicsFaultType Failure){
        this.PhysicsWorldStorage.Remove(Entity);
        this.PhysicsSyncFailures[Entity]=Failure;
    }

    internal void EndPhysicsSynchronization(){
        this.PhysicsRemovalScratch.Clear();
        this.PhysicsStateScratch.Clear();
        this.PhysicsWorldStorage.CollectBodyStates(this.PhysicsStateScratch);
        foreach(PhysicsBodyState Body in this.PhysicsStateScratch){
            if(!this.PhysicsSeenEntities.Contains(Body.Entity)){ this.PhysicsRemovalScratch.Add(Body.Entity); }
        }
        foreach(Entity Entity in this.PhysicsRemovalScratch){
            this.PhysicsWorldStorage.Remove(Entity);
            this.PhysicsSyncFailures.Remove(Entity);
        }
        this.PhysicsRemovalScratch.Clear();
        this.PhysicsStateScratch.Clear();
    }
}
