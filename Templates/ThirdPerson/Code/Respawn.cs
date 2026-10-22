using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
namespace ThirdPerson;
/// <summary>ユーザー定義のComponentの例。シーンJSONの<c>"Respawn"</c>から設定できる。</summary>
public struct Respawn {
    /// <summary>この高さより下に落ちたら戻す。</summary>
    public LogicalScalar KillHeight;
    internal LogicalVector3 LastGround;
}
/// <summary>落下したら、最後に地面に立っていた場所へ戻す。</summary>
internal struct ReturnToGround:ISystem {
    /// <summary>Entityが条件に初めて合ったときに1回だけ呼ばれる。初回フラグは要らない。</summary>
    public void Start(in Transform Body,ref Respawn Respawn){
        Respawn.LastGround=Body.Position;
    }
    public void Update(ref Transform Body,ref Respawn Respawn,in CharacterMotionState Motion){
        if(Motion.OnGround){
            Respawn.LastGround=Body.Position;
        } else if(Body.Position.U<Respawn.KillHeight){
            Body.Position=Respawn.LastGround;
        }
    }
}
