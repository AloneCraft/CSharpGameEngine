using System;
using System.Collections.Generic;
using CSharpGameEngine.SceneAnalysis;

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

    internal System.Threading.Tasks.Task<SceneModel?>ResolveSceneAsync(
        SceneKey Key,System.Threading.CancellationToken CancellationToken)=>
        this.Workspace.ResolveSceneAsync(Key,CancellationToken);

    /// <summary>UIコマンドの表示判定用。Roslyn解析を開始したり待機したりしない。</summary>
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
