using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Runtime.Physics;

public readonly record struct PhysicsRay(LogicalVector3 Origin,LogicalVector3 Direction,LogicalScalar MaxDistance) {
    public PhysicsRay(LogicalVector3 Origin,LogicalVector3 Direction,int MaxDistance):this(Origin,Direction,LogicalScalar.FromInt64(MaxDistance)){}
}

public readonly record struct PhysicsRayHit(Entity Entity,ulong StableId,LogicalScalar Distance,LogicalVector3 Point,LogicalVector3 Normal);
public readonly record struct PhysicsOverlapHit(Entity Entity,ulong StableId);
/// <summary>
/// 問い合わせ形状がボディに貫入している箇所。<see cref="Normal"/> は問い合わせ形状からボディへ向く。<see cref="SurfaceNormal"/> は接触面自体の外向き法線（
/// 問い合わせ形状側を向く三角形の面法線で、辺や角では接触法線と異なる）。
/// </summary>
public readonly record struct PhysicsShapeContact(Entity Entity,ulong StableId,LogicalVector3 Point,LogicalVector3 Normal,LogicalScalar Penetration,LogicalVector3 SurfaceNormal);
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
    /// <summary>ワールド座標上の点に加える力（ADR-0078/M0109）。<see cref="Force"/> と同様に線速度を変化させ、重心まわりのトルクによって角速度も変化させる。</summary>
    ForceAtPoint,
    /// <summary>1ティック分のワールド座標系トルク（ADR-0078/M0109）。</summary>
    Torque,
    /// <summary>線速度と角速度を設定する（UE の SetPhysicsLinearVelocity / SetPhysicsAngularVelocity、ADR-0078/M0109）。</summary>
    SetVelocity,
    /// <summary>動的ボディをただちにスリープさせて停止する（車両に対する UE の SetSleeping(true)、ADR-0078/M0109）。以後のコマンドで起きる。</summary>
    Sleep,
}

/// <summary>
/// 1ティック分のコマンド。<see cref="PhysicsCommandType.ForceAtPoint"/> の場合、<see cref="Point"/> はワールド座標上の力点を表す。
/// <see cref="PhysicsCommandType.SetVelocity"/> の場合は角速度を表し、線速度は <see cref="Vector"/> に格納する。他のコマンドではゼロのま
/// まにする。
/// </summary>
public readonly record struct PhysicsCommand(
    ulong TargetTick,ulong SourceStableId,uint Sequence,PhysicsCommandType Type,
    Entity Entity,LogicalVector3 Vector,LogicalQuaternion Rotation,LogicalVector3 Point=default);

public sealed class PhysicsCommands {
    private readonly CSharpGameEngine.Runtime.Simulation.SimulationCommandBuffer<PhysicsCommand> Buffer=new();
    public IReadOnlyList<CSharpGameEngine.Runtime.Simulation.SimulationCommand<PhysicsCommand>> Pending=>this.Buffer.Pending;
    public void Force(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,LogicalVector3 Force)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.Force,Entity,Force,LogicalQuaternion.Identity);
    public void Impulse(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,LogicalVector3 Impulse)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.Impulse,Entity,Impulse,LogicalQuaternion.Identity);
    public void Teleport(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,LogicalVector3 Position,LogicalQuaternion Rotation)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.Teleport,Entity,Position,Rotation);
    public void SetKinematicTarget(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,LogicalVector3 Position,LogicalQuaternion Rotation)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.KinematicTarget,Entity,Position,Rotation);
    /// <summary>動的ボディのワールド座標上の <paramref name="Point"/> に、1ティック分の <paramref name="Force"/>（N）を加える（ADR-0078/M0109）。</summary>
    public void ForceAtPoint(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,LogicalVector3 Force,LogicalVector3 Point)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.ForceAtPoint,Entity,Force,LogicalQuaternion.Identity,Point);
    /// <summary>動的ボディにワールド座標系の <paramref name="Torque"/>（N·m）を1ティック分加える（ADR-0078/M0109）。</summary>
    public void Torque(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,LogicalVector3 Torque)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.Torque,Entity,Torque,LogicalQuaternion.Identity);
    /// <summary>動的ボディの線速度（m/s）と角速度（rad/s、ワールド軸）を設定し、スリープを解除する（ADR-0078/M0109）。</summary>
    public void SetVelocity(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity,LogicalVector3 Linear,LogicalVector3 Angular)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.SetVelocity,Entity,Linear,LogicalQuaternion.Identity,Angular);
    /// <summary>動的ボディを速度ゼロでスリープさせる（ADR-0078/M0109）。その後、力、力積、テレポート、速度設定のいずれかで起きる。</summary>
    public void Sleep(ulong Tick,ulong SourceStableId,uint Sequence,Entity Entity)=>this.Enqueue(Tick,SourceStableId,Sequence,PhysicsCommandType.Sleep,Entity,LogicalVector3.Zero,LogicalQuaternion.Identity);
    internal int Commit(ulong Tick,List<CSharpGameEngine.Runtime.Simulation.SimulationCommand<PhysicsCommand>> Results)=>this.Buffer.Commit(Tick,Results);
    private void Enqueue(ulong Tick,ulong SourceStableId,uint Sequence,PhysicsCommandType Type,Entity Entity,LogicalVector3 Vector,LogicalQuaternion Rotation,LogicalVector3 Point=default){
        PhysicsCommand Command=new(Tick,SourceStableId,Sequence,Type,Entity,Vector,Rotation,Point);
        this.Buffer.Enqueue(new CSharpGameEngine.Runtime.Simulation.SimulationCommand<PhysicsCommand>(Tick,SourceStableId,Sequence,Command));
    }
}
