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
    public bool Update(ref Transform Transform,ref CharacterMovement Movement,ref CharacterMovementState MovementState,ref CharacterInput Input,EngineHost Host,ref State State){
        if(Transform.Frame!=Host.PhysicsFrame){
            return true;
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
            return true;
        }
        Transform.Position=Position;
        if(Movement.OrientRotationToMovement){
            Transform.Rotation=CharacterMotor.FacingRotation(MovementState.Facing);
        }
        return true;
    }
}
