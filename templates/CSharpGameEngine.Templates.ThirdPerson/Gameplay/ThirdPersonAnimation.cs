using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Animation;
using CSharpGameEngine.Runtime.Character;
namespace CgeThirdPerson.Gameplay;
/// <summary>
/// ABP_Unarmedのイベントグラフ相当。親キャラクターの移動状態から、水平速度・落下中・上昇速度を
/// メッシュのLocomotionAnimatorへ毎フレーム渡す。アニメーションの評価はエンジン(LocomotionAnimation)が行う。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[SystemOrder(10)]
internal struct ThirdPersonAnimation:ISystem {
    public bool Update(Entity Entity,ref LocomotionAnimator Animator,ref State State){
        if(!State.World.TryGetComponent(Entity,out Parent Parent)||!State.World.TryGetComponent(Parent.Value,out CharacterMovementState Movement)){
            return true;
        }
        var Velocity=Movement.Velocity;
        if(Velocity.R.TryToSingle(out var Right)&&Velocity.U.TryToSingle(out var Up)&&Velocity.F.TryToSingle(out var Forward)){
            Animator.Speed=MathF.Sqrt((Right*Right)+(Forward*Forward));
            Animator.VerticalSpeed=Up;
        }
        Animator.Falling=Movement.Mode==CharacterMovementMode.Falling;
        return true;
    }
}
