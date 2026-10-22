using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Animation;
using CSharpGameEngine.Runtime.Character;
namespace CgeThirdPerson.Gameplay;
/// <summary>
/// ABP_Unarmedのイベントグラフ相当。親キャラクターの移動状態から、水平速度・落下中・上昇速度を
/// メッシュのLocomotionへ毎フレーム渡す。アニメーションの評価はエンジン(LocomotionAnimation)が行う。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsBefore(typeof(LocomotionAnimation))]
internal struct ThirdPersonAnimation:ISystem {
    public void Update(ref Locomotion Animator,in Parent<CharacterMotionState> Body){
        var Movement=Body.Value;
        var Velocity=Movement.Velocity;
        if(Velocity.R.TryToSingle(out var Right)&&Velocity.U.TryToSingle(out var Up)&&Velocity.F.TryToSingle(out var Forward)){
            Animator.Speed=MathF.Sqrt((Right*Right)+(Forward*Forward));
            Animator.VerticalSpeed=Up;
        }
        Animator.Falling=Movement.Mode==CharacterMotionMode.Falling;
    }
}
