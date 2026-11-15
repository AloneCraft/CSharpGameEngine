using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Animation;
using CSharpGameEngine.Runtime.Character;
namespace CgeThirdPerson.Gameplay;
/// <summary>
/// ABP_UnarmedのイベントグラフのBlueprintUpdateAnimation相当。親キャラクターの動き(速度・加速の有無・向き・落下)から、地面の速さ・
/// 向きの角度(CalculateDirection)・ShouldMove・落下中・上昇速度をメッシュのLocomotionへ毎フレーム渡す。8方向のブレンドスペースや状態機械、
/// 足のIKなどアニメーションの評価はエンジン(LocomotionAnimation、FootIkUpdate)が行う。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsBefore(typeof(LocomotionAnimation))]
internal struct ThirdPersonAnimation:ISystem {
    public void Update(ref Locomotion Animator,in Parent<CharacterMotionState> Body,in Parent<MoveIntent> Intent,in Parent<CharacterMotion> Settings,in Parent<Transform> Placement){
        LocomotionObserver.Observe(ref Animator,Body.Value,Intent.Value,Settings.Value,Placement.Value);
    }
}
