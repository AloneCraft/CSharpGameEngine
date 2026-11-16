using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>キャラクターが地面からジャンプした(ADR-0077)。<see cref="Source"/>はキャラクターのEntity。</summary>
public struct Jumped:IEvent {
    public Entity Source;
}
/// <summary>キャラクターが地面に降りた。<see cref="Impact"/>は降りる直前の落下の強さ(0〜1。8 m/s 以上で 1)。</summary>
public struct Landed:IEvent {
    public Entity Source;
    public float Impact;
}
/// <summary>キャラクターが地面を歩いていて、<see cref="CharacterMotion.StepLength"/>だけ進むごとに1回(足音の合図)。</summary>
public struct Footstep:IEvent {
    public Entity Source;
}
