using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Simulation;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class PhysicsBakeTests {
    [Fact]
    public void CookingKey_ChangesForEveryVersionedInput(){
        PhysicsCookingInput Baseline=new(new byte[]{1,2,3},"asset-guid","ruf-meters-v1","box","1,1,1","7","cooker-1","schema-1","q32.32");
        string First=PhysicsCookingKey.Compute(Baseline);
        string Same=PhysicsCookingKey.Compute(Baseline);
        string Changed=PhysicsCookingKey.Compute(Baseline with { Seed="8" });
        Assert.Equal(64,First.Length);
        Assert.Equal(First,Same);
        Assert.NotEqual(First,Changed);
    }

    [Fact]
    public void PhysicsCache_RoundTripsFullStateAndRejectsMismatchedIdentity(){
        SimulationCacheIdentity Identity=new("physics","Logical256",60,"abc123",PhysicsCacheSerializer.SchemaVersion);
        PhysicsBodyState Body=new(
            new Entity(9),42,PhysicsMotionType.Dynamic,new PhysicsVector3(1,2,3),PhysicsQuaternion.Identity,
            new PhysicsVelocity { Linear=new PhysicsVector3(4,5,6) },true,60);
        PhysicsEvent Event=new(7,99,PhysicsEventType.Sleep,new Entity(9),default,default,default);
        PhysicsCacheFrame Frame=new(7,[Body],[Event]);
        using MemoryStream Stream=new();
        PhysicsCacheSerializer.Write(Stream,Identity,[Frame]);

        Stream.Position=0;
        List<PhysicsCacheFrame> Frames=[];
        Assert.True(PhysicsCacheSerializer.TryRead(Stream,Identity,Frames,out string Error),Error);
        Assert.Single(Frames);
        Assert.Equal(Body,Assert.Single(Frames[0].Bodies));
        Assert.Equal(Event,Assert.Single(Frames[0].Events));

        Stream.Position=0;
        Assert.False(PhysicsCacheSerializer.TryRead(Stream,Identity with { DependencyHash="different" },[],out string Mismatch));
        Assert.Contains("does not match",Mismatch,StringComparison.Ordinal);
    }

    [Fact]
    public void Prefracture_ResolvesBondsInStableIndexOrder(){
        PrefractureAsset Asset=new(){
            SourceHash="hash",Seed=17,
            Fragments=[new(0,"a",default,1),new(1,"b",default,1),new(2,"c",default,1)],
            Bonds=[new(0,1,2),new(1,2,3)],
        };
        Assert.True(Asset.IsDeterministicallyOrdered());
        PrefractureRuntime Runtime=new();
        Runtime.Accumulate(5,1,4);
        Runtime.Accumulate(5,0,2);
        List<FractureBreak> Breaks=[];
        Assert.Equal(2,Runtime.Resolve(5,Asset,Breaks));
        Assert.Equal([0,1],Breaks.Select(Break=>Break.BondIndex));
    }
}
