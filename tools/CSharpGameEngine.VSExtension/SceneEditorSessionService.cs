using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;

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

    internal SceneDocumentSnapshot? GetSnapshotForModel(SceneModel Model){
        SceneKey?Key=this.Workspace.TryCreateSceneKey(Model);
        return Key is { } Value?this.Publish(Model,Value):null;
    }

    internal bool TryCreateSceneKey(SceneModel Model,out SceneKey Key){
        SceneKey?Candidate=this.Workspace.TryCreateSceneKey(Model);
        if(Candidate is { } Value){
            Key=Value;
            return true;
        }

        Key=default;
        return false;
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
    /// M0039: ソース書き戻しチェーンの一部が失敗したとき、以降の予測はもう信用できない基準の上に
    /// 積み上がっている(<see cref="SceneModelEditApplier"/>自体は無罪 — 失敗より後の予測が
    /// 「本当は起きていない前段の編集」を前提に計算されてしまうのが問題)。予測を経由せず
    /// Roslynへ直接再解決し、そこで得られた実際のソース内容を pin中snapshotとして強制的に
    /// 配信し直すことで、画面をソースの真実へ同期し直す。呼び出し側(<see cref="SceneHwndHost"/>)は
    /// これと合わせて先読みUndo/Redoスタックを丸ごと破棄する — 既存の「Property編集・複製・
    /// 別Scene切替」でのClearと同じ確立済みのidiom。
    /// </summary>
    internal async Task RefreshPinnedSnapshotFromSourceAsync(CancellationToken CancellationToken){
        if(this.Disposed||this.PinnedSceneKey is not { }Key){
            return;
        }

        SceneModel?Model=await this.Workspace.ResolveSceneAsync(Key,CancellationToken).ConfigureAwait(false);
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        if(this.Disposed||Model is null||!Key.Equals(this.PinnedSceneKey)){
            return;
        }

        SceneDocumentSnapshot Snapshot=this.Publish(Model,Key);
        this.PinnedSnapshot=Snapshot;
        this.PinnedSnapshotChanged?.Invoke(Snapshot);
    }

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

            // M0044: 全文を文字列化して無関係なSourceText同士で差分を取ると、1文字の変更でも
            // ファイル全体を覆う1個のTextChangeになり(実測)、エディタが全再レイアウトを起こす。
            // Edited.RootはContext.Rootへ ReplaceNode した結果でツリーを共有しているため、
            // 構文ツリー同士の差分なら実際に変わった領域だけを取り出せる。
            //
            // ただし差分の出所を変えることはソースを壊しうるので、毎回「適用したら編集後ツリーと
            // 一致するか」を検算し、一致しなければ従来の全文差分へ落とす。書き込みが無言で
            // 消えるより、遅くても確実に反映される方を優先する。
            string ExpectedText=Edited.Root.ToFullString();
            IReadOnlyList<TextChange>Changes=SceneTextChangePlanner.Plan(
                Context.Root.SyntaxTree,Edited.Root.SyntaxTree);
            if(!string.Equals(Context.Text.WithChanges(Changes).ToString(),ExpectedText,StringComparison.Ordinal)){
                Changes=SceneTextChangePlanner.Plan(
                    Context.Text,SourceText.From(ExpectedText,Context.Text.Encoding));
            }
            SceneLiveBufferWriteResult Written=await this.Workspace.ApplyTextAsync(
                Context,Changes,Request.UndoDescription,CancellationToken).ConfigureAwait(false);
            if(Written.Succeeded){
                return new SceneEditCommitResult(true,ErrorMessage:null,Edited.WasRebased||Attempt>0);
            }
            if(!Written.LiveTextChanged||Attempt>0){
                return new SceneEditCommitResult(false,Written.ErrorMessage,Edited.WasRebased||Attempt>0);
            }
        }

        return new SceneEditCommitResult(false,"C#ソースが連続して変化したため上書きを中止しました。",WasRebased:true);
    }

    /// <summary>
    /// M0036: シーンビューのCtrl+Z/Yから呼ばれる。ピン留め中のSceneを裏付けるC#ファイルへ、実際の
    /// VS Undo/Redoを1回実行する(唯一の編集履歴は引き続きVSのテキストUndo、ADR-0040)。
    /// </summary>
    internal bool TryUndo(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.PinnedSnapshot is { }Snapshot
            &&(Snapshot.LastKnownGoodModel??Snapshot.CurrentModel)is { }Model
            &&this.Workspace.TryUndo(Model.FilePath);
    }

    internal bool TryRedo(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.PinnedSnapshot is { }Snapshot
            &&(Snapshot.LastKnownGoodModel??Snapshot.CurrentModel)is { }Model
            &&this.Workspace.TryRedo(Model.FilePath);
    }

    /// <summary>
    /// M0038: コミット要求を受けた瞬間に同期で呼ぶ。編集後のモデルを予測して pin 中 snapshot として
    /// 配信し、Renderer/Hierarchy/Inspector を Roslyn 再解析を待たずに更新する。
    ///
    /// <para>
    /// <b>予測は解析結果の履歴(<see cref="Snapshots"/>)には入れない。</b>あれは
    /// <see cref="SceneEditConflictMerger"/> と次回 diff の基準になる「実際に解析された事実」の列で
    /// あり、予測を混ぜると書き戻しの競合解決が壊れる。ここが配るのは購読者向けの表示用 snapshot だけ。
    /// </para>
    ///
    /// <para>
    /// 予測できなければ<c>false</c>を返すだけで、呼び出し側は従来どおりの往復にフォールバックする
    /// (画面は再解析が landしたときに更新される = 今日と同じ速度)。
    /// </para>
    ///
    /// <para>
    /// M0039: <see cref="SceneDocumentSnapshot.DocumentVersion"/>は<b>元のsnapshotから引き継ぎ、
    /// 進めない</b>。これは<see cref="SceneEditConflictMerger"/>が「ドラッグ開始時から実際の
    /// ソースが変化したか」を判定する楽観的並行性制御の版数であり、まだソースへ書き込まれていない
    /// 予測がこれを進めると、実際には何も割り込んでいないのに毎回rebase経路へ落ちてしまう
    /// (rebase経路自体は正しく合成できるが、書き戻し失敗時の巻き戻りと組み合わさると壊れる —
    /// 詳細はM0039のCommit記録を参照)。<see cref="SceneDocumentSnapshot.AnalysisGeneration"/>は
    /// レンダラの増分更新判定(<c>SceneHwndHost.SetTarget</c>)が新旧を区別するために引き続き
    /// 新しい値を払い出す。
    /// </para>
    /// </summary>
    internal bool TryPublishPrediction(SceneEditCommitRequest Request){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed
            ||this.PinnedSceneKey is not { }Key||!Key.Equals(Request.SceneKey)
            ||this.PinnedSnapshot is not { }Current
            ||Current.CurrentModel is not { }Model||Current.Projection is not { }Projection
            ||!SceneModelEditApplier.TryApply(
                Model,Projection,CreateOperations(Request),out SceneModel Predicted)){
            return false;
        }

        // ID空間は実解析と共有する(別インスタンスにすると同じNodeへ別IDが振られうる)。値のみの
        // 変更ではNameもComponent構成も変わらないため、ここで新しいIDが払い出されることはない。
        if(!this.Reconcilers.TryGetValue(Key,out SceneProjectionReconciler?Reconciler)){
            return false;
        }
        SceneProjection PredictedProjection=Reconciler.Reconcile(Key,Predicted,Projection);
        var Snapshot=new SceneDocumentSnapshot(
            Key,Current.DocumentId,Current.DocumentVersion,++this.Generation,
            Predicted,Predicted,PredictedProjection,
            SceneDiffEngine.Compare(Projection,PredictedProjection),HasBlockingErrors:false);
        this.PinnedSnapshot=Snapshot;
        this.PinnedSnapshotChanged?.Invoke(Snapshot);
        return true;
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
