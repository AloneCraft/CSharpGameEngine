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
    internal static PrefractureAsset Cook(string Name,LogicalVector3 Half,int Count,int Seed,int Density=500,int Strength=2000,int Clusters=0,int Levels=1){
        var Source=DestructionTestGeometry.Box(Name,LogicalVector3.Zero,Half);
        Assert.True(PrefractureCooker.TryGenerate(Source,new PrefractureCookSettings(Count,Seed,LogicalScalar.FromInt64(Density),LogicalScalar.FromInt64(Strength),Clusters,Levels),out var Asset,out var Error),Error);
        return Asset!;
    }
    private static readonly Lazy<bool>Assets=new(static ()=>{
        CSharpGameEngine.Destruction.Destruction.Register("DropCrate",Cook("DropCrate",Half(0.5f,0.5f,0.5f),12,3));
        CSharpGameEngine.Destruction.Destruction.Register("TestWall",Cook("TestWall",Half(0.15f,1.5f,2f),24,11,2400,1200));
        CSharpGameEngine.Destruction.Destruction.Register("FieldCrate",Cook("FieldCrate",Half(0.5f,0.5f,0.5f),10,5));
        // M0124: the drop crate's cut with ten times the bond strength, so it stands on the floor under its own weight (the 2000 N·s/m² crate loses its
        // weakest bonds, of 41 to 74 N·s, to the first contact of its weight, as in M0113) and only the game's hits strain it.
        CSharpGameEngine.Destruction.Destruction.Register("StrongCrate",Cook("DropCrate",Half(0.5f,0.5f,0.5f),12,3,Strength:20000));        return true;
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
    /// <summary>
    /// M0124: the dropped crate of <c>DestructionDropScene</c>, cut with ten times the bond strength (<c>StrongCrate</c>), set down on the floor (not dropped;
    /// or <paramref name="Floating"/> in the air without gravity, touching nothing) and settled for two seconds; then its top fragment, the point
    /// the game hits, and the impulse of <paramref name="Share"/> % of the weakest bond of that fragment. <paramref name="Decay"/> overrides the crate's
    /// <c>StrainDecay</c>.
    /// </summary>
    private static (Entity Root,LogicalVector3 Point,LogicalScalar Impulse,int Settled)SetDownACrate(Sim Sim,LogicalScalar?Decay=null,int Share=40,bool Floating=false){
        Sim.World.InstantiateScene<PhysicsGroundScene>();
        Sim.World.InstantiateScene<DestructionDropScene>();
        var Root=Assert.Single(Sim.With<Destructible>());
        Assert.True(Sim.World.TryGetComponent(Root,out Transform Placement));
        Placement.Position=new LogicalVector3(0,LogicalScalar.FromSingle(Floating?3f:0.5005f),0);
        Assert.True(Sim.World.TrySetComponent(Root,in Placement));
        if(Floating){
            Assert.True(Sim.World.TryGetComponent(Root,out RigidBody Rigid));
            Rigid.GravityFactor=LogicalScalar.Zero;
            Assert.True(Sim.World.TrySetComponent(Root,in Rigid));
        }
        Assert.True(Sim.World.TryGetComponent(Root,out Destructible Settings));
        Settings.FractureAsset="StrongCrate";
        if(Decay is { }Value){ Settings.StrainDecay=Value; }
        Assert.True(Sim.World.TrySetComponent(Root,in Settings));
        // Settle on the floor first: the crate must stand whole under its own weight (its tiniest bonds, of a few mm², may go at the first contact).
        Sim.Tick(120);
        Assert.True(Sim.StateOf(Root).Loaded,Sim.StateOf(Root).Problem);
        Assert.False(Sim.StateOf(Root).Broken,"the crate came apart under its own weight");
        var Settled=Sim.StateOf(Root).BrokenBonds;
        Sim.Tick(60);
        Assert.Equal(Settled,Sim.StateOf(Root).BrokenBonds);
        Assert.True(Sim.Service().TryGetState(Root,out var Fracture));
        var Top=Enumerable.Range(0,Fracture.Asset.Fragments.Count).MaxBy(Fragment=>Fracture.Asset.Fragments[Fragment].LocalCenter.U);
        // A hit loads the hit fragment's bonds fully and each further ring of bonds by half (the default DamagePropagation), up to four rings: the hit is
        // the share of the least of strength × 2^ring over the intact bonds it reaches, so no bond takes more than that share of its strength.
        var Asset=Fracture.Asset;
        var Rings=new int[Asset.Fragments.Count];
        Array.Fill(Rings,-1);
        Rings[Top]=0;
        for(var Changed=true;Changed;){
            Changed=false;
            for(var Bond=0;Bond<Asset.Bonds.Count;Bond++){
                if(Fracture.IsBondBroken(Bond)){ continue; }
                foreach(var (From,To) in new[]{ (Asset.Bonds[Bond].FirstFragment,Asset.Bonds[Bond].SecondFragment),(Asset.Bonds[Bond].SecondFragment,Asset.Bonds[Bond].FirstFragment) }){
                    if(Rings[From]>=0&&(Rings[To]<0||Rings[To]>Rings[From]+1)){ Rings[To]=Rings[From]+1;Changed=true; }
                }
            }
        }
        var Weakest=LogicalScalar.MaxValue;
        for(var Bond=0;Bond<Asset.Bonds.Count;Bond++){
            var Ring=Math.Min(Rings[Asset.Bonds[Bond].FirstFragment]<0?99:Rings[Asset.Bonds[Bond].FirstFragment],Rings[Asset.Bonds[Bond].SecondFragment]<0?99:Rings[Asset.Bonds[Bond].SecondFragment]);
            if(Fracture.IsBondBroken(Bond)||Ring>4){ continue; }
            var Limit=Fracture.StrengthOf(Bond)*LogicalScalar.FromInt64(1L<<Ring);
            if(Limit<Weakest){ Weakest=Limit; }
        }
        Assert.True(Sim.Physics.TryGetBodyState(Root,out var Body));
        return (Root,Body.Position+Fracture.Asset.Fragments[Top].LocalCenter,Weakest*LogicalScalar.FromInt64(Share)/LogicalScalar.FromInt64(100),Settled);
    }
    /// <summary>The tick (from the first) at which a bond of the set-down crate first broke under the game's hits at the given ticks, or -1.</summary>
    private static int HitAResting(int[]Hits,int Ticks,LogicalScalar?Decay=null,bool Floating=false){
        using var Sim=new Sim();
        var (Root,Point,Impulse,Settled)=SetDownACrate(Sim,Decay,Floating:Floating);
        for(var Tick=0;Tick<Ticks;Tick++){
            if(Hits.Contains(Tick)){ Sim.Service().ApplyImpulse(Root,Point,Impulse); }
            Sim.Tick();
            if(Sim.StateOf(Root).BrokenBonds>Settled){ return Tick; }
        }
        return -1;
    }
    [Fact]
    public void SmallHitsBreakACrateWhenThreeComeCloseTogetherButNotOneOrHitsFarApart(){
        // A crate in the air (no load but the hits), each hit 0.4 of what the bond it loads most can take: one hit is not enough, the third a tenth of a
        // second after the first is (0.4 × 0.95² + 0.4 × 0.95 + 0.4 ≥ 1), hits two seconds apart never add up (at most 0.4 × 0.37 / 0.63 + 0.4 ≈ 0.64),
        // and with the decay of 60/s (every tick on its own, as M0113) close hits do not add up either.
        Assert.Equal(-1,HitAResting([0],600,Floating:true));
        Assert.Equal(12,HitAResting([0,6,12],600,Floating:true));
        Assert.Equal(-1,HitAResting([0,120,240,360,480],600,Floating:true));
        Assert.Equal(-1,HitAResting([0,6,12],600,LogicalScalar.FromInt64(60),Floating:true));
    }
    [Fact]
    public void ACrateSetDownOnTheFloorStandsUnderItsWeightAndOneSmallHit(){
        // The steady load of a body at rest strains each tick only: twenty seconds on the floor wear nothing down, and one hit on top does not break it.
        Assert.Equal(-1,HitAResting([],1200));
        Assert.Equal(-1,HitAResting([0],600));
    }
    /// <summary>
    /// M0124: a live run hits a crate twice while it records the physics cache; another run plays that recording back (no strain is computed in
    /// playback) and goes on live from where it ends. The third hit, a tenth of a second after the first, breaks the crate's bonds in both runs at the
    /// same tick, which it does only when the played-back frames restored the internal strain the first hits left; the worlds stay the same tick by tick.
    /// The crate floats without gravity: the cache keeps no contacts (warm-started impulses, begun or going on), so a body resting on another at the
    /// hand-off takes its first live tick's load as a contact that began (a known limit of the hand-off, not of the internal strain).
    /// </summary>
    [Fact]
    public void ARecordedHitsInternalStrainIsRestoredByPlaybackSoTheLiveRunThatFollowsBreaksAlike(){
        const int Recorded=8;
        using var Live=new Sim();
        var (Root,Point,Impulse,Settled)=SetDownACrate(Live,Floating:true);
        var Identity=new SimulationCacheIdentity("physics","LogicalScalar-Q64.64",Live.State.FixedTickRate,"destruction-strain",PhysicsCacheSerializer.SchemaVersion);
        var StartTick=Live.State.FixedTick;
        using MemoryStream Cache=new();
        using(PhysicsCacheStreamWriter Writer=new(Cache,Identity,Recorded)){
            for(var Tick=0;Tick<Recorded;Tick++){
                if(Tick is 0 or 6){ Live.Service().ApplyImpulse(Root,Point,Impulse); }
                var Current=Live.State.FixedTick;
                Live.Tick();
                Live.Host.WritePhysicsCacheFrame(Writer,Current);
            }
            Writer.Complete();
        }
        List<PhysicsCacheFrame>Frames=[];
        Cache.Position=0;
        Assert.True(PhysicsCacheSerializer.TryRead(Cache,Identity,Frames,out var ReadError),ReadError);
        Assert.NotEmpty(Frames[0].Strains);
        using var Played=new Sim();
        var (PlayedRoot,_,_,_)=SetDownACrate(Played,Floating:true);
        Assert.Equal(StartTick,Played.State.FixedTick);
        Cache.Position=0;
        Assert.True(Played.Host.TryLoadPhysicsCache(Cache,Identity,true,out var LoadError),LoadError);
        Played.Tick(Recorded);
        Assert.True(Played.Host.TryHandoffPhysicsCache(Played.State.FixedTick));
        Assert.True(Live.Service().TryGetState(Root,out var LiveFracture));
        Assert.True(Played.Service().TryGetState(PlayedRoot,out var PlayedFracture));
        for(var Bond=0;Bond<LiveFracture.Asset.Bonds.Count;Bond++){ Assert.Equal(LiveFracture.InternalStrainOf(Bond),PlayedFracture.InternalStrainOf(Bond)); }
        var LiveWorld=(DeterministicPhysicsWorld)Live.Physics;
        var PlayedWorld=(DeterministicPhysicsWorld)Played.Physics;
        var BrokeAt=-1;
        for(var Tick=Recorded;Tick<30;Tick++){
            if(Tick==12){
                Live.Service().ApplyImpulse(Root,Point,Impulse);
                Played.Service().ApplyImpulse(PlayedRoot,Point,Impulse);
            }
            Live.Tick();
            Played.Tick();
            Assert.Equal(LiveWorld.ComputeStateHash(),PlayedWorld.ComputeStateHash());
            Assert.Equal(Live.StateOf(Root).BrokenBonds,Played.StateOf(PlayedRoot).BrokenBonds);
            if(BrokeAt<0&&Live.StateOf(Root).BrokenBonds>Settled){ BrokeAt=Tick; }
        }
        Assert.Equal(12,BrokeAt);
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
