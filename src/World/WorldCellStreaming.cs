using System.Collections.Immutable;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.World;
public readonly record struct WorldCellStreamingBudget(int MaximumActiveCells,int MaximumNewCells,int MaximumConcurrentLoads=4) {
    public static WorldCellStreamingBudget Unlimited=>new(int.MaxValue,int.MaxValue,8);
    public bool IsValid=>this.MaximumActiveCells>=0&&this.MaximumNewCells>=0&&this.MaximumConcurrentLoads>0;
}
public enum WorldCellStreamingErrorCode { None,InvalidBudget,UnknownCell,BudgetExceeded,FaultedCell,InvalidState,Cancelled,CorruptArtifact,LoadFailed,ActivationFailed,RuntimeRejected,UnloadFailed }
public readonly record struct WorldCellStreamingError(WorldCellStreamingErrorCode Code,WorldCellId Cell,string Message) {
    public static WorldCellStreamingError None=>new(WorldCellStreamingErrorCode.None,default,string.Empty);
}
public sealed class WorldCellStreamingResult {
    internal WorldCellStreamingResult(WorldCellSnapshot Snapshot,WorldCellStreamingError Error){ this.Snapshot=Snapshot;this.Error=Error; }
    public WorldCellSnapshot Snapshot{get;}
    public WorldCellStreamingError Error{get;}
    public bool Succeeded=>this.Error.Code==WorldCellStreamingErrorCode.None;
}
public interface IWorldCellArtifactSource {
    ValueTask<ReadOnlyMemory<byte>>ReadAsync(AssetArtifactReference Reference,CancellationToken CancellationToken);
    ValueTask ReleaseAsync(AssetArtifactReference Reference);
}
public sealed class LoadedWorldCell {
    internal LoadedWorldCell(WorldCellDefinition Definition,ImmutableDictionary<AssetKey,ContentArtifact>Artifacts){ this.Definition=Definition;this.Artifacts=Artifacts; }
    public WorldCellDefinition Definition{get;}
    public ImmutableDictionary<AssetKey,ContentArtifact> Artifacts{get;}
}
public interface IWorldCellActivationSink {
    bool TryApply(WorldCellTransition Transition,IReadOnlyDictionary<WorldCellId,LoadedWorldCell>LoadedCells,out string Error);
}
public sealed class WorldCellStreamingService:IAsyncDisposable {
    private readonly SemaphoreSlim OperationGate=new(1,1);
    private readonly Dictionary<WorldCellId,LoadedWorldCell>Loaded=[];
    private readonly WorldCellRuntime Runtime;
    private readonly IWorldCellArtifactSource Source;
    private readonly IWorldCellActivationSink Activation;
    private ReferenceFrameSnapshot?LastFrames;
    private bool Disposed;
    public WorldCellStreamingService(WorldCellRuntime Runtime,IWorldCellArtifactSource Source,IWorldCellActivationSink Activation){
        this.Runtime=Runtime??throw new ArgumentNullException(nameof(Runtime));
        this.Source=Source??throw new ArgumentNullException(nameof(Source));
        this.Activation=Activation??throw new ArgumentNullException(nameof(Activation));
    }
    public WorldCellSnapshot Current=>this.Runtime.Current;
    public async ValueTask<WorldCellStreamingResult>RequestAsync(IEnumerable<WorldCellId>Requested,ReferenceFrameSnapshot Frames,WorldCellStreamingBudget Budget,CancellationToken CancellationToken=default){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        ArgumentNullException.ThrowIfNull(Requested);
        ArgumentNullException.ThrowIfNull(Frames);
        if(!Budget.IsValid){ return this.Fail(WorldCellStreamingErrorCode.InvalidBudget,default,"Streaming counts cannot be negative and concurrent loads must be positive."); }
        try { await this.OperationGate.WaitAsync(CancellationToken).ConfigureAwait(false); }
        catch(OperationCanceledException){ return this.Fail(WorldCellStreamingErrorCode.Cancelled,default,"Streaming request was cancelled before it acquired the operation lease."); }
        try { return await this.RequestExclusiveAsync(Requested,Frames,Budget,CancellationToken).ConfigureAwait(false); }
        finally { this.OperationGate.Release(); }
    }
    private async ValueTask<WorldCellStreamingResult>RequestExclusiveAsync(IEnumerable<WorldCellId>Requested,ReferenceFrameSnapshot Frames,WorldCellStreamingBudget Budget,CancellationToken CancellationToken){
        var Desired=new HashSet<WorldCellId>();
        foreach(var Cell in Requested){
            if(!this.Runtime.Manifest.TryGetActivationOrder(Cell,out var Closure)){ return this.Fail(WorldCellStreamingErrorCode.UnknownCell,Cell,"Requested cell is absent from the partition manifest."); }
            foreach(var Required in Closure){ Desired.Add(Required); }
        }
        var OrderedDesired=this.Runtime.Manifest.OrderSubset(Desired,false);
        var Current=this.Runtime.Current;
        var CurrentSet=Current.ActiveCells.ToHashSet();
        var NewCells=OrderedDesired.Where(Cell=>!CurrentSet.Contains(Cell)).ToArray();
        if(OrderedDesired.Length>Budget.MaximumActiveCells||NewCells.Length>Budget.MaximumNewCells){
            return this.Fail(WorldCellStreamingErrorCode.BudgetExceeded,default,"Requested active set exceeds the streaming budget.");
        }
        if(OrderedDesired.SequenceEqual(Current.ActiveCells)){ return new WorldCellStreamingResult(Current,WorldCellStreamingError.None); }
        var LoadedThisRequest=new List<WorldCellId>();
        var CellsToLoad=new List<WorldCellId>();
        foreach(var Cell in NewCells){
            var Status=this.Runtime.GetStatus(Cell);
            if(Status.State==WorldCellState.Faulted){
                await this.CleanupLoadedAsync(LoadedThisRequest).ConfigureAwait(false);
                return this.Fail(WorldCellStreamingErrorCode.FaultedCell,Cell,"Faulted cells require an explicit reset before retry.");
            }
            if(Status.State==WorldCellState.Loaded){
                if(!this.Loaded.ContainsKey(Cell)){
                    await this.CleanupLoadedAsync(LoadedThisRequest).ConfigureAwait(false);
                    return this.Fail(WorldCellStreamingErrorCode.InvalidState,Cell,"Loaded cell has no streaming-owned artifact set.");
                }
                continue;
            }
            if(Status.State!=WorldCellState.Unloaded){
                await this.CleanupLoadedAsync(LoadedThisRequest).ConfigureAwait(false);
                return this.Fail(WorldCellStreamingErrorCode.InvalidState,Cell,$"World cell must be Unloaded before streaming, but is {Status.State}.");
            }
            CellsToLoad.Add(Cell);
        }
        var Pending=CellsToLoad.ToHashSet();
        while(Pending.Count!=0){
            var Wave=CellsToLoad.Where(Cell=>Pending.Contains(Cell)
                &&this.Runtime.Manifest[Cell].Dependencies.All(Dependency=>!Pending.Contains(Dependency))).ToArray();
            if(Wave.Length==0){
                await this.CleanupLoadedAsync(LoadedThisRequest).ConfigureAwait(false);
                return this.Fail(WorldCellStreamingErrorCode.RuntimeRejected,default,"Streaming dependency prefetch could not make progress.");
            }
            if(!this.Runtime.TryBeginLoads(Wave,out var BeginError)){
                await this.CleanupLoadedAsync(LoadedThisRequest).ConfigureAwait(false);
                return this.Fail(WorldCellStreamingErrorCode.InvalidState,BeginError.Cell,BeginError.Message);
            }
            var Results=await this.LoadWaveAsync(Wave,Budget.MaximumConcurrentLoads,CancellationToken).ConfigureAwait(false);
            var Completions=new WorldCellLoadCompletion[Wave.Length];
            for(var Index=0;Index<Wave.Length;Index++){
                var Result=Results[Index];
                Completions[Index]=new WorldCellLoadCompletion(Wave[Index],Result.Succeeded
                    ?WorldCellFault.None:new WorldCellFault(Result.FaultCode,Result.Message));
            }
            if(!this.Runtime.TryFinishLoads(Completions,out var CompleteError)){
                foreach(var Cell in Wave){
                    if(this.Runtime.GetStatus(Cell).State==WorldCellState.Loading){
                        this.Runtime.TryFailLoad(Cell,WorldCellFaultCode.LoadFailed,CompleteError.Message,out _);
                    }
                }
                foreach(var Result in Results){ if(Result.Cell is { }LoadedCell){ await this.TryReleaseCellAsync(LoadedCell).ConfigureAwait(false); } }
                await this.CleanupLoadedAsync(LoadedThisRequest).ConfigureAwait(false);
                return this.Fail(WorldCellStreamingErrorCode.RuntimeRejected,CompleteError.Cell,CompleteError.Message);
            }
            CellLoadResult?FirstFailure=null;
            WorldCellId FirstFailureCell=default;
            for(var Index=0;Index<Wave.Length;Index++){
                var Result=Results[Index];
                if(Result.Succeeded){
                    this.Loaded.Add(Wave[Index],Result.Cell!);
                    LoadedThisRequest.Add(Wave[Index]);
                    Pending.Remove(Wave[Index]);
                }else if(FirstFailure is null){
                    FirstFailure=Result;
                    FirstFailureCell=Wave[Index];
                }
            }
            if(FirstFailure is not null){
                await this.CleanupLoadedAsync(LoadedThisRequest).ConfigureAwait(false);
                return this.Fail(FirstFailure.ErrorCode,FirstFailureCell,FirstFailure.Message);
            }
        }
        if(!this.Runtime.TryBeginTransition(OrderedDesired,Frames,out var Transition,out var TransitionError)){
            await this.CleanupLoadedAsync(LoadedThisRequest).ConfigureAwait(false);
            return this.Fail(WorldCellStreamingErrorCode.RuntimeRejected,TransitionError.Cell,TransitionError.Message);
        }
        if(!this.Activation.TryApply(Transition,this.Loaded,out var ActivationError)){
            this.Runtime.TryFailTransition(Transition,WorldCellFaultCode.ActivationFailed,ActivationError,out _);
            foreach(var Cell in Transition.ToActivate){
                if(!this.Loaded.Remove(Cell,out var LoadedCell)){ continue; }
                var ReleaseError=await this.TryReleaseCellAsync(LoadedCell).ConfigureAwait(false);
                if(ReleaseError is not null){ ActivationError=string.Concat(ActivationError," Cleanup: ",ReleaseError); }
            }
            return this.Fail(WorldCellStreamingErrorCode.ActivationFailed,default,ActivationError);
        }
        if(!this.Runtime.TryCommitTransition(Transition,Frames,out var Snapshot,out var CommitError)){
            this.Runtime.TryFailTransition(Transition,WorldCellFaultCode.ActivationFailed,CommitError.Message,out _);
            return this.Fail(WorldCellStreamingErrorCode.RuntimeRejected,CommitError.Cell,CommitError.Message);
        }
        this.LastFrames=Frames;
        foreach(var Cell in Transition.ToDeactivate){
            if(this.Loaded.Remove(Cell,out var LoadedCell)){
                try { await this.ReleaseCellAsync(LoadedCell).ConfigureAwait(false); }
                catch(Exception Exception){
                    this.Runtime.TryFailUnload(Cell,Exception.Message,out _);
                    return this.Fail(WorldCellStreamingErrorCode.UnloadFailed,Cell,Exception.Message);
                }
            }
            if(!this.Runtime.TryCompleteUnload(Cell,out var UnloadError)){ return this.Fail(WorldCellStreamingErrorCode.UnloadFailed,Cell,UnloadError.Message); }
        }
        return new WorldCellStreamingResult(Snapshot,WorldCellStreamingError.None);
    }
    public async ValueTask DisposeAsync(){
        if(this.Disposed){ return; }
        await this.OperationGate.WaitAsync().ConfigureAwait(false);
        var Completed=false;
        try {
            if(this.Runtime.Current.ActiveCells.Length!=0){
                if(this.LastFrames is null){ throw new InvalidOperationException("Active cells have no owning frame snapshot."); }
                var Result=await this.RequestExclusiveAsync([],this.LastFrames,WorldCellStreamingBudget.Unlimited,CancellationToken.None).ConfigureAwait(false);
                if(!Result.Succeeded){ throw new InvalidOperationException($"World streaming shutdown failed: {Result.Error.Message}"); }
            }
            foreach(var Cell in this.Loaded.Values.ToArray()){
                await this.ReleaseCellAsync(Cell).ConfigureAwait(false);
            }
            this.Loaded.Clear();
            this.Disposed=true;
            Completed=true;
        } finally {
            this.OperationGate.Release();
            if(Completed){ this.OperationGate.Dispose(); }
        }
    }
    private async ValueTask<CellLoadResult>LoadCellAsync(WorldCellDefinition Definition,CancellationToken CancellationToken){
        var Artifacts=ImmutableDictionary.CreateBuilder<AssetKey,ContentArtifact>();
        var Acquired=new List<AssetArtifactReference>();
        try {
            foreach(var Reference in Definition.ArtifactClosure.OrderBy(static Artifact=>Artifact.Key)){
                var Bytes=await this.Source.ReadAsync(Reference,CancellationToken).ConfigureAwait(false);
                Acquired.Add(Reference);
                if(!ContentArtifactCodec.TryRead(Bytes.Span,out var Artifact,out var Error)||Artifact is null){
                    var ReleaseError=await this.TryReleaseReferencesAsync(Acquired).ConfigureAwait(false);
                    return CellLoadResult.Fail(WorldCellStreamingErrorCode.CorruptArtifact,WorldCellFaultCode.CorruptArtifact,AppendCleanupError(Error.Message,ReleaseError));
                }
                if(Artifact.ContentHash!=Reference.ContentHash){
                    var ReleaseError=await this.TryReleaseReferencesAsync(Acquired).ConfigureAwait(false);
                    return CellLoadResult.Fail(WorldCellStreamingErrorCode.CorruptArtifact,WorldCellFaultCode.CorruptArtifact,AppendCleanupError("Artifact payload hash does not match the manifest closure.",ReleaseError));
                }
                Artifacts.Add(Reference.Key,Artifact);
            }
            return CellLoadResult.Success(new LoadedWorldCell(Definition,Artifacts.ToImmutable()));
        }catch(OperationCanceledException){
            var ReleaseError=await this.TryReleaseReferencesAsync(Acquired).ConfigureAwait(false);
            return CellLoadResult.Fail(WorldCellStreamingErrorCode.Cancelled,WorldCellFaultCode.Cancelled,AppendCleanupError("Cell load was cancelled.",ReleaseError));
        }catch(Exception Exception){
            var ReleaseError=await this.TryReleaseReferencesAsync(Acquired).ConfigureAwait(false);
            return CellLoadResult.Fail(WorldCellStreamingErrorCode.LoadFailed,WorldCellFaultCode.LoadFailed,AppendCleanupError(Exception.Message,ReleaseError));
        }
    }
    private async Task<CellLoadResult[]>LoadWaveAsync(WorldCellId[]Cells,int MaximumConcurrentLoads,CancellationToken CancellationToken){
        using var Gate=new SemaphoreSlim(Math.Min(Cells.Length,MaximumConcurrentLoads));
        var Tasks=new Task<CellLoadResult>[Cells.Length];
        for(var Index=0;Index<Cells.Length;Index++){
            var Cell=Cells[Index];
            Tasks[Index]=LoadWithGateAsync(this.Runtime.Manifest[Cell]);
        }
        return await Task.WhenAll(Tasks).ConfigureAwait(false);

        async Task<CellLoadResult>LoadWithGateAsync(WorldCellDefinition Definition){
            await Gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try { return await this.LoadCellAsync(Definition,CancellationToken).ConfigureAwait(false); }
            finally { Gate.Release(); }
        }
    }
    private async ValueTask CleanupLoadedAsync(IEnumerable<WorldCellId>Cells){
        foreach(var Cell in Cells.Reverse()){
            if(this.Runtime.GetStatus(Cell).State==WorldCellState.Loaded&&this.Runtime.TryBeginUnload(Cell,out _)){
                if(this.Loaded.Remove(Cell,out var LoadedCell)&&await this.TryReleaseCellAsync(LoadedCell).ConfigureAwait(false) is { }ReleaseError){
                    this.Runtime.TryFailUnload(Cell,ReleaseError,out _);
                    continue;
                }
                this.Runtime.TryCompleteUnload(Cell,out _);
            }
        }
    }
    private async ValueTask ReleaseCellAsync(LoadedWorldCell Cell){
        foreach(var Reference in Cell.Definition.ArtifactClosure.Reverse()){ await this.Source.ReleaseAsync(Reference).ConfigureAwait(false); }
    }
    private async ValueTask<string?>TryReleaseCellAsync(LoadedWorldCell Cell)=>await this.TryReleaseReferencesAsync(Cell.Definition.ArtifactClosure).ConfigureAwait(false);
    private async ValueTask<string?>TryReleaseReferencesAsync(IEnumerable<AssetArtifactReference>References){
        try {
            foreach(var Reference in References.Reverse()){ await this.Source.ReleaseAsync(Reference).ConfigureAwait(false); }
            return null;
        }catch(Exception Exception){ return Exception.Message; }
    }
    private static string AppendCleanupError(string Message,string?CleanupError)=>CleanupError is null?Message:string.Concat(Message," Cleanup: ",CleanupError);
    private WorldCellStreamingResult Fail(WorldCellStreamingErrorCode Code,WorldCellId Cell,string Message)=>new(this.Runtime.Current,new WorldCellStreamingError(Code,Cell,Message));
    private sealed class CellLoadResult {
        private CellLoadResult(LoadedWorldCell?Cell,WorldCellStreamingErrorCode ErrorCode,WorldCellFaultCode FaultCode,string Message){ this.Cell=Cell;this.ErrorCode=ErrorCode;this.FaultCode=FaultCode;this.Message=Message; }
        public LoadedWorldCell?Cell{get;}
        public WorldCellStreamingErrorCode ErrorCode{get;}
        public WorldCellFaultCode FaultCode{get;}
        public string Message{get;}
        public bool Succeeded=>this.Cell is not null;
        public static CellLoadResult Success(LoadedWorldCell Cell)=>new(Cell,WorldCellStreamingErrorCode.None,WorldCellFaultCode.None,string.Empty);
        public static CellLoadResult Fail(WorldCellStreamingErrorCode ErrorCode,WorldCellFaultCode FaultCode,string Message)=>new(null,ErrorCode,FaultCode,Message);
    }
}
