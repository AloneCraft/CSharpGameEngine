using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>Control-rotation helpers for gameplay input code (Unreal's AddControllerYaw/PitchInput and DoMove).</summary>
public static class PlayerControl {
    /// <summary>
    /// Adds look input in degrees like Unreal's AddControllerYaw/PitchInput: positive yaw turns toward
    /// the screen right. <see cref="PlayerController.ControlYawDegrees"/> turns +F toward +R, which a
    /// right-handed view (Matrix4x4.CreateLookAt) shows on the left, so the yaw input is subtracted.
    /// Yaw wraps into [-180, 180) and pitch is clamped to the controller's range.
    /// </summary>
    public static void AddLook(ref PlayerController Controller,float YawDegrees,float PitchDegrees){
        if(float.IsFinite(YawDegrees)){
            var Yaw=(Controller.ControlYawDegrees-YawDegrees)%360f;
            Controller.ControlYawDegrees=Yaw>=180f?Yaw-360f:Yaw<-180f?Yaw+360f:Yaw;
        }
        if(float.IsFinite(PitchDegrees)){
            Controller.ControlPitchDegrees=Math.Clamp(Controller.ControlPitchDegrees+PitchDegrees,Controller.MinPitchDegrees,Controller.MaxPitchDegrees);
        }
    }
    /// <summary>World rotation turning +F into the control direction: pitch about +R, then yaw about +U.</summary>
    public static LogicalQuaternion ControlRotation(in PlayerController Controller){
        var HalfYaw=Controller.ControlYawDegrees*MathF.PI/360f;
        var HalfPitch=Controller.ControlPitchDegrees*MathF.PI/360f;
        // A positive rotation about +R turns +F toward -U, so looking up is a negative angle.
        var Yaw=new LogicalQuaternion(LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Sin(HalfYaw)),LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Cos(HalfYaw)));
        var Pitch=new LogicalQuaternion(LogicalScalar.FromSingle(-MathF.Sin(HalfPitch)),LogicalScalar.Zero,LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Cos(HalfPitch)));
        return LogicalQuaternion.TryMultiply(Yaw,Pitch,out var Product)&&Product.TryNormalize(out var Result)?Result:LogicalQuaternion.Identity;
    }
    /// <summary>
    /// Horizontal world direction for 2D move input (X right, Y forward) relative to the control yaw,
    /// with its length clamped to 1 for <see cref="CharacterInput.Move"/>.
    /// </summary>
    public static LogicalVector3 MoveDirection(in PlayerController Controller,float Right,float Forward){
        if(!float.IsFinite(Right)||!float.IsFinite(Forward)){
            return LogicalVector3.Zero;
        }
        var Length=MathF.Sqrt(Right*Right+Forward*Forward);
        if(Length>1f){
            Right/=Length;
            Forward/=Length;
        }
        var Yaw=Controller.ControlYawDegrees*MathF.PI/180f;
        var Sin=MathF.Sin(Yaw);
        var Cos=MathF.Cos(Yaw);
        // Yaw turns +F toward +R: forward is (sin, 0, cos). The screen right of a right-handed view is
        // Forward x Up = (-cos, 0, sin).
        return new LogicalVector3(LogicalScalar.FromSingle(Forward*Sin-Right*Cos),LogicalScalar.Zero,LogicalScalar.FromSingle(Forward*Cos+Right*Sin));
    }
}
/// <summary>
/// Spring-arm placement against the deterministic physics world. The arm is the capsule swept by the
/// probe sphere from the pivot backward; its length is the longest one that touches nothing, found by
/// bisection. A growing capsule overlaps monotonically, so thin walls cannot be skipped.
/// </summary>
public sealed class SpringArmSolver {
    public const int Bisections=16;
    private static readonly LogicalScalar Half=LogicalScalar.One/LogicalScalar.FromInt64(2);
    private static readonly LogicalScalar HalfSqrtTwo=LogicalScalar.FromSingle(0.70710678f);
    private readonly List<PhysicsShapeContact>Contacts=[];
    private readonly DeterministicPhysicsWorld World;
    public SpringArmSolver(DeterministicPhysicsWorld World){
        this.World=World??throw new ArgumentNullException(nameof(World));
    }
    /// <summary>Camera world position for an owner at <paramref name="OwnerPosition"/>; false when the numbers cannot be represented.</summary>
    public bool TrySolve(in SpringArm Arm,in LogicalVector3 OwnerPosition,in LogicalQuaternion OwnerRotation,in LogicalQuaternion ControlRotation,out LogicalVector3 CameraPosition,out LogicalScalar ArmLength){
        CameraPosition=default;
        ArmLength=default;
        if(!OwnerRotation.TryRotate(Arm.PivotOffset,out var Offset)||!ControlRotation.TryRotate(LogicalVector3.UnitF,out var Forward)){
            return false;
        }
        var Pivot=OwnerPosition+Offset;
        var Length=Arm.TargetArmLength>LogicalScalar.Zero?Arm.TargetArmLength:LogicalScalar.Zero;
        if(Arm.DoCollisionTest&&Arm.ProbeRadius>LogicalScalar.Zero){
            // The capsule axis (local +U) along the arm: a quarter turn about +R after the control rotation.
            if(!LogicalQuaternion.TryMultiply(ControlRotation,new LogicalQuaternion(HalfSqrtTwo,LogicalScalar.Zero,LogicalScalar.Zero,HalfSqrtTwo),out var AxisRotation)){
                return false;
            }
            if(this.Blocked(Arm,Pivot,Forward,AxisRotation,Length)){
                var Clear=LogicalScalar.Zero;
                var Hit=Length;
                for(var Iteration=0;Iteration<Bisections;Iteration++){
                    var Middle=(Clear+Hit)*Half;
                    if(this.Blocked(Arm,Pivot,Forward,AxisRotation,Middle)){
                        Hit=Middle;
                    }
                    else {
                        Clear=Middle;
                    }
                }
                Length=Clear;
            }
        }
        ArmLength=Length;
        CameraPosition=Pivot-Forward*Length;
        return true;
    }
    private bool Blocked(in SpringArm Arm,LogicalVector3 Pivot,LogicalVector3 Forward,LogicalQuaternion AxisRotation,LogicalScalar Length){
        var HalfLength=Length*Half;
        var Probe=new Collider { Shape=ColliderShape.Capsule,Radius=Arm.ProbeRadius,HalfHeight=HalfLength };
        this.Contacts.Clear();
        var Result=this.World.CollectContacts(Probe,Pivot-Forward*HalfLength,AxisRotation,Arm.CollidesWith,this.Contacts);
        // A query that cannot be evaluated counts as blocked, keeping the camera at the pivot side.
        return Result.Status!=PhysicsQueryStatus.Success||this.Contacts.Count>0;
    }
}
