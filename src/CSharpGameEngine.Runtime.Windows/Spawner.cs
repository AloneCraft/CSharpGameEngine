using CSharpGameEngine.Authoring;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using EcsWorld=CSharpGameEngine.Entities.World;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// Creates a Scene while the game runs (ADR-0078/M0105). A System takes a <c>Spawner</c> as an argument and writes
/// <c>Spawner.Spawn&lt;CursorMarker&gt;(Hit.Point)</c>: <c>CursorMarker</c> is a scene of the game (<c>Scenes/CursorMarker.json</c>), and the call is
/// checked when the game is built, because only a Scene whose root has a <see cref="Transform"/> can be named (<see cref="ISpawnableScene"/>).
/// <para>
/// The Scene appears at the end of the frame the call was made in (the requests are carried out by <see cref="SpawnerFlush"/> through the
/// structural command queue, which is the only safe way to create Entities from a System), and is drawn from the next frame. The root's
/// position is replaced by the one given; its rotation and scale stay as the Scene says unless a rotation is given too. The children keep
/// their positions relative to the root. Spawning the same Scene again gives an independent copy.
/// </para>
/// <para>
/// A Scene with physics bodies should not be alive twice at once: its <c>PhysicsStableId</c>s come from the Scene's definition, so two copies share them.
/// </para>
/// </summary>
[EngineService]
public sealed class Spawner {
    private delegate SceneInstance Place(ref EcsWorld World,LogicalVector3 Position,LogicalQuaternion Rotation,bool HasRotation);
    private readonly record struct Request(Place Place,LogicalVector3 Position,LogicalQuaternion Rotation,bool HasRotation);
    private static class Placer<TScene>where TScene:ISpawnableScene {
        internal static readonly Place Run=Instantiate;
        private static SceneInstance Instantiate(ref EcsWorld World,LogicalVector3 Position,LogicalQuaternion Rotation,bool HasRotation)=>
            HasRotation?TScene.InstantiateDeferredAt(ref World,Position,Rotation):TScene.InstantiateDeferredAt(ref World,Position);
    }
    private readonly List<Request>Pending=[];
    /// <summary>How many Scenes are waiting for the end of the frame.</summary>
    public int PendingCount=>this.Pending.Count;
    /// <summary>Asks for a copy of <typeparamref name="TScene"/> with its root at <paramref name="Position"/>.</summary>
    public void Spawn<TScene>(LogicalVector3 Position)where TScene:ISpawnableScene=>
        this.Pending.Add(new Request(Placer<TScene>.Run,Position,default,false));
    /// <summary>The same, with the root turned to <paramref name="Rotation"/>.</summary>
    public void Spawn<TScene>(LogicalVector3 Position,LogicalQuaternion Rotation)where TScene:ISpawnableScene=>
        this.Pending.Add(new Request(Placer<TScene>.Run,Position,Rotation,true));
    /// <summary>
    /// Queues every requested Scene into <paramref name="World"/>'s command buffer and forgets the requests. Returns how many.
    /// <paramref name="Created"/>, if given, receives the handle of each Scene (usable once the frame's structural changes are played back).
    /// </summary>
    public int Flush(ref EcsWorld World,ICollection<SceneInstance>?Created=null){
        var Count=this.Pending.Count;
        try {
            foreach(var Request in this.Pending){
                var Instance=Request.Place(ref World,Request.Position,Request.Rotation,Request.HasRotation);
                Created?.Add(Instance);
            }
        } finally {
            this.Pending.Clear();
        }
        return Count;
    }
}
/// <summary>ADR-0078/M0105: carries out the Scenes the game asked its <see cref="Spawner"/> for, once per frame after everything else.</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(Present))]
internal struct SpawnerFlush:ISystem {
    public void Update(ref LevelRuntime Runtime,Spawner Spawner,ref State State){
        Spawner.Flush(ref State.World);
    }
}
