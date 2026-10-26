using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Simulation;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// M0124：<c>.cgephyscache</c>スキーマ4では、各フレームの結合破断記録後に、衝撃で内部ひずみが上昇した結合を正規順で記録する
///（破壊可能物の安定ID、結合、内部ひずみ）。スキーマ3（M0113）のファイルはひずみ記録なしのフレームとして引き続き読み込み、
/// 再生フレームではワールドにひずみを復元する。
/// </summary>
public sealed class PhysicsCacheBondStrainTests {
    private static SimulationCacheIdentity Identity(int Schema=PhysicsCacheSerializer.SchemaVersion)=>new("physics","LogicalScalar-Q64.64",60,"strain-test",Schema);
    private static PhysicsBodyState Body(ulong StableId)=>new(new Entity((int)StableId,1),StableId,PhysicsMotionType.Dynamic,new LogicalVector3(1,2,3),LogicalQuaternion.Identity,PhysicsVelocity.Zero,false,0);
    [Fact]
    public void TheInternalStrainsOfAFrameRoundTripInCanonicalOrderAfterItsBreaks(){
        Assert.Equal(4,PhysicsCacheSerializer.SchemaVersion);
        PhysicsCacheFrame Frame=new(4,[Body(7)],[],[new PhysicsBondBreak(7,2,LogicalScalar.FromInt64(9))],
            [new PhysicsBondStrain(9,1,LogicalScalar.One),new PhysicsBondStrain(7,5,LogicalScalar.FromInt64(3)),new PhysicsBondStrain(7,3,LogicalScalar.FromInt64(2))]);
        using MemoryStream Stream=new();
        PhysicsCacheSerializer.Write(Stream,Identity(),[Frame,new PhysicsCacheFrame(5,[Body(7)],[])]);
        Stream.Position=0;
        List<PhysicsCacheFrame>Frames=[];
        Assert.True(PhysicsCacheSerializer.TryRead(Stream,Identity(),Frames,out var Error),Error);
        Assert.Equal([new PhysicsBondStrain(7,3,LogicalScalar.FromInt64(2)),new PhysicsBondStrain(7,5,LogicalScalar.FromInt64(3)),new PhysicsBondStrain(9,1,LogicalScalar.One)],Frames[0].Strains);
        Assert.Equal([new PhysicsBondBreak(7,2,LogicalScalar.FromInt64(9))],Frames[0].Breaks);
        Assert.Empty(Frames[1].Strains);
    }
    [Fact]
    public void ABondRecordedTwiceAZeroOrBadStrainAndAStrainInAnOlderSchemaAreRefusedByTheWriter(){
        using MemoryStream Stream=new();
        using(PhysicsCacheStreamWriter Writer=new(Stream,Identity(),1)){
            Assert.Throws<ArgumentException>(()=>Writer.WriteFrame(new PhysicsCacheFrame(1,[],[],null,[new PhysicsBondStrain(3,1,LogicalScalar.One),new PhysicsBondStrain(3,1,LogicalScalar.FromInt64(2))])));
            Assert.Throws<ArgumentException>(()=>Writer.WriteFrame(new PhysicsCacheFrame(1,[],[],null,[new PhysicsBondStrain(3,1,LogicalScalar.Zero)])));
            Assert.Throws<ArgumentException>(()=>Writer.WriteFrame(new PhysicsCacheFrame(1,[],[],null,[new PhysicsBondStrain(0,1,LogicalScalar.One)])));
            Assert.Throws<ArgumentException>(()=>Writer.WriteFrame(new PhysicsCacheFrame(1,[],[],null,[new PhysicsBondStrain(3,-1,LogicalScalar.One)])));
        }
        using MemoryStream Old=new();
        using PhysicsCacheStreamWriter Three=new(Old,Identity(3),1);
        Assert.Throws<ArgumentException>(()=>Three.WriteFrame(new PhysicsCacheFrame(1,[],[],null,[new PhysicsBondStrain(3,1,LogicalScalar.One)])));
    }
    [Fact]
    public void ASchemaThreeCacheIsReadAndPlayedBackWithItsBreaksAndNoInternalStrains(){
        using MemoryStream Old=new();
        PhysicsCacheSerializer.Write(Old,Identity(3),[new PhysicsCacheFrame(1,[Body(1)],[],[new PhysicsBondBreak(1,4,LogicalScalar.One)]),new PhysicsCacheFrame(2,[Body(1)],[])]);
        var Bytes=Old.ToArray();
        List<PhysicsCacheFrame>Frames=[];
        using(MemoryStream Input=new(Bytes)){ Assert.True(PhysicsCacheSerializer.TryRead(Input,Identity(),Frames,out var Error),Error); }
        Assert.Equal([new PhysicsBondBreak(1,4,LogicalScalar.One)],Frames[0].Breaks);
        Assert.All(Frames,static Frame=>Assert.Empty(Frame.Strains));
        // スキーマ4では各フレームにカウントを追加する（4バイト）。
        using MemoryStream New=new();
        PhysicsCacheSerializer.Write(New,Identity(),Frames);
        Assert.Equal(Bytes.Length+2*4,New.Length);
        using(MemoryStream Input=new(Bytes)){
            Assert.True(PhysicsCachePlayer.TryLoad(Input,Identity(),false,out var Player,out var LoadError),LoadError);
            DeterministicPhysicsWorld World=new();
            World.Upsert(new PhysicsBodyDescription(new Entity(1,1),1,new Collider(),new RigidBody(),LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero));
            Assert.True(Player!.TryApply(1,World));
            Assert.Single(World.BondBreaks);
            Assert.Empty(World.BondStrains);
        }
    }
    [Fact]
    public void AReportedInternalStrainIsARecordUntilTheNextTickAndAPlayedBackFrameRestoresIt(){
        DeterministicPhysicsWorld World=new();
        World.Upsert(PhysicsBodyDescription.CreateDynamicBox(new Entity(5,1),new LogicalVector3(0,10,0),new LogicalVector3(1,1,1),LogicalScalar.One));
        World.Step(0);
        World.ReportBondStrain(6,3,LogicalScalar.FromInt64(2));
        Assert.Equal([new PhysicsBondStrain(6,3,LogicalScalar.FromInt64(2))],World.BondStrains);
        Assert.DoesNotContain(World.Events,static Event=>Event.Type==PhysicsEventType.BondBroken);
        Assert.Throws<ArgumentOutOfRangeException>(()=>World.ReportBondStrain(6,3,LogicalScalar.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(()=>World.ReportBondStrain(0,3,LogicalScalar.One));
        List<PhysicsBodyState>States=[];
        World.CollectBodyStates(States);
        using MemoryStream Stream=new();
        PhysicsCacheSerializer.Write(Stream,Identity(),[new PhysicsCacheFrame(1,[.. States],[],null,World.BondStrains)]);
        World.Step(1);
        Assert.Empty(World.BondStrains);
        Stream.Position=0;
        Assert.True(PhysicsCachePlayer.TryLoad(Stream,Identity(),false,out var Player,out var Error),Error);
        Assert.True(Player!.TryApply(1,World));
        Assert.Equal([new PhysicsBondStrain(6,3,LogicalScalar.FromInt64(2))],World.BondStrains);
    }
}
