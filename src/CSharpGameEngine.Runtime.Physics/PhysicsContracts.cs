using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Runtime.Physics;

public readonly record struct PhysicsAabb(LogicalVector3 Min,LogicalVector3 Max) {
    public static PhysicsAabb FromCenterHalfExtents(in LogicalVector3 Center,in LogicalVector3 HalfExtents)=>new(Center-HalfExtents,Center+HalfExtents);
    public bool Intersects(in PhysicsAabb Other)=>this.Min.R<=Other.Max.R&&this.Max.R>=Other.Min.R
        &&this.Min.U<=Other.Max.U&&this.Max.U>=Other.Min.U&&this.Min.F<=Other.Max.F&&this.Max.F>=Other.Min.F;
    public bool Contains(in PhysicsAabb Other)=>this.Min.R<=Other.Min.R&&this.Min.U<=Other.Min.U&&this.Min.F<=Other.Min.F
        &&this.Max.R>=Other.Max.R&&this.Max.U>=Other.Max.U&&this.Max.F>=Other.Max.F;
}

public readonly record struct PhysicsRay(LogicalVector3 Origin,LogicalVector3 Direction,LogicalScalar MaxDistance) {
    public PhysicsRay(LogicalVector3 Origin,LogicalVector3 Direction,int MaxDistance):this(Origin,Direction,LogicalScalar.FromInt64(MaxDistance)){}
}

public readonly record struct PhysicsRayHit(Entity Entity,ulong StableId,LogicalScalar Distance,LogicalVector3 Point,LogicalVector3 Normal);
public readonly record struct PhysicsOverlapHit(Entity Entity,ulong StableId);
/// <summary>A penetration of a query shape into a body; <see cref="Normal"/> points from the query shape into the body.</summary>
public readonly record struct PhysicsShapeContact(Entity Entity,ulong StableId,LogicalVector3 Point,LogicalVector3 Normal,LogicalScalar Penetration);
public readonly record struct PhysicsSweepHit(Entity Entity,ulong StableId,LogicalScalar Distance,LogicalVector3 Point,LogicalVector3 Normal);
public readonly record struct PhysicsContactPoint(
    LogicalVector3 Position,LogicalVector3 Normal,LogicalScalar Penetration,
    LogicalScalar AccumulatedNormalImpulse);
public readonly record struct PhysicsContactManifold(
    ulong PairId,Entity First,Entity Second,IReadOnlyList<PhysicsContactPoint> Points);

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
    ReferenceFrameMismatch,
    CacheMismatch,
    InvalidShape,
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

public readonly record struct PhysicsEvent(ulong Tick,ulong PairId,PhysicsEventType Type,Entity First,Entity Second,LogicalScalar Impulse,PhysicsFaultType Fault);

public readonly record struct PhysicsBodyState(
    Entity Entity,ulong StableId,PhysicsMotionType? MotionType,
    LogicalVector3 Position,LogicalQuaternion Rotation,PhysicsVelocity Velocity,
    bool IsSleeping,ushort SleepTicks);

public readonly record struct PhysicsBodyDescription(
    Entity Entity,ulong StableId,Collider Collider,RigidBody? RigidBody,
    LogicalVector3 Position,LogicalQuaternion Rotation,PhysicsVelocity Velocity){
    public static PhysicsBodyDescription CreateStaticBox(Entity Entity,LogicalVector3 Position,LogicalVector3 HalfExtents)=>new(
        Entity,checked((ulong)Entity.Id),new Collider { Shape=ColliderShape.Box,HalfExtents=HalfExtents },null,
        Position,LogicalQuaternion.Identity,PhysicsVelocity.Zero);
    public static PhysicsBodyDescription CreateDynamicBox(Entity Entity,LogicalVector3 Position,LogicalVector3 HalfExtents,LogicalScalar Mass)=>new(
        Entity,checked((ulong)Entity.Id),new Collider { Shape=ColliderShape.Box,HalfExtents=HalfExtents },
        new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=Mass },Position,LogicalQuaternion.Identity,PhysicsVelocity.Zero);
}

public interface IReadOnlyPhysicsWorld {
    int BodyCount { get; }
    IReadOnlyList<PhysicsEvent> Events { get; }
    PhysicsQueryResult Raycast(in PhysicsRay Ray,PhysicsLayerMask Layers,List<PhysicsRayHit> Results);
    PhysicsQueryResult Sweep(in Collider Shape,in LogicalVector3 Origin,in LogicalVector3 Direction,LogicalScalar Distance,PhysicsLayerMask Layers,List<PhysicsSweepHit> Results);
    PhysicsQueryResult Overlap(in PhysicsAabb Bounds,PhysicsLayerMask Layers,List<PhysicsOverlapHit> Results);
    bool TryGetBodyState(Entity Entity,out PhysicsBodyState State);
}

public enum PhysicsCommandType:byte {
    Force,
    Impulse,
    Teleport,
    KinematicTarget,
}

public readonly record struct PhysicsCommand(
    ulong TargetTick,ulong SourceStableId,uint Sequence,PhysicsCommandType Type,
    Entity Entity,LogicalVector3 Vector,LogicalQuaternion Rotation);

public sealed class PhysicsCommands {
    private readonly CSharpGameEngine.Runtime.Simulation.SimulationCommandBuffer<PhysicsCommand> Buffer=new();
    public IReadOnlyList<CSharpGameEngine.Runtime.Simulation.SimulationCommand<PhysicsCommand>> Pending=>this.Buffer.Pending;
    public void Force(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,LogicalVector3 Force)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.Force,Entity,Force,LogicalQuaternion.Identity);
    public void Impulse(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,LogicalVector3 Impulse)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.Impulse,Entity,Impulse,LogicalQuaternion.Identity);
    public void Teleport(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,LogicalVector3 Position,LogicalQuaternion Rotation)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.Teleport,Entity,Position,Rotation);
    public void SetKinematicTarget(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,LogicalVector3 Position,LogicalQuaternion Rotation)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.KinematicTarget,Entity,Position,Rotation);
    internal int Commit(ulong Tick,List<CSharpGameEngine.Runtime.Simulation.SimulationCommand<PhysicsCommand>> Results)=>this.Buffer.Commit(Tick,Results);
    private void Enqueue(ulong Tick,ulong SourceStableId,uint Sequence,PhysicsCommandType Type,Entity Entity,LogicalVector3 Vector,LogicalQuaternion Rotation){
        PhysicsCommand Command=new(Tick,SourceStableId,Sequence,Type,Entity,Vector,Rotation);
        this.Buffer.Enqueue(new CSharpGameEngine.Runtime.Simulation.SimulationCommand<PhysicsCommand>(Tick,SourceStableId,Sequence,Command));
    }
}
