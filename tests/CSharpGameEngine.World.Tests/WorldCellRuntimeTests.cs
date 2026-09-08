using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Numerics;
using System.Text;
namespace CSharpGameEngine.World.Tests;
public sealed class WorldCellRuntimeTests {
    private static readonly StableId Namespace=StableId.Parse("9ac22bf4-1d3d-5387-b82b-a4ad96ec846f");
    private static readonly ReferenceFrameId Root=FrameId("root");
    private static readonly ReferenceFrameId Planet=FrameId("planet");
    [Fact]
    public void ManifestRejectsMissingDuplicateAndCyclicDependencies(){
        var A=CellId("a");
        var B=CellId("b");
        Assert.False(WorldPartitionManifest.TryCreate([Cell(A,Dependencies:[B])],out _,out var Missing));
        Assert.Equal(WorldPartitionErrorCode.MissingDependency,Missing.Code);
        Assert.False(WorldPartitionManifest.TryCreate([Cell(A),Cell(A)],out _,out var Duplicate));
        Assert.Equal(WorldPartitionErrorCode.DuplicateCell,Duplicate.Code);
        Assert.False(WorldPartitionManifest.TryCreate([Cell(A,Dependencies:[B]),Cell(B,Dependencies:[A])],out _,out var Cycle));
        Assert.Equal(WorldPartitionErrorCode.DependencyCycle,Cycle.Code);
    }
    [Fact]
    public void ManifestCopiesInputAndProducesDependenciesFirstActivationOrder(){
        var A=CellId("a");
        var B=CellId("b");
        var C=CellId("c");
        var Dependencies=new List<WorldCellId>{B};
        Assert.True(WorldPartitionManifest.TryCreate([Cell(A,Dependencies:Dependencies),Cell(B,Dependencies:[C]),Cell(C)],out var Manifest,out var Error),Error.Message);
        Dependencies.Clear();
        Assert.True(Manifest.TryGetActivationOrder(A,out var Order));
        Assert.Equal(new[]{C,B,A},Order.ToArray());
        Assert.Equal(new[]{B},Manifest[A].Dependencies.ToArray());
    }
    [Fact]
    public void ActivationPublishesOnlyAtCommitAndUsesOneAtomicActiveSet(){
        var Dependency=CellId("dependency");
        var RootCell=CellId("root-cell");
        var Runtime=CreateRuntime(Cell(Dependency),Cell(RootCell,Dependencies:[Dependency]));
        Load(Runtime,Dependency);
        Load(Runtime,RootCell);
        var Before=Runtime.Current;
        var Frames=CreateFrames();
        Assert.True(Runtime.TryBeginTransition([RootCell,Dependency],Frames,out var Transition,out var Error),Error.Message);
        Assert.Equal(WorldCellState.Activating,Runtime.GetStatus(Dependency).State);
        Assert.Equal(WorldCellState.Activating,Runtime.GetStatus(RootCell).State);
        Assert.Same(Before,Runtime.Current);
        Assert.Empty(Runtime.Current.ActiveCells);
        Assert.Equal(new[]{Dependency,RootCell},Transition.ToActivate.ToArray());
        Assert.True(Runtime.TryCommitTransition(Transition,Frames,out var Active,out Error),Error.Message);
        Assert.True(Active.Contains(Dependency));
        Assert.True(Active.Contains(RootCell));
        Assert.Equal(WorldCellState.Active,Runtime.GetStatus(Dependency).State);
        Assert.Equal(WorldCellState.Active,Runtime.GetStatus(RootCell).State);
    }
    [Fact]
    public void DependencyOmissionAndUnavailableDependencyCannotPartiallyActivate(){
        var Dependency=CellId("dependency");
        var RootCell=CellId("root-cell");
        var Runtime=CreateRuntime(Cell(Dependency),Cell(RootCell,Dependencies:[Dependency]));
        Load(Runtime,RootCell);
        var Before=Runtime.Current;
        Assert.False(Runtime.TryBeginTransition([RootCell],CreateFrames(),out _,out var Missing));
        Assert.Equal(WorldCellOperationErrorCode.DependencyNotDesired,Missing.Code);
        Assert.False(Runtime.TryBeginTransition([Dependency,RootCell],CreateFrames(),out _,out var Unavailable));
        Assert.Equal(WorldCellOperationErrorCode.CellNotLoaded,Unavailable.Code);
        Assert.Same(Before,Runtime.Current);
        Assert.Equal(WorldCellState.Loaded,Runtime.GetStatus(RootCell).State);
        Assert.Equal(WorldCellState.Unloaded,Runtime.GetStatus(Dependency).State);
    }
    [Fact]
    public void FailedActivationPreservesLastKnownGoodAndRequiresExplicitFaultReset(){
        var Existing=CellId("existing");
        var Added=CellId("added");
        var Runtime=CreateRuntime(Cell(Existing),Cell(Added,Dependencies:[Existing]));
        var Frames=CreateFrames();
        Load(Runtime,Existing);
        Activate(Runtime,Frames,[Existing]);
        var Good=Runtime.Current;
        Load(Runtime,Added);
        Assert.True(Runtime.TryBeginTransition([Existing,Added],Frames,out var Transition,out var Error),Error.Message);
        Assert.True(Runtime.TryFailTransition(Transition,WorldCellFaultCode.ActivationFailed,"injected failure",out Error),Error.Message);
        Assert.Same(Good,Runtime.Current);
        Assert.True(Runtime.Current.Contains(Existing));
        Assert.False(Runtime.Current.Contains(Added));
        Assert.Equal(WorldCellState.Active,Runtime.GetStatus(Existing).State);
        Assert.Equal(WorldCellState.Faulted,Runtime.GetStatus(Added).State);
        Assert.False(Runtime.TryBeginLoad(Added,out var NoAutomaticRetry));
        Assert.Equal(WorldCellOperationErrorCode.InvalidState,NoAutomaticRetry.Code);
        Assert.True(Runtime.TryResetFault(Added,out Error),Error.Message);
        Assert.Equal(WorldCellState.Unloaded,Runtime.GetStatus(Added).State);
    }
    [Fact]
    public void DeactivationRemainsPublishedUntilBarrierThenUnloadsInOrder(){
        var Dependency=CellId("dependency");
        var Dependent=CellId("dependent");
        var Runtime=CreateRuntime(Cell(Dependency),Cell(Dependent,Dependencies:[Dependency]));
        var Frames=CreateFrames();
        Load(Runtime,Dependency);
        Load(Runtime,Dependent);
        Activate(Runtime,Frames,[Dependency,Dependent]);
        var BothActive=Runtime.Current;
        Assert.False(Runtime.TryBeginTransition([Dependent],Frames,out _,out var Invalid));
        Assert.Equal(WorldCellOperationErrorCode.DependencyNotDesired,Invalid.Code);
        Assert.Same(BothActive,Runtime.Current);
        Assert.True(Runtime.TryBeginTransition([Dependency],Frames,out var Transition,out var Error),Error.Message);
        Assert.Equal(new[]{Dependent},Transition.ToDeactivate.ToArray());
        Assert.Equal(WorldCellState.Deactivating,Runtime.GetStatus(Dependent).State);
        Assert.True(Runtime.Current.Contains(Dependent));
        Assert.True(Runtime.TryCommitTransition(Transition,Frames,out var DependencyOnly,out Error),Error.Message);
        Assert.False(DependencyOnly.Contains(Dependent));
        Assert.Equal(WorldCellState.Unloading,Runtime.GetStatus(Dependent).State);
        Assert.True(Runtime.TryCompleteUnload(Dependent,out Error),Error.Message);
        Assert.Equal(WorldCellState.Unloaded,Runtime.GetStatus(Dependent).State);
    }
    [Fact]
    public void FrameDisappearingBeforeCommitFaultsNewCellWithoutPublishingIt(){
        var CellId=WorldCellRuntimeTests.CellId("cell");
        var Runtime=CreateRuntime(Cell(CellId));
        var Graph=new ReferenceFrameGraph(Root);
        Graph.Add(Planet,Root,FramePose.Identity);
        Assert.True(Graph.TryCommit(1,out var WithPlanet,out var FrameError),FrameError.Message);
        Load(Runtime,CellId);
        Assert.True(Runtime.TryBeginTransition([CellId],WithPlanet,out var Transition,out var Error),Error.Message);
        var Good=Runtime.Current;
        Graph.Remove(Planet);
        Assert.True(Graph.TryCommit(2,out var WithoutPlanet,out FrameError),FrameError.Message);
        Assert.False(Runtime.TryCommitTransition(Transition,WithoutPlanet,out var Result,out var MissingFrame));
        Assert.Equal(WorldCellOperationErrorCode.MissingReferenceFrame,MissingFrame.Code);
        Assert.Same(Good,Result);
        Assert.Same(Good,Runtime.Current);
        Assert.Equal(WorldCellState.Faulted,Runtime.GetStatus(CellId).State);
    }
    [Fact]
    public void StaleFrameSnapshotCannotCommitAndPendingTransitionCanUseCurrentSnapshot(){
        var Id=CellId("cell");
        var Runtime=CreateRuntime(Cell(Id));
        var Graph=new ReferenceFrameGraph(Root);
        Graph.Add(Planet,Root,FramePose.Identity);
        Assert.True(Graph.TryCommit(1,out var OldFrames,out var FrameError),FrameError.Message);
        Graph.SetPose(Planet,new FramePose(new LogicalVector3(1,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Assert.True(Graph.TryCommit(2,out var CurrentFrames,out FrameError),FrameError.Message);
        Load(Runtime,Id);
        Assert.True(Runtime.TryBeginTransition([Id],CurrentFrames,out var Transition,out var Error),Error.Message);
        var Good=Runtime.Current;
        Assert.False(Runtime.TryCommitTransition(Transition,OldFrames,out var Result,out var Stale));
        Assert.Equal(WorldCellOperationErrorCode.StaleFrameSnapshot,Stale.Code);
        Assert.Same(Good,Result);
        Assert.True(Runtime.HasPendingTransition);
        Assert.True(Runtime.TryCommitTransition(Transition,CurrentFrames,out var Active,out Error),Error.Message);
        Assert.True(Active.Contains(Id));
        Assert.Equal(2ul,Active.Tick);
    }
    [Fact]
    public void LoadedPrefetchCanBeDiscardedWithoutPublishingIt(){
        var Id=CellId("cell");
        var Runtime=CreateRuntime(Cell(Id));
        Load(Runtime,Id);
        var Good=Runtime.Current;
        Assert.True(Runtime.TryBeginUnload(Id,out var Error),Error.Message);
        Assert.Equal(WorldCellState.Unloading,Runtime.GetStatus(Id).State);
        Assert.Same(Good,Runtime.Current);
        Assert.True(Runtime.TryCompleteUnload(Id,out Error),Error.Message);
        Assert.Equal(WorldCellState.Unloaded,Runtime.GetStatus(Id).State);
        Assert.Same(Good,Runtime.Current);
    }
    [Theory]
    [InlineData(WorldCellFaultCode.Cancelled)]
    [InlineData(WorldCellFaultCode.CorruptArtifact)]
    public void LoadFailureNeverChangesActiveSetAndEndsInFaulted(WorldCellFaultCode Code){
        var Id=CellId("cell");
        var Runtime=CreateRuntime(Cell(Id));
        var Good=Runtime.Current;
        Assert.True(Runtime.TryBeginLoad(Id,out var Error),Error.Message);
        Assert.True(Runtime.TryFailLoad(Id,Code,"injected",out Error),Error.Message);
        Assert.Same(Good,Runtime.Current);
        Assert.Equal(WorldCellState.Faulted,Runtime.GetStatus(Id).State);
        Assert.Equal(Code,Runtime.GetStatus(Id).Fault.Code);
    }
    private static WorldCellRuntime CreateRuntime(params WorldCellDefinition[]Cells){
        Assert.True(WorldPartitionManifest.TryCreate(Cells,out var Manifest,out var Error),Error.Message);
        return new WorldCellRuntime(Manifest);
    }
    private static WorldCellDefinition Cell(WorldCellId Id,IEnumerable<WorldCellId>?Dependencies=null){
        AssetKey Payload=new(new AssetId(StableId.FromName(Namespace,"payload:"+Id)),null);
        AssetArtifactReference Reference=new(Payload,Hash256.Compute(Encoding.UTF8.GetBytes("payload:"+Id)));
        return new WorldCellDefinition(
            Id,Planet,new FrameLocalBounds(LogicalVector3.Zero,new LogicalVector3(100,100,100)),
            Payload,0,Dependencies,[Reference]);
    }
    private static void Load(WorldCellRuntime Runtime,WorldCellId Cell){
        Assert.True(Runtime.TryBeginLoad(Cell,out var Error),Error.Message);
        Assert.True(Runtime.TryCompleteLoad(Cell,out Error),Error.Message);
    }
    private static void Activate(WorldCellRuntime Runtime,ReferenceFrameSnapshot Frames,IEnumerable<WorldCellId>Cells){
        Assert.True(Runtime.TryBeginTransition(Cells,Frames,out var Transition,out var Error),Error.Message);
        Assert.True(Runtime.TryCommitTransition(Transition,Frames,out _,out Error),Error.Message);
    }
    private static ReferenceFrameSnapshot CreateFrames(){
        var Graph=new ReferenceFrameGraph(Root);
        Graph.Add(Planet,Root,FramePose.Identity);
        Assert.True(Graph.TryCommit(1,out var Snapshot,out var Error),Error.Message);
        return Snapshot;
    }
    private static ReferenceFrameId FrameId(string Name)=>new(StableId.FromName(Namespace,Name));
    private static WorldCellId CellId(string Name)=>new(StableId.FromName(Namespace,Name));
}
