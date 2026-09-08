using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.World.Tests;
public sealed class KeplerFramePoseProviderTests {
    private const ulong PeriodTicks=4096;
    private static readonly ReferenceFrameId Root=Id("root");
    private static readonly ReferenceFrameId Body=Id("body");
    [Fact]
    public void CircularOrbitHitsQuarterTurnsAndClosesOnePeriod(){
        var Provider=CreateProvider(Eccentricity:LogicalScalar.Zero,SemiMajorAxis:1000);
        Assert.True(Provider.TryEvaluate(ReferenceFrameTime.Fixed(0),out var Start,out var Error),Error.Message);
        Assert.True(Provider.TryEvaluate(ReferenceFrameTime.Fixed(PeriodTicks/4),out var Quarter,out Error),Error.Message);
        Assert.True(Provider.TryEvaluate(ReferenceFrameTime.Fixed(PeriodTicks),out var Closed,out Error),Error.Message);
        AssertPosition(Start.Pose.Position,1000,0,0,0.0001);
        AssertPosition(Quarter.Pose.Position,0,0,1000,0.0001);
        AssertVectorNear(Start.Pose.Position,Closed.Pose.Position,0.0001);
    }
    [Fact]
    public void EccentricInclinedOrbitIsRepeatableAndPreservesSpecificOrbitalEnergy(){
        var Provider=CreateProvider(
            Eccentricity:LogicalScalar.ParseInvariant("0.7"),
            SemiMajorAxis:1_000_000,
            Inclination:LogicalScalar.ParseInvariant("0.4"),
            AscendingNode:LogicalScalar.ParseInvariant("0.2"),
            Periapsis:LogicalScalar.ParseInvariant("0.3"));
        var Time=ReferenceFrameTime.Fixed(1733);
        Assert.True(Provider.TryEvaluate(Time,out var First,out var Error),Error.Message);
        Assert.True(Provider.TryEvaluate(Time,out var Repeated,out Error),Error.Message);
        Assert.Equal(First,Repeated);
        Assert.True(Provider.TryEvaluate(ReferenceFrameTime.Fixed(1733+PeriodTicks),out var NextPeriod,out Error),Error.Message);
        AssertVectorNear(First.Pose.Position,NextPeriod.Pose.Position,0.001);
        var Position=ToTuple(First.Pose.Position);
        var Velocity=ToTuple(First.Velocity);
        var Radius=Math.Sqrt(Position.R*Position.R+Position.U*Position.U+Position.F*Position.F);
        var SpeedSquared=Velocity.R*Velocity.R+Velocity.U*Velocity.U+Velocity.F*Velocity.F;
        var SemiMajorAxis=ToDouble(Provider.Orbit.SemiMajorAxis);
        var MeanMotion=ToDouble(Provider.Orbit.MeanMotionPerTick);
        var Mu=MeanMotion*MeanMotion*SemiMajorAxis*SemiMajorAxis*SemiMajorAxis;
        var SpecificEnergy=SpeedSquared/2-Mu/Radius;
        Assert.InRange(Math.Abs(SpecificEnergy-(-Mu/(2*SemiMajorAxis))),0,Math.Abs(SpecificEnergy)*2e-10);
    }
    [Fact]
    public void HighEccentricityLargeTickAndDomainTimeScaleRemainDeterministic(){
        var Provider=CreateProvider(LogicalScalar.ParseInvariant("0.95"),10_000);
        var Time=new ReferenceFrameTime(ulong.MaxValue,LogicalScalar.ParseInvariant("0.25"));
        Assert.True(Provider.TryEvaluate(Time,out var First,out var Error),Error.Message);
        Assert.True(Provider.TryEvaluate(Time,out var Second,out Error),Error.Message);
        Assert.Equal(First,Second);
        Assert.True(Provider.TryEvaluate(new ReferenceFrameTime(123,LogicalScalar.Zero),out var FrozenA,out Error),Error.Message);
        Assert.True(Provider.TryEvaluate(new ReferenceFrameTime(456,LogicalScalar.Zero),out var FrozenB,out Error),Error.Message);
        Assert.Equal(FrozenA,FrozenB);
        Assert.Equal(LogicalVector3.Zero,FrozenA.Velocity);
    }
    [Fact]
    public void ProviderQueuesOnlyAFramePoseCommandForTheCommitBoundary(){
        var Graph=new ReferenceFrameGraph(Root);
        Graph.Add(Body,Root,FramePose.Identity);
        Assert.True(Graph.TryCommit(1,out var Initial,out var FrameError),FrameError.Message);
        var Provider=CreateProvider(LogicalScalar.ParseInvariant("0.2"),1000);
        Assert.True(Provider.TryEnqueue(Graph,ReferenceFrameTime.Fixed(2),out var Expected,out var Error),Error.Message);
        Assert.Equal(FramePose.Identity,Initial.GetPose(Body));
        Assert.True(Graph.TryCommit(2,out var Published,out FrameError),FrameError.Message);
        Assert.Equal(Expected.Pose,Published.GetPose(Body));
    }
    [Fact]
    public void InvalidElementsAndMissingTargetFrameFailExplicitly(){
        var Invalid=new KeplerOrbit(0,LogicalScalar.Zero,0,0,0,0,LogicalScalar.One,0);
        Assert.Throws<ArgumentException>(()=>new KeplerFramePoseProvider(Body,Invalid));
        var Provider=CreateProvider(LogicalScalar.Zero,1000);
        var Graph=new ReferenceFrameGraph(Root);
        Assert.False(Provider.TryEnqueue(Graph,ReferenceFrameTime.Fixed(1),out _,out var Error));
        Assert.Equal(KeplerPropagationErrorCode.MissingFrame,Error.Code);
    }
    [Fact]
    public void PropagationHotPathDoesNotAllocateAfterInitialization(){
        var Provider=CreateProvider(LogicalScalar.ParseInvariant("0.6"),1000);
        for(ulong Tick=0;Tick<100;Tick++){ Assert.True(Provider.TryEvaluate(ReferenceFrameTime.Fixed(Tick),out _,out _)); }
        var Before=GC.GetAllocatedBytesForCurrentThread();
        for(ulong Tick=0;Tick<1000;Tick++){ Assert.True(Provider.TryEvaluate(ReferenceFrameTime.Fixed(Tick),out _,out _)); }
        var After=GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(Before,After);
    }
    private static KeplerFramePoseProvider CreateProvider(LogicalScalar Eccentricity,LogicalScalar SemiMajorAxis,LogicalScalar Inclination=default,LogicalScalar AscendingNode=default,LogicalScalar Periapsis=default){
        var MeanMotion=LogicalMath.TwoPi/LogicalScalar.FromInt64((long)PeriodTicks);
        return new KeplerFramePoseProvider(Body,new KeplerOrbit(SemiMajorAxis,Eccentricity,Inclination,AscendingNode,Periapsis,LogicalScalar.Zero,MeanMotion,0));
    }
    private static void AssertPosition(LogicalVector3 Actual,double R,double U,double F,double Tolerance){
        var Value=ToTuple(Actual);
        Assert.InRange(Math.Abs(Value.R-R),0,Tolerance);
        Assert.InRange(Math.Abs(Value.U-U),0,Tolerance);
        Assert.InRange(Math.Abs(Value.F-F),0,Tolerance);
    }
    private static void AssertVectorNear(LogicalVector3 Left,LogicalVector3 Right,double Tolerance){
        var A=ToTuple(Left);
        var B=ToTuple(Right);
        Assert.InRange(Math.Abs(A.R-B.R),0,Tolerance);
        Assert.InRange(Math.Abs(A.U-B.U),0,Tolerance);
        Assert.InRange(Math.Abs(A.F-B.F),0,Tolerance);
    }
    private static (double R,double U,double F)ToTuple(LogicalVector3 Value)=>(ToDouble(Value.R),ToDouble(Value.U),ToDouble(Value.F));
    private static double ToDouble(LogicalScalar Value)=>double.CreateChecked(Value.Raw)/18446744073709551616d;
    private static ReferenceFrameId Id(string Name)=>new(StableId.FromName(StableId.Parse("e6af0fb1-dac2-5598-b2e5-488238411536"),Name));
}
