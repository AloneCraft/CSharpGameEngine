using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Animation;
using CSharpGameEngine.Runtime.Character;
namespace CgeTopDown;
/// <summary>
/// ABP_UnarmedのイベントグラフのBlueprintUpdateAnimation相当(TopDownもThirdPersonと同じABP_Unarmedを使う)。親キャラクターの動きから、
/// 地面の速さ・向きの角度・ShouldMove・落下中・上昇速度をメッシュのLocomotionへ毎フレーム渡す。評価はエンジンが行う。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsBefore(typeof(LocomotionAnimation))]
internal struct TopDownAnimation:ISystem {
    public void Update(ref Locomotion Animator,in Parent<CharacterMotionState> Body,in Parent<MoveIntent> Intent,in Parent<CharacterMotion> Settings,in Parent<Transform> Placement){
        LocomotionObserver.Observe(ref Animator,Body.Value,Intent.Value,Settings.Value,Placement.Value);
    }
}
