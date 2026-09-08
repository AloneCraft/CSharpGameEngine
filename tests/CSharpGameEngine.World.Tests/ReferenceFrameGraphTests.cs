using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.World.Tests;
public sealed class ReferenceFrameGraphTests {
    private static readonly ReferenceFrameId Root=Id("root");
    [Fact]
    public void SnapshotPublishesOnlyAtCommitBoundaryAndRemainsImmutable(){
        var Graph=new ReferenceFrameGraph(Root);
        var Child=Id("child");
        Graph.Add(Child,Root,new FramePose(new LogicalVector3(10,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Assert.False(Graph.Current.Contains(Child));
        Assert.True(Graph.TryCommit(1,out var First,out var Error),Error.Message);
        Assert.True(First.Contains(Child));
        Graph.SetPose(Child,new FramePose(new LogicalVector3(20,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Assert.True(Graph.TryCommit(2,out var Second,out Error),Error.Message);
        Assert.Equal(new LogicalVector3(10,0,0),First.GetPose(Child).Position);
        Assert.Equal(new LogicalVector3(20,0,0),Second.GetPose(Child).Position);
        Assert.Equal(1ul,First.Tick);
        Assert.Equal(2ul,Second.Tick);
    }
    [Fact]
    public void FailedCommitDoesNotChangeTheLastKnownGoodSnapshot(){
        var Graph=new ReferenceFrameGraph(Root);
        var Child=Id("child");
        Graph.Add(Child,Root,FramePose.Identity);
        Assert.True(Graph.TryCommit(1,out var Good,out _));
        Graph.Reparent(Child,Id("missing"));
        Assert.False(Graph.TryCommit(2,out var Result,out var Error));
        Assert.Equal(ReferenceFrameErrorCode.MissingParent,Error.Code);
        Assert.Same(Good,Result);
        Assert.Same(Good,Graph.Current);
        Assert.Equal(1ul,Graph.Current.Tick);
    }
    [Fact]
    public void CycleAndMultipleParentAssignmentAreRejected(){
        var Graph=new ReferenceFrameGraph(Root);
        var First=Id("first");
        var Second=Id("second");
        Graph.Add(First,Root,FramePose.Identity);
        Graph.Add(Second,First,FramePose.Identity);
        Assert.True(Graph.TryCommit(1,out _,out _));
        Graph.Reparent(First,Second);
        Assert.False(Graph.TryCommit(2,out _,out var Cycle));
        Assert.Equal(ReferenceFrameErrorCode.Cycle,Cycle.Code);
        Graph.Reparent(Second,Root);
        Graph.Reparent(Second,First);
        Assert.False(Graph.TryCommit(3,out _,out var MultipleParent));
        Assert.Equal(ReferenceFrameErrorCode.MultipleParentAssignment,MultipleParent.Code);
    }
    [Fact]
    public void DepthThirtyTwoIsAllowedAndThirtyThreeIsRejectedAtomically(){
        var Graph=new ReferenceFrameGraph(Root);
        var Parent=Root;
        for(var Depth=1;Depth<=ReferenceFrameGraph.MaximumDepth;Depth++){
            var Child=Id($"depth-{Depth}");
            Graph.Add(Child,Parent,FramePose.Identity);
            Parent=Child;
        }
        Assert.True(Graph.TryCommit(1,out var AtLimit,out var Error),Error.Message);
        var TooDeep=Id("too-deep");
        Graph.Add(TooDeep,Parent,FramePose.Identity);
        Assert.False(Graph.TryCommit(2,out var Result,out Error));
        Assert.Equal(ReferenceFrameErrorCode.DepthExceeded,Error.Code);
        Assert.Same(AtLimit,Result);
        Assert.False(Result.Contains(TooDeep));
    }
    [Fact]
    public void ParentWithChildrenAndInvalidPoseCannotBeRemovedOrPublished(){
        var Graph=new ReferenceFrameGraph(Root);
        var Parent=Id("parent");
        var Child=Id("child");
        Graph.Add(Parent,Root,FramePose.Identity);
        Graph.Add(Child,Parent,FramePose.Identity);
        Assert.True(Graph.TryCommit(1,out _,out _));
        Graph.Remove(Parent);
        Assert.False(Graph.TryCommit(2,out _,out var HasChildren));
        Assert.Equal(ReferenceFrameErrorCode.HasChildren,HasChildren.Code);
        Graph.SetPose(Child,new FramePose(LogicalVector3.Zero,LogicalQuaternion.Identity,LogicalVector3.Zero));
        Assert.False(Graph.TryCommit(3,out _,out var InvalidPose));
        Assert.Equal(ReferenceFrameErrorCode.InvalidPose,InvalidPose.Code);
    }
    [Fact]
    public void CrossFrameRoundTripPreservesFrameLocalPoint(){
        var Graph=new ReferenceFrameGraph(Root);
        var Planet=Id("planet");
        var Ship=Id("ship");
        Graph.Add(Planet,Root,new FramePose(new LogicalVector3(1000,2000,3000),LogicalQuaternion.Identity,LogicalVector3.One));
        Graph.Add(Ship,Planet,new FramePose(new LogicalVector3(50,25,-10),LogicalQuaternion.Identity,LogicalVector3.One));
        Assert.True(Graph.TryCommit(1,out var Snapshot,out var Error),Error.Message);
        var Local=new LogicalVector3(3,4,5);
        Assert.True(Snapshot.TryTransformPoint(Local,Ship,Root,out var RootPoint));
        Assert.Equal(new LogicalVector3(1053,2029,2995),RootPoint);
        Assert.True(Snapshot.TryTransformPoint(RootPoint,Root,Ship,out var RoundTrip));
        Assert.Equal(Local,RoundTrip);
    }
    [Fact]
    public void RotationScaleAndTranslationAreAppliedWithoutGlobalFloatCoordinates(){
        var Graph=new ReferenceFrameGraph(Root);
        var Rotated=Id("rotated");
        var HalfTurnAroundF=new LogicalQuaternion(0,0,1,0);
        Graph.Add(Rotated,Root,new FramePose(new LogicalVector3(10,20,30),HalfTurnAroundF,new LogicalVector3(2,3,4)));
        Assert.True(Graph.TryCommit(1,out var Snapshot,out var Error),Error.Message);
        Assert.True(Snapshot.TryTransformPoint(new LogicalVector3(1,2,3),Rotated,Root,out var RootPoint));
        Assert.Equal(new LogicalVector3(8,14,42),RootPoint);
        Assert.True(Snapshot.TryTransformPoint(RootPoint,Root,Rotated,out var LocalPoint));
        Assert.Equal(new LogicalVector3(1,2,3),LocalPoint);
    }
    [Fact]
    public void SiblingConversionStopsAtLeastCommonAncestorBeforeRemoteRootOffsetCanOverflow(){
        var Graph=new ReferenceFrameGraph(Root);
        var RemoteSystem=Id("remote-system");
        var Source=Id("source");
        var Target=Id("target");
        Graph.Add(RemoteSystem,Root,new FramePose(new LogicalVector3(LogicalScalar.MaxValue-1,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Graph.Add(Source,RemoteSystem,new FramePose(new LogicalVector3(10,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Graph.Add(Target,RemoteSystem,new FramePose(new LogicalVector3(12,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Assert.True(Graph.TryCommit(1,out var Snapshot,out var Error),Error.Message);
        Assert.True(Snapshot.TryTransformPoint(new LogicalVector3(5,0,0),Source,Target,out var Relative));
        Assert.Equal(new LogicalVector3(3,0,0),Relative);
        Assert.False(Snapshot.TryTransformPoint(new LogicalVector3(5,0,0),Source,Root,out _));
    }
    [Fact]
    public void CrossFrameAffineRetainsGeneralBasisAndMatchesPointConversion(){
        var Graph=new ReferenceFrameGraph(Root);
        var Planet=Id("affine-planet");
        var Ship=Id("affine-ship");
        var Camera=Id("affine-camera");
        var QuarterTurnAroundU=LogicalQuaternion.FromEulerRuf(new LogicalVector3(0,LogicalMath.HalfPi,0));
        Graph.Add(Planet,Root,new FramePose(new LogicalVector3(LogicalScalar.MaxValue-1,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Graph.Add(Ship,Planet,new FramePose(new LogicalVector3(20,3,4),QuarterTurnAroundU,new LogicalVector3(2,3,4)));
        Graph.Add(Camera,Planet,new FramePose(new LogicalVector3(25,5,6),LogicalQuaternion.Identity,LogicalVector3.One));
        Assert.True(Graph.TryCommit(1,out var Snapshot,out var Error),Error.Message);
        var ObjectRotation=LogicalQuaternion.FromEulerRuf(new LogicalVector3(LogicalMath.HalfPi,0,0));
        Assert.True(LogicalAffineTransform.TryCreateTrs(new LogicalVector3(7,8,9),ObjectRotation,new LogicalVector3(3,2,1),out var Local));
        Assert.True(Snapshot.TryTransformAffine(Local,Ship,Camera,out var Relative));
        var Vertex=new LogicalVector3(1,2,3);
        Assert.True(Local.TryTransformPoint(Vertex,out var SourcePoint));
        Assert.True(Snapshot.TryTransformPoint(SourcePoint,Ship,Camera,out var Expected));
        Assert.True(Relative.TryTransformPoint(Vertex,out var Actual));
        AssertVectorNear(Expected,Actual,32);
        Assert.False(Snapshot.TryTransformAffine(Local,Ship,Root,out _));
    }
    [Fact]
    public void RootMutationAndNonMonotonicTicksAreRejected(){
        var Graph=new ReferenceFrameGraph(Root);
        Graph.SetPose(Root,new FramePose(new LogicalVector3(1,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Assert.False(Graph.TryCommit(1,out _,out var RootMutation));
        Assert.Equal(ReferenceFrameErrorCode.RootMutation,RootMutation.Code);
        var Child=Id("child");
        Graph.Add(Child,Root,FramePose.Identity);
        Assert.True(Graph.TryCommit(2,out var Good,out _));
        Graph.SetPose(Child,new FramePose(new LogicalVector3(2,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Assert.False(Graph.TryCommit(2,out var SameTick,out var StaleTick));
        Assert.Equal(ReferenceFrameErrorCode.StaleTick,StaleTick.Code);
        Assert.Same(Good,SameTick);
        Assert.Equal(FramePose.Identity,Good.GetPose(Child));
    }
    private static ReferenceFrameId Id(string Name)=>new(StableId.FromName(StableId.Parse("d510315d-7e0f-5578-a56d-65481a53c4cb"),Name));
    private static void AssertVectorNear(LogicalVector3 Expected,LogicalVector3 Actual,Int128 RawTolerance){
        Assert.InRange(Int128.Abs(Expected.R.Raw-Actual.R.Raw),0,RawTolerance);
        Assert.InRange(Int128.Abs(Expected.U.Raw-Actual.U.Raw),0,RawTolerance);
        Assert.InRange(Int128.Abs(Expected.F.Raw-Actual.F.Raw),0,RawTolerance);
    }
}
