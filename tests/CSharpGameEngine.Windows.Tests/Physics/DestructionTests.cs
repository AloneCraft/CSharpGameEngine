using System.Reflection;
using CSharpGameEngine.Destruction;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Simulation;
using UserEcsProject.Scenes;
using EcsWorld=CSharpGameEngine.Entities.World;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>The closed box the Destruction Cook module cuts for a box collider (the same twelve outward triangles; the Cook tests compare the bytes).</summary>
internal static class DestructionTestGeometry {
    internal static PhysicsShapeAsset Box(string Id,LogicalVector3 Center,LogicalVector3 Half){
        var Vertices=new LogicalVector3[8];
        for(var Index=0;Index<8;Index++){
            Vertices[Index]=Center+new LogicalVector3((Index&1)==0?-Half.R:Half.R,(Index&2)==0?-Half.U:Half.U,(Index&4)==0?-Half.F:Half.F);
        }
        int[]Indices=[0,4,6,0,6,2,1,3,7,1,7,5,0,1,5,0,5,4,2,6,7,2,7,3,0,2,3,0,3,1,4,5,7,4,7,6];
        return PhysicsShapeAsset.CreateStaticTriangleMesh(Id,Vertices,Indices);
    }
}
/// <summary>
/// ADR-0078/M0113: destructibles break in the running game. Runs the showcase project's generated fixed ticks (physics, destruction, follow) without a
/// window over the destruction scenes, and checks what a player would see: the pieces, where they go and where they stop, and that the same input breaks
/// the same way and plays back the same from the physics cache.
/// </summary>
public sealed class DestructionTests {
    /// <summary>The cooks of the scenes' destructibles, made the way the build's Cook module makes them (a box collider cut by the engine's cooker).</summary>
    internal static PrefractureAsset Cook(string Name,LogicalVector3 Half,int Count,int Seed,int Density=500,int Strength=2000,int Clusters=0){
        var Source=DestructionTestGeometry.Box(Name,LogicalVector3.Zero,Half);
        Assert.True(PrefractureCooker.TryGenerate(Source,new PrefractureCookSettings(Count,Seed,LogicalScalar.FromInt64(Density),LogicalScalar.FromInt64(Strength),Clusters),out var Asset,out var Error),Error);
        return Asset!;
    }
    private static readonly Lazy<bool>Assets=new(static ()=>{
        CSharpGameEngine.Destruction.Destruction.Register("DropCrate",Cook("DropCrate",Half(0.5f,0.5f,0.5f),12,3));
        CSharpGameEngine.Destruction.Destruction.Register("TestWall",Cook("TestWall",Half(0.15f,1.5f,2f),24,11,2400,1200));
        CSharpGameEngine.Destruction.Destruction.Register("FieldCrate",Cook("FieldCrate",Half(0.5f,0.5f,0.5f),10,5));        return true;
    });
    internal static LogicalVector3 Half(float R,float U,float F)=>new(LogicalScalar.FromSingle(R),LogicalScalar.FromSingle(U),LogicalScalar.FromSingle(F));
    internal sealed class Sim:IDisposable {
        internal readonly EngineHost Host=new();
        internal State State;
        private GeneratedSystemSet Systems;
        internal Sim(){
            _=Assets.Value;
            this.State=this.Host.CreateState();
            this.State.Enabled=true;
            this.State.ExecutionMode=EngineExecutionMode.Runtime;
        }
        internal ref EcsWorld World=>ref this.State.World;
        internal void Tick(int Count=1){
            for(var Index=0;Index<Count;Index++){ GeneratedGameLoop.RunBakeTick(ref this.State,ref this.Systems,this.Host); }
        }
        internal CSharpGameEngine.Destruction.Destruction Service(){
            var Field=typeof(GeneratedSystemSet).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(Candidate=>Candidate.FieldType==typeof(CSharpGameEngine.Destruction.Destruction));
            return (CSharpGameEngine.Destruction.Destruction)Field.GetValue(this.Systems)!;
        }
        internal List<Entity>With<T>()where T:struct{
            List<Entity>Found=[];
            for(var Slot=0;Slot<this.World.GetStatistics().EntitySlotCount;Slot++){
                var Entity=this.World.EntityArchetypeIndices.GetRefUnchecked(Slot).Entity;
                if(this.World.IsAlive(Entity)&&HasComponent<T>(Entity)){ Found.Add(Entity); }
            }
            return Found;
        }
        private bool HasComponent<T>(Entity Entity)where T:struct=>typeof(T)==typeof(Destructible)?this.World.TryGetComponent(Entity,out Destructible _)
            :typeof(T)==typeof(DestructiblePiece)?this.World.TryGetComponent(Entity,out DestructiblePiece _)
            :typeof(T)==typeof(DestructibleFragment)?this.World.TryGetComponent(Entity,out DestructibleFragment _)
            :throw new NotSupportedException(typeof(T).Name);
        internal DestructibleState StateOf(Entity Root)=>this.World.TryGetComponent(Root,out DestructibleState Value)?Value:throw new InvalidOperationException("not a destructible");
        internal IReadOnlyPhysicsWorld Physics=>this.Host.PhysicsWorld;
        /// <summary>The physics bodies of the loose (dynamic) pieces.</summary>
        internal List<PhysicsBodyState>LooseBodies(){
            List<PhysicsBodyState>Bodies=[];
            foreach(var Entity in this.With<DestructiblePiece>()){
                if(this.Physics.TryGetBodyState(Entity,out var Body)&&Body.MotionType==PhysicsMotionType.Dynamic){ Bodies.Add(Body); }
            }
            return Bodies;
        }
        public void Dispose(){
            GeneratedGameLoop.Shutdown(ref this.State,ref this.Systems,this.Host);
            this.Host.Dispose();
        }
    }
    private static float Single(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    [Fact]
    public void ADroppedCrateBreaksOnTheFloorAndItsPiecesComeToRestOnIt(){
        using var Sim=new Sim();
        Sim.World.InstantiateScene<PhysicsGroundScene>();
        Sim.World.InstantiateScene<DestructionDropScene>();
        Sim.Tick(10);
        var Root=Assert.Single(Sim.With<Destructible>());
        var Loaded=Sim.StateOf(Root);
        Assert.True(Loaded.Loaded,Loaded.Problem);
        Assert.False(Loaded.Broken);
        Assert.Equal(12,Sim.With<DestructibleFragment>().Count);
        Sim.Tick(110);
        var Broken=Sim.StateOf(Root);
        Assert.True(Broken.Broken,"the crate did not break when it hit the floor");
        Assert.True(Broken.LoosePieces>=2,$"only {Broken.LoosePieces} loose piece(s)");
        Sim.Tick(480);
        var Bodies=Sim.LooseBodies();
        Assert.NotEmpty(Bodies);
        foreach(var Body in Bodies){
            // On the floor (its top is at height 0), not through it, and at rest.
            Assert.True(Single(Body.Position.U)>-0.02f,$"a piece sank to {Single(Body.Position.U)}");
            Assert.True(Single(Body.Position.U)<1.2f,$"a piece is still up at {Single(Body.Position.U)}");
            Assert.True(Body.IsSleeping||Single(Body.Velocity.Linear.LengthSquared)<0.01f,$"a piece still moves at {Single(Body.Velocity.Linear.LengthSquared)} m²/s²");
        }
        // Every drawn fragment rides on a live piece and sits at its body.
        foreach(var Visual in Sim.With<DestructibleFragment>()){
            Assert.True(Sim.World.TryGetComponent(Visual,out DestructibleFragment Fragment));
            Assert.True(Sim.World.IsAlive(Fragment.Body));
            Assert.True(Sim.World.TryGetComponent(Visual,out Transform Drawn));
            Assert.True(Sim.World.TryGetComponent(Fragment.Body,out Transform Carrier));
            var Rotation=Sim.World.TryGetComponent(Fragment.Body,out PhysicsRotation Turn)?Turn.Value:Carrier.Rotation;
            Assert.True(Rotation.TryRotate(Fragment.Offset,out var Offset));
            Assert.Equal(Carrier.Position+Offset,Drawn.Position);
        }
    }
    /// <summary>
    /// ADR-0078/M0113 (left open by M0105): two instances of one scene with a body each get their own physics stable IDs (the first keeps the definition's,
    /// the next a hash of it and the instance's ordinal), so both are simulated, both break and their pieces are told apart.
    /// </summary>
    [Fact]
    public void TwoInstancesOfOneSceneAreTwoBodiesThatEachBreak(){
        using var Sim=new Sim();
        Sim.World.InstantiateScene<PhysicsGroundScene>();
        DestructionDropScene.InstantiateDeferredAt(ref Sim.World,new LogicalVector3(-2,3,0));
        DestructionDropScene.InstantiateDeferredAt(ref Sim.World,new LogicalVector3(2,3,0));
        Sim.Tick(120);
        var Roots=Sim.With<Destructible>();
        Assert.Equal(2,Roots.Count);
        var Ids=Roots.Select(Root=>Sim.World.TryGetComponent(Root,out PhysicsStableId Id)?Id.Value:0UL).ToArray();
        Assert.DoesNotContain(0UL,Ids);
        Assert.NotEqual(Ids[0],Ids[1]);
        foreach(var Root in Roots){
            var State=Sim.StateOf(Root);
            Assert.True(State.Broken,"one of the two crates did not break");
            Assert.True(State.LoosePieces>=2,$"only {State.LoosePieces} loose piece(s)");
        }
        // Each piece belongs to one of the two, and every piece body has its own stable ID.
        var PieceIds=Sim.With<DestructiblePiece>().Select(Piece=>Sim.World.TryGetComponent(Piece,out PhysicsStableId Id)?Id.Value:0UL).ToArray();
        Assert.Equal(PieceIds.Length,PieceIds.Distinct().Count());
    }
    /// <summary>The ball, the wall and the floor of the shot-wall test.</summary>
    private static Entity Wall(Sim Sim){
        Sim.World.InstantiateScene<PhysicsGroundScene>();
        Sim.World.InstantiateScene<DestructionWallScene>();
        Sim.World.InstantiateScene<DestructionBallScene>();
        Sim.Tick(1);
        return Assert.Single(Sim.With<Destructible>());
    }
    [Fact]
    public void AShotWallLosesTheHitFragmentsAndKeepsItsFootStill(){
        using var Sim=new Sim();
        var Root=Wall(Sim);
        Sim.Tick(40);
        var State=Sim.StateOf(Root);
        Assert.True(State.Broken,"the ball did not break the wall");
        Assert.True(State.LoosePieces>=1,"nothing came loose");
        Assert.True(Sim.Service().TryGetState(Root,out var Fracture));
        // The pieces that hold an anchored (bottom) fragment are static bodies; the loose ones move.
        foreach(var Piece in Fracture.Pieces){
            var Body=Sim.Service().PieceBody(Root,Piece.Id);
            Assert.True(Sim.World.IsAlive(Body));
            Assert.Equal(!Piece.Anchored,Sim.World.TryGetComponent(Body,out RigidBody _));
        }
        Assert.Contains(Fracture.Pieces,static Piece=>Piece.Anchored);
        for(var Fragment=0;Fragment<Fracture.Asset.Fragments.Count;Fragment++){
            if(Fracture.IsAnchored(Fragment)){ Assert.True(Fracture.PieceOf(Fragment)!.Anchored); }
        }
        // The hit is near the middle, where the ball struck (U 1.6 m, F 0.1 m): a broken bond is within a metre of it.
        var Near=Enumerable.Range(0,Fracture.Asset.Bonds.Count).Where(Fracture.IsBondBroken).Select(Bond=>{
            var Pair=Fracture.Asset.Bonds[Bond];
            var Middle=(Fracture.Asset.Fragments[Pair.FirstFragment].LocalCenter+Fracture.Asset.Fragments[Pair.SecondFragment].LocalCenter)/LogicalScalar.FromInt64(2);
            return MathF.Sqrt(Single((Middle-new LogicalVector3(0,LogicalScalar.FromSingle(0.1f),LogicalScalar.FromSingle(0.1f))).LengthSquared));
        }).Min();
        Assert.True(Near<1f,$"the nearest broken bond is {Near} m from the hit");
    }
    [Fact]
    public void AnExplosionBreaksEveryBondInItsReachAndPushesThePiecesAway(){
        using var Sim=new Sim();
        Sim.World.InstantiateScene<PhysicsGroundScene>();
        Sim.World.InstantiateScene<DestructionFieldScene>();
        Sim.Tick(3);
        var Root=Assert.Single(Sim.With<Destructible>());
        var State=Sim.StateOf(Root);
        Assert.True(Sim.Service().TryGetState(Root,out var Fracture));
        Assert.Equal(Fracture.Asset.Bonds.Count,State.BrokenBonds);
        Assert.Equal(Fracture.Asset.Fragments.Count,State.LoosePieces);
        var Center=new LogicalVector3(0,LogicalScalar.FromSingle(0.5f),0);
        foreach(var Body in Sim.LooseBodies()){
            var Away=Body.Position-Center;
            Assert.True(Single(LogicalVector3.Dot(Away,Body.Velocity.Linear))>0f,"a piece is not moving away from the explosion");
        }
    }
    [Fact]
    public void EveryBrokenBondIsReportedOnceAsABondBrokenEventOfItsDestructible(){
        using var Sim=new Sim();
        Sim.World.InstantiateScene<PhysicsGroundScene>();
        Sim.World.InstantiateScene<DestructionFieldScene>();
        var Root=default(Entity);
        HashSet<ulong>Pairs=[];
        var Events=0;
        for(var Tick=0;Tick<10;Tick++){
            Sim.Tick();
            foreach(var Event in Sim.Physics.Events.Where(static Event=>Event.Type==PhysicsEventType.BondBroken)){
                Root=Event.First;
                Events++;
                Assert.True(Pairs.Add(Event.PairId),"a bond broke twice");
                Assert.True(Event.Impulse>LogicalScalar.Zero);
            }
        }
        Assert.Equal(Sim.StateOf(Root).BrokenBonds,Events);
    }
    [Fact]
    public void AtMostTheFragmentBudgetOfLoosePiecesLiveAndTheirFragmentsGoWithThem(){
        using var Sim=new Sim();
        Sim.World.InstantiateScene<PhysicsGroundScene>();
        Sim.World.InstantiateScene<DestructionFieldScene>();
        var Root=Assert.Single(Sim.With<Destructible>());
        Assert.True(Sim.World.TryGetComponent(Root,out Destructible Settings));
        Settings.FragmentBudget=3;
        Assert.True(Sim.World.TrySetComponent(Root,in Settings));
        Sim.Tick(5);
        var State=Sim.StateOf(Root);
        Assert.Equal(3,State.LoosePieces);
        Assert.Equal(3,Sim.LooseBodies().Count);
        Assert.Equal(3,Sim.With<DestructibleFragment>().Count);
    }
    [Fact]
    public void ALoosePieceIsRemovedWhenItsLifetimeRunsOutAndNotBefore(){
        using var Sim=new Sim();
        Sim.World.InstantiateScene<PhysicsGroundScene>();
        Sim.World.InstantiateScene<DestructionFieldScene>();
        var Root=Assert.Single(Sim.With<Destructible>());
        Assert.True(Sim.World.TryGetComponent(Root,out Destructible Settings));
        Settings.FragmentLifetime=LogicalScalar.FromSingle(0.5f);
        Assert.True(Sim.World.TrySetComponent(Root,in Settings));
        Sim.Tick(25);
        Assert.Equal(10,Sim.StateOf(Root).LoosePieces);
        Sim.Tick(15);
        Assert.Equal(0,Sim.StateOf(Root).LoosePieces);
        Assert.Empty(Sim.LooseBodies());
        Assert.Empty(Sim.With<DestructibleFragment>());
    }
    [Fact]
    public void AMissingAssetOrAScaleIsReportedOnceAndTheObjectStaysWhole(){
        using var Sim=new Sim();
        Sim.World.InstantiateScene<PhysicsGroundScene>();
        Sim.World.InstantiateScene<DestructionFieldScene>();
        var Root=Assert.Single(Sim.With<Destructible>());
        Assert.True(Sim.World.TryGetComponent(Root,out Destructible Settings));
        Settings.FractureAsset="NoSuchFracture";
        Assert.True(Sim.World.TrySetComponent(Root,in Settings));
        Sim.Tick(5);
        var State=Sim.StateOf(Root);
        Assert.False(State.Loaded);
        Assert.StartsWith("DST002",State.Problem);
        Assert.False(State.Broken);
        Assert.Empty(Sim.With<DestructibleFragment>());
        using var Scaled=new Sim();
        Scaled.World.InstantiateScene<DestructionFieldScene>();
        var Other=Assert.Single(Scaled.With<Destructible>());
        Assert.True(Scaled.World.TryGetComponent(Other,out Transform Placement));
        Placement.Scale=new LogicalVector3(2,2,2);
        Assert.True(Scaled.World.TrySetComponent(Other,in Placement));
        Scaled.Tick(2);
        Assert.StartsWith("DST004",Scaled.StateOf(Other).Problem);
    }
    /// <summary>The physics world's state hash and the broken bonds after each tick of a run.</summary>
    private static List<(ulong Hash,int Broken)>Record(int Ticks,bool Together){
        using var Sim=new Sim();
        Sim.World.InstantiateScene<PhysicsGroundScene>();
        Sim.World.InstantiateScene<DestructionFieldScene>();
        if(Together){
            // A dropped crate beside the exploding one: both break, each by its own cause.
            Sim.World.InstantiateScene<DestructionDropScene>();
            foreach(var Root in Sim.With<Destructible>()){
                if(Sim.World.TryGetComponent(Root,out Transform Placement)&&Placement.Position.U>LogicalScalar.One){
                    Placement.Position=new LogicalVector3(6,3,0);
                    Assert.True(Sim.World.TrySetComponent(Root,in Placement));
                }
            }
        }
        List<(ulong,int)>Record=[];
        var World=(DeterministicPhysicsWorld)Sim.Physics;
        for(var Tick=0;Tick<Ticks;Tick++){
            Sim.Tick();
            Record.Add((World.ComputeStateHash(),Sim.With<Destructible>().Sum(Root=>Sim.StateOf(Root).BrokenBonds)));
        }
        return Record;
    }
    [Fact]
    public void TheSameRunBreaksTheSameWayEveryTime(){
        Assert.Equal(Record(120,false),Record(120,false));
    }
    [Fact]
    public void TwoDestructiblesAtOnceBreakEachByItsOwnCauseTheSameWayEveryTime(){
        var First=Record(150,true);
        Assert.Equal(First,Record(150,true));
        using var Sim=new Sim();
        Sim.World.InstantiateScene<PhysicsGroundScene>();
        Sim.World.InstantiateScene<DestructionFieldScene>();
        Sim.World.InstantiateScene<DestructionDropScene>();
        foreach(var Root in Sim.With<Destructible>()){
            if(Sim.World.TryGetComponent(Root,out Transform Placement)&&Placement.Position.U>LogicalScalar.One){
                Placement.Position=new LogicalVector3(6,3,0);
                Assert.True(Sim.World.TrySetComponent(Root,in Placement));
            }
        }
        Sim.Tick(150);
        Assert.All(Sim.With<Destructible>(),Root=>Assert.True(Sim.StateOf(Root).Broken,$"{Root} did not break"));
        Assert.Equal(2,Sim.Service().Count);
    }
    private const string FieldScene="UserEcsProject.Scenes.DestructionFieldScene";
    private static byte[]Bake(int Ticks){
        _=Assets.Value;
        using MemoryStream Stream=new();
        Assert.True(GeneratedSceneBakeHost.TryBake(FieldScene,Ticks,"destruction-bake",Stream,CancellationToken.None,out var Error),Error);
        return Stream.ToArray();
    }
    [Fact]
    public void ABakedCacheRecordsTheBreaksAndThePiecesAndPlaysBackToTheSameBytes(){
        var Baked=Bake(90);
        Assert.Equal(Baked,Bake(90));
        List<PhysicsCacheFrame>Frames=[];
        using(MemoryStream Input=new(Baked)){
            Assert.True(PhysicsCacheSerializer.TryRead(Input,new SimulationCacheIdentity("physics","LogicalScalar-Q64.64",60,"destruction-bake",PhysicsCacheSerializer.SchemaVersion),Frames,out var Error),Error);
        }
        Assert.Contains(Frames,static Frame=>Frame.Breaks.Count>0);
        Assert.True(Frames[^1].Bodies.Count>Frames[0].Bodies.Count,"the pieces are not in the cache");
        Assert.Equal(Frames.Sum(static Frame=>Frame.Breaks.Count),Frames.Sum(static Frame=>Frame.Events.Count(static Event=>Event.Type==PhysicsEventType.BondBroken)));
        // Played back from the cache (no strain is computed: the recorded breaks make the pieces), the world holds exactly what was baked, tick by tick;
        // played back again, the same.
        for(var Run=0;Run<2;Run++){
            using MemoryStream Cache=new(Baked);
            using MemoryStream Replayed=new();
            Assert.True(GeneratedSceneBakeHost.TryReplay(FieldScene,90,"destruction-bake",Cache,Replayed,CancellationToken.None,out var ReplayError),ReplayError);
            Assert.Equal(Baked,Replayed.ToArray());
        }
    }
    [Fact]
    public void TheRealtimeRunWritesTheSameFramesAsTheBake(){
        var Baked=Bake(60);
        using var Sim=new Sim();
        Sim.World.InstantiateScene<DestructionFieldScene>();
        using MemoryStream Stream=new();
        var Identity=new SimulationCacheIdentity("physics","LogicalScalar-Q64.64",Sim.State.FixedTickRate,"destruction-bake",PhysicsCacheSerializer.SchemaVersion);
        using(PhysicsCacheStreamWriter Writer=new(Stream,Identity,60)){
            for(var Tick=0;Tick<60;Tick++){
                var Current=Sim.State.FixedTick;
                Sim.Tick();
                Sim.Host.WritePhysicsCacheFrame(Writer,Current);
            }
            Writer.Complete();
        }
        Assert.Equal(Baked,Stream.ToArray());
    }
    [Fact]
    public void ACacheThatNoLongerMatchesTheWorldStopsTheReplayWithAReason(){
        var Baked=Bake(30);
        using MemoryStream Cache=new(Baked);
        using MemoryStream Replayed=new();
        Assert.False(GeneratedSceneBakeHost.TryReplay("UserEcsProject.Scenes.DestructionDropScene",30,"destruction-bake",Cache,Replayed,CancellationToken.None,out var Error));
        Assert.Contains("playback stopped",Error);
    }
}
