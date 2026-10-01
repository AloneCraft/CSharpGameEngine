using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// The player's control rotation (Unreal's controller rotation), in degrees on the character root.
/// Yaw 0 looks along +F and grows toward +R; positive pitch looks up. Presentation data: the look
/// input changes it every frame, and movement reads its yaw to build a camera-relative direction.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,13)]
public struct PlayerController() {
    [Angle(AngleUnit.Degrees)]
    public float ControlYawDegrees=0f;
    [Angle(AngleUnit.Degrees)]
    public float ControlPitchDegrees=0f;
    [Angle(AngleUnit.Degrees)]
    public float MinPitchDegrees=-89f;
    [Angle(AngleUnit.Degrees)]
    public float MaxPitchDegrees=89f;
}
/// <summary>
/// Places its entity (a camera that is a direct child of a character root) behind the parent's
/// <see cref="PlayerController"/> rotation, as Unreal's SpringArmComponent with
/// bUsePawnControlRotation. A probe sphere swept from the pivot shortens the arm at obstacles.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Camera,1)]
public struct SpringArm() {
    /// <summary>Pivot in the parent's local frame (Unreal's spring arm relative location).</summary>
    public LogicalVector3 PivotOffset=LogicalVector3.Zero;
    public LogicalScalar TargetArmLength=LogicalScalar.FromSingle(3f);
    public LogicalScalar ProbeRadius=LogicalScalar.FromSingle(0.12f);
    public bool DoCollisionTest=true;
    public PhysicsLayerMask CollidesWith=PhysicsLayerMask.All;
}
