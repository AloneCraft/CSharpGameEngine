using CSharpGameEngine.Entities;
namespace FirstPerson;
/// <summary>足音と着地音。一人称では音源が耳の位置なので、位置を渡さず「自分の音」として鳴らす。</summary>
internal struct FootstepSounds:ISystem {
    public void Update(in Footstep Event,Audio Audio)=>Audio.PlayLocal(Assets.Sounds.Footstep);
    public void Update(in Landed Event,Audio Audio)=>Audio.PlayLocal(Assets.Sounds.Land,Volume:Event.Impact);
}
