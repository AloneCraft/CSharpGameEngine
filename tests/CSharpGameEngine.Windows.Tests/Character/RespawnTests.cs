using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
using EcsWorld=CSharpGameEngine.Entities.World;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0107: a pawn that falls below its KillZ is destroyed (UE's AActor::FellOutOfWorld), and the controller that respawns it picks one of the level's
/// PlayerStarts (the first one, or one at random). The respawn itself is the template's code (<c>Spawner.Spawn</c> of the pawn's Scene at the start), checked by the
/// WARP run of the TopDown template.
/// </summary>
public sealed class RespawnTests {
    private sealed class Sim:IDisposable {
        internal readonly EngineHost Host=new();
        internal State State;
        private GeneratedSystemSet Systems;
        internal Sim(){
            this.State=this.Host.CreateState();
            this.State.Enabled=true;
        }
        internal ref EcsWorld World=>ref this.State.World;
        internal void Tick(int Count=1){
            for(var Index=0;Index<Count;Index++){ GeneratedGameLoop.RunBakeTick(ref this.State,ref this.Systems,this.Host); }
        }
        public void Dispose()=>this.Host.Dispose();
    }
    private static Transform At(float U)=>new() { Position=new LogicalVector3(0f,U,0f) };
    [Fact]
    public void APawnBelowItsKillZIsDestroyedAtTheNextTick(){
        using var Sim=new Sim();
        var Below=Sim.World.Create(At(-1f),new KillZ { Height=LogicalScalar.FromSingle(-0.5f) });
        var Above=Sim.World.Create(At(0f),new KillZ { Height=LogicalScalar.FromSingle(-0.5f) });
        var Level=Sim.World.Create(At(-1f),new KillZ { Height=LogicalScalar.FromSingle(-1f) });
        Sim.Tick();
        Assert.False(Sim.World.IsAlive(Below));
        Assert.True(Sim.World.IsAlive(Above));
        // UE compares with "<": exactly at the KillZ is still inside the world.
        Assert.True(Sim.World.IsAlive(Level));
    }
    [Fact]
    public void TheDefaultKillZIsUnrealsDefaultOfMinus1048575Centimetres(){
        Assert.Equal(-10485.75f,new KillZ().Height.TryToSingle(out var Height)?Height:float.NaN);
        using var Sim=new Sim();
        var Far=Sim.World.Create(At(-10000f),new KillZ());
        var Beyond=Sim.World.Create(At(-10486f),new KillZ());
        Sim.Tick();
        Assert.True(Sim.World.IsAlive(Far));
        Assert.False(Sim.World.IsAlive(Beyond));
    }
    [Fact]
    public void KillZOnTheRootOfASceneDestroysTheWholeScene(){
        using var Sim=new Sim();
        var Instance=Sim.World.BeginSceneInstance();
        var Root=Sim.World.AttachToScene(Instance,Sim.World.Create(At(-5f),new KillZ { Height=LogicalScalar.Zero }));
        var Child=Sim.World.AttachToScene(Instance,Sim.World.Create(new Transform(),new PrimitiveMesh()));
        Sim.World.EndSceneInstance(Instance,Root);
        Sim.Tick();
        Assert.False(Sim.World.IsAlive(Root));
        Assert.False(Sim.World.IsAlive(Child));
        Assert.False(Sim.World.IsSceneAlive(Instance));
    }
    private static (PlayerStarts Starts,Entity[]Entities,EcsWorld World)NoteStarts(Sim Sim,int Count){
        var Starts=new PlayerStarts();
        var Entities=new Entity[Count];
        for(var Index=0;Index<Count;Index++){
            Entities[Index]=Sim.World.Create(At(Index),new KillZ { Height=LogicalScalar.FromInt64(-100) });
            Starts.Note(Entities[Index],new LogicalVector3(Index,0f,0f),LogicalQuaternion.Identity);
        }
        return (Starts,Entities,Sim.World);
    }
    [Fact]
    public void PickFirstTakesTheStartNotedFirstAndPickRandomIsDeterministicForASeed(){
        using var Sim=new Sim();
        var (Starts,_,World)=NoteStarts(Sim,4);
        Assert.True(Starts.TryPick(World,PlayerStartChoice.First,123UL,out var First,out _));
        Assert.Equal(0f,First.R.TryToSingle(out var Value)?Value:float.NaN);
        Assert.True(Starts.TryPick(World,PlayerStartChoice.Random,7UL,out var One,out _));
        Assert.True(Starts.TryPick(World,PlayerStartChoice.Random,7UL,out var Again,out _));
        Assert.Equal(One,Again);
    }
    [Fact]
    public void PickRandomReachesEveryStartAndNoneIsFavoured(){
        using var Sim=new Sim();
        var (Starts,_,World)=NoteStarts(Sim,4);
        var Counts=new int[4];
        for(var Seed=0UL;Seed<800UL;Seed++){
            Assert.True(Starts.TryPick(World,PlayerStartChoice.Random,Seed,out var Position,out _));
            Counts[Position.R.TryToSingle(out var Index)?(int)Index:0]++;
        }
        Assert.All(Counts,Count=>Assert.InRange(Count,140,260));
    }
    [Fact]
    public void AStartWhoseEntityIsGoneIsForgottenAndNoStartMeansNoPick(){
        using var Sim=new Sim();
        var (Starts,Entities,World)=NoteStarts(Sim,2);
        Sim.World.DestroyDeferred(Entities[0],ref Sim.State);
        Sim.Tick();
        Assert.True(Starts.TryPick(World,PlayerStartChoice.First,0UL,out var Position,out _));
        Assert.Equal(1f,Position.R.TryToSingle(out var Value)?Value:float.NaN);
        Assert.Equal(1,Starts.Count);
        Sim.World.DestroyDeferred(Entities[1],ref Sim.State);
        Sim.Tick();
        Assert.False(Starts.TryPick(World,PlayerStartChoice.Random,0UL,out _,out var Rotation));
        Assert.Equal(LogicalQuaternion.Identity,Rotation);
        Assert.False(new PlayerStarts().TryPick(World,PlayerStartChoice.First,0UL,out _,out _));
    }
    [Fact]
    public void NotingAStartAgainUpdatesItInPlaceInsteadOfAddingAnother(){
        using var Sim=new Sim();
        var (Starts,Entities,World)=NoteStarts(Sim,1);
        Starts.Note(Entities[0],new LogicalVector3(9f,0f,0f),LogicalQuaternion.Identity);
        Assert.Equal(1,Starts.Count);
        Assert.True(Starts.TryPick(World,PlayerStartChoice.First,0UL,out var Position,out _));
        Assert.Equal(9f,Position.R.TryToSingle(out var Value)?Value:float.NaN);
    }
}
