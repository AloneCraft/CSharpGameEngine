using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// AsyncPackage が保持する Scene Editor session。static global state を廃し、対象 Scene を
/// <see cref="SceneKey"/> で pin して、Roslyn 解析結果を immutable snapshot として購読側へ発行する。
/// </summary>
internal sealed class SceneEditorSessionService : IDisposable {
    private readonly SceneWorkspaceService Workspace;
    private readonly Dictionary<SceneKey,SceneDocumentSnapshot>Snapshots=[];
    private readonly Dictionary<SceneKey,SceneProjectionReconciler>Reconcilers=[];
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

    /// <summary>pin 中の Scene の最新 snapshot が差し替わったときに発火する。Viewport の retarget も購読する。</summary>
    internal event Action<SceneDocumentSnapshot?>?PinnedSnapshotChanged;

    /// <summary>Hierarchyなど、アクティブDocument内の全Sceneモデルを表示するUIが購読するイベント。</summary>
    internal event Action<IReadOnlyList<SceneModel>>?ActiveDocumentModelsChanged;

    internal SceneDocumentSnapshot Pin(SceneModel Model){
        SceneKey Key=this.Workspace.TryCreateSceneKey(Model)
            ??throw new InvalidOperationException("Scene Designer の対象にできるのはソリューション内の C# Document だけです。");
        SceneDocumentSnapshot Snapshot=this.Publish(Model,Key);
        this.PinnedSceneKey=Key;
        this.PinnedSnapshot=Snapshot;
        this.PinnedSnapshotChanged?.Invoke(Snapshot);
        return Snapshot;
    }

    internal bool TryGetSnapshot(SceneKey Key,out SceneDocumentSnapshot Snapshot)=>
        this.Snapshots.TryGetValue(Key,out Snapshot!);

    /// <summary>選択 Scene が削除・解析不能になったとき、Viewport を明示的な空状態へ戻します。</summary>
    internal void ClearPin(){
        if(this.PinnedSceneKey is null&&this.PinnedSnapshot is null){
            return;
        }

        this.PinnedSceneKey=null;
        this.PinnedSnapshot=null;
        this.PinnedSnapshotChanged?.Invoke(null);
    }

    /// <summary>右クリック時に捕捉したDocumentを即時解析します。pin先の決定は可変アクティブ文書を読みません。</summary>
    internal System.Threading.Tasks.Task<IReadOnlyList<SceneModel>>AnalyzeFileNowAsync(
        string FilePath,System.Threading.CancellationToken CancellationToken)=>
        this.Workspace.AnalyzeFileNowAsync(FilePath,CancellationToken);

    internal System.Threading.Tasks.Task<ProjectSceneCatalog?>GetActiveProjectSceneCatalogAsync(
        System.Threading.CancellationToken CancellationToken)=>
        this.Workspace.GetActiveProjectSceneCatalogAsync(CancellationToken);

    internal System.Threading.Tasks.Task<ProjectSceneCatalog?>GetProjectSceneCatalogAsync(
        SceneKey?PreferredSceneKey,System.Threading.CancellationToken CancellationToken)=>
        this.Workspace.GetProjectSceneCatalogAsync(PreferredSceneKey,CancellationToken);

    internal System.Threading.Tasks.Task<SceneModel?>ResolveSceneAsync(
        SceneKey Key,System.Threading.CancellationToken CancellationToken)=>
        this.Workspace.ResolveSceneAsync(Key,CancellationToken);

    /// <summary>
    /// 1回のギズモ操作を、commit直前に取得したRoslyn世代へ再解決し、live bufferの単一Undoとして反映する。
    /// 解決後から適用直前までにtextが変化した場合だけ、最新世代で1回再試行する。
    /// </summary>
    internal async Task<SceneEditCommitResult>CommitAsync(
        SceneEditCommitRequest Request,CancellationToken CancellationToken){
        for(var Attempt=0;Attempt<2;Attempt++){
            SceneWorkspaceEditContext?Context=await this.Workspace
                .ResolveSceneEditContextAsync(Request.SceneKey,CancellationToken)
                .ConfigureAwait(false);
            if(Context is null){
                return new SceneEditCommitResult(false,"対象Sceneの最新C#ソースを解決できません。",WasRebased:false);
            }
            if(Context.Model.HasErrors||Context.Model.Root is null){
                return new SceneEditCommitResult(
                    false,"対象SceneにC#解析エラーがあるため、ギズモ編集を反映できません。",WasRebased:false);
            }

            SceneDocumentSnapshot Snapshot=this.Publish(Context.Model,Request.SceneKey);
            SceneEditConflictMergeResult Merge=SceneEditConflictMerger.Merge(Request,Snapshot);
            if(!Merge.Succeeded){
                return new SceneEditCommitResult(false,Merge.ErrorMessage,WasRebased:true);
            }
            SceneSourceEditResult Edited=SceneSourceEditor.ApplyLatestBatch(
                Context.Root,Snapshot,Merge.Operations);
            if(!Edited.Succeeded||Edited.Root is null){
                return new SceneEditCommitResult(
                    false,Edited.ErrorMessage??"SceneのTransform更新に失敗しました。",Edited.WasRebased);
            }

            SourceText After=SourceText.From(Edited.Root.ToFullString(),Context.Text.Encoding);
            SceneLiveBufferWriteResult Written=await this.Workspace.ApplyTextAsync(
                Context,After,Request.UndoDescription,CancellationToken).ConfigureAwait(false);
            if(Written.Succeeded){
                return new SceneEditCommitResult(true,ErrorMessage:null,Edited.WasRebased||Attempt>0);
            }
            if(!Written.LiveTextChanged||Attempt>0){
                return new SceneEditCommitResult(false,Written.ErrorMessage,Edited.WasRebased||Attempt>0);
            }
        }

        return new SceneEditCommitResult(false,"C#ソースが連続して変化したため上書きを中止しました。",WasRebased:true);
    }

    /// <summary>UIコマンドの表示判定用。Roslyn解析を開始したり待機したりしない。</summary>
    internal static SceneEditOperation[]CreateOperations(SceneEditCommitRequest Request){
        if(Request.DirectOperations.Count>0){
            return Request.DirectOperations.ToArray();
        }
        return Request.IsDuplicate
            ?Request.Changes.Select(Change=>(SceneEditOperation)new DuplicateNodeOperation(
                Request.SceneKey,Change.NodeId,Request.DocumentVersion,Change.Local,
                Request.Members)).ToArray()
            :Request.Changes.Select(Change=>(SceneEditOperation)new SetTransformOperation(
                Request.SceneKey,Change.NodeId,Request.DocumentVersion,Change.Local,
                Request.Members)).ToArray();
    }

    internal SceneDocumentMenuSnapshot GetMenuSnapshot(string FilePath){
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        return this.Workspace.GetMenuSnapshot(FilePath);
    }

    private void OnWorkspaceModelsChanged(IReadOnlyList<SceneModel>Models){
        if(this.Disposed){
            return;
        }

        this.ActiveDocumentModelsChanged?.Invoke(Models);
        foreach(SceneModel Model in Models){
            SceneKey?Key=this.Workspace.TryCreateSceneKey(Model);
            if(Key is not { } Value){
                continue;
            }

            SceneDocumentSnapshot Snapshot=this.Publish(Model,Value);
            if(this.PinnedSceneKey is { } Pinned&&Pinned.Equals(Value)){
                this.PinnedSnapshot=Snapshot;
                this.PinnedSnapshotChanged?.Invoke(Snapshot);
            }
        }
    }

    private SceneDocumentSnapshot Publish(SceneModel Model,SceneKey Key){
        this.Snapshots.TryGetValue(Key,out SceneDocumentSnapshot?Previous);
        if(!this.Reconcilers.TryGetValue(Key,out SceneProjectionReconciler?Reconciler)){
            Reconciler=new SceneProjectionReconciler();
            this.Reconcilers.Add(Key,Reconciler);
        }

        return this.Snapshots[Key]=SceneDocumentSnapshot.Create(
            Key,
            DocumentId:Model.FilePath,
            DocumentVersion:++this.Generation,
            AnalysisGeneration:this.Generation,
            Model,
            HasBlockingErrors:Model.HasErrors||Model.Root is null,
            Previous,
            Reconciler);
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;
        this.Workspace.AllModelsChanged-=this.OnWorkspaceModelsChanged;
        this.Snapshots.Clear();
        this.Reconcilers.Clear();
    }
}
