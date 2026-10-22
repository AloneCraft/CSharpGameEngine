using CSharpGameEngine.Entities;
namespace ThirdPerson;
/// <summary>体(親)の動きをメッシュ(子)のアニメーションへ渡す。クリップの選択とブレンドはエンジンのLocomotionが行う。</summary>
internal struct CharacterAnimation:ISystem {
    public void Update(ref Locomotion Animation,in Parent<CharacterMotionState> Body){
        Animation.Speed=Body.Value.Velocity.Planar.Length;
        Animation.VerticalSpeed=Body.Value.Velocity.U;
        Animation.Falling=Body.Value.Falling;
    }
}
