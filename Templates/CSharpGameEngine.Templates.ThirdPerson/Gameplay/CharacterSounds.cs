using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Windows;
namespace CgeThirdPerson;
/// <summary>
/// キャラクターのイベントに効果音を結ぶ。イベントはエンジンのCharacterMotionが送り、1件ごとに該当のUpdateが呼ばれる。
/// <c>Assets.Sounds.*</c>は<c>Assets/Sounds/*.wav</c>から生成した型付きの参照で、ファイルが無ければコンパイルエラーになる。
/// 音は送り手の後・同じフレームのうちに、キャラクターの位置から鳴る(リスナーはカメラのAudioListener)。
/// </summary>
internal struct CharacterSounds:ISystem {
    public void Update(in Jumped Event,Audio Audio)=>Audio.Play(Assets.Sounds.Jump,Event.Source);
    public void Update(in Landed Event,Audio Audio)=>Audio.Play(Assets.Sounds.Land,Event.Source,Event.Impact);
    public void Update(in Footstep Event,Audio Audio)=>Audio.Play(Assets.Sounds.Footstep,Event.Source);
}
