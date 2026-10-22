using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>Control-rotation helpers for gameplay input code (Unreal's AddControllerYaw/PitchInput and DoMove).</summary>
public static class PlayerControl {
    /// <summary>
    /// Adds look input in degrees like Unreal's AddControllerYaw/PitchInput: positive yaw turns toward
    /// the screen right. <see cref="ViewAngles.YawDegrees"/> turns +F toward +R, which a
    /// right-handed view (Matrix4x4.CreateLookAt) shows on the left, so the yaw input is subtracted.
    /// Yaw wraps into [-180, 180) and pitch stays between the controller's two pitch bounds, in either order.
    /// </summary>
    public static void AddLook(ref ViewAngles Controller,float YawDegrees,float PitchDegrees){
        if(float.IsFinite(YawDegrees)){
            var Yaw=(Controller.YawDegrees-YawDegrees)%360f;
            Controller.YawDegrees=Yaw>=180f?Yaw-360f:Yaw<-180f?Yaw+360f:Yaw;
        }
        if(float.IsFinite(PitchDegrees)){
            // Math.Clamp throws for a minimum above the maximum; swapped bounds in a scene still name the range.
            var Low=MathF.Min(Controller.MinPitchDegrees,Controller.MaxPitchDegrees);
            var High=MathF.Max(Controller.MinPitchDegrees,Controller.MaxPitchDegrees);
            Controller.PitchDegrees=Math.Clamp(Controller.PitchDegrees+PitchDegrees,Low,High);
        }
    }
    /// <summary>
    /// The yaw of <see cref="ControlRotation"/> alone: a turn about +U by <see cref="ViewAngles.YawDegrees"/>. What UE's bUseControllerRotationYaw gives the pawn: the body turns with the
    /// view but does not look up or down (ADR-0078/M0107).
    /// </summary>
    public static LogicalQuaternion YawRotation(in ViewAngles Controller){
        var HalfYaw=Controller.YawDegrees*MathF.PI/360f;
        var Yaw=new LogicalQuaternion(LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Sin(HalfYaw)),LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Cos(HalfYaw)));
        return Yaw.TryNormalize(out var Result)?Result:LogicalQuaternion.Identity;
    }
    /// <summary>World rotation turning +F into the control direction: pitch about +R, then yaw about +U.</summary>
    public static LogicalQuaternion ControlRotation(in ViewAngles Controller){
        var HalfYaw=Controller.YawDegrees*MathF.PI/360f;
        var HalfPitch=Controller.PitchDegrees*MathF.PI/360f;
        // A positive rotation about +R turns +F toward -U, so looking up is a negative angle.
        var Yaw=new LogicalQuaternion(LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Sin(HalfYaw)),LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Cos(HalfYaw)));
        var Pitch=new LogicalQuaternion(LogicalScalar.FromSingle(-MathF.Sin(HalfPitch)),LogicalScalar.Zero,LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Cos(HalfPitch)));
        return LogicalQuaternion.TryMultiply(Yaw,Pitch,out var Product)&&Product.TryNormalize(out var Result)?Result:LogicalQuaternion.Identity;
    }
    /// <summary>
    /// Horizontal world direction for 2D move input (X right, Y forward) relative to the control yaw,
    /// with its length clamped to 1 for <see cref="MoveIntent.Direction"/>.
    /// </summary>
    public static LogicalVector3 MoveDirection(in ViewAngles Controller,float Right,float Forward){
        if(!float.IsFinite(Right)||!float.IsFinite(Forward)){
            return LogicalVector3.Zero;
        }
        var Length=MathF.Sqrt(Right*Right+Forward*Forward);
        if(Length>1f){
            Right/=Length;
            Forward/=Length;
        }
        var Yaw=Controller.YawDegrees*MathF.PI/180f;
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
public sealed class OrbitCameraSolver {
    public const int Bisections=16;
    private static readonly LogicalScalar Half=LogicalScalar.One/LogicalScalar.FromInt64(2);
    private static readonly LogicalScalar HalfSqrtTwo=LogicalScalar.FromSingle(0.70710678f);
    private readonly List<PhysicsShapeContact>Contacts=[];
    private readonly DeterministicPhysicsWorld World;
    public OrbitCameraSolver(DeterministicPhysicsWorld World){
        this.World=World??throw new ArgumentNullException(nameof(World));
    }
    /// <summary>Camera world position for an owner at <paramref name="OwnerPosition"/>; false when the numbers cannot be represented.</summary>
    public bool TrySolve(in OrbitCamera Arm,in LogicalVector3 OwnerPosition,in LogicalQuaternion OwnerRotation,in LogicalQuaternion ControlRotation,out LogicalVector3 CameraPosition,out LogicalScalar ArmLength){
        CameraPosition=default;
        ArmLength=default;
        if(!OwnerRotation.TryRotate(Arm.Pivot,out var Offset)){
            return false;
        }
        return this.TrySolveFrom(Arm,OwnerPosition+Offset,ControlRotation,out CameraPosition,out ArmLength);
    }
    /// <summary>Camera world position for an arm whose pivot is already at <paramref name="Pivot"/> (a lagged pivot, for one); false when the numbers cannot be represented.</summary>
    public bool TrySolveFrom(in OrbitCamera Arm,in LogicalVector3 Pivot,in LogicalQuaternion ControlRotation,out LogicalVector3 CameraPosition,out LogicalScalar ArmLength){
        CameraPosition=default;
        ArmLength=default;
        if(!ControlRotation.TryRotate(LogicalVector3.UnitF,out var Forward)){
            return false;
        }
        var Length=Arm.Distance>LogicalScalar.Zero?Arm.Distance:LogicalScalar.Zero;
        if(Arm.Collide&&Arm.Radius>LogicalScalar.Zero){
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
        if(Arm.SocketOffset!=LogicalVector3.Zero){
            if(!ControlRotation.TryRotate(Arm.SocketOffset,out var Socket)){
                return false;
            }
            CameraPosition+=Socket;
        }
        return true;
    }
    private bool Blocked(in OrbitCamera Arm,LogicalVector3 Pivot,LogicalVector3 Forward,LogicalQuaternion AxisRotation,LogicalScalar Length){
        var HalfLength=Length*Half;
        var Probe=new Collider { Shape=ColliderShape.Capsule,Radius=Arm.Radius,HalfHeight=HalfLength };
        this.Contacts.Clear();
        var Result=this.World.CollectContacts(Probe,Pivot-Forward*HalfLength,AxisRotation,Arm.CollidesWith,this.Contacts);
        // A query that cannot be evaluated counts as blocked, keeping the camera at the pivot side.
        return Result.Status!=PhysicsQueryStatus.Success||this.Contacts.Count>0;
    }
}
/// <summary>
/// The lag of UE's SpringArmComponent (CameraLagSpeed, CameraLagMaxTimeStep, CameraLagMaxDistance, CameraRotationLagSpeed; ADR-0078/M0107): the
/// pivot and the view rotation chase their targets with an exponential approach. Frames longer than the maximum time step are walked in steps of that
/// length, so the result does not depend on the frame rate. Presentation only: the camera never feeds back into the simulation.
/// </summary>
public static class CameraLag {
    /// <summary>UE's KINDA_SMALL_NUMBER (1e-4 cm) in metres, squared: closer than this, the target is taken.</summary>
    private static readonly LogicalScalar SmallDistanceSquared=LogicalScalar.FromSingle(1e-12f);
    /// <summary>UE's VInterpTo: moves <paramref name="Current"/> toward <paramref name="Target"/> by the fraction <c>clamp(dt * speed, 0, 1)</c> of the distance; speed 0 or a tiny distance returns the target.</summary>
    public static LogicalVector3 InterpolateTo(LogicalVector3 Current,LogicalVector3 Target,float DeltaSeconds,float Speed){
        if(!(Speed>0f)||!float.IsFinite(Speed)){
            return Target;
        }
        var Distance=Target-Current;
        if(Distance.LengthSquared<SmallDistanceSquared){
            return Target;
        }
        var Fraction=Math.Clamp((float.IsFinite(DeltaSeconds)?Math.Max(DeltaSeconds,0f):0f)*Speed,0f,1f);
        return Current+(Distance*LogicalScalar.FromSingle(Fraction));
    }
    /// <summary>
    /// The lagged pivot: <paramref name="Previous"/> chases <paramref name="Target"/> at <paramref name="Speed"/> per second. A frame longer than
    /// <paramref name="MaxTimeStep"/> is stepped, the target moving a step's share each time (UE's substepping); <paramref name="MaxDistance"/> above zero
    /// keeps the result within that distance of the target.
    /// </summary>
    public static LogicalVector3 Pivot(LogicalVector3 Previous,LogicalVector3 Target,float DeltaSeconds,float Speed,float MaxTimeStep,float MaxDistance){
        if(!(Speed>0f)||!float.IsFinite(Speed)||!(DeltaSeconds>0f)||!float.IsFinite(DeltaSeconds)){
            return Target;
        }
        LogicalVector3 Result;
        if(MaxTimeStep>0f&&DeltaSeconds>MaxTimeStep){
            var Chase=Previous;
            var Step=(Target-Previous)*LogicalScalar.FromSingle(MaxTimeStep/DeltaSeconds);
            var Remaining=DeltaSeconds;
            Result=Previous;
            while(Remaining>1e-4f){
                var Amount=MathF.Min(MaxTimeStep,Remaining);
                Chase+=Step*LogicalScalar.FromSingle(Amount/MaxTimeStep);
                Remaining-=Amount;
                Result=InterpolateTo(Result,Chase,Amount,Speed);
            }
        }
        else {
            Result=InterpolateTo(Previous,Target,DeltaSeconds,Speed);
        }
        if(MaxDistance>0f&&LogicalScalar.TryFromSingle(MaxDistance,out var Limit)){
            var Behind=Result-Target;
            if(Behind.LengthSquared>Limit*Limit&&Behind.TryNormalize(out var Direction)){
                Result=Target+(Direction*Limit);
            }
        }
        return Result;
    }
    /// <summary>UE's QInterpTo: a slerp from <paramref name="Current"/> to <paramref name="Target"/> by <c>clamp(dt * speed, 0, 1)</c>; speed 0 returns the target. A long frame is walked in steps of <paramref name="MaxTimeStep"/>.</summary>
    public static System.Numerics.Quaternion Rotation(System.Numerics.Quaternion Current,System.Numerics.Quaternion Target,float DeltaSeconds,float Speed,float MaxTimeStep){
        if(!(Speed>0f)||!float.IsFinite(Speed)||!(DeltaSeconds>0f)||!float.IsFinite(DeltaSeconds)){
            return Target;
        }
        var Remaining=DeltaSeconds;
        var Result=Current;
        do {
            var Amount=MaxTimeStep>0f?MathF.Min(MaxTimeStep,Remaining):Remaining;
            Result=System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Slerp(Result,Target,Math.Clamp(Amount*Speed,0f,1f)));
            Remaining-=Amount;
        } while(Remaining>1e-4f);
        return Result;
    }
}
