using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Animation;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// ABP_Unarmedのイベントグラフ（BlueprintUpdateAnimation、ADR-0078/M0107）。アニメーショングラフへ、各フレームのキャラクター情報を渡す。
/// Ground speedは水平速度の大きさ、Directionは<c>CalculateDirection(Velocity, ActorRotation)</c>（ボディの前方から速度方向への符号付き角度。
/// 「Right」クリップ側を正とする）。キャラクターが移動方向を向く場合（<c>bOrientRotationToMovement</c>）、後ろ向きクリップを旋回中に再生しないよう±45°に制限する。
/// ShouldMoveは「接地速度が0.01 cm/sを超え、加速度がある」こと（押されている状態。<see cref="MoveIntent.Direction"/>がゼロでない）。
/// IsFallingは移動モードを表す。
/// </summary>
public static class LocomotionObserver {
    /// <summary>キャラクターが移動中と見なされる接地速度のしきい値（m/s）：0.01 cm/s。</summary>
    public const float MovingSpeed=0.0001f;
    /// <summary>キャラクターが移動方向を向いている間の、方向角度の上限。</summary>
    public const float OrientedDirectionLimit=45f;
    /// <summary>
    /// ボディの向き（<paramref name="FacingR"/>／<paramref name="FacingF"/>。水平な単位ベクトル）から水平速度までの符号付き角度（−180～180度）。
    /// マネキンクリップで「右」とされる側、すなわちボディの向きが+Fの場合は+R方向への速度を正とする。
    /// インポートされたスケルトンはUEのものと左右反転している（左手系エンジンの軸へ合わせてインポーターがglTFのZを反射し、
    /// 右手系ビューでは画面左側が+Rになる）。クリップは名前に対応するジョイント側へ移動するため、
    /// MF_Unarmed_*_Rightはキャラクターを+R方向へ移動させる（M0107、クリップの足の動きから測定）。速度がなければ0。
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
        // Rightクリップが移動する側は（R、F）座標で（FacingF, -FacingR）。向きが+Fなら+R方向。
        var Right=(FacingF*VelocityR)-(FacingR*VelocityF);
        return Right<0f?-Delta:Delta;
    }
    /// <summary>ボディの移動、意図、回転から、<paramref name="Animator"/>へこのフレームの入力を書き込む。</summary>
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
