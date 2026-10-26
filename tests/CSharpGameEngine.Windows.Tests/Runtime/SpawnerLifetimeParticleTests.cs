using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;
using UserEcsProject.Scenes;
using EcsWorld=CSharpGameEngine.Entities.World;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0105：ゲーム実行中に Scene を作成し、Entity は自身を終了でき、パーティクルのバーストが発生して飛び散り、消えていく。ウィンドウなしでショーケースプロジェクトの生成済み固定 tick を実行し、シミュレーションの tick ごとの動作を検証する。
/// </summary>
public sealed class SpawnerLifetimeParticleTests {
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
        internal List<(Transform Transform,Particle Particle)>Particles(){
            var Found=new List<(Transform,Particle)>();
            for(var Slot=0;Slot<this.World.GetStatistics().EntitySlotCount;Slot++){
                var Entity=this.World.EntityArchetypeIndices.GetRefUnchecked(Slot).Entity;
                if(this.World.IsAlive(Entity)&&this.World.TryGetComponent(Entity,out Particle Particle)&&this.World.TryGetComponent(Entity,out Transform Transform)){
                    Found.Add((Transform,Particle));
                }
            }
            return Found;
        }
        public void Dispose()=>this.Host.Dispose();
    }
    private static float Single(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private static ParticleBurst Burst(uint Seed=1u,int Count=12)=>new(){
        Count=Count,
        Lifetime=LogicalScalar.FromSingle(0.2f),
        Speed=LogicalScalar.FromInt64(3),
        SpeedJitter=LogicalScalar.FromSingle(0.5f),
        Spread=LogicalScalar.FromSingle(0.5f),
        Gravity=LogicalScalar.FromInt64(2),
        StartScale=LogicalScalar.One,
        EndScale=LogicalScalar.Zero,
        Seed=Seed,
    };
    [Fact]
    public void AnEntityWithALifetimeIsDestroyedWhenItRunsOutAndNotBefore(){
        using var Sim=new Sim();
        var Entity=Sim.World.Create(new Lifetime { Seconds=LogicalScalar.FromSingle(0.1f) });
        Sim.Tick(5);
        Assert.True(Sim.World.IsAlive(Entity),"it ended early");
        Sim.Tick(2);
        Assert.False(Sim.World.IsAlive(Entity),"it did not end");
    }
    [Fact]
    public void ALifetimeOfZeroEndsTheEntityAtTheFirstTick(){
        using var Sim=new Sim();
        var Entity=Sim.World.Create(new Lifetime());
        Sim.Tick();
        Assert.False(Sim.World.IsAlive(Entity));
    }
    [Fact]
    public void ALifetimeOnTheRootOfASceneEndsTheWholeScene(){
        using var Sim=new Sim();
        var Instance=Sim.World.BeginSceneInstance();
        var Root=Sim.World.AttachToScene(Instance,Sim.World.Create(new Lifetime { Seconds=LogicalScalar.FromSingle(0.05f) }));
        var Child=Sim.World.AttachToScene(Instance,Sim.World.Create(new Transform(),new PrimitiveMesh()));
        Sim.World.EndSceneInstance(Instance,Root);
        Sim.Tick(2);
        Assert.True(Sim.World.IsAlive(Root)&&Sim.World.IsAlive(Child));
        Sim.Tick(3);
        Assert.False(Sim.World.IsAlive(Root));
        Assert.False(Sim.World.IsAlive(Child));
        Assert.False(Sim.World.IsSceneAlive(Instance));
    }
    [Fact]
    public void ABurstEmitsItsParticlesOnceAtTheNodesWorldPosition(){
        using var Sim=new Sim();
        var Origin=new LogicalVector3(1f,2f,3f);
        Sim.World.Create(new Transform { Position=Origin },Burst(),new ParticleBurstRuntime());
        Sim.Tick();
        var Born=Sim.Particles();
        Assert.Equal(12,Born.Count);
        Assert.All(Born,Pair=>Assert.Equal(Origin,Pair.Transform.Position));
        Assert.All(Born,Pair=>Assert.Equal(LogicalScalar.One,Pair.Transform.Scale.R));
        Sim.Tick(3);
        Assert.Equal(12,Sim.Particles().Count);
        Sim.Tick(30);
        Assert.Empty(Sim.Particles());
        // 1 回だけ発生した。tick をさらに進めても 2 回目のバーストは起きない。
        Sim.Tick(5);
        Assert.Empty(Sim.Particles());
    }
    [Fact]
    public void ParticlesFlyOutFallShrinkAndEndWithTheirLifetime(){
        using var Sim=new Sim();
        Sim.World.Create(new Transform(),Burst(),new ParticleBurstRuntime());
        Sim.Tick(2);
        var Early=Sim.Particles();
        Assert.All(Early,Pair=>Assert.True(Single(Pair.Transform.Position.U)>0f,"a particle did not rise"));
        Sim.Tick(6);
        var Middle=Sim.Particles();
        Assert.Equal(12,Middle.Count);
        // tick 1 で生成され、tick 2～8 で移動する（移動処理は寿命が減る前に読み取る）：寿命 0.2 秒の 12 tick 中 6 tick が経過し、スケールは半分。
        Assert.All(Middle,Pair=>Assert.InRange(Single(Pair.Transform.Scale.R),0.49f,0.51f));
        Assert.All(Middle,Pair=>Assert.InRange(Single(Pair.Particle.Velocity.U),0f,5f));
        Sim.Tick(10);
        Assert.Empty(Sim.Particles());
    }
    [Fact]
    public void TheSameSeedMakesTheSameBurstAndAnotherSeedADifferentOne(){
        List<(Transform Transform,Particle Particle)>Run(uint Seed){
            using var Sim=new Sim();
            Sim.World.Create(new Transform(),Burst(Seed),new ParticleBurstRuntime());
            Sim.Tick(4);
            return Sim.Particles();
        }
        var First=Run(7u);
        var Again=Run(7u);
        var Other=Run(8u);
        Assert.Equal(First.Select(static P=>P.Transform.Position),Again.Select(static P=>P.Transform.Position));
        Assert.NotEqual(First.Select(static P=>P.Transform.Position),Other.Select(static P=>P.Transform.Position));
    }
    [Theory]
    [InlineData(1000,ParticleBurst.MaxCount)]
    [InlineData(0,0)]
    [InlineData(-5,0)]
    public void TheCountIsClampedToTheLimit(int Requested,int Expected){
        using var Sim=new Sim();
        Sim.World.Create(new Transform(),Burst(Count:Requested),new ParticleBurstRuntime());
        Sim.Tick();
        Assert.Equal(Expected,Sim.Particles().Count);
    }
    [Fact]
    public void ABurstWithNoLifetimeLeavesParticlesThatEndAtOnce(){
        using var Sim=new Sim();
        var Instant=Burst() with { Lifetime=LogicalScalar.Zero };
        Sim.World.Create(new Transform(),Instant,new ParticleBurstRuntime());
        Sim.Tick();
        Assert.Equal(12,Sim.Particles().Count);
        Sim.Tick();
        Assert.Empty(Sim.Particles());
    }
    [Fact]
    public void ASpawnedSceneIsCreatedAtTheGivenPositionAndKeepsItsOwnScale(){
        using var Sim=new Sim();
        var Spawner=new Spawner();
        var Position=new LogicalVector3(-2f,1f,4f);
        Spawner.Spawn<PrimitiveShowcaseBoxScene>(Position);
        Assert.Equal(1,Spawner.PendingCount);
        var Created=new List<SceneInstance>();
        Assert.Equal(1,Spawner.Flush(ref Sim.World,Created));
        Assert.Equal(0,Spawner.PendingCount);
        Sim.World.PlaybackStructuralChanges(ref Sim.State);
        var Root=Sim.World.GetSceneRoot(Assert.Single(Created));
        Assert.True(Sim.World.TryGetComponent(Root,out Transform Placed));
        Assert.Equal(Position,Placed.Position);
        Assert.Equal(LogicalVector3.One,Placed.Scale);
        Assert.True(Sim.World.TryGetComponent(Root,out PrimitiveMesh Mesh));
        Assert.Equal(PrimitiveType.Box,Mesh.Type);
        // 再度生成されるのは、同じ Entity ではなく独立した複製。
        Spawner.Spawn<PrimitiveShowcaseBoxScene>(new LogicalVector3(5f,0f,0f));
        Spawner.Flush(ref Sim.World,Created);
        Sim.World.PlaybackStructuralChanges(ref Sim.State);
        Assert.Equal(2,Created.Count);
        Assert.NotEqual(Root,Sim.World.GetSceneRoot(Created[1]));
    }
    [Fact]
    public void ASpawnWithARotationTurnsTheRoot(){
        using var Sim=new Sim();
        var Spawner=new Spawner();
        var Turned=LogicalQuaternion.FromEulerRuf(new LogicalVector3(0f,MathF.PI/2f,0f));
        Spawner.Spawn<PrimitiveShowcaseBoxScene>(LogicalVector3.Zero,Turned);
        var Created=new List<SceneInstance>();
        Spawner.Flush(ref Sim.World,Created);
        Sim.World.PlaybackStructuralChanges(ref Sim.State);
        Assert.True(Sim.World.TryGetComponent(Sim.World.GetSceneRoot(Created[0]),out Transform Placed));
        Assert.Equal(Turned,Placed.Rotation);
    }
    [Fact]
    public void NothingIsCreatedUntilTheRequestsAreFlushed(){
        using var Sim=new Sim();
        var Spawner=new Spawner();
        var Before=Sim.World.GetStatistics().AliveEntityCount;
        Spawner.Spawn<PrimitiveShowcaseBoxScene>(LogicalVector3.Zero);
        Sim.Tick(3);
        Assert.Equal(Before,Sim.World.GetStatistics().AliveEntityCount);
        Assert.Equal(1,Spawner.PendingCount);
    }
}
