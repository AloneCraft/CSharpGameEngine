using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.World.Tests;
public sealed class WorldCellStreamingTests {
    private static readonly StableId Namespace=StableId.Parse("7b59a2b1-7315-55d6-84c6-857389761d62");
    private static readonly ReferenceFrameId Root=new(StableId.FromName(Namespace,"root"));
    [Fact]
    public async Task DependenciesLoadTopologicallyAndActiveSetChangesOnce(){
        var Fixture=CreateFixture();
        var Result=await Fixture.Service.RequestAsync([Fixture.Child],Fixture.Frames,WorldCellStreamingBudget.Unlimited);
        Assert.True(Result.Succeeded,Result.Error.Message);
        Assert.Equal(new[]{Fixture.Dependency,Fixture.Child},Result.Snapshot.ActiveCells.ToArray());
        Assert.Equal(new[]{Fixture.Dependency,Fixture.Child},Fixture.Sink.Activated.ToArray());
        Assert.Equal(1ul,Result.Snapshot.Version);
    }
    [Fact]
    public async Task CorruptionAndCancellationPreserveLastKnownGoodAndDoNotRetryFaults(){
        var Fixture=CreateFixture();
        Assert.True((await Fixture.Service.RequestAsync([Fixture.Dependency],Fixture.Frames,WorldCellStreamingBudget.Unlimited)).Succeeded);
        var Good=Fixture.Service.Current;
        Fixture.Source.Corrupt=Fixture.Child;
        var Corrupt=await Fixture.Service.RequestAsync([Fixture.Child],Fixture.Frames,WorldCellStreamingBudget.Unlimited);
        Assert.Equal(WorldCellStreamingErrorCode.CorruptArtifact,Corrupt.Error.Code);
        Assert.Same(Good,Corrupt.Snapshot);
        Assert.Equal(WorldCellState.Faulted,Fixture.Runtime.GetStatus(Fixture.Child).State);
        var Reads=Fixture.Source.ReadCount;
        var Retry=await Fixture.Service.RequestAsync([Fixture.Child],Fixture.Frames,WorldCellStreamingBudget.Unlimited);
        Assert.Equal(WorldCellStreamingErrorCode.FaultedCell,Retry.Error.Code);
        Assert.Equal(Reads,Fixture.Source.ReadCount);
        var CancelFixture=CreateFixture();
        CancelFixture.Source.Cancel=CancelFixture.Child;
        var Cancelled=await CancelFixture.Service.RequestAsync([CancelFixture.Child],CancelFixture.Frames,WorldCellStreamingBudget.Unlimited);
        Assert.Equal(WorldCellStreamingErrorCode.Cancelled,Cancelled.Error.Code);
        Assert.Empty(Cancelled.Snapshot.ActiveCells);
        Assert.Equal(WorldCellState.Faulted,CancelFixture.Runtime.GetStatus(CancelFixture.Child).State);
        Assert.Equal(WorldCellFaultCode.Cancelled,CancelFixture.Runtime.GetStatus(CancelFixture.Child).Fault.Code);
        Assert.Equal(WorldCellState.Unloaded,CancelFixture.Runtime.GetStatus(CancelFixture.Dependency).State);
    }
    [Fact]
    public async Task ActivationFailureAndBudgetExhaustionNeverReplaceActiveSnapshot(){
        var Fixture=CreateFixture();
        Assert.True((await Fixture.Service.RequestAsync([Fixture.Dependency],Fixture.Frames,WorldCellStreamingBudget.Unlimited)).Succeeded);
        var Good=Fixture.Service.Current;
        Fixture.Sink.Fail=true;
        var Activation=await Fixture.Service.RequestAsync([Fixture.Child],Fixture.Frames,WorldCellStreamingBudget.Unlimited);
        Assert.Equal(WorldCellStreamingErrorCode.ActivationFailed,Activation.Error.Code);
        Assert.Same(Good,Activation.Snapshot);
        Assert.Equal(WorldCellState.Active,Fixture.Runtime.GetStatus(Fixture.Dependency).State);
        Assert.Equal(WorldCellState.Faulted,Fixture.Runtime.GetStatus(Fixture.Child).State);
        var BudgetFixture=CreateFixture();
        var Budget=await BudgetFixture.Service.RequestAsync([BudgetFixture.Child],BudgetFixture.Frames,new WorldCellStreamingBudget(1,1));
        Assert.Equal(WorldCellStreamingErrorCode.BudgetExceeded,Budget.Error.Code);
        Assert.Empty(Budget.Snapshot.ActiveCells);
        Assert.Equal(0,BudgetFixture.Source.ReadCount);
    }
    [Fact]
    public async Task RapidChurnUnloadsInReverseDependencyOrderAndReleasesArtifacts(){
        var Fixture=CreateFixture();
        Assert.True((await Fixture.Service.RequestAsync([Fixture.Child],Fixture.Frames,WorldCellStreamingBudget.Unlimited)).Succeeded);
        var DependencyOnly=await Fixture.Service.RequestAsync([Fixture.Dependency],Fixture.Frames,WorldCellStreamingBudget.Unlimited);
        Assert.True(DependencyOnly.Succeeded,DependencyOnly.Error.Message);
        Assert.Equal(new[]{Fixture.Dependency},DependencyOnly.Snapshot.ActiveCells.ToArray());
        Assert.Equal(new[]{Fixture.Child},Fixture.Sink.Deactivated.ToArray());
        Assert.Equal(WorldCellState.Unloaded,Fixture.Runtime.GetStatus(Fixture.Child).State);
        Assert.True(Fixture.Source.ReleaseCount>0);
        var Reactivated=await Fixture.Service.RequestAsync([Fixture.Child],Fixture.Frames,WorldCellStreamingBudget.Unlimited);
        Assert.True(Reactivated.Succeeded,Reactivated.Error.Message);
        Assert.Equal(new[]{Fixture.Dependency,Fixture.Child},Reactivated.Snapshot.ActiveCells.ToArray());
    }
    [Fact]
    public async Task DependencyWavesPrefetchWithABoundedConcurrencyAndDoNotStartDependentsAfterFailure(){
        var Cells=Enumerable.Range(0,4).Select(Index=>Cell("parallel-"+Index)).ToArray();
        var Source=new MemorySource {DelayMilliseconds=30};
        var Definitions=Cells.Select(CellId=>Definition(CellId,[],Source)).ToArray();
        Assert.True(WorldPartitionManifest.TryCreate(Definitions,out var Manifest,out var Error),Error.Message);
        var Graph=new ReferenceFrameGraph(Root);
        Assert.True(Graph.TryCommit(1,out var Frames,out _));
        var Service=new WorldCellStreamingService(new WorldCellRuntime(Manifest),Source,new RecordingSink());

        var Loaded=await Service.RequestAsync(Cells,Frames,new WorldCellStreamingBudget(4,4,2));

        Assert.True(Loaded.Succeeded,Loaded.Error.Message);
        Assert.Equal(2,Source.MaximumConcurrentReads);

        var Failed=CreateFixture();
        Failed.Source.Corrupt=Failed.Dependency;
        var Result=await Failed.Service.RequestAsync([Failed.Child],Failed.Frames,new WorldCellStreamingBudget(2,2,2));
        Assert.Equal(WorldCellStreamingErrorCode.CorruptArtifact,Result.Error.Code);
        Assert.DoesNotContain(Failed.Child,Failed.Source.ReadCells);
    }
    [Fact]
    public async Task CasSourceResolvesWholeEnvelopeAddressAndRetainsUntilRelease(){
        var RootPath=Path.Combine(Path.GetTempPath(),"CgeWorldCasTests",Guid.NewGuid().ToString("N"));
        try {
            var Store=new ContentAddressedStore(RootPath);
            var Key=new AssetKey(new AssetId(StableId.FromName(Namespace,"cas-artifact")),null);
            var Artifact=new ContentArtifact(ContentArtifactKind.Scene,new ArtifactProvenance("test","asset://cas",Hash256.Compute([1]),"fixture",Hash256.Compute([2])),null,[3,4,5]);
            var Published=await Store.PublishAsync(Key,ContentArtifactCodec.Write(Artifact));
            var Source=new ContentAddressedWorldCellArtifactSource(Store,new ContentArtifactIndex([Published]));

            var First=await Source.ReadAsync(Published.Reference,CancellationToken.None);
            var Second=await Source.ReadAsync(Published.Reference,CancellationToken.None);
            Assert.True(First.Span.SequenceEqual(Second.Span));
            await Source.ReleaseAsync(Published.Reference);
            await Source.ReleaseAsync(Published.Reference);
            await Assert.ThrowsAsync<InvalidOperationException>(async()=>await Source.ReleaseAsync(Published.Reference));
        } finally {
            if(Directory.Exists(RootPath)){ Directory.Delete(RootPath,true); }
        }
    }
    private static Fixture CreateFixture(){
        var Dependency=Cell("dependency");
        var Child=Cell("child");
        var Source=new MemorySource();
        var DependencyDefinition=Definition(Dependency,[],Source);
        var ChildDefinition=Definition(Child,[Dependency],Source);
        Assert.True(WorldPartitionManifest.TryCreate([ChildDefinition,DependencyDefinition],out var Manifest,out var Error),Error.Message);
        var Runtime=new WorldCellRuntime(Manifest);
        var Sink=new RecordingSink();
        var FramesGraph=new ReferenceFrameGraph(Root);
        Assert.True(FramesGraph.TryCommit(1,out var Frames,out _));
        return new Fixture(Dependency,Child,Runtime,new WorldCellStreamingService(Runtime,Source,Sink),Source,Sink,Frames);
    }
    private static WorldCellDefinition Definition(WorldCellId Cell,IEnumerable<WorldCellId>Dependencies,MemorySource Source){
        var Key=new AssetKey(new AssetId(StableId.FromName(Namespace,"asset-"+Cell)),null);
        var Artifact=new ContentArtifact(ContentArtifactKind.Scene,new ArtifactProvenance("test","asset://fixture",Hash256.Compute([1]),"fixture",Hash256.Compute([2])),null,[3,4,5]);
        var Reference=new AssetArtifactReference(Key,Artifact.ContentHash);
        Source.Add(Cell,Reference,ContentArtifactCodec.Write(Artifact));
        return new WorldCellDefinition(Cell,Root,new FrameLocalBounds(LogicalVector3.Zero,LogicalVector3.One),Key,Dependencies:Dependencies,ArtifactClosure:[Reference]);
    }
    private static WorldCellId Cell(string Name)=>new(StableId.FromName(Namespace,Name));
    private sealed record Fixture(WorldCellId Dependency,WorldCellId Child,WorldCellRuntime Runtime,WorldCellStreamingService Service,MemorySource Source,RecordingSink Sink,ReferenceFrameSnapshot Frames);
    private sealed class MemorySource:IWorldCellArtifactSource {
        private readonly Dictionary<AssetKey,(WorldCellId Cell,byte[]Bytes)>Items=[];
        public WorldCellId Corrupt;
        public WorldCellId Cancel;
        public int ReadCount;
        public int ReleaseCount;
        public int DelayMilliseconds;
        public int MaximumConcurrentReads;
        public readonly List<WorldCellId>ReadCells=[];
        private int ActiveReads;
        public void Add(WorldCellId Cell,AssetArtifactReference Reference,byte[]Bytes)=>this.Items.Add(Reference.Key,(Cell,Bytes));
        public async ValueTask<ReadOnlyMemory<byte>>ReadAsync(AssetArtifactReference Reference,CancellationToken CancellationToken){
            this.ReadCount++;
            var Item=this.Items[Reference.Key];
            lock(this.ReadCells){ this.ReadCells.Add(Item.Cell); }
            var Active=Interlocked.Increment(ref this.ActiveReads);
            UpdateMaximum(ref this.MaximumConcurrentReads,Active);
            try {
                if(this.DelayMilliseconds!=0){ await Task.Delay(this.DelayMilliseconds,CancellationToken); }
                if(Item.Cell==this.Cancel){ throw new OperationCanceledException(CancellationToken); }
                if(Item.Cell==this.Corrupt){ var Bytes=(byte[])Item.Bytes.Clone();Bytes[^1]^=0xff;return Bytes; }
                return Item.Bytes;
            } finally { Interlocked.Decrement(ref this.ActiveReads); }
        }
        public ValueTask ReleaseAsync(AssetArtifactReference Reference){ this.ReleaseCount++;return ValueTask.CompletedTask; }
        private static void UpdateMaximum(ref int Target,int Candidate){
            int Current;
            while(Candidate>(Current=Volatile.Read(ref Target))&&Interlocked.CompareExchange(ref Target,Candidate,Current)!=Current){ }
        }
    }
    private sealed class RecordingSink:IWorldCellActivationSink {
        public readonly List<WorldCellId>Activated=[];
        public readonly List<WorldCellId>Deactivated=[];
        public bool Fail;
        public bool TryApply(WorldCellTransition Transition,IReadOnlyDictionary<WorldCellId,LoadedWorldCell>LoadedCells,out string Error){
            if(this.Fail){ Error="injected activation failure";return false; }
            this.Activated.AddRange(Transition.ToActivate);
            this.Deactivated.AddRange(Transition.ToDeactivate);
            Error=string.Empty;
            return true;
        }
    }
}
