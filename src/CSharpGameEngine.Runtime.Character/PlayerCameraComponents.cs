using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Input;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// The player's control rotation (Unreal's controller rotation), in degrees on the character root.
/// Yaw 0 looks along +F and grows toward +R; positive pitch looks up. Presentation data: the look
/// input changes it every frame, and movement reads its yaw to build a camera-relative direction.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,13)]
public struct ViewAngles() {
    [Angle(AngleUnit.Degrees)]
    public float YawDegrees=0f;
    [Angle(AngleUnit.Degrees)]
    public float PitchDegrees=0f;
    [Angle(AngleUnit.Degrees)]
    public float MinPitchDegrees=-89f;
    [Angle(AngleUnit.Degrees)]
    public float MaxPitchDegrees=89f;
    /// <summary>この視点へ、今フレームの視点入力(turns)を足す。Xは右へ、Yは上へ。上下の制限は<see cref="MinPitchDegrees"/>と<see cref="MaxPitchDegrees"/>(ADR-0077)。</summary>
    public void Turn(Look Look)=>PlayerControl.AddLook(ref this,Look.X*360f,Look.Y*360f);
    /// <summary>移動入力(Xは右、Yは前)を、この視点の向きを基準にしたワールドの水平な方向にする。長さは1に丸められる(ADR-0077)。</summary>
    public readonly LogicalVector3 Planar(Stick Move)=>PlayerControl.MoveDirection(this,Move.X,Move.Y);
}
/// <summary>
/// Places its entity (a camera that is a direct child of a character root) behind the parent's
/// <see cref="ViewAngles"/> rotation, as Unreal's SpringArmComponent with
/// bUsePawnControlRotation. A probe sphere swept from the pivot shortens the arm at obstacles.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Camera,1)]
public struct OrbitCamera() {
    /// <summary>Pivot in the parent's local frame (Unreal's spring arm relative location).</summary>
    public LogicalVector3 Pivot=LogicalVector3.Zero;
    public LogicalScalar Distance=LogicalScalar.FromSingle(3f);
    public LogicalScalar Radius=LogicalScalar.FromSingle(0.12f);
    public bool Collide=true;
    public PhysicsLayerMask CollidesWith=PhysicsLayerMask.All;
}
