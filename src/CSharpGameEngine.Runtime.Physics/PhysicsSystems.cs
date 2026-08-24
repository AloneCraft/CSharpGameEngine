using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Runtime.Physics;

[SystemPhase(SystemPhase.PhysicsSimulation)]
[SystemOrder(10_000)]
[ExecuteInEditor(Realtime=true)]
internal struct PhysicsSyncBegin:IWorldSystem {
    public bool Update(ref State State){
        State.World.BeginPhysicsSynchronization();
        return true;
    }
}

[SystemPhase(SystemPhase.PhysicsSimulation)]
[SystemOrder(10_001)]
[ExecuteInEditor(Realtime=true)]
internal struct PhysicsBodySync:ISystem {
    public void Spawn(ref PhysicsVelocity Velocity){}
    public void Spawn(ref PhysicsRotation Rotation){}
    public void Spawn(ref PhysicsStableId StableId){}

    public bool Update(Entity Entity,ref Transform Transform,ref Collider Collider,ref State State){
        if(State.World.TryGetComponent(Entity,out RigidBody _)){ return true; }
        if(!TryPosition(Transform.Position,out PhysicsVector3 Position)){
            State.World.ReportPhysicsSyncFailure(Entity,PhysicsFaultType.NumericOverflow);
            return true;
        }
        PhysicsBodyDescription Description=new(
            Entity,State.World.ResolvePhysicsStableId(Entity),Collider,null,
            Position,ResolveRotation(Entity,ref State),PhysicsVelocity.Zero);
        State.World.ApplyPhysicsBody(Description);
        return true;
    }

    public bool Update(Entity Entity,ref Transform Transform,ref Collider Collider,ref RigidBody RigidBody,ref State State){
        if(RigidBody.MotionType==PhysicsMotionType.Dynamic&&State.World.TryGetComponent(Entity,out Parent Parent)&&State.World.IsAlive(Parent.Value)){
            State.World.ReportPhysicsSyncFailure(Entity,PhysicsFaultType.InvalidHierarchy);
            return true;
        }
        if(Transform.Scale!=Logical.Vector3.One){
            State.World.ReportPhysicsSyncFailure(Entity,PhysicsFaultType.InvalidScale);
            return true;
        }
        if(!TryPosition(Transform.Position,out PhysicsVector3 Position)){
            State.World.ReportPhysicsSyncFailure(Entity,PhysicsFaultType.NumericOverflow);
            return true;
        }
        State.World.TryGetComponent(Entity,out PhysicsVelocity Velocity);
        PhysicsBodyDescription Description=new(
            Entity,State.World.ResolvePhysicsStableId(Entity),Collider,RigidBody,
            Position,ResolveRotation(Entity,ref State),Velocity);
        State.World.ApplyPhysicsBody(Description);
        return true;
    }

    private static PhysicsQuaternion ResolveRotation(Entity Entity,ref State State)=>
        State.World.TryGetComponent(Entity,out PhysicsRotation Rotation)?Rotation.Value:PhysicsQuaternion.Identity;

    private static bool TryPosition(Logical.Vector3 Position,out PhysicsVector3 Result){
        Result=default;
        if(!PhysicsScalar.TryFromLogical(Position.R,out PhysicsScalar R)
            ||!PhysicsScalar.TryFromLogical(Position.U,out PhysicsScalar U)
            ||!PhysicsScalar.TryFromLogical(Position.F,out PhysicsScalar F)){
            return false;
        }
        Result=new PhysicsVector3(R,U,F);
        return true;
    }
}

[SystemPhase(SystemPhase.PhysicsSimulation)]
[SystemOrder(10_002)]
[ExecuteInEditor(Realtime=true)]
internal struct PhysicsStep:IWorldSystem {
    public bool Update(ref State State){
        State.World.EndPhysicsSynchronization();
        PhysicsDebugHub.EnsureStarted();
        PhysicsSimulationControl.Register(State.World.PhysicsWorldDebugId);
        if(PhysicsSimulationControl.ConsumeReset(State.World.PhysicsWorldDebugId)){
            State.World.MutablePhysicsWorld.Reset(State.FixedTick);
        }
        if(PhysicsSimulationControl.ShouldStep(State.World.PhysicsWorldDebugId)){
            State.World.MutablePhysicsWorld.Step(State.FixedTick);
        }
        PhysicsDebugHub.PublishIfRequested(State.World.PhysicsWorldDebugId,State.World.MutablePhysicsWorld,State.FixedTick);
        return true;
    }
}

[SystemPhase(SystemPhase.PhysicsSimulation)]
[SystemOrder(10_003)]
[ExecuteInEditor(Realtime=true)]
internal struct PhysicsTransformWriteback:ISystem {
    public bool Update(Entity Entity,ref Transform Transform,ref RigidBody RigidBody,ref State State){
        if(!State.World.GlobalPhysicsWorld.TryGetBodyState(Entity,out PhysicsBodyState Body)
            ||Body.MotionType is not (PhysicsMotionType.Dynamic or PhysicsMotionType.Kinematic)){
            return true;
        }
        if(!Body.Position.R.TryToLogical(out Logical.Scalar R)
            ||!Body.Position.U.TryToLogical(out Logical.Scalar U)
            ||!Body.Position.F.TryToLogical(out Logical.Scalar F)){
            State.World.ReportPhysicsSyncFailure(Entity,PhysicsFaultType.NumericOverflow);
            return true;
        }
        Transform.Position=new Logical.Vector3(R,U,F);
        return true;
    }
}
