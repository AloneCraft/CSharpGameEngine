using CSharpGameEngine.Entities;
namespace ThirdPerson;
/// <summary>
/// キャラクターのイベントに効果音を結びつける。イベントはエンジンが発行し、1件ごとに該当のUpdateが呼ばれる(送り手の後・同じフレームのうち)。
/// 音はイベントの<c>Source</c>(Entity)の位置から鳴る。
/// <c>Assets.Sounds.*</c>は<c>Assets/Sounds/*.wav</c>から生成され、ファイルが無ければコンパイルエラーになる。
/// </summary>
internal struct CharacterSounds:ISystem {
    public void Update(in Jumped Event,Audio Audio)=>Audio.Play(Assets.Sounds.Jump,Event.Source);
    public void Update(in Landed Event,Audio Audio)=>Audio.Play(Assets.Sounds.Land,Event.Source,Volume:Event.Impact);
    public void Update(in Footstep Event,Audio Audio)=>Audio.Play(Assets.Sounds.Footstep,Event.Source);
}
