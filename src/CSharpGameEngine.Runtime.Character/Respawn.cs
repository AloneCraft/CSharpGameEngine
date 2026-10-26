using CSharpGameEngine.Authoring;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using EcsWorld=CSharpGameEngine.Entities.World;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// 1つのPawnに適用するUEワールドのKillZ（WorldSettings.KillZ、ADR-0078/M0107）。位置が<see cref="Height"/>を下回ったPawnを破棄し
///（AActor::FellOutOfWorld）、<see cref="PawnDestroyed"/>を通知する。UEの既定値は-1048575 cm。4つのテンプレートのうち、変更しているのはVehicleの-5000 cmのみ。
/// PawnのSceneルートに付けること。カメラやメッシュを含むScene全体を破棄する。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,14)]
public struct KillZ() {
    /// <summary>Pawnを破棄する高さ（m）。UEの既定KillZは-1048575 cm。</summary>
    public LogicalScalar Height=LogicalScalar.FromSingle(-10485.75f);
}
/// <summary>Pawnが<see cref="KillZ"/>より下へ落ち、ゲームを離れたことを示す。UEのPawnのOnDestroyedデリゲートに相当し、コントローラーが受けて再出現させる。</summary>
public struct PawnDestroyed:IEvent {
    public Entity Source;
    /// <summary>ワールド外へ落ちたときのPawnの位置。</summary>
    public LogicalVector3 Position;
}
/// <summary>キャラクター移動後に、<see cref="KillZ"/>より下へ落ちたすべてのPawnを破棄し、<see cref="PawnDestroyed"/>を通知する。</summary>
[SystemPhase(SystemPhase.PostPhysics)]
internal struct KillZUpdate:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref KillZ Limit,in EventWriter<PawnDestroyed> Destroyed,ref State State){
        if(!(Transform.Position.U<Limit.Height)){
            return;
        }
        Destroyed.Send(new PawnDestroyed { Source=Entity,Position=Transform.Position });
        var Scene=State.World.GetOwningScene(Entity);
        if(!Scene.IsNone&&State.World.GetSceneRoot(Scene)==Entity){
            State.World.UnloadSceneDeferred(Scene,ref State);
        } else {
            State.World.DestroyDeferred(Entity,ref State);
        }
    }
}
/// <summary><see cref="PlayerStarts.TryPick"/> が選ぶPlayerStart。</summary>
public enum PlayerStartChoice:byte {
    /// <summary>ゲームが記録した最初の開始地点（UEのGetAllActorsOfClass(PlayerStart)[0]。Vehicleテンプレート）。</summary>
    First,
    /// <summary>シードに基づき開始地点を1つ選ぶ（UEのArray_Random。TopDownテンプレート）。同じシードなら同じ地点となり、リプレイでも同じ位置に再出現する。</summary>
    Random,
}
/// <summary>
/// レベルのPlayerStart一覧（UEのPlayerStartアクター、ADR-0078/M0107）。ゲーム側のPlayerStartマーカーシステムが、各地点をサービスへ知らせる
///（<see cref="Note"/>。毎フレーム呼び出してよい）。再出現するコントローラーは地点を取得し（<see cref="TryPick"/>）、
/// <c>Spawner</c>でPawnのSceneをそこに生成する。Entityが消滅した開始地点は、取得要求時に忘れる。
/// </summary>
[EngineService]
public sealed class PlayerStarts {
    private readonly record struct Entry(Entity Source,LogicalVector3 Position,LogicalQuaternion Rotation);
    private readonly List<Entry>Entries=[];
    /// <summary>記録した開始地点の数（Entityが消滅済みのものを含む）。</summary>
    public int Count=>this.Entries.Count;
    /// <summary><paramref name="Source"/>の位置を記録し、以前の値を置き換える。新しい開始地点は末尾に追加する。</summary>
    public void Note(Entity Source,LogicalVector3 Position,LogicalQuaternion Rotation){
        for(var Index=0;Index<this.Entries.Count;Index++){
            if(this.Entries[Index].Source==Source){
                this.Entries[Index]=new Entry(Source,Position,Rotation);
                return;
            }
        }
        this.Entries.Add(new Entry(Source,Position,Rotation));
    }
    /// <summary><paramref name="World"/>内でEntityが生存中の開始地点を1つ取得する。存在しない場合はfalse。</summary>
    public bool TryPick(EcsWorld World,PlayerStartChoice Choice,ulong Seed,out LogicalVector3 Position,out LogicalQuaternion Rotation){
        this.Entries.RemoveAll(Entry=>!World.IsAlive(Entry.Source));
        if(this.Entries.Count==0){
            Position=default;
            Rotation=LogicalQuaternion.Identity;
            return false;
        }
        var Picked=this.Entries[Choice==PlayerStartChoice.First?0:(int)(Mix(Seed)%(ulong)this.Entries.Count)];
        Position=Picked.Position;
        Rotation=Picked.Rotation;
        return true;
    }
    /// <summary>SplitMix64のファイナライザー。カウンター値をよく分散された数値に変換する。</summary>
    private static ulong Mix(ulong Value){
        Value+=0x9E3779B97F4A7C15UL;
        Value=(Value^(Value>>30))*0xBF58476D1CE4E5B9UL;
        Value=(Value^(Value>>27))*0x94D049BB133111EBUL;
        return Value^(Value>>31);
    }
}
