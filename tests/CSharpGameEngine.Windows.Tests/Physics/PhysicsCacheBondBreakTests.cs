using System.Text;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Simulation;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0113: the <c>.cgephyscache</c> schema 3 records the bonds that broke in each tick after the events, and a schema 2 file (M0055) is still read
/// as frames without bond breaks.
/// </summary>
public sealed class PhysicsCacheBondBreakTests {
    private static SimulationCacheIdentity Identity(int Schema=PhysicsCacheSerializer.SchemaVersion)=>new("physics","LogicalScalar-Q64.64",60,"bond-test",Schema);
    private static PhysicsBodyState Body(ulong StableId)=>new(new Entity((int)StableId,1),StableId,PhysicsMotionType.Dynamic,new LogicalVector3(1,2,3),LogicalQuaternion.Identity,PhysicsVelocity.Zero,false,0);
    [Fact]
    public void TheBondBreaksOfAFrameRoundTripInCanonicalOrder(){
        var Event=new PhysicsEvent(4,DeterministicPhysicsWorld.BondPairId(7,2),PhysicsEventType.BondBroken,new Entity(7,1),default,LogicalScalar.FromInt64(9),default);
        PhysicsCacheFrame Frame=new(4,[Body(7)],[Event],[new PhysicsBondBreak(9,1,LogicalScalar.One),new PhysicsBondBreak(7,5,LogicalScalar.FromInt64(3)),new PhysicsBondBreak(7,2,LogicalScalar.FromInt64(9))]);
        using MemoryStream Stream=new();
        PhysicsCacheSerializer.Write(Stream,Identity(),[Frame,new PhysicsCacheFrame(5,[Body(7)],[])]);
        Stream.Position=0;
        List<PhysicsCacheFrame>Frames=[];
        Assert.True(PhysicsCacheSerializer.TryRead(Stream,Identity(),Frames,out var Error),Error);
        Assert.Equal([new PhysicsBondBreak(7,2,LogicalScalar.FromInt64(9)),new PhysicsBondBreak(7,5,LogicalScalar.FromInt64(3)),new PhysicsBondBreak(9,1,LogicalScalar.One)],Frames[0].Breaks);
        Assert.Empty(Frames[1].Breaks);
        Assert.Equal(Event,Assert.Single(Frames[0].Events));
    }
    /// <summary>
    /// M0113: a tumbling fragment's rotation comes out of the integrator's normalization, which is not idempotent in Q64.64 (normalizing it again moves the
    /// last bits). The cache took only exact fixed points of normalization and refused the bake of every spinning body; it takes unit quaternions now.
    /// </summary>
    [Fact]
    public void ASpinningBodyIsBakedEveryTickAndANonUnitRotationIsStillRefused(){
        DeterministicPhysicsWorld World=new();
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(new Entity(1,1),1,new Collider { Shape=ColliderShape.Box,HalfExtents=new LogicalVector3(0.5f,0.2f,0.3f) },
            new RigidBody { GravityFactor=LogicalScalar.Zero,AllowSleep=false },LogicalVector3.Zero,LogicalQuaternion.Identity,
            new PhysicsVelocity { Angular=new LogicalVector3(1.3f,-2.1f,0.7f) })));
        const int Ticks=120;
        using MemoryStream Stream=new();
        using(PhysicsCacheStreamWriter Writer=new(Stream,Identity(),Ticks)){
            List<PhysicsBodyState>States=[];
            for(var Tick=1UL;Tick<=Ticks;Tick++){
                World.Step(Tick);
                States.Clear();
                World.CollectBodyStates(States);
                Writer.WriteFrame(new PhysicsCacheFrame(Tick,[.. States],[]));
            }
            Writer.Complete();
        }
        Stream.Position=0;
        List<PhysicsCacheFrame>Frames=[];
        Assert.True(PhysicsCacheSerializer.TryRead(Stream,Identity(),Frames,out var Error),Error);
        Assert.Equal(Ticks,Frames.Count);
        Assert.NotEqual(LogicalQuaternion.Identity,Frames[^1].Bodies[0].Rotation);
        using MemoryStream Refused=new();
        using PhysicsCacheStreamWriter Bad=new(Refused,Identity(),1);
        var Doubled=Body(1) with { Rotation=new LogicalQuaternion(LogicalScalar.Zero,LogicalScalar.Zero,LogicalScalar.Zero,LogicalScalar.FromInt64(2)) };
        Assert.Throws<ArgumentException>(()=>Bad.WriteFrame(new PhysicsCacheFrame(1,[Doubled],[])));
        var Zero=Body(1) with { Rotation=default };
        Assert.Throws<ArgumentException>(()=>Bad.WriteFrame(new PhysicsCacheFrame(1,[Zero],[])));
    }
    [Fact]
    public void ABondBrokenTwiceInOneFrameOrABadBreakIsRefusedByTheWriter(){
        using MemoryStream Stream=new();
        using PhysicsCacheStreamWriter Writer=new(Stream,Identity(),1);
        Assert.Throws<ArgumentException>(()=>Writer.WriteFrame(new PhysicsCacheFrame(1,[],[],[new PhysicsBondBreak(3,1,LogicalScalar.One),new PhysicsBondBreak(3,1,LogicalScalar.One)])));
        Assert.Throws<ArgumentException>(()=>Writer.WriteFrame(new PhysicsCacheFrame(1,[],[],[new PhysicsBondBreak(0,1,LogicalScalar.One)])));
        Assert.Throws<ArgumentException>(()=>Writer.WriteFrame(new PhysicsCacheFrame(1,[],[],[new PhysicsBondBreak(3,-1,LogicalScalar.One)])));
        Assert.Throws<ArgumentException>(()=>Writer.WriteFrame(new PhysicsCacheFrame(1,[],[],[new PhysicsBondBreak(3,1,-LogicalScalar.One)])));
    }
    [Fact]
    public void ASchemaTwoCacheIsReadAndPlayedBackWithoutBondBreaks(){
        using MemoryStream Old=new();
        PhysicsCacheSerializer.Write(Old,Identity(2),[new PhysicsCacheFrame(1,[Body(1)],[]),new PhysicsCacheFrame(2,[Body(1)],[])]);
        var Bytes=Old.ToArray();
        // Schema 2 frames end after their events: no bond-break count.
        Assert.Equal(2,BitConverter.ToInt32(Bytes,8));
        List<PhysicsCacheFrame>Frames=[];
        using(MemoryStream Input=new(Bytes)){ Assert.True(PhysicsCacheSerializer.TryRead(Input,Identity(),Frames,out var Error),Error); }
        Assert.Equal(2,Frames.Count);
        Assert.All(Frames,static Frame=>Assert.Empty(Frame.Breaks));
        using(MemoryStream Input=new(Bytes)){
            Assert.True(PhysicsCachePlayer.TryLoad(Input,Identity(),false,out var Player,out var LoadError),LoadError);
            DeterministicPhysicsWorld World=new();
            World.Upsert(new PhysicsBodyDescription(new Entity(1,1),1,new Collider(),new RigidBody(),LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero));
            Assert.True(Player!.TryApply(1,World));
            Assert.True(Player.TryApply(2,World));
            Assert.Empty(World.BondBreaks);
        }
    }
    [Fact]
    public void ASchemaTwoCacheCannotRecordABondBreakAndOtherIdentitiesStayRefused(){
        using MemoryStream Stream=new();
        using(PhysicsCacheStreamWriter Writer=new(Stream,Identity(2),1)){
            Assert.Throws<ArgumentException>(()=>Writer.WriteFrame(new PhysicsCacheFrame(1,[],[],[new PhysicsBondBreak(3,1,LogicalScalar.One)])));
        }
        Assert.Throws<ArgumentOutOfRangeException>(()=>new PhysicsCacheStreamWriter(new MemoryStream(),Identity(1),1));
        using MemoryStream Old=new();
        PhysicsCacheSerializer.Write(Old,Identity(2),[]);
        foreach(var Expected in new[]{ Identity() with { DependencyHash="other" },Identity() with { TickRate=30 },Identity(4) }){
            Old.Position=0;
            Assert.False(PhysicsCacheSerializer.TryRead(Old,Expected,[],out var Error));
            Assert.Contains("does not match",Error);
        }
    }
    [Fact]
    public void AReportedBondBreakIsAnEventInOrderAndARecordUntilTheNextTick(){
        DeterministicPhysicsWorld World=new();
        World.Upsert(PhysicsBodyDescription.CreateDynamicBox(new Entity(5),new LogicalVector3(0,10,0),new LogicalVector3(1,1,1),LogicalScalar.One));
        World.Step(0);
        World.ReportBondBreak(0,new Entity(5),6,3,LogicalScalar.FromInt64(2));
        World.ReportBondBreak(0,new Entity(5),6,1,LogicalScalar.FromInt64(4));
        Assert.Equal(2,World.BondBreaks.Count);
        var Events=World.Events.Where(static Event=>Event.Type==PhysicsEventType.BondBroken).ToArray();
        Assert.Equal(2,Events.Length);
        Assert.All(Events,static Event=>Assert.Equal(new Entity(5),Event.First));
        Assert.Equal(World.Events.OrderBy(static Event=>Event.Tick).ThenBy(static Event=>Event.PairId).ThenBy(static Event=>Event.Type),World.Events);
        Assert.NotEqual(DeterministicPhysicsWorld.BondPairId(6,1),DeterministicPhysicsWorld.BondPairId(6,3));
        Assert.Throws<ArgumentOutOfRangeException>(()=>World.ReportBondBreak(0,new Entity(5),0,1,LogicalScalar.One));
        World.Step(1);
        Assert.Empty(World.BondBreaks);
        Assert.DoesNotContain(World.Events,static Event=>Event.Type==PhysicsEventType.BondBroken);
    }
}
