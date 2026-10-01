using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Runtime.Physics;

[SystemPhase(SystemPhase.PhysicsSimulation)]
[SystemOrder(10_000)]
[ExecuteInEditor(Realtime=true)]
internal struct PhysicsSyncBegin:IWorldSystem {
    public void Update(EngineHost Host,ref State State){
        Host.ActivePhysicsDomain.BeginSynchronization();
    }
}

[SystemPhase(SystemPhase.PhysicsSimulation)]
[SystemOrder(10_001)]
[ExecuteInEditor(Realtime=true)]
internal struct PhysicsBodySync:ISystem {
    public void Spawn(ref PhysicsVelocity Velocity){}
    public void Spawn(ref PhysicsRotation Rotation){}
    public void Spawn(ref PhysicsStableId StableId){}

    public void Update(Entity Entity,ref Transform Transform,ref Collider Collider,EngineHost Host,ref State State){
        PhysicsDomainRuntime Domain=Host.ActivePhysicsDomain;
        if(Transform.Frame!=Host.PhysicsFrame){
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

    public void Update(Entity Entity,ref Transform Transform,ref Collider Collider,ref RigidBody RigidBody,EngineHost Host,ref State State){
        PhysicsDomainRuntime Domain=Host.ActivePhysicsDomain;
        if(Transform.Frame!=Host.PhysicsFrame){
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

[SystemPhase(SystemPhase.PhysicsSimulation)]
[SystemOrder(10_002)]
[ExecuteInEditor(Realtime=true)]
internal struct PhysicsStep:IWorldSystem {
    public void Update(EngineHost Host,ref State State){
        PhysicsDomainRuntime Domain=Host.ActivePhysicsDomain;
        Domain.EndSynchronization();
        PhysicsDebugHub.EnsureStarted();
        PhysicsSimulationControl.Register(Domain.DebugId);
        if(PhysicsSimulationControl.ConsumeReset(Domain.DebugId)){
            Domain.MutableWorld.Reset(State.FixedTick);
        }
        if(PhysicsSimulationControl.ShouldStep(Domain.DebugId)){
            if(!Domain.OwnsPlaybackTick(State.FixedTick)){ Domain.MutableWorld.Step(State.FixedTick); }
        }
        PhysicsDebugHub.PublishIfRequested(Domain.DebugId,Domain.MutableWorld,State.FixedTick);
    }
}

[SystemPhase(SystemPhase.PhysicsSimulation)]
[SystemOrder(10_003)]
[ExecuteInEditor(Realtime=true)]
internal struct PhysicsTransformWriteback:ISystem {
    public void Update(
        Entity Entity,ref Transform Transform,ref RigidBody RigidBody,ref PhysicsRotation Rotation,
        EngineHost Host,ref State State){
        if(Transform.Frame!=Host.PhysicsFrame){ return; }
        if(!Host.PhysicsWorld.TryGetBodyState(Entity,out PhysicsBodyState Body)
            ||Body.MotionType is not (PhysicsMotionType.Dynamic or PhysicsMotionType.Kinematic)){
            return;
        }
        Transform.Position=Body.Position;
        Rotation.Value=Body.Rotation;
    }
}
