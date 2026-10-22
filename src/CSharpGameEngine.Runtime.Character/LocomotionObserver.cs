using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Animation;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// ABP_Unarmed's event graph (BlueprintUpdateAnimation, ADR-0078/M0107): what the animation graph learns from the character each frame.
/// Ground speed is the length of the horizontal velocity; Direction is <c>CalculateDirection(Velocity, ActorRotation)</c> (the signed angle from the
/// body's forward to the velocity, positive toward the side the "Right" clips travel to), clamped to ±45° when the character turns toward its movement
/// (<c>bOrientRotationToMovement</c>) so backward clips do not play while turning around; ShouldMove is "above 0.01 cm/s and has an acceleration"
/// (the character is being pushed: <see cref="MoveIntent.Direction"/> is not zero); IsFalling is the movement mode.
/// </summary>
public static class LocomotionObserver {
    /// <summary>The ground speed (m/s) above which the character counts as moving: 0.01 cm/s.</summary>
    public const float MovingSpeed=0.0001f;
    /// <summary>The direction limit (degrees) while the character faces its movement.</summary>
    public const float OrientedDirectionLimit=45f;
    /// <summary>
    /// The signed angle in degrees (−180 to 180) from the body's facing (<paramref name="FacingR"/>, <paramref name="FacingF"/>; a horizontal unit vector) to the horizontal
    /// velocity, positive when the velocity is toward the body's "right" in the sense of the Mannequin clips: +R when facing +F. The imported skeleton is a mirror image
    /// of the one in UE (the importer reflects glTF's Z for the left-handed engine axes and the right-handed view shows +R on the screen's left), and the clips travel to
    /// the side of the joint they are named for, so MF_Unarmed_*_Right moves the character toward +R (measured from the clips' foot motion, M0107). Zero without velocity.
    /// </summary>
    public static float CalculateDirection(float VelocityR,float VelocityF,float FacingR,float FacingF){
        var Length=MathF.Sqrt((VelocityR*VelocityR)+(VelocityF*VelocityF));
        var FacingLength=MathF.Sqrt((FacingR*FacingR)+(FacingF*FacingF));
        if(!(Length>1e-6f)||!(FacingLength>1e-6f)){
            return 0f;
        }
        VelocityR/=Length;
        VelocityF/=Length;
        FacingR/=FacingLength;
        FacingF/=FacingLength;
        var Forward=(FacingR*VelocityR)+(FacingF*VelocityF);
        var Delta=MathF.Acos(Math.Clamp(Forward,-1f,1f))*180f/MathF.PI;
        // The side the Right clips travel to is (FacingF, -FacingR) in (R, F): +R when facing +F.
        var Right=(FacingF*VelocityR)-(FacingR*VelocityF);
        return Right<0f?-Delta:Delta;
    }
    /// <summary>Writes this frame's inputs of <paramref name="Animator"/> from the body's movement, intent and rotation.</summary>
    public static void Observe(ref Locomotion Animator,in CharacterMotionState Motion,in MoveIntent Intent,in CharacterMotion Settings,in Transform Body){
        var Velocity=Motion.Velocity;
        if(!Velocity.R.TryToSingle(out var R)||!Velocity.U.TryToSingle(out var U)||!Velocity.F.TryToSingle(out var F)){
            return;
        }
        var Speed=MathF.Sqrt((R*R)+(F*F));
        Animator.Speed=Speed;
        Animator.VerticalSpeed=U;
        Animator.Falling=Motion.Mode==CharacterMotionMode.Falling;
        var Facing=CharacterMotor.FacingOf(Body.Rotation);
        if(Facing.R.TryToSingle(out var FacingR)&&Facing.F.TryToSingle(out var FacingF)){
            var Direction=CalculateDirection(R,F,FacingR,FacingF);
            Animator.Direction=Settings.FaceMovement?Math.Clamp(Direction,-OrientedDirectionLimit,OrientedDirectionLimit):Direction;
        }
        var Pushed=!Intent.Direction.R.IsZero||!Intent.Direction.F.IsZero;
        Animator.ShouldMove=Speed>MovingSpeed&&Pushed;
    }
}
