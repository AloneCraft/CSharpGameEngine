using System.Runtime.CompilerServices;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// Moves every character once per fixed tick, after the physics step so static geometry is
/// synchronized. Characters must be root nodes: their Transform position is the capsule centre in
/// the physics frame.
/// </summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[SystemOrder(10_010)]
internal struct CharacterMovementUpdate:ISystem {
    private static readonly ConditionalWeakTable<DeterministicPhysicsWorld,CharacterMotor>Motors=[];
    public void Update(ref Transform Transform,ref CharacterMovement Movement,ref CharacterMovementState MovementState,ref CharacterInput Input,EngineHost Host,ref State State){
        if(Transform.Frame!=Host.PhysicsFrame){
            return;
        }
        var World=Host.ActivePhysicsDomain.MutableWorld;
        var Motor=Motors.GetValue(World,static Key=>new CharacterMotor(Key));
        if(MovementState.Facing.LengthSquared.IsZero){
            MovementState.Facing=CharacterMotor.FacingOf(Transform.Rotation);
        }
        var Position=Transform.Position;
        try {
            Motor.Step(Movement,ref Position,ref MovementState,ref Input,LogicalScalar.One/LogicalScalar.FromInt64(State.FixedTickRate));
        }
        catch(OverflowException){
            // A numerically invalid configuration leaves the character where it was this tick.
            return;
        }
        Transform.Position=Position;
        if(Movement.OrientRotationToMovement){
            Transform.Rotation=CharacterMotor.FacingRotation(MovementState.Facing);
        }
    }
}
/// <summary>Registers PlayerController with the generated World lookup surface that SpringArmUpdate reads through the parent.</summary>
internal struct PlayerControllerQueryRegistration:ISystem { public void Spawn(ref PlayerController Controller){} }
/// <summary>
/// Places spring-arm cameras after the fixed ticks and before the renderer takes the camera pose
/// (CameraOriginSync, order 1). The arm's entity must be a direct child of a character root with a
/// <see cref="PlayerController"/>; its local Transform is rewritten so the world pose is the control
/// rotation at the arm's end.
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[SystemOrder(0)]
internal struct SpringArmUpdate:ISystem {
    private static readonly ConditionalWeakTable<DeterministicPhysicsWorld,SpringArmSolver>Solvers=[];
    public void Update(Entity Entity,ref Transform Transform,ref SpringArm Arm,EngineHost Host,ref State State){
        if(!State.World.TryGetComponent(Entity,out Parent Parent)
            ||!State.World.TryGetComponent(Parent.Value,out Transform Owner)
            ||!State.World.TryGetComponent(Parent.Value,out PlayerController Controller)
            ||Owner.Frame!=Host.PhysicsFrame){
            return;
        }
        var Solver=Solvers.GetValue(Host.ActivePhysicsDomain.MutableWorld,static Key=>new SpringArmSolver(Key));
        var Control=PlayerControl.ControlRotation(Controller);
        var Inverse=Owner.Rotation.Conjugate();
        try {
            if(!Solver.TrySolve(Arm,Owner.Position,Owner.Rotation,Control,out var CameraPosition,out _)
                ||!Inverse.TryRotate(CameraPosition-Owner.Position,out var Local)
                ||!LogicalQuaternion.TryMultiply(Inverse,Control,out var LocalRotation)
                ||Owner.Scale.R.IsZero||Owner.Scale.U.IsZero||Owner.Scale.F.IsZero){
                return;
            }
            Transform.Position=new LogicalVector3(Local.R/Owner.Scale.R,Local.U/Owner.Scale.U,Local.F/Owner.Scale.F);
            Transform.Rotation=LocalRotation;
        }
        catch(OverflowException){
            // An unrepresentable placement keeps last frame's camera.
        }
    }
}
