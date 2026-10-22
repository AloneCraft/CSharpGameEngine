using CSharpGameEngine.Authoring;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using EcsWorld=CSharpGameEngine.Entities.World;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// UE's world KillZ (WorldSettings.KillZ) for one pawn, ADR-0078/M0107: a pawn whose position falls below <see cref="Height"/> is destroyed
/// (AActor::FellOutOfWorld), which sends <see cref="PawnDestroyed"/>. UE's default is -1048575 cm, which none of the four templates changes except Vehicle (-5000 cm).
/// Put it on the root of the pawn's Scene; the whole Scene (camera and mesh included) goes with it.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,14)]
public struct KillZ() {
    /// <summary>The height (m) below which the pawn is destroyed. UE's default KillZ, -1048575 cm.</summary>
    public LogicalScalar Height=LogicalScalar.FromSingle(-10485.75f);
}
/// <summary>A pawn left the game: it fell below its <see cref="KillZ"/>. UE's pawn OnDestroyed delegate, which the controller listens to in order to respawn.</summary>
public struct PawnDestroyed:IEvent {
    public Entity Source;
    /// <summary>Where the pawn was when it fell out.</summary>
    public LogicalVector3 Position;
}
/// <summary>Destroys every pawn that fell below its <see cref="KillZ"/> and says so with <see cref="PawnDestroyed"/>, after the character moved.</summary>
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
/// <summary>Which PlayerStart <see cref="PlayerStarts.TryPick"/> takes.</summary>
public enum PlayerStartChoice:byte {
    /// <summary>The first one the game noted (UE's GetAllActorsOfClass(PlayerStart)[0]; the Vehicle template).</summary>
    First,
    /// <summary>One of them, chosen by the seed (UE's Array_Random; the TopDown template). The same seed gives the same start, so a replay respawns where it did.</summary>
    Random,
}
/// <summary>
/// The PlayerStarts of the level (UE's PlayerStart actors), ADR-0078/M0107. A game's own PlayerStart marker System tells the service where each one
/// is (<see cref="Note"/>, every frame is fine); a controller that respawns its pawn asks for one (<see cref="TryPick"/>) and spawns the pawn's Scene there
/// with the <c>Spawner</c>. A start whose Entity is gone is forgotten when asked.
/// </summary>
[EngineService]
public sealed class PlayerStarts {
    private readonly record struct Entry(Entity Source,LogicalVector3 Position,LogicalQuaternion Rotation);
    private readonly List<Entry>Entries=[];
    /// <summary>How many starts have been noted (including ones whose Entity may be gone).</summary>
    public int Count=>this.Entries.Count;
    /// <summary>Records where <paramref name="Source"/> is, replacing what was noted for it before. A new start goes to the end.</summary>
    public void Note(Entity Source,LogicalVector3 Position,LogicalQuaternion Rotation){
        for(var Index=0;Index<this.Entries.Count;Index++){
            if(this.Entries[Index].Source==Source){
                this.Entries[Index]=new Entry(Source,Position,Rotation);
                return;
            }
        }
        this.Entries.Add(new Entry(Source,Position,Rotation));
    }
    /// <summary>Takes a start whose Entity is alive in <paramref name="World"/>; false when there is none.</summary>
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
    /// <summary>SplitMix64's finalizer: a well-spread number from a counter.</summary>
    private static ulong Mix(ulong Value){
        Value+=0x9E3779B97F4A7C15UL;
        Value=(Value^(Value>>30))*0xBF58476D1CE4E5B9UL;
        Value=(Value^(Value>>27))*0x94D049BB133111EBUL;
        return Value^(Value>>31);
    }
}
