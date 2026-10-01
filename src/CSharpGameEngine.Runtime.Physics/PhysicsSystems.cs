using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Runtime.Physics;

[SystemPhase(SystemPhase.PhysicsSimulation)]
[ExecuteInEditor(Realtime=true)]
internal struct PhysicsSyncBegin:IWorldSystem {
    public void Update(IPhysicsHost Host,ref State State){
        Host.Physics.Active.BeginSynchronization();
    }
}

[SystemPhase(SystemPhase.PhysicsSimulation)]
[ExecuteInEditor(Realtime=true)]
[RunsAfter(typeof(PhysicsSyncBegin))]
internal struct PhysicsBodySync:ISystem {
    public void Spawn(ref PhysicsVelocity Velocity){}
    public void Spawn(ref PhysicsRotation Rotation){}
    public void Spawn(ref PhysicsStableId StableId){}

    public void Update(Entity Entity,ref Transform Transform,ref Collider Collider,IPhysicsHost Host,ref State State){
        PhysicsDomainRuntime Domain=Host.Physics.Active;
        if(Transform.Frame!=Host.Physics.Frame){
            Domain.ReportSyncFailure(Entity,PhysicsFaultType.ReferenceFrameMismatch);
            return;
        }
        if(State.World.TryGetComponent(Entity,out RigidBody _)){ return; }
        var Position=Transform.Position;
        PhysicsBodyDescription Description=new(
            Entity,PhysicsDomainRuntime.ResolveStableId(Entity,State.World),Collider,null,
            Position,ResolveRotation(Entity,ref State),PhysicsVelocity.Zero);
        Domain.ApplyBody(Description);
    }

    public void Update(Entity Entity,ref Transform Transform,ref Collider Collider,ref RigidBody RigidBody,IPhysicsHost Host,ref State State){
        PhysicsDomainRuntime Domain=Host.Physics.Active;
        if(Transform.Frame!=Host.Physics.Frame){
            Domain.ReportSyncFailure(Entity,PhysicsFaultType.ReferenceFrameMismatch);
            return;
        }
        if(RigidBody.MotionType==PhysicsMotionType.Dynamic&&State.World.TryGetComponent(Entity,out Parent Parent)&&State.World.IsAlive(Parent.Value)){
            Domain.ReportSyncFailure(Entity,PhysicsFaultType.InvalidHierarchy);
            return;
        }
        if(Transform.Scale!=LogicalVector3.One){
            Domain.ReportSyncFailure(Entity,PhysicsFaultType.InvalidScale);
            return;
        }
        var Position=Transform.Position;
        State.World.TryGetComponent(Entity,out PhysicsVelocity Velocity);
        PhysicsBodyDescription Description=new(
            Entity,PhysicsDomainRuntime.ResolveStableId(Entity,State.World),Collider,RigidBody,
            Position,ResolveRotation(Entity,ref State),Velocity);
        Domain.ApplyBody(Description);
    }

    private static LogicalQuaternion ResolveRotation(Entity Entity,ref State State)=>
        State.World.TryGetComponent(Entity,out PhysicsRotation Rotation)?Rotation.Value:LogicalQuaternion.Identity;

}

/// <summary>
/// Steps the physics world in the running game. M0097: the editor world only registers the scene's bodies, so the
/// physics debug overlay matches the objects the Scene view draws; it never advances time.
/// </summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[ExecuteInEditor(Realtime=true)]
[RunsAfter(typeof(StaticMeshColliderSync))]
internal struct PhysicsStep:IWorldSystem {
    public void Update(IPhysicsHost Host,ref State State){
        PhysicsDomainRuntime Domain=Host.Physics.Active;
        Domain.EndSynchronization();
        PhysicsDebugHub.EnsureStarted();
        if(State.ExecutionMode==EngineExecutionMode.Runtime&&!Domain.OwnsPlaybackTick(State.FixedTick)){
            Domain.MutableWorld.Step(State.FixedTick);
        }
        PhysicsDebugHub.PublishIfRequested(Domain.DebugId,Domain.MutableWorld,State.FixedTick);
    }
}

[SystemPhase(SystemPhase.PhysicsSimulation)]
[ExecuteInEditor(Realtime=true)]
[RunsAfter(typeof(PhysicsStep))]
internal struct PhysicsTransformWriteback:ISystem {
    public void Update(
        Entity Entity,ref Transform Transform,ref RigidBody RigidBody,ref PhysicsRotation Rotation,
        IPhysicsHost Host,ref State State){
        if(Transform.Frame!=Host.Physics.Frame){ return; }
        if(!Host.Physics.Active.World.TryGetBodyState(Entity,out PhysicsBodyState Body)
            ||Body.MotionType is not (PhysicsMotionType.Dynamic or PhysicsMotionType.Kinematic)){
            return;
        }
        Transform.Position=Body.Position;
        Rotation.Value=Body.Rotation;
    }
}
