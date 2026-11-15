using CgeTopDown.Scenes;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Windows;
namespace CgeTopDown;
/// <summary>PlayerStartのActorがどこにあるかを<see cref="PlayerStarts"/>へ伝える(UEのGetAllActorsOfClass(PlayerStart)に当たる)。</summary>
internal struct NotePlayerStart:ISystem {
    public void Update(Entity Entity,ref PlayerStart Start,ref Transform Transform,PlayerStarts Starts)=>Starts.Note(Entity,Transform.Position,Transform.Rotation);
}
/// <summary>
/// BP_TopDownControllerのReceivePossess / OnDestroyed_Eventに相当する。Pawnが壊れた(KillZより下へ落ちた)ら、全PlayerStartから無作為に1つを選んで
/// 同じPawn(<see cref="TopDownCharacter"/>)をそこに生成する。生成したPawnのカメラが画面を引き継ぐ(UEのPossess)。無作為は決定的で、同じ
/// フレームなら同じ地点になる(リプレイのため)。
/// </summary>
[SystemPhase(SystemPhase.PostPhysics)]
internal struct RespawnPlayer:ISystem {
    public void Update(in PawnDestroyed Event,PlayerStarts Starts,Spawner Spawner,ref State State){
        if(Starts.TryPick(State.World,PlayerStartChoice.Random,(ulong)State.FixedTick,out var Position,out var Rotation)){
            Spawner.Spawn<TopDownCharacter>(Position,Rotation);
        }
    }
}
