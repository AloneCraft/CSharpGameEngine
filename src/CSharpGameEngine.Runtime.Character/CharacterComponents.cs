using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
public enum CharacterMovementMode:byte {
    Walking,
    Falling,
}
/// <summary>
/// Kinematic character movement (ADR-0067), after Unreal's CharacterMovementComponent. Values are
/// metres, seconds and degrees with Y up; the defaults are Unreal's ThirdPerson Blueprint values.
/// The Entity's Transform position is the capsule centre. <see cref="CapsuleHalfHeight"/> includes
/// the hemispheres, as in Unreal.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,10)]
public struct CharacterMovement() {
    public LogicalScalar CapsuleRadius=LogicalScalar.FromSingle(0.35f);
    public LogicalScalar CapsuleHalfHeight=LogicalScalar.FromSingle(0.9f);
    public LogicalScalar MaxWalkSpeed=LogicalScalar.FromSingle(6f);
    public LogicalScalar MinAnalogWalkSpeed=LogicalScalar.FromSingle(0.2f);
    public LogicalScalar MaxAcceleration=LogicalScalar.FromSingle(15f);
    public LogicalScalar BrakingDecelerationWalking=LogicalScalar.FromSingle(20f);
    public LogicalScalar BrakingDecelerationFalling=LogicalScalar.FromSingle(15f);
    public LogicalScalar GroundFriction=LogicalScalar.FromSingle(8f);
    /// <summary>Friction while braking without input (Unreal's separate braking friction × factor).</summary>
    public LogicalScalar BrakingFriction=LogicalScalar.Zero;
    public LogicalScalar JumpZVelocity=LogicalScalar.FromSingle(5f);
    public LogicalScalar AirControl=LogicalScalar.FromSingle(0.35f);
    public LogicalScalar AirControlBoostMultiplier=LogicalScalar.FromSingle(2f);
    public LogicalScalar AirControlBoostVelocityThreshold=LogicalScalar.FromSingle(0.25f);
    public LogicalScalar GravityScale=LogicalScalar.One;
    public LogicalScalar MaxStepHeight=LogicalScalar.FromSingle(0.45f);
    [Angle(AngleUnit.Degrees)]
    public LogicalScalar WalkableFloorAngleDegrees=LogicalScalar.FromSingle(44.765083f);
    [Angle(AngleUnit.Degrees)]
    public LogicalScalar RotationRateDegrees=LogicalScalar.FromSingle(500f);
    public bool OrientRotationToMovement=true;
    public PhysicsLayerMask CollidesWith=PhysicsLayerMask.All;
}
/// <summary>Authoritative movement state; part of the snapshot so replays continue deterministically.</summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,11)]
public struct CharacterMovementState {
    public LogicalVector3 Velocity;
    public CharacterMovementMode Mode;
    /// <summary>Horizontal unit facing; zero until the first tick initializes it from the Transform.</summary>
    public LogicalVector3 Facing;
}
/// <summary>
/// What the controller asks for this fixed tick: a world-space horizontal direction whose length
/// (at most 1) scales acceleration and speed, and a jump request consumed by the next tick.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,12)]
public struct CharacterInput {
    public LogicalVector3 Move;
    public bool Jump;
}
