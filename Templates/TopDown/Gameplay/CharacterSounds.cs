using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Windows;
namespace CgeTopDown;
/// <summary>
/// 足音と着地の音。キャラクターの位置から鳴り、遠いカメラでは小さく聞こえる。TopDownはジャンプしないので、ジャンプの音は無い。
/// <c>Assets.Sounds.*</c>は<c>Assets/Sounds/*.wav</c>から生成した型付きの参照で、ファイルが無ければコンパイルエラーになる。
/// </summary>
internal struct CharacterSounds:ISystem {
    public void Update(in Landed Event,Audio Audio)=>Audio.Play(Assets.Sounds.Land,Event.Source,Event.Impact);
    public void Update(in Footstep Event,Audio Audio)=>Audio.Play(Assets.Sounds.Footstep,Event.Source);
}
