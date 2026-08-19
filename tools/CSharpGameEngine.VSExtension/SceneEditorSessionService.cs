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
///
/// <para>
/// M0047: 編集の正本を<see cref="SceneEditDocument"/>(メモリ内、即時・無制限Undo)へ移した。
/// <see cref="Apply"/>は同期で完結し、C#ソースへは<see cref="SaveAsync"/>を呼んだときだけ
/// まとめて書き戻す。M0038〜M0046の非同期コミットチェーン・楽観的並行性マージ
/// (<see cref="SceneEditConflictMerger"/>)・予測配信はここでは使わない
/// (Documentが常にセッションの唯一の正本であり、競合する書き込み元が無いため)。
/// </para>
/// </summary>
internal sealed class SceneEditorSessionService : IDisposable {
    private readonly SceneWorkspaceService Workspace;
    private readonly Dictionary<SceneKey,SceneDocumentSnapshot>Snapshots=[];
    private readonly Dictionary<SceneKey,SceneProjectionReconciler>Reconcilers=[];
    private readonly Dictionary<SceneKey,SceneEditDocument>Documents=[];
    private readonly Dictionary<SceneKey,SceneProjection>LastPublishedProjections=[];
    private readonly HashSet<SceneKey>SavesInFlight=[];
    private long Generation;
    private bool Disposed;

    /// <summary>
    /// 保存が書き戻した直後の再解析だけは「自分自身の反映」として扱い、外部変更確認を出さずに
    /// Documentのベースラインを黙って更新する。保存を開始した瞬間にtrueへ、その後最初に届いた
    /// 再解析(対象は問わない — 1セッションで同時に複数Sceneを保存することは無いため十分)でfalseへ戻す。
    /// </summary>
    private bool AwaitingOwnSaveReanalysis;

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

    /// <summary>
    /// 未保存の編集がある状態で対象C#ファイルが外部(コードエディタでの直接編集等)で変化したときに発火する。
    /// 呼び出し側は確認ダイアログを出し、破棄して良ければ<see cref="DiscardUnsavedChanges"/>を呼ぶ。
    /// </summary>
    internal event Action<SceneKey>?ExternalChangeDetected;

    internal SceneDocumentSnapshot Pin(SceneModel Model){
        SceneKey Key=this.Workspace.TryCreateSceneKey(Model)
            ??throw new InvalidOperationException("Scene Designer の対象にできるのはソリューション内の C# Document だけです。");
        SceneDocumentSnapshot RealSnapshot=this.Publish(Model,Key);
        this.PinnedSceneKey=Key;
        SceneDocumentSnapshot Published=this.EnsureDocumentAndPublish(Key,RealSnapshot);
        this.PinnedSnapshotChanged?.Invoke(Published);
        return Published;
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

    internal bool IsDirty(SceneKey Key)=>
        this.Documents.TryGetValue(Key,out SceneEditDocument?Document)&&Document.IsDirty;

    /// <summary>
    /// 保存の書き戻し(<see cref="SaveAsync"/>)が同じSceneのDocumentへ非同期でアクセスしている間、
    /// <see cref="Apply"/>/<see cref="TryUndo"/>/<see cref="TryRedo"/>を止める。保存は
    /// <c>Document.History</c>のスナップショットを取ってから非同期でC#へ書き出すため、その間に
    /// さらに編集/Undoされると「実際に書いた内容」と「MarkSavedが指すCurrent.Model」がずれる。
    /// </summary>
    internal bool IsSaving(SceneKey Key)=>this.SavesInFlight.Contains(Key);

    internal bool CanUndo(SceneKey Key)=>
        this.Documents.TryGetValue(Key,out SceneEditDocument?Document)&&Document.CanUndo;

    internal bool CanRedo(SceneKey Key)=>
        this.Documents.TryGetValue(Key,out SceneEditDocument?Document)&&Document.CanRedo;

    /// <summary>
    /// M0047: 編集要求を即時・同期でDocumentへ適用する。Roslyn再解析もC#書き戻しも待たない — これが
    /// 「きびきび動く」の実体。対応できない操作(未実装のOperation種別)であれば素直にfalseを返す。
    /// </summary>
    internal bool Apply(SceneEditCommitRequest Request){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed||this.SavesInFlight.Contains(Request.SceneKey)
            ||!this.Documents.TryGetValue(Request.SceneKey,out SceneEditDocument?Document)
            ||!Document.TryApply(CreateOperations(Request),Request.UndoDescription)){
            return false;
        }

        this.PublishDocument(Request.SceneKey,Document);
        return true;
    }

    /// <summary>
    /// M0036由来: シーンビューのCtrl+Z/Yから呼ばれる。M0047でDocumentの即時Undo/Redoへ差し替えた
    /// (以前はここが対象.csファイルへ実際のVS Undoを1回実行していたが、C#への書き込みは保存操作でしか
    /// 起きなくなったため、その経路はもう意味を持たない)。
    /// </summary>
    internal bool TryUndo(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.PinnedSceneKey is not { }Key||this.SavesInFlight.Contains(Key)
            ||!this.Documents.TryGetValue(Key,out SceneEditDocument?Document)||!Document.TryUndo()){
            return false;
        }

        this.PublishDocument(Key,Document);
        return true;
    }

    internal bool TryRedo(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.PinnedSceneKey is not { }Key||this.SavesInFlight.Contains(Key)
            ||!this.Documents.TryGetValue(Key,out SceneEditDocument?Document)||!Document.TryRedo()){
            return false;
        }

        this.PublishDocument(Key,Document);
        return true;
    }

    /// <summary>
    /// M0047: セッション中に積み上げたDocumentの全履歴を、最新の実C#構文へ一度だけ再生してlive
    /// bufferへ書き戻す(<see cref="SceneSourceEditor.ApplyForSave"/>)。書き戻しは1つの
    /// <c>ITextChange</c>列・1つのVS Undoトランザクションにまとまる。
    /// </summary>
    internal async Task<SceneEditCommitResult>SaveAsync(SceneKey Key,CancellationToken CancellationToken){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        if(!this.Documents.TryGetValue(Key,out SceneEditDocument?Document)){
            return new SceneEditCommitResult(false,"保存対象のSceneが開かれていません。",WasRebased:false);
        }
        if(!Document.IsDirty){
            return new SceneEditCommitResult(true,ErrorMessage:null,WasRebased:false);
        }
        if(!this.SavesInFlight.Add(Key)){
            return new SceneEditCommitResult(false,"既にこのSceneの保存が進行中です。",WasRebased:false);
        }

        try {
            SceneWorkspaceEditContext?Context=await this.Workspace
                .ResolveSceneEditContextAsync(Key,CancellationToken).ConfigureAwait(false);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
            if(this.Disposed){
                return new SceneEditCommitResult(false,"セッションが終了しました。",WasRebased:false);
            }
            if(Context is null){
                return new SceneEditCommitResult(false,"対象Sceneの最新C#ソースを解決できません。",WasRebased:false);
            }
            if(Context.Model.HasErrors||Context.Model.Root is null){
                return new SceneEditCommitResult(false,"対象SceneにC#解析エラーがあるため保存できません。",WasRebased:false);
            }
            if(!this.Reconcilers.TryGetValue(Key,out SceneProjectionReconciler?Reconciler)){
                return new SceneEditCommitResult(false,"対象SceneのID対応表が見つかりません。",WasRebased:false);
            }

            // 保存前の最後に実解析されたsnapshot(Publish済み)を基準にIDを引き継ぐ。実ソースが
            // pin以降に一切変化していなければ、これはpin時のProjectionと同じ形になる。
            this.Snapshots.TryGetValue(Key,out SceneDocumentSnapshot?LastReal);
            SceneProjection RealProjection=Reconciler.Reconcile(Key,Context.Model,LastReal?.Projection);

            SceneSourceEditResult Edited=SceneSourceEditor.ApplyForSave(
                Context.Root,Key,RealProjection,Document.History);
            if(!Edited.Succeeded||Edited.Root is null){
                return new SceneEditCommitResult(false,Edited.ErrorMessage??"保存に失敗しました。",WasRebased:false);
            }

            // M0044と同じ安全網: 構文ツリー差分の出所を変えることはソースを壊しうるため、適用結果が
            // 再生結果の全文と一致するか毎回検算し、一致しなければ全文差分へ落とす。
            string ExpectedText=Edited.Root.ToFullString();
            IReadOnlyList<TextChange>Changes=SceneTextChangePlanner.Plan(Context.Root.SyntaxTree,Edited.Root.SyntaxTree);
            if(!string.Equals(Context.Text.WithChanges(Changes).ToString(),ExpectedText,StringComparison.Ordinal)){
                Changes=SceneTextChangePlanner.Plan(Context.Text,SourceText.From(ExpectedText,Context.Text.Encoding));
            }

            this.AwaitingOwnSaveReanalysis=true;
            SceneLiveBufferWriteResult Written=await this.Workspace
                .ApplyTextAsync(Context,Changes,"Scene の保存",CancellationToken).ConfigureAwait(false);
            if(!Written.Succeeded){
                this.AwaitingOwnSaveReanalysis=false;
                return new SceneEditCommitResult(false,Written.ErrorMessage,WasRebased:false);
            }

            Document.MarkSaved();
            return new SceneEditCommitResult(true,ErrorMessage:null,WasRebased:false);
        } finally {
            this.SavesInFlight.Remove(Key);
        }
    }

    /// <summary>
    /// <see cref="ExternalChangeDetected"/>のダイアログで「破棄して読み込み直す」が選ばれたときに呼ぶ。
    /// 直近の実解析(<see cref="Snapshots"/>)へDocumentを丸ごと差し替える。
    /// </summary>
    internal void DiscardUnsavedChanges(SceneKey Key){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(!this.Documents.TryGetValue(Key,out SceneEditDocument?Document)
            ||!this.Snapshots.TryGetValue(Key,out SceneDocumentSnapshot?Real)
            ||Real.CurrentModel is not { }Model||Real.Projection is not { }Projection){
            return;
        }

        Document.ResetTo(new SceneEditSnapshot(Model,Projection));
        this.PublishDocument(Key,Document);
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

    private SceneDocumentSnapshot EnsureDocumentAndPublish(SceneKey Key,SceneDocumentSnapshot RealSnapshot){
        if(this.Documents.TryGetValue(Key,out SceneEditDocument?Existing)){
            // 既にセッションが持つ未保存の編集を、再pinしただけで失わない。
            var ExistingSnapshot=this.BuildSnapshot(Key,Existing);
            this.PinnedSnapshot=ExistingSnapshot;
            return ExistingSnapshot;
        }

        if(RealSnapshot.CurrentModel is { }Model&&RealSnapshot.Projection is { }Projection
            &&this.Reconcilers.TryGetValue(Key,out SceneProjectionReconciler?Reconciler)){
            var NewDocument=new SceneEditDocument(Key,new SceneEditSnapshot(Model,Projection),Reconciler);
            this.Documents[Key]=NewDocument;
            var Snapshot=this.BuildSnapshot(Key,NewDocument);
            this.PinnedSnapshot=Snapshot;
            return Snapshot;
        }

        // 解析エラー等でDocumentを起こせない間は、実解析snapshotをそのまま見せる。
        this.PinnedSnapshot=RealSnapshot;
        return RealSnapshot;
    }

    private void PublishDocument(SceneKey Key,SceneEditDocument Document){
        SceneDocumentSnapshot Snapshot=this.BuildSnapshot(Key,Document);
        if(this.PinnedSceneKey is { }Pinned&&Pinned.Equals(Key)){
            this.PinnedSnapshot=Snapshot;
            this.PinnedSnapshotChanged?.Invoke(Snapshot);
        }
    }

    private SceneDocumentSnapshot BuildSnapshot(SceneKey Key,SceneEditDocument Document){
        this.LastPublishedProjections.TryGetValue(Key,out SceneProjection?Previous);
        this.LastPublishedProjections[Key]=Document.Projection;
        return new SceneDocumentSnapshot(
            Key,Document.Model.FilePath,DocumentVersion:++this.Generation,AnalysisGeneration:this.Generation,
            Document.Model,Document.Model,Document.Projection,
            SceneDiffEngine.Compare(Previous,Document.Projection),HasBlockingErrors:false);
    }

    private void OnWorkspaceModelsChanged(IReadOnlyList<SceneModel>Models){
        if(this.Disposed){
            return;
        }

        this.ActiveDocumentModelsChanged?.Invoke(Models);
        bool IsOwnSaveLanding=this.AwaitingOwnSaveReanalysis;
        this.AwaitingOwnSaveReanalysis=false;

        foreach(SceneModel Model in Models){
            SceneKey?Key=this.Workspace.TryCreateSceneKey(Model);
            if(Key is not { } Value){
                continue;
            }

            // M0048: VSの背景解析は対象Sceneの内容と無関係な理由でも頻繁に発火しうる。再解析イベントが
            // 来たという事実だけでDocumentを触ってよい根拠にはならない — 直前の実投影と比較して
            // 本当に変わったかを必ず経由する(SceneExternalChangePolicy参照)。
            this.Snapshots.TryGetValue(Value,out SceneDocumentSnapshot?PreviousReal);
            SceneDocumentSnapshot RealSnapshot=this.Publish(Model,Value);
            if(!this.Documents.TryGetValue(Value,out SceneEditDocument?Document)){
                if(this.PinnedSceneKey is { } Pinned&&Pinned.Equals(Value)){
                    this.PinnedSnapshot=RealSnapshot;
                    this.PinnedSnapshotChanged?.Invoke(RealSnapshot);
                }
                continue;
            }

            bool RealContentActuallyChanged=PreviousReal?.Projection is not { }OldProjection
                ||RealSnapshot.Projection is not { }NewProjectionForDiff
                ||!SceneDiffEngine.Compare(OldProjection,NewProjectionForDiff).IsEmpty;
            SceneExternalChangeAction Action=SceneExternalChangePolicy.Evaluate(
                IsOwnSaveLanding,RealContentActuallyChanged,Document.IsDirty);

            switch(Action){
                case SceneExternalChangeAction.Ignore:
                    // 実際には何も変わっていない再解析。Document/Undo/Redo履歴に一切触れない
                    // (ここで触ると、保存直後などIsDirty==falseのタイミングで無関係な再解析が
                    // Redoスタックを黙って消してしまう — 実機で確認された退行)。
                    break;
                case SceneExternalChangeAction.TrustOwnSave:
                case SceneExternalChangeAction.FollowSilently:
                    if(RealSnapshot.CurrentModel is { }NewModel&&RealSnapshot.Projection is { }NewProjection){
                        Document.ResetTo(new SceneEditSnapshot(NewModel,NewProjection));
                    }
                    this.PublishDocument(Value,Document);
                    break;
                case SceneExternalChangeAction.AskUser:
                    // 未保存の編集があるのに実際に外部でソースが変わった → 呼び出し側の確認に委ねる。
                    // ここではpin中snapshotを差し替えない(在庫中の編集を守る)。
                    this.ExternalChangeDetected?.Invoke(Value);
                    break;
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
        this.Documents.Clear();
        this.LastPublishedProjections.Clear();
    }
}
