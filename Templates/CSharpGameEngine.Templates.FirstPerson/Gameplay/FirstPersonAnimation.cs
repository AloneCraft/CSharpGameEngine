using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Animation;
using CSharpGameEngine.Runtime.Character;
namespace CgeFirstPerson;
/// <summary>
/// ABP_UnarmedのイベントグラフのBlueprintUpdateAnimation相当(FirstPersonの体のメッシュも同じABP_Unarmedを使う)。親キャラクターの動きから、地面の速さ・
/// 向きの角度・ShouldMove・落下中・上昇速度をメッシュのLocomotionへ毎フレーム渡す。FirstPersonは移動の向きへ体を回さない(bOrientRotationToMovement なし)ので、
/// 向きの角度は±45度に制限されず、横歩きや後ろ歩きのクリップが再生される。腕のメッシュ(FirstPersonMesh)はこの体のポーズを写す(エンジンのFirstPersonPoseUpdate)。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsBefore(typeof(LocomotionAnimation))]
internal struct FirstPersonAnimation:ISystem {
    public void Update(ref Locomotion Animator,in Parent<CharacterMotionState> Body,in Parent<MoveIntent> Intent,in Parent<CharacterMotion> Settings,in Parent<Transform> Placement){
        LocomotionObserver.Observe(ref Animator,Body.Value,Intent.Value,Settings.Value,Placement.Value);
    }
}
