using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Destruction;
/// <summary>
/// Registers the Entities the destruction plugin makes at run time (ADR-0078/M0113): a drawn fragment, the body of a loose piece and the body of an anchored
/// piece. Archetypes are a closed set, so they are declared here.
/// </summary>
internal struct DestructionShapes:ISystem {
    public void Spawn(ref Transform Transform,ref FragmentMesh Mesh,ref DestructibleFragment Fragment){}
    public void Spawn(ref Transform Transform,ref Collider Collider,ref RigidBody Body,ref PhysicsRotation Rotation,ref PhysicsVelocity Velocity,ref PhysicsStableId StableId,ref DestructiblePiece Piece){}
    public void Spawn(ref Transform Transform,ref Collider Collider,ref PhysicsRotation Rotation,ref PhysicsStableId StableId,ref DestructiblePiece Piece){}
}
/// <summary>Loads each <see cref="Destructible"/> the first fixed tick it exists (once; a problem is printed once and kept in <see cref="DestructibleState.Problem"/>).</summary>
[SystemPhase(SystemPhase.FixedSimulation)]
internal struct DestructibleLoad:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref Destructible Destructible,ref DestructibleState Runtime,Destruction Destruction,ref State State){
        if(Runtime.Attempted){ return; }
        Destruction.Load(Entity,in Transform,in Destructible,ref Runtime,ref State);
    }
}
/// <summary>
/// A character with a <see cref="DestructionImpact"/> that is about to move into a destructible (or a piece of one) this tick hits it: the point of contact
/// takes the character's mass times the approach speed as strain, and a loose piece is pushed by the same impulse. Runs after the physics step and before
/// the character moves, so the approach speed is the one the character arrives with. M0124: the strain is an impact (it adds to the internal strain) only in
/// the tick the character's touch of that body began.
/// </summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsAfter(typeof(PhysicsTransformWriteback))]
[RunsBefore(typeof(CharacterMotionUpdate))]
internal struct DestructionImpacts:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref CharacterMotion Motion,ref CharacterMotionState Moving,ref DestructionImpact Impact,Destruction Destruction,IPhysicsHost Host,ref State State){
        if(Transform.Frame!=Host.Physics.Frame||State.FixedTickRate<=0||Destruction.Count==0){ return; }
        var Velocity=Moving.Velocity;
        if(Velocity.LengthSquared<=Impact.MinimumSpeed*Impact.MinimumSpeed){ return; }
        var Step=LogicalScalar.One/LogicalScalar.FromInt64(State.FixedTickRate);
        var Radius=Motion.CapsuleRadius;
        var SegmentHalf=Motion.CapsuleHalfHeight-Radius;
        var Capsule=new Collider { Shape=ColliderShape.Capsule,Radius=Radius,HalfHeight=SegmentHalf<LogicalScalar.Zero?LogicalScalar.Zero:SegmentHalf };
        List<PhysicsShapeContact>Contacts=[];
        Host.Physics.Active.MutableWorld.CollectContacts(Capsule,Transform.Position+Velocity*Step,LogicalQuaternion.Identity,Motion.CollidesWith,Contacts);
        var Sequence=0u;
        foreach(var Contact in Contacts){
            if(!Destruction.Owns(Contact.Entity)){ continue; }
            var Approach=LogicalVector3.Dot(Velocity,Contact.Normal);
            if(Approach<Impact.MinimumSpeed){ continue; }
            var Impulse=Impact.Mass*Approach;
            Destruction.ApplyCharacterImpulse(Entity,Contact.Entity,Contact.Point,Impulse,State.FixedTick);
            if(Host.Physics.Active.World.TryGetBodyState(Contact.Entity,out var Body)&&Body.MotionType==PhysicsMotionType.Dynamic){
                Host.Physics.Active.Commands.Impulse(State.FixedTick+1,(1UL<<61)|(ulong)(uint)Entity.Index,Sequence++,Contact.Entity,Contact.Normal*Impulse);
            }
        }
    }
}
/// <summary>Hands every <see cref="StrainField"/> to the <see cref="Destruction"/> step of this tick (at its first tick, or every tick when it repeats).</summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsAfter(typeof(PhysicsStep))]
[RunsBefore(typeof(DestructionStep))]
internal struct StrainFieldApply:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref StrainField Field,ref StrainFieldState Runtime,Destruction Destruction,ref State State){
        if(Runtime.Fired&&!Field.Repeat){ return; }
        if(RuntimeWorldTransform.TryResolveWorld(Entity,State.World,out var World)!=FixedTransformResolveStatus.Success){ return; }
        Runtime.Fired=true;
        Destruction.ApplyStrain(new LogicalVector3(World.M41,World.M42,World.M43),Field.Radius,Field.Strain);
    }
}
/// <summary>The destruction step of the tick (<see cref="Destruction"/>): after the physics step and the impacts, before the bodies are written back.</summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsAfter(typeof(PhysicsStep))]
[RunsAfter(typeof(DestructionImpacts))]
internal struct DestructionStep:IWorldSystem {
    public void Update(Destruction Destruction,IPhysicsHost Host,ref State State){
        if(State.ExecutionMode!=EngineExecutionMode.Runtime||Destruction.Count==0){ return; }
        Destruction.Step(Host,ref State);
    }
}
/// <summary>Pushes the moving bodies around every <see cref="ForceField"/> (each body once over the field's life), for the next tick.</summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsAfter(typeof(DestructionStep))]
internal struct ForceFieldApply:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref ForceField Field,ref ForceFieldState Runtime,Destruction Destruction,IPhysicsHost Host,ref State State){
        if(State.ExecutionMode!=EngineExecutionMode.Runtime||Transform.Frame!=Host.Physics.Frame){ return; }
        if(RuntimeWorldTransform.TryResolveWorld(Entity,State.World,out var World)!=FixedTransformResolveStatus.Success){ return; }
        Destruction.Push(Entity,new LogicalVector3(World.M41,World.M42,World.M43),in Field,ref Runtime,Host,ref State);
    }
}
/// <summary>
/// Places every drawn fragment where its body is: the body's position plus the fragment's offset turned by the body's rotation (a body's
/// <c>PhysicsRotation</c> when it has one). Runs after the physics writes the bodies back, so the fragments are drawn with this tick's poses.
/// </summary>
[SystemPhase(SystemPhase.PostPhysics)]
internal struct DestructionFollow:ISystem {
    public void Update(ref Transform Transform,ref DestructibleFragment Fragment,ref State State){
        if(!State.World.TryGetComponent(Fragment.Body,out Transform Body)){ return; }
        var Rotation=Destruction.CurrentRotation(Fragment.Body,State.World,Body.Rotation);
        if(!Rotation.TryRotate(Fragment.Offset,out var Offset)){ return; }
        Transform.Position=Body.Position+Offset;
        Transform.Rotation=Rotation;
    }
}
/// <summary>
/// Removes the bodies and the drawn fragments of a destructible whose Entity is gone (its scene was unloaded or it was destroyed), the tick after: a body
/// made in the tick its destructible went could not be destroyed in that tick.
/// </summary>
[SystemPhase(SystemPhase.FixedSimulation)]
internal struct DestructionOrphans:ISystem {
    public void Update(Entity Entity,ref DestructiblePiece Piece,ref State State){
        if(!State.World.IsAlive(Piece.Root)){ State.World.DestroyDeferred(Entity,ref State); }
    }
    public void Update(Entity Entity,ref DestructibleFragment Fragment,ref State State){
        if(!State.World.IsAlive(Fragment.Root)){ State.World.DestroyDeferred(Entity,ref State); }
    }
}
