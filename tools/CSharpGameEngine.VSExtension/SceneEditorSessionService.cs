using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
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

    /// <summary>
    /// M0051 Step 1-6: 自分が最後にバッファを触った直後の版番号と、テキスト側のRedoスタックに
    /// 残っている自分の単位数(<see cref="SceneTextSyncPolicy"/>)。Undo/Redoの同期が
    /// ユーザーの手打ち編集まで巻き戻さないための所有判定に使う。
    /// </summary>
    private readonly Dictionary<SceneKey,SceneTextSyncState>TextSyncStates=[];
    private long Generation;
    private bool Disposed;

    /// <summary>
    /// 自分の書き込み(保存の書き戻し、Undo/Redoのテキスト同期)の着地だけは外部変更として扱わない。
    /// 書き込みの直前に対象Sceneのキーを入れ、その後最初に届いた再解析でnullへ戻す。
    /// 着地の扱いは<see cref="SceneDocumentLineagePolicy"/>が決める — Documentは既に正しい内容を
    /// 持っているので、リネージが一致していれば何もしないのが正解。
    ///
    /// <para>
    /// M0051 Step 1-5でboolから<see cref="SceneKey"/>へ変えた。boolだと「対象は問わない」判定に
    /// なるが、再解析イベントは<b>1回で複数のSceneModel</b>を運ぶため、保存した1つと同じイベントに
    /// 乗った<b>無関係なScene</b>まで<c>TrustOwnSave</c>と誤判定され、
    /// <see cref="SceneEditDocument.ResetTo"/>で未保存の編集とUndo履歴が消えていた
    /// (<see cref="SceneOwnSaveLandingPolicy"/>参照)。
    /// </para>
    /// </summary>
    private SceneKey?AwaitingOwnWriteKey;

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
    internal string?TryGetRuntimeProcessName(SceneKey Key)=>this.Workspace.TryGetProjectAssemblyName(Key);

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

    /// <summary>M0053: 追加するComponent型のシンボル解決。pin中のSceneのProjectを優先します。</summary>
    internal System.Threading.Tasks.Task<(Microsoft.CodeAnalysis.INamedTypeSymbol Symbol,Microsoft.CodeAnalysis.Compilation Compilation)?>ResolveComponentTypeAsync(
        string FullyQualifiedName,System.Threading.CancellationToken CancellationToken)=>
        this.Workspace.ResolveComponentTypeAsync(this.PinnedSceneKey,FullyQualifiedName,CancellationToken);

    /// <summary>M0052: Componentパレットの候補。pin中のSceneがあればそのProjectを優先します。</summary>
    internal System.Threading.Tasks.Task<IReadOnlyList<ComponentPaletteEntry>>GetProjectComponentCatalogAsync(
        System.Threading.CancellationToken CancellationToken)=>
        this.Workspace.GetProjectComponentCatalogAsync(this.PinnedSceneKey,CancellationToken);

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

        // M0051 Step 1-6: テキストに触れない新しい編集が積まれるとDocumentのRedoStackが捨てられる。
        // テキスト側に残っている自分のRedo単位はもう対応物が無いので無効化する — これを忘れると、
        // メモリ内だけ戻した編集をRedoしたときに無関係な古いテキストRedoが走って乖離する。
        this.TextSyncStates[Request.SceneKey]=
            SceneTextSyncPolicy.AfterInMemoryEdit(this.GetSyncState(Request.SceneKey));

        // M0051 Step 1-7: Deferで消費されなかった着地待ちフラグが古いまま残っていると、
        // 無関係な解析を自分の着地と誤判定する。新しい編集を始めた時点で必ず捨てる。
        this.AwaitingOwnWriteKey=null;

        this.PublishDocument(Request.SceneKey,Document);
        return true;
    }

    /// <summary>
    /// シーンビュー・Hierarchy・InspectorのCtrl+Zから呼ばれる。
    ///
    /// <para>
    /// M0051 Step 1-6: <b>Undoは常にメモリ内スタックが駆動する</b>。テキストへの反映は
    /// ウォーターマークより下(=既にC#へ書き出した分)を戻すときの<b>副作用</b>にすぎない。
    /// Step 1-5は保存の着地で履歴を捨て、保存後のCtrl+Zをテキストへ委譲していたため、
    /// 表示の追従がRoslyn再解析待ちになり「保存すると遅くなる」という実機報告になった。
    /// </para>
    /// </summary>
    internal bool TryUndo(out string?BlockedReason){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.TryStep(Redo:false,out BlockedReason);
    }

    internal bool TryRedo(out string?BlockedReason){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.TryStep(Redo:true,out BlockedReason);
    }

    /// <summary>
    /// <paramref name="BlockedReason"/>はユーザーへ伝えるべき理由があるときだけ非null。
    /// 「戻すものが無い」は普通のことなので黙って何もしない — 伝えるのは
    /// <b>戻せるはずなのに戻せない</b>ケース(C#を直接編集されてテキストと同期できない)だけ。
    /// </summary>
    private bool TryStep(bool Redo,out string?BlockedReason){
        BlockedReason=null;
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.PinnedSceneKey is not { }Key
            ||!this.Documents.TryGetValue(Key,out SceneEditDocument?Document)){
            return false;
        }

        bool IsSaving=this.SavesInFlight.Contains(Key);
        bool CanSyncText=this.CanSyncText(Key,Redo,out string FilePath);
        SceneUndoAction Action=Redo
            ?SceneTextWatermarkPolicy.DecideRedo(
                Document.RedoCount,Document.History.Count,Document.TextWatermark,CanSyncText,IsSaving)
            :SceneTextWatermarkPolicy.DecideUndo(
                Document.History.Count,Document.TextWatermark,CanSyncText,IsSaving);
        if(Action==SceneUndoAction.None){
            // 戻せる履歴はあるのにテキストと同期できない = C#を直接編集された状態。
            // 黙って無反応だと原因が分からないので、ここだけは理由を返す。
            if(!IsSaving&&Document.History.Count>0&&Document.History.Count<=Document.TextWatermark){
                BlockedReason="C#ソースが直接編集されたため、ここから先はコードエディタのCtrl+Zで戻してください。";
            }

            return false;
        }

        // テキスト側を先に動かす。失敗したらメモリ内も動かさない — 片方だけ進むと
        // 「テキスト == History[0..TextWatermark)」の不変条件が壊れ、次の保存が破綻する。
        if(Action==SceneUndoAction.InMemoryWithText&&!this.TrySyncText(Key,FilePath,Redo)){
            return false;
        }

        if(!(Redo?Document.TryRedo():Document.TryUndo())){
            return false;
        }

        if(Action==SceneUndoAction.InMemoryWithText){
            if(Redo){
                Document.AdvanceWatermark();
            } else {
                Document.RollbackWatermark();
            }
        }

        this.PublishDocument(Key,Document);
        return true;
    }

    /// <summary>
    /// テキスト側のUndo/Redoを自分のものとして実行してよいか(<see cref="SceneTextSyncPolicy"/>)。
    /// ユーザーがコードエディタで直接C#を編集していた場合はfalse — テキストUndoスタックの一番上は
    /// その編集なので、触るとユーザーの作業を巻き戻してしまう。
    /// </summary>
    private bool CanSyncText(SceneKey Key,bool Redo,out string FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(!this.TryGetSceneFilePath(Key,out FilePath)
            ||!this.Workspace.TryGetBufferVersion(FilePath,out int CurrentVersion)){
            return false;
        }

        SceneTextSyncState State=this.GetSyncState(Key);
        return Redo
            ?SceneTextSyncPolicy.CanRedoText(State,CurrentVersion)
            :SceneTextSyncPolicy.CanUndoText(State,CurrentVersion);
    }

    /// <summary>
    /// C#テキストを1トランザクション分だけ戻す/進める。これでC#ソースがその瞬間に追従し、
    /// タブに<c>*</c>が付く(ユーザー確認済みの仕様)。表示はメモリ内スナップショットが即座に
    /// 更新するので、この後の再解析を待つ必要は無い。
    /// </summary>
    private bool TrySyncText(SceneKey Key,string FilePath,bool Redo){
        ThreadHelper.ThrowIfNotOnUIThread();
        SceneTextSyncState State=this.GetSyncState(Key);
        if(!(Redo?this.Workspace.TryRedo(FilePath):this.Workspace.TryUndo(FilePath))){
            return false;
        }

        this.Workspace.TryGetBufferVersion(FilePath,out int VersionAfter);
        this.TextSyncStates[Key]=Redo
            ?SceneTextSyncPolicy.AfterRedo(State,VersionAfter)
            :SceneTextSyncPolicy.AfterUndo(State,VersionAfter);

        // 自分の書き込みの着地として扱わせる。着地でリネージ検証が走り、食い違っていれば
        // 安全側(ResetTo)へ縮退する。デバウンスは挟まない。
        this.AwaitingOwnWriteKey=Key;
        this.Workspace.ScheduleAnalysisNow();
        return true;
    }

    /// <summary>
    /// 対象Sceneを宣言している<c>.cs</c>のパス。直近の実解析snapshotが持つ
    /// <see cref="SceneModel.FilePath"/>から同期で取れる(<see cref="SceneWorkspaceService.ResolveSceneEditContextAsync"/>は
    /// 非同期なのでCtrl+Zの経路からは使えない)。
    /// </summary>
    private bool TryGetSceneFilePath(SceneKey Key,out string FilePath){
        FilePath="";
        if(!this.Snapshots.TryGetValue(Key,out SceneDocumentSnapshot?Snapshot)){
            return false;
        }

        string?Resolved=(Snapshot.CurrentModel??Snapshot.LastKnownGoodModel)?.FilePath;
        if(string.IsNullOrEmpty(Resolved)){
            return false;
        }

        FilePath=Resolved!;
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

            // M0051 Step 1-6: 履歴はセッション全体で保持するようになったので、毎回先頭から再生すると
            // 二重適用になる。まだテキストへ書き出していない部分列だけを再生する。
            // 潜った直後(保存済みの編集をUndoした状態)は部分列が空で、テキストは既に同じ位置に
            // あるので保存は成功として何もしない — 空を ApplyForSaveSteps へ渡すと FinalRoot が
            // null になり「保存に失敗しました」と出てしまう。
            if(SceneSaveSuffixPolicy.Decide(Document.History.Count,Document.TextWatermark)
                ==SceneSaveSuffixKind.AlreadyInSync){
                return new SceneEditCommitResult(true,ErrorMessage:null,WasRebased:false);
            }

            // 保存前の最後に実解析されたsnapshot(Publish済み)を基準にIDを引き継ぐ。実ソースが
            // pin以降に一切変化していなければ、これはpin時のProjectionと同じ形になる。
            this.Snapshots.TryGetValue(Key,out SceneDocumentSnapshot?LastReal);
            SceneProjection RealProjection=Reconciler.Reconcile(Key,Context.Model,LastReal?.Projection);

            // M0051 Step 1-5: 履歴エントリごとの中間構文木を受け取り、1操作=1 VS Undo単位で
            // 書き戻す。以前はApplyForSave(最終木だけ)を使っていたため、3回編集してから保存すると
            // 保存後のCtrl+Zが3回分まとめて戻ってしまった(確定仕様は「1操作=1 Undo単位」)。
            SceneSourceEditStepsResult Edited=SceneSourceEditor.ApplyForSaveSteps(
                Context.Root,Key,RealProjection,Document.UnwrittenHistory);
            if(!Edited.Succeeded||Edited.FinalRoot is null){
                return new SceneEditCommitResult(false,Edited.ErrorMessage??"保存に失敗しました。",WasRebased:false);
            }

            // M0051: 書き戻した構文木を読み直し、Documentの編集内容と意味的に一致するか検算する。
            // M0049(JSON移行)の動機だった「C#に反映されていない」を、黙って起こさせないための関門。
            // 一致しない場合はテキストへ一切書き込まずに中止する(壊れた結果を保存するより、
            // 保存できないほうがユーザーの作業を守れる)。検算対象は最終形だけでよい —
            // 中間ステップはUndo単位を分けるためだけのもので、保存の成果物は最終形。
            if(!SceneSaveVerification.Verify(
                Edited.FinalRoot,Context.Root,Context.Compilation,Document.Model,
                Key.ClassFullyQualifiedName,CancellationToken,out string?VerificationError)){
                return new SceneEditCommitResult(false,VerificationError,WasRebased:false);
            }

            IReadOnlyList<SceneTextWriteStep>WriteSteps=BuildWriteSteps(Context,Edited);

            this.AwaitingOwnWriteKey=Key;
            SceneLiveBufferWriteResult Written;
            try {
                Written=await this.Workspace
                    .ApplyTextStepsAsync(Context,WriteSteps,CancellationToken).ConfigureAwait(false);
            } catch {
                // M0051 Step 1-4(バグ修正): 以前はfinallyがSavesInFlightしか戻さず、書き戻しが
                // 例外で終わるとAwaitingOwnWriteKeyが残っていた。すると次に来た
                // 無関係な再解析が自分の着地と誤判定され、Document.ResetToで未保存の編集が
                // 全て消える。書き戻しが完了しなかった経路では必ずフラグを戻す。
                this.AwaitingOwnWriteKey=null;
                throw;
            }

            if(!Written.Succeeded){
                this.AwaitingOwnWriteKey=null;
                return new SceneEditCommitResult(false,Written.ErrorMessage,WasRebased:false);
            }

            // M0051 Step 1-6: 書き出し済みの位置をここまで進める。以降のCtrl+Zはこの位置より下を
            // 戻すときだけテキストUndoを同期実行する(SceneTextWatermarkPolicy)。
            Document.MarkWritten();
            this.TextSyncStates[Key]=SceneTextSyncPolicy.AfterWrite(Written.VersionAfterWrite);

            // M0051 Step 1-6: 自分の書き込みの着地はデバウンスを待たせない。着地しないと
            // 外部変更検知のベースラインが更新されないまま次の編集へ進んでしまう。
            this.Workspace.ScheduleAnalysisNow();

            // M0051 Step 1-4: ここまではlive bufferへの反映(タブに*が付く)だけで、ファイルは
            // 保存されていなかった。Ctrl+S一回でディスクまで完了させる(実機報告への対応)。
            // 保存自体に失敗しても書き戻しは成功しているので、編集は失われない — 警告に留める。
            if(!this.Workspace.TrySaveDocumentToDisk(Context.FilePath,out string?SaveError)){
                return new SceneEditCommitResult(
                    true,$"C#ソースへ反映しましたが、ファイル保存に失敗しました: {SaveError}",WasRebased:false);
            }

            return new SceneEditCommitResult(true,ErrorMessage:null,WasRebased:false);
        } finally {
            this.SavesInFlight.Remove(Key);
        }
    }

    /// <summary>
    /// M0051 Step 1-5: 中間構文木の列を、テキストバッファへ順に適用できる書き込みステップへ変換する。
    ///
    /// <para>
    /// M0044と同じ安全網を<b>各ステップに</b>掛ける: 構文ツリー差分は最小化されている代わりに
    /// 出所を誤るとソースを壊しうるため、適用結果が期待する全文と一致するか毎回検算し、
    /// 一致しなければそのステップだけ全文差分へ落とす。ここで全ステップ分を先に組み立てて
    /// 検算し切るので、実際の書き込み(<see cref="SceneWorkspaceService.ApplyTextStepsAsync"/>)は
    /// UIスレッド上で連続適用するだけになり、途中で破綻する余地が無い。
    /// </para>
    /// </summary>
    private static IReadOnlyList<SceneTextWriteStep>BuildWriteSteps(
        SceneWorkspaceEditContext Context,SceneSourceEditStepsResult Edited){
        List<SceneTextWriteStep>Steps=[];
        SourceText Running=Context.Text;
        SyntaxTree PreviousTree=Context.Root.SyntaxTree;

        for(int Index=0;Index<Edited.Steps.Count;Index++){
            SyntaxNode StepRoot=Edited.Steps[Index];
            string ExpectedText=StepRoot.ToFullString();
            IReadOnlyList<TextChange>Changes=SceneTextChangePlanner.Plan(PreviousTree,StepRoot.SyntaxTree);
            if(!string.Equals(Running.WithChanges(Changes).ToString(),ExpectedText,StringComparison.Ordinal)){
                Changes=SceneTextChangePlanner.Plan(Running,SourceText.From(ExpectedText,Running.Encoding));
            }

            Steps.Add(new SceneTextWriteStep(
                Running,Changes,SceneUndoTransactionNaming.Format(Edited.Labels[Index])));
            Running=Running.WithChanges(Changes);
            PreviousTree=StepRoot.SyntaxTree;
        }

        return Steps;
    }

    /// <summary>
    /// <see cref="ExternalChangeDetected"/>のダイアログで「破棄して読み込み直す」が選ばれたときに呼ぶ。
    /// 直近の実解析(<see cref="Snapshots"/>)へDocumentを丸ごと差し替える。
    /// </summary>
    internal void DiscardUnsavedChanges(SceneKey Key){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(!this.Documents.TryGetValue(Key,out SceneEditDocument?Document)
            ||!this.Snapshots.TryGetValue(Key,out SceneDocumentSnapshot?Real)){
            return;
        }

        // M0051 Step 1-6: 履歴・ウォーターマーク・テキスト同期状態は必ず3つ一緒に捨てる
        // (どれか1つでも残すと次の保存が古い部分列を再生する)。RebaseDocumentが3つまとめて面倒を見る。
        this.RebaseDocument(Key,Document,Real);
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

        // M0051 Step 1-7: 以前はここで無条件にnull化していたため、対象Sceneのモデルが載っていない
        // イベントでもフラグが消費されていた。消費されると本当の着地が外部変更として扱われ、
        // MarkSaved後はIsDirty==falseなのでFollowSilently→RebaseDocumentでウォーターマークが飛ぶ。
        // そのイベントに対象Sceneが実際に載っているときだけ消費する。
        SceneKey?AwaitingOwnWrite=this.AwaitingOwnWriteKey;
        if(SceneOwnSaveLandingPolicy.ShouldConsumeAwaitingKey(
            AwaitingOwnWrite,this.EventContainsScene(Models,AwaitingOwnWrite))){
            this.AwaitingOwnWriteKey=null;
        }

        foreach(SceneModel Model in Models){
            SceneKey?Key=this.Workspace.TryCreateSceneKey(Model);
            if(Key is not { } Value){
                continue;
            }

            bool IsOwnLanding=SceneOwnSaveLandingPolicy.IsOwnSaveLanding(AwaitingOwnWrite,Value);

            // M0051 Step 1-6: 自分の書き込み(保存・Undo/Redoの同期)の着地だけは、Documentの投影を
            // 基準にIDを引き継ぐ。セッション中に追加したノードは旧実投影に存在しないため、
            // 既定の基準(ひとつ前の実投影)だと新IDが払い直され、Document側のIDを参照している
            // 未書き出しの履歴が次の保存で解決できなくなる。
            SceneProjection?IdentityBasis=IsOwnLanding
                &&this.Documents.TryGetValue(Value,out SceneEditDocument?BasisDocument)
                ?BasisDocument.Projection
                :null;

            // M0048: VSの背景解析は対象Sceneの内容と無関係な理由でも頻繁に発火しうる。再解析イベントが
            // 来たという事実だけでDocumentを触ってよい根拠にはならない — 直前の実投影と比較して
            // 本当に変わったかを必ず経由する(SceneExternalChangePolicy参照)。
            this.Snapshots.TryGetValue(Value,out SceneDocumentSnapshot?PreviousReal);
            SceneDocumentSnapshot RealSnapshot=this.Publish(Model,Value,IdentityBasis);
            if(!this.Documents.TryGetValue(Value,out SceneEditDocument?Document)){
                if(this.PinnedSceneKey is { } Pinned&&Pinned.Equals(Value)){
                    this.PinnedSnapshot=RealSnapshot;
                    this.PinnedSnapshotChanged?.Invoke(RealSnapshot);
                }
                continue;
            }

            // M0051 Step 1-7: 自分の書き込みの着地は SceneExternalChangePolicy ではなく
            // SceneLandingDecision が扱う。Documentは既に正しい内容を持っているので、
            // リネージが一致していれば<b>何もしない</b>のが正解 — ResetToを呼ぶと履歴と
            // ウォーターマークが消え、保存後のUndoが再び遅い経路へ落ちる。
            //
            // 判定材料の作り方まで SceneLandingDecision の中に閉じてあるのが要点。Step 1-6は
            // ここで SceneDiffEngine.Compare(...).IsEmpty を使っており、EmitExpressionText の
            // 比較のせいで実機では一度も KeepDocument にならなかった(ポリシー自体は正しいのに
            // 引数の作り方が誤っていたため、ユニットテストでは捕まらなかった)。
            if(IsOwnLanding){
                switch(SceneLandingDecision.Evaluate(
                    RealSnapshot.CurrentModel,RealSnapshot.Projection,
                    Document.Model,Document.Projection,IsOwnLanding:true)){
                    case SceneLandingAction.KeepDocument:
                    case SceneLandingAction.Defer:
                        // Defer: 解析エラー中で判断材料が無い。触らずに次の正常な解析を待つ。
                        continue;
                    default:
                        // リネージが食い違った(区別できない兄弟でIDが払い直された、書き込みから
                        // 着地までの窓でユーザーがバッファを直接編集した等)。壊れた状態で進むより、
                        // 実解析結果へ戻して従来の遅い経路へ縮退させる。
                        this.RebaseDocument(Value,Document,RealSnapshot);
                        continue;
                }
            }

            bool RealContentActuallyChanged=PreviousReal?.Projection is not { }OldProjection
                ||RealSnapshot.Projection is not { }NewProjectionForDiff
                ||!SceneDiffEngine.Compare(OldProjection,NewProjectionForDiff).IsEmpty;
            SceneExternalChangeAction Action=SceneExternalChangePolicy.Evaluate(
                RealContentActuallyChanged,Document.IsDirty);

            switch(Action){
                case SceneExternalChangeAction.Ignore:
                    // 実際には何も変わっていない再解析。Document/Undo/Redo履歴に一切触れない
                    // (ここで触ると、保存直後などIsDirty==falseのタイミングで無関係な再解析が
                    // Redoスタックを黙って消してしまう — 実機で確認された退行)。
                    break;
                case SceneExternalChangeAction.FollowSilently:
                    this.RebaseDocument(Value,Document,RealSnapshot);
                    break;
                case SceneExternalChangeAction.AskUser:
                    // 未保存の編集があるのに実際に外部でソースが変わった → 呼び出し側の確認に委ねる。
                    // ここではpin中snapshotを差し替えない(在庫中の編集を守る)。
                    this.ExternalChangeDetected?.Invoke(Value);
                    break;
            }
        }
    }

    /// <summary>
    /// <paramref name="IdentityBasis"/>は<see cref="EditorNodeId"/>の引き継ぎ元。既定(null)では
    /// ひとつ前の実解析投影を使う。<b>自分の書き込みの着地でだけ</b>Documentの投影を渡す
    /// (M0051 Step 1-6、<see cref="SceneDocumentLineagePolicy"/>)。
    /// pin時・外部編集の追従・単発解析は既定のままにすること — Documentを基準にすると、
    /// 実C#にしか無いノード(ユーザーが手で書き足した分)の扱いが読めなくなる。
    /// </summary>
    private SceneDocumentSnapshot Publish(SceneModel Model,SceneKey Key,SceneProjection?IdentityBasis=null){
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
            Reconciler,
            IdentityBasis);
    }

    /// <summary>
    /// M0051 Step 1-6: Documentを実解析結果へ戻す縮退経路。履歴・ウォーターマーク・テキスト同期状態を
    /// まとめて捨てるので、必ず3つ一緒に行うこと(どれか1つでも残すと次の保存が古い部分列を再生する)。
    /// </summary>
    private void RebaseDocument(SceneKey Key,SceneEditDocument Document,SceneDocumentSnapshot RealSnapshot){
        // M0051 Step 1-7: 全か無か。以前は ResetTo を条件付きでスキップしつつ TextSyncStates.Remove は
        // 無条件に実行していたため、解析エラー中に「履歴は残るのに同期状態だけ消える」半端な縮退が
        // 起き、次のCtrl+Zが誤って「C#ソースが直接編集されたため…」を出していた。
        if(RealSnapshot.CurrentModel is not { }NewModel||RealSnapshot.Projection is not { }NewProjection){
            return;
        }

        Document.ResetTo(new SceneEditSnapshot(NewModel,NewProjection));
        this.TextSyncStates.Remove(Key);
        this.PublishDocument(Key,Document);
    }

    /// <summary>
    /// M0051 Step 1-7: 未登録キーの既定を<see cref="SceneTextSyncPolicy.Empty"/>にする。
    /// <c>TryGetValue</c>のout既定は<c>BufferVersion=0</c>で、版番号0のバッファと偶然一致しうる。
    /// </summary>
    private SceneTextSyncState GetSyncState(SceneKey Key)=>
        this.TextSyncStates.TryGetValue(Key,out SceneTextSyncState State)?State:SceneTextSyncPolicy.Empty;

    /// <summary>この再解析イベントに対象Sceneのモデルが載っているか。</summary>
    private bool EventContainsScene(IReadOnlyList<SceneModel>Models,SceneKey?Key){
        if(Key is not { }Target){
            return false;
        }

        foreach(SceneModel Model in Models){
            if(this.Workspace.TryCreateSceneKey(Model)is { }Candidate&&Candidate.Equals(Target)){
                return true;
            }
        }

        return false;
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
