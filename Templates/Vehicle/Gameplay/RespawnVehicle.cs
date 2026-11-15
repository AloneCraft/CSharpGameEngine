using CgeVehicle.Scenes;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Windows;
namespace CgeVehicle;
/// <summary>PlayerStartのActorがどこにあるかを<see cref="PlayerStarts"/>へ伝える(UEのGetAllActorsOfClass(PlayerStart)に当たる)。</summary>
internal struct NotePlayerStart:ISystem {
    public void Update(Entity Entity,ref PlayerStart Start,ref Transform Transform,PlayerStarts Starts)=>Starts.Note(Entity,Transform.Position,Transform.Rotation);
}
/// <summary>
/// BP_VehicleAdvPlayerControllerのReceivePossess / On Pawn Destroyedに相当する。車が壊れた(KillZ、UEのこのレベルでは-50 mより下へ落ちた)ら、最初の
/// PlayerStartに同じ車(<see cref="SportsCar"/>)を生成する。生成した車のカメラが画面を引き継ぐ(UEのPossess)。
/// </summary>
[SystemPhase(SystemPhase.PostPhysics)]
internal struct RespawnVehicle:ISystem {
    public void Update(in PawnDestroyed Event,PlayerStarts Starts,Spawner Spawner,ref State State){
        if(Starts.TryPick(State.World,PlayerStartChoice.First,(ulong)State.FixedTick,out var Position,out var Rotation)){
            Spawner.Spawn<SportsCar>(Position,Rotation);
        }
    }
}
