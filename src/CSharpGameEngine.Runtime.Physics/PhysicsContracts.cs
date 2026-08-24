using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Runtime.Physics;

public readonly record struct PhysicsAabb(PhysicsVector3 Min,PhysicsVector3 Max) {
    public static PhysicsAabb FromCenterHalfExtents(in PhysicsVector3 Center,in PhysicsVector3 HalfExtents)=>new(Center-HalfExtents,Center+HalfExtents);
    public bool Intersects(in PhysicsAabb Other)=>this.Min.R<=Other.Max.R&&this.Max.R>=Other.Min.R
        &&this.Min.U<=Other.Max.U&&this.Max.U>=Other.Min.U&&this.Min.F<=Other.Max.F&&this.Max.F>=Other.Min.F;
    public bool Contains(in PhysicsAabb Other)=>this.Min.R<=Other.Min.R&&this.Min.U<=Other.Min.U&&this.Min.F<=Other.Min.F
        &&this.Max.R>=Other.Max.R&&this.Max.U>=Other.Max.U&&this.Max.F>=Other.Max.F;
}

public readonly record struct PhysicsRay(PhysicsVector3 Origin,PhysicsVector3 Direction,PhysicsScalar MaxDistance) {
    public PhysicsRay(PhysicsVector3 Origin,PhysicsVector3 Direction,int MaxDistance):this(Origin,Direction,PhysicsScalar.FromInt32(MaxDistance)){}
}

public readonly record struct PhysicsRayHit(Entity Entity,ulong StableId,PhysicsScalar Distance,PhysicsVector3 Point,PhysicsVector3 Normal);
public readonly record struct PhysicsOverlapHit(Entity Entity,ulong StableId);
public readonly record struct PhysicsSweepHit(Entity Entity,ulong StableId,PhysicsScalar Distance,PhysicsVector3 Point,PhysicsVector3 Normal);

public enum PhysicsQueryStatus:byte {
    Success,
    InvalidInput,
    NumericOverflow,
}

public readonly record struct PhysicsQueryResult(PhysicsQueryStatus Status,int AddedCount);

public enum PhysicsUpdateStatus:byte {
    Success,
    InvalidInput,
    InvalidMass,
    InvalidShape,
    NumericOverflow,
}

public enum PhysicsFaultType:ushort {
    NumericOverflow,
    InvalidMass,
    InvalidQuaternion,
    InvalidHierarchy,
    InvalidScale,
    CacheMismatch,
}

public enum PhysicsEventType:ushort {
    ContactBegin,
    ContactPersist,
    ContactEnd,
    TriggerBegin,
    TriggerEnd,
    Sleep,
    Wake,
    BondBroken,
    Fault,
}

public readonly record struct PhysicsEvent(ulong Tick,ulong PairId,PhysicsEventType Type,Entity First,Entity Second,PhysicsScalar Impulse,PhysicsFaultType Fault);

public readonly record struct PhysicsBodyState(
    Entity Entity,ulong StableId,PhysicsMotionType? MotionType,
    PhysicsVector3 Position,PhysicsQuaternion Rotation,PhysicsVelocity Velocity,
    bool IsSleeping,ushort SleepTicks);

public readonly record struct PhysicsBodyDescription(
    Entity Entity,ulong StableId,Collider Collider,RigidBody? RigidBody,
    PhysicsVector3 Position,PhysicsQuaternion Rotation,PhysicsVelocity Velocity){
    public static PhysicsBodyDescription CreateStaticBox(Entity Entity,PhysicsVector3 Position,PhysicsVector3 HalfExtents)=>new(
        Entity,checked((ulong)Entity.Id),new Collider { Shape=ColliderShape.Box,HalfExtents=HalfExtents },null,
        Position,PhysicsQuaternion.Identity,PhysicsVelocity.Zero);
    public static PhysicsBodyDescription CreateDynamicBox(Entity Entity,PhysicsVector3 Position,PhysicsVector3 HalfExtents,PhysicsScalar Mass)=>new(
        Entity,checked((ulong)Entity.Id),new Collider { Shape=ColliderShape.Box,HalfExtents=HalfExtents },
        new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=Mass },Position,PhysicsQuaternion.Identity,PhysicsVelocity.Zero);
}

public interface IReadOnlyPhysicsWorld {
    int BodyCount { get; }
    IReadOnlyList<PhysicsEvent> Events { get; }
    PhysicsQueryResult Raycast(in PhysicsRay Ray,PhysicsLayerMask Layers,List<PhysicsRayHit> Results);
    PhysicsQueryResult Sweep(in Collider Shape,in PhysicsVector3 Origin,in PhysicsVector3 Direction,PhysicsScalar Distance,PhysicsLayerMask Layers,List<PhysicsSweepHit> Results);
    PhysicsQueryResult Overlap(in PhysicsAabb Bounds,PhysicsLayerMask Layers,List<PhysicsOverlapHit> Results);
    bool TryGetBodyState(Entity Entity,out PhysicsBodyState State);
}

public enum PhysicsCommandType:byte {
    Force,
    Impulse,
    Teleport,
    KinematicTarget,
    RealtimeHandoff,
}

public readonly record struct PhysicsCommand(
    ulong TargetTick,ulong SourceStableId,uint Sequence,PhysicsCommandType Type,
    Entity Entity,PhysicsVector3 Vector,PhysicsQuaternion Rotation);

public sealed class PhysicsCommands {
    private readonly CSharpGameEngine.Runtime.Simulation.SimulationCommandBuffer<PhysicsCommand> Buffer=new();
    public IReadOnlyList<CSharpGameEngine.Runtime.Simulation.SimulationCommand<PhysicsCommand>> Pending=>this.Buffer.Pending;
    public void Force(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,PhysicsVector3 Force)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.Force,Entity,Force,PhysicsQuaternion.Identity);
    public void Impulse(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,PhysicsVector3 Impulse)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.Impulse,Entity,Impulse,PhysicsQuaternion.Identity);
    public void Teleport(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,PhysicsVector3 Position,PhysicsQuaternion Rotation)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.Teleport,Entity,Position,Rotation);
    public void SetKinematicTarget(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,PhysicsVector3 Position,PhysicsQuaternion Rotation)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.KinematicTarget,Entity,Position,Rotation);
    public void RealtimeHandoff(ulong Tick,ulong SourceStableId,uint Sequence)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.RealtimeHandoff,default,default,PhysicsQuaternion.Identity);
    internal int Commit(ulong Tick,List<CSharpGameEngine.Runtime.Simulation.SimulationCommand<PhysicsCommand>> Results)=>this.Buffer.Commit(Tick,Results);
    private void Enqueue(ulong Tick,ulong SourceStableId,uint Sequence,PhysicsCommandType Type,Entity Entity,PhysicsVector3 Vector,PhysicsQuaternion Rotation){
        PhysicsCommand Command=new(Tick,SourceStableId,Sequence,Type,Entity,Vector,Rotation);
        this.Buffer.Enqueue(new CSharpGameEngine.Runtime.Simulation.SimulationCommand<PhysicsCommand>(Tick,SourceStableId,Sequence,Command));
    }
}
