using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Owns one Scene Designer session while C# remains the only authoring source of truth.
/// Completed gestures are planned against one latest Roslyn generation, written to the live
/// buffer as one native Undo unit, and replaced by the authoritative reparse.
/// </summary>
internal sealed class SceneEditorSessionService : IDisposable {
    private readonly SceneWorkspaceService Workspace;
    private readonly Dictionary<SceneKey,SceneDocumentSnapshot>Snapshots=[];
    private readonly Dictionary<SceneKey,SceneProjectionReconciler>Reconcilers=[];
    private readonly HashSet<SceneKey>SourceWritesInFlight=[];
    private readonly Dictionary<SceneKey,PendingSourceLanding>PendingSourceLandings=[];
    private long Generation;
    private bool Disposed;

    internal SceneEditorSessionService(SceneWorkspaceService Workspace){
        this.Workspace=Workspace??throw new ArgumentNullException(nameof(Workspace));
        this.Workspace.AllModelsChanged+=this.OnWorkspaceModelsChanged;
        this.Selection=new SceneSelectionService();
    }

    internal SceneSelectionService Selection { get; }

    internal SceneKey?PinnedSceneKey { get;private set;}

    internal SceneDocumentSnapshot?PinnedSnapshot { get;private set;}

    internal event Action<SceneDocumentSnapshot?>?PinnedSnapshotChanged;

    internal event Action<IReadOnlyList<SceneModel>>?ActiveDocumentModelsChanged;

    internal SceneDocumentSnapshot Pin(SceneModel Model){
        SceneKey Key=this.Workspace.TryCreateSceneKey(Model)
            ??throw new InvalidOperationException(VsixText.Get("SessionPinDocumentOnly"));
        SceneDocumentSnapshot Snapshot=this.Publish(Model,Key);
        this.PinnedSceneKey=Key;
        this.PublishPinned(Snapshot);
        return Snapshot;
    }

    internal SceneDocumentSnapshot?GetSnapshotForModel(SceneModel Model){
        SceneKey?Key=this.Workspace.TryCreateSceneKey(Model);
        return Key is { }Value?this.Publish(Model,Value):null;
    }

    internal bool TryCreateSceneKey(SceneModel Model,out SceneKey Key){
        SceneKey?Candidate=this.Workspace.TryCreateSceneKey(Model);
        if(Candidate is { }Value){
            Key=Value;
            return true;
        }

        Key=default;
        return false;
    }

    internal bool TryGetSnapshot(SceneKey Key,out SceneDocumentSnapshot Snapshot)=>
        this.Snapshots.TryGetValue(Key,out Snapshot!);

    internal string?TryGetRuntimeProcessName(SceneKey Key)=>
        this.Workspace.TryGetProjectAssemblyName(Key);

    internal void ClearPin(){
        if(this.PinnedSceneKey is null&&this.PinnedSnapshot is null){ return; }
        this.PinnedSceneKey=null;
        this.PinnedSnapshot=null;
        this.PinnedSnapshotChanged?.Invoke(null);
    }

    internal Task<IReadOnlyList<SceneModel>>AnalyzeFileNowAsync(
        string FilePath,CancellationToken CancellationToken)=>
        this.Workspace.AnalyzeFileNowAsync(FilePath,CancellationToken);

    internal Task<ProjectSceneCatalog?>GetActiveProjectSceneCatalogAsync(
        CancellationToken CancellationToken)=>
        this.Workspace.GetActiveProjectSceneCatalogAsync(CancellationToken);

    internal Task<ProjectSceneCatalog?>GetProjectSceneCatalogAsync(
        SceneKey?PreferredSceneKey,CancellationToken CancellationToken)=>
        this.Workspace.GetProjectSceneCatalogAsync(PreferredSceneKey,CancellationToken);

    internal Task<(Microsoft.CodeAnalysis.INamedTypeSymbol Symbol,
        Microsoft.CodeAnalysis.Compilation Compilation)?>ResolveComponentTypeAsync(
        string FullyQualifiedName,CancellationToken CancellationToken)=>
        this.Workspace.ResolveComponentTypeAsync(
            this.PinnedSceneKey,FullyQualifiedName,CancellationToken);

    internal Task<IReadOnlyList<ComponentPaletteEntry>>GetProjectComponentCatalogAsync(
        CancellationToken CancellationToken)=>
        this.Workspace.GetProjectComponentCatalogAsync(this.PinnedSceneKey,CancellationToken);

    internal Task<SceneModel?>ResolveSceneAsync(
        SceneKey Key,CancellationToken CancellationToken)=>
        this.Workspace.ResolveSceneAsync(Key,CancellationToken);

    /// <summary>
    /// Saves only the requested Scene source documents before an out-of-process Bake reads disk.
    /// Source commits already changed the live buffers; Bake does not replay any editor history.
    /// </summary>
    internal async Task<SceneEditCommitResult>PrepareForBakeAsync(
        IReadOnlyList<ProjectSceneCatalogEntry>Entries,CancellationToken CancellationToken){
        if(Entries is null){ throw new ArgumentNullException(nameof(Entries)); }
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);

        if(Entries.Select(Entry=>Entry.Key).Distinct().Any(this.SourceWritesInFlight.Contains)){
            return new SceneEditCommitResult(
                false,VsixText.Get("SessionSaveAlreadyInProgress"),WasRebased:false);
        }

        foreach(string FilePath in Entries
            .Select(Entry=>Path.GetFullPath(Entry.FilePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)){
            CancellationToken.ThrowIfCancellationRequested();
            if(!this.Workspace.TrySaveDocumentToDisk(
                FilePath,out string?Error,MissingDocumentIsSuccess:true)){
                return new SceneEditCommitResult(
                    false,Error??VsixText.Get("SessionSaveFailed"),WasRebased:false);
            }
        }

        return new SceneEditCommitResult(true,ErrorMessage:null,WasRebased:false);
    }

    internal bool IsSaving(SceneKey Key)=>this.SourceWritesInFlight.Contains(Key);

    /// <summary>
    /// Commits one completed gesture directly to the latest live C# buffer. A stale request is
    /// explicitly rebased or rejected before the native Undo transaction is opened.
    /// </summary>
    internal async Task<SceneEditCommitResult>CommitAsync(
        SceneEditCommitRequest Request,CancellationToken CancellationToken){
        if(Request is null){ throw new ArgumentNullException(nameof(Request)); }
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        if(this.Disposed){
            return new SceneEditCommitResult(
                false,VsixText.Get("SessionEnded"),WasRebased:false);
        }
        if(!this.SourceWritesInFlight.Add(Request.SceneKey)){
            return new SceneEditCommitResult(
                false,VsixText.Get("SessionSaveAlreadyInProgress"),WasRebased:false);
        }

        try {
            SceneWorkspaceEditContext?WorkspaceContext=await this.Workspace
                .ResolveSceneEditContextAsync(Request.SceneKey,CancellationToken)
                .ConfigureAwait(false);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
            if(this.Disposed){
                return new SceneEditCommitResult(
                    false,VsixText.Get("SessionEnded"),WasRebased:false);
            }
            if(WorkspaceContext is null){
                return new SceneEditCommitResult(
                    false,VsixText.Get("SessionSaveSourceUnavailable"),WasRebased:false);
            }

            SceneDocumentSnapshot Latest=this.BuildCommitSnapshot(
                Request.SceneKey,WorkspaceContext.Model);
            if(this.PinnedSceneKey is { }Pinned&&Pinned.Equals(Request.SceneKey)){
                this.PublishPinned(Latest);
            }

            SceneSourceCommitPlan Plan=SceneSourceCommitPlanner.Plan(
                new SceneSourceCommitContext(
                    WorkspaceContext.Model,WorkspaceContext.Root,WorkspaceContext.Text,
                    WorkspaceContext.Compilation),
                Latest,Request,this.Reconcilers[Request.SceneKey],CancellationToken);
            if(!Plan.Succeeded||Plan.WriteStep is null||Plan.PredictedModel is null
                ||Plan.PredictedProjection is null||Plan.FinalRoot is null){
                return new SceneEditCommitResult(
                    false,Plan.ErrorMessage??VsixText.Get("SourceEditOperationFailed"),
                    Plan.WasRebased);
            }

            this.PendingSourceLandings[Request.SceneKey]=PendingSourceLanding.Begin(
                Plan.PredictedProjection,Plan.PredictedModel);
            SceneLiveBufferWriteResult Written;
            try {
                Written=await this.Workspace.ApplyTextAsync(
                    WorkspaceContext,Plan.WriteStep.Changes,Plan.WriteStep.UndoDescription,
                    CancellationToken).ConfigureAwait(false);
            } catch {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(
                    CancellationToken.None);
                this.ClearPendingLanding(Request.SceneKey);
                throw;
            }

            // Once the live buffer call returns, transaction closure is non-cancellable. Reporting
            // cancellation after a successful source mutation would invite a duplicate retry.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken.None);
            if(!Written.Succeeded){
                this.ClearPendingLanding(Request.SceneKey);
                return new SceneEditCommitResult(false,Written.ErrorMessage,Plan.WasRebased);
            }
            if(this.Disposed){
                this.ClearPendingLanding(Request.SceneKey);
                return new SceneEditCommitResult(true,ErrorMessage:null,Plan.WasRebased);
            }

            if(this.PendingSourceLandings.TryGetValue(
                Request.SceneKey,out PendingSourceLanding?Pending)){
                this.PendingSourceLandings[Request.SceneKey]=Pending.CompleteWrite();
            }
            this.PublishCommittedPlan(Request.SceneKey,Latest,Plan);
            this.Workspace.ScheduleAnalysisNow();
            return new SceneEditCommitResult(true,ErrorMessage:null,Plan.WasRebased);
        } finally {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken.None);
            this.SourceWritesInFlight.Remove(Request.SceneKey);
        }
    }

    internal bool TryUndo(out string?BlockedReason){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.TryNativeSourceStep(Redo:false,out BlockedReason);
    }

    internal bool TryRedo(out string?BlockedReason){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.TryNativeSourceStep(Redo:true,out BlockedReason);
    }

    private bool TryNativeSourceStep(bool Redo,out string?BlockedReason){
        ThreadHelper.ThrowIfNotOnUIThread();
        BlockedReason=null;
        if(this.PinnedSceneKey is not { }Key||this.SourceWritesInFlight.Contains(Key)
            ||!this.TryGetSceneFilePath(Key,out string FilePath)){
            return false;
        }
        if(!(Redo?this.Workspace.TryRedo(FilePath):this.Workspace.TryUndo(FilePath))){
            return false;
        }

        this.ClearPendingLanding(Key);
        this.Workspace.ScheduleAnalysisNow();
        return true;
    }

    /// <summary>Persists the authoritative live C# buffer; it never performs edit writeback.</summary>
    internal async Task<SceneEditCommitResult>SaveAsync(
        SceneKey Key,CancellationToken CancellationToken){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        if(this.SourceWritesInFlight.Contains(Key)){
            return new SceneEditCommitResult(
                false,VsixText.Get("SessionSaveAlreadyInProgress"),WasRebased:false);
        }
        if(!this.TryGetSceneFilePath(Key,out string FilePath)){
            return new SceneEditCommitResult(
                false,VsixText.Get("SessionSaveSourceUnavailable"),WasRebased:false);
        }
        return this.Workspace.TrySaveDocumentToDisk(FilePath,out string?Error)
            ?new SceneEditCommitResult(true,ErrorMessage:null,WasRebased:false)
            :new SceneEditCommitResult(
                false,Error??VsixText.Get("SessionSaveFailed"),WasRebased:false);
    }

    private bool TryGetSceneFilePath(SceneKey Key,out string FilePath){
        FilePath="";
        if(!this.Snapshots.TryGetValue(Key,out SceneDocumentSnapshot?Snapshot)){ return false; }
        string?Resolved=(Snapshot.CurrentModel??Snapshot.LastKnownGoodModel)?.FilePath;
        if(string.IsNullOrEmpty(Resolved)){ return false; }
        FilePath=Resolved!;
        return true;
    }

    /// <summary>Converts the UI request without reading mutable session state.</summary>
    internal static SceneEditOperation[]CreateOperations(SceneEditCommitRequest Request){
        if(Request.DirectOperations.Count>0){ return Request.DirectOperations.ToArray(); }
        return Request.IsDuplicate
            ?Request.Changes.Select(Change=>(SceneEditOperation)new DuplicateNodeOperation(
                Request.SceneKey,Change.NodeId,Request.DocumentVersion,Change.Local,
                Request.Members)).ToArray()
            :Request.Changes.Select(Change=>(SceneEditOperation)new SetTransformOperation(
                Request.SceneKey,Change.NodeId,Request.DocumentVersion,Change.Local,
                Request.Members)).ToArray();
    }

    internal SceneDocumentMenuSnapshot GetMenuSnapshot(string FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.Workspace.GetMenuSnapshot(FilePath);
    }

    private SceneDocumentSnapshot BuildCommitSnapshot(SceneKey Key,SceneModel Model){
        SceneProjectionReconciler Reconciler=this.GetOrCreateReconciler(Key);
        this.Snapshots.TryGetValue(Key,out SceneDocumentSnapshot?Previous);
        SceneProjection Projection=Reconciler.Reconcile(Key,Model,Previous?.Projection);
        SceneDiff Diff=SceneDiffEngine.Compare(Previous?.Projection,Projection);
        long AnalysisGeneration=++this.Generation;
        long DocumentVersion=Previous is not null&&!Previous.HasBlockingErrors&&Diff.IsEmpty
            ?Previous.DocumentVersion
            :AnalysisGeneration;
        SceneDocumentSnapshot Snapshot=new(
            Key,Model.FilePath,DocumentVersion,AnalysisGeneration,Model,Model,Projection,Diff,
            HasBlockingErrors:Model.HasErrors||Model.Root is null);
        this.Snapshots[Key]=Snapshot;
        return Snapshot;
    }

    private void PublishCommittedPlan(
        SceneKey Key,SceneDocumentSnapshot Before,SceneSourceCommitPlan Plan){
        SceneProjection Projection=Plan.PredictedProjection!;
        SceneModel Model=Plan.PredictedModel!;
        long Version=++this.Generation;
        SceneDocumentSnapshot Snapshot=new(
            Key,Model.FilePath,Version,Version,Model,Model,Projection,
            SceneDiffEngine.Compare(Before.Projection,Projection),HasBlockingErrors:false);
        this.Snapshots[Key]=Snapshot;
        if(this.PinnedSceneKey is { }Pinned&&Pinned.Equals(Key)){
            this.PublishPinned(Snapshot);
        }
    }

    private void PublishPinned(SceneDocumentSnapshot Snapshot){
        this.PinnedSnapshot=Snapshot;
        this.PinnedSnapshotChanged?.Invoke(Snapshot);
    }

    private void ClearPendingLanding(SceneKey Key){
        this.PendingSourceLandings.Remove(Key);
    }

    private void OnWorkspaceModelsChanged(IReadOnlyList<SceneModel>Models){
        if(this.Disposed){ return; }
        this.ActiveDocumentModelsChanged?.Invoke(Models);

        foreach(SceneModel Model in Models){
            SceneKey?Candidate=this.Workspace.TryCreateSceneKey(Model);
            if(Candidate is not { }Key){ continue; }

            SceneProjection?IdentityBasis=null;
            if(this.PendingSourceLandings.TryGetValue(Key,out PendingSourceLanding?Pending)){
                if(Pending.Matches(Model)){
                    IdentityBasis=Pending.Projection;
                    this.ClearPendingLanding(Key);
                } else if(Pending.WriteCompleted){
                    // A completed write whose expected canonical model never landed was
                    // superseded by another source edit. Never carry its identity into a later
                    // unrelated generation.
                    this.ClearPendingLanding(Key);
                }
            }
            SceneDocumentSnapshot Snapshot=this.Publish(Model,Key,IdentityBasis);
            if(this.PinnedSceneKey is { }Pinned&&Pinned.Equals(Key)){
                this.PublishPinned(Snapshot);
            }
        }
    }

    private SceneDocumentSnapshot Publish(
        SceneModel Model,SceneKey Key,SceneProjection?IdentityBasis=null){
        this.Snapshots.TryGetValue(Key,out SceneDocumentSnapshot?Previous);
        long Version=++this.Generation;
        SceneDocumentSnapshot Snapshot=SceneDocumentSnapshot.Create(
            Key,Model.FilePath,Version,Version,Model,
            Model.HasErrors||Model.Root is null,Previous,this.GetOrCreateReconciler(Key),IdentityBasis);
        this.Snapshots[Key]=Snapshot;
        return Snapshot;
    }

    private SceneProjectionReconciler GetOrCreateReconciler(SceneKey Key){
        if(!this.Reconcilers.TryGetValue(Key,out SceneProjectionReconciler?Reconciler)){
            Reconciler=new SceneProjectionReconciler();
            this.Reconcilers.Add(Key,Reconciler);
        }
        return Reconciler;
    }

    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        this.Workspace.AllModelsChanged-=this.OnWorkspaceModelsChanged;
        this.Snapshots.Clear();
        this.Reconcilers.Clear();
        this.SourceWritesInFlight.Clear();
        this.PendingSourceLandings.Clear();
    }

}

/// <summary>
/// One expected Roslyn landing for one Scene source write. The semantic signature prevents an
/// older analysis or a later external edit from consuming the identity basis.
/// </summary>
internal sealed class PendingSourceLanding {
    private PendingSourceLanding(
        SceneProjection Projection,string StructuralSignature,bool WriteCompleted){
        this.Projection=Projection;
        this.StructuralSignature=StructuralSignature;
        this.WriteCompleted=WriteCompleted;
    }

    internal SceneProjection Projection { get; }

    internal bool WriteCompleted { get; }

    private string StructuralSignature { get; }

    internal static PendingSourceLanding Begin(
        SceneProjection Projection,SceneModel CanonicalModel)=>
        new(Projection,SceneModelStructuralSignature.Compute(CanonicalModel),WriteCompleted:false);

    internal PendingSourceLanding CompleteWrite()=>
        new(this.Projection,this.StructuralSignature,WriteCompleted:true);

    internal bool Matches(SceneModel Model)=>string.Equals(
        this.StructuralSignature,SceneModelStructuralSignature.Compute(Model),
        StringComparison.Ordinal);
}
