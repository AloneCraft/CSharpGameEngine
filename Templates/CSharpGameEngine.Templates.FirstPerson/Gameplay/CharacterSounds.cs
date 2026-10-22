using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Windows;
namespace CgeFirstPerson;
/// <summary>
/// 足音・ジャンプ・着地の音。一人称では音源が自分の耳元なので、位置を持たない<c>PlayLocal</c>で鳴らす。
/// <c>Assets.Sounds.*</c>は<c>Assets/Sounds/*.wav</c>から生成した型付きの参照で、ファイルが無ければコンパイルエラーになる。
/// </summary>
internal struct CharacterSounds:ISystem {
    public void Update(in Jumped Event,Audio Audio)=>Audio.PlayLocal(Assets.Sounds.Jump);
    public void Update(in Landed Event,Audio Audio)=>Audio.PlayLocal(Assets.Sounds.Land,Event.Impact);
    public void Update(in Footstep Event,Audio Audio)=>Audio.PlayLocal(Assets.Sounds.Footstep);
}
