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
    /// M0051 Step 1-5: 保存がテキストUndo履歴へ積んだトランザクションの残数と、自分が最後に触った
    /// 直後のバッファ版番号(<see cref="SceneTextUndoLedgerPolicy"/>)。委譲Undoが
    /// ユーザーの手打ち編集まで巻き戻さないための所有判定に使う。
    /// </summary>
    private readonly Dictionary<SceneKey,SceneTextUndoLedgerEntry>TextUndoLedger=[];
    private long Generation;
    private bool Disposed;

    /// <summary>
    /// 保存が書き戻した直後の再解析だけは「自分自身の反映」として扱い、外部変更確認を出さずに
    /// Documentのベースラインを黙って更新する。保存を開始した瞬間に対象Sceneのキーを入れ、
    /// その後最初に届いた再解析でnullへ戻す。
    ///
    /// <para>
    /// M0051 Step 1-5でboolから<see cref="SceneKey"/>へ変えた。boolだと「対象は問わない」判定に
    /// なるが、再解析イベントは<b>1回で複数のSceneModel</b>を運ぶため、保存した1つと同じイベントに
    /// 乗った<b>無関係なScene</b>まで<c>TrustOwnSave</c>と誤判定され、
    /// <see cref="SceneEditDocument.ResetTo"/>で未保存の編集とUndo履歴が消えていた
    /// (<see cref="SceneOwnSaveLandingPolicy"/>参照)。
    /// </para>
    /// </summary>
    private SceneKey?AwaitingOwnSaveKey;

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
        this.PublishPinnedFilePathToWorkspace();
        SceneDocumentSnapshot Published=this.EnsureDocumentAndPublish(Key,RealSnapshot);
        this.PinnedSnapshotChanged?.Invoke(Published);
        return Published;
    }

    /// <summary>
    /// M0051 Step 1-6: pin対象の<c>.cs</c>を<see cref="SceneWorkspaceService"/>へ伝える。
    /// これが無いと、その<c>.cs</c>がアクティブなコードエディタ文書でない間は再解析が来ず、
    /// Scene Designerが自分で書き換えたテキストにすら表示が追いつかない。
    /// </summary>
    private void PublishPinnedFilePathToWorkspace(){
        this.Workspace.SetPinnedSceneFilePath(
            this.PinnedSceneKey is { }Key&&this.TryGetSceneFilePath(Key,out string FilePath)?FilePath:null);
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
        this.PublishPinnedFilePathToWorkspace();
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
    /// シーンビュー・Hierarchy・InspectorのCtrl+Z/Yから呼ばれる。行き先は
    /// <see cref="SceneTextUndoDelegationPolicy"/>が決める2択:
    /// 未反映の編集が残っていればメモリ内スタックを1件戻し、無ければC#テキストのUndoへ委譲する。
    ///
    /// <para>
    /// M0036はここで常に対象<c>.cs</c>のテキストUndoを実行していた。M0047でメモリ内Documentへ
    /// 全面的に移し、テキスト経路は一度使われなくなったが、M0051 Step 1-5で
    /// <b>保存後の取り消し</b>のために復活させた(保存点より前へ戻せるのはテキスト履歴だけ)。
    /// 両者は排他ではなく、未反映分をメモリ内で消化してからテキストへ降りる直列の関係にある。
    /// </para>
    /// </summary>
    internal bool TryUndo(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.PinnedSceneKey is not { }Key
            ||!this.Documents.TryGetValue(Key,out SceneEditDocument?Document)){
            return false;
        }

        switch(SceneTextUndoDelegationPolicy.Decide(
            Document.CanUndo,this.SavesInFlight.Contains(Key),HasPinnedSnapshot:true)){
            case SceneUndoRoute.InMemory:
                if(!Document.TryUndo()){
                    return false;
                }

                this.PublishDocument(Key,Document);
                return true;
            case SceneUndoRoute.DelegateToText:
                return this.TryDelegateToText(Key,Redo:false);
            default:
                return false;
        }
    }

    internal bool TryRedo(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.PinnedSceneKey is not { }Key
            ||!this.Documents.TryGetValue(Key,out SceneEditDocument?Document)){
            return false;
        }

        switch(SceneTextUndoDelegationPolicy.Decide(
            Document.CanRedo,this.SavesInFlight.Contains(Key),HasPinnedSnapshot:true)){
            case SceneUndoRoute.InMemory:
                if(!Document.TryRedo()){
                    return false;
                }

                this.PublishDocument(Key,Document);
                return true;
            case SceneUndoRoute.DelegateToText:
                return this.TryDelegateToText(Key,Redo:true);
            default:
                return false;
        }
    }

    /// <summary>
    /// M0051 Step 1-5: メモリ内スタックが空のときのCtrl+Z / Ctrl+YをC#テキストのUndo履歴へ委譲する
    /// (実機報告「保存した後も取り消しできるようにして」への対応)。
    ///
    /// <para>
    /// 保存が着地すると<see cref="SceneEditDocument.ResetTo"/>がスタックを消すため、保存後の
    /// Ctrl+Zにはメモリ内に戻す先が無い。履歴を残しても解決しない —
    /// <see cref="SceneEditDocument"/>は逆操作を持たず、保存の書き戻しは履歴を<b>前方向に再生</b>する
    /// 設計だからで、保存点より前へ戻せるのはテキスト履歴だけ(<see cref="SceneTextUndoDelegationPolicy"/>)。
    /// </para>
    ///
    /// <para>
    /// <b>自分が積んだトランザクションのときだけ委譲する。</b> 同じテキスト履歴にはユーザーが
    /// コードエディタで手打ちした編集も積まれており、区別せず委譲するとデザイナー由来でない編集まで
    /// 巻き戻してしまう。判定は説明文の接頭辞(<see cref="SceneUndoTransactionNaming"/>)で行う。
    /// </para>
    ///
    /// <para>
    /// 委譲が成功するとテキストが変わり、通常の再解析が届く。そのとき未保存の編集は無い
    /// (<c>IsDirty==false</c>)ので<see cref="SceneExternalChangePolicy"/>は<c>FollowSilently</c>を返し、
    /// デザイナー表示は確認ダイアログ無しで自動追従する。ここで表示を触る必要は無い。
    /// </para>
    /// </summary>
    private bool TryDelegateToText(SceneKey Key,bool Redo){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(!this.TryGetSceneFilePath(Key,out string FilePath)){
            return false;
        }

        if(!this.Workspace.TryGetBufferVersion(FilePath,out int CurrentVersion)){
            return false;
        }

        this.TextUndoLedger.TryGetValue(Key,out SceneTextUndoLedgerEntry Entry);
        bool Allowed=Redo
            ?SceneTextUndoLedgerPolicy.CanDelegateRedo(Entry,CurrentVersion)
            :SceneTextUndoLedgerPolicy.CanDelegateUndo(Entry,CurrentVersion);
        if(!Allowed||!(Redo?this.Workspace.TryRedo(FilePath):this.Workspace.TryUndo(FilePath))){
            return false;
        }

        // Undo/Redoでバッファが変わるので版番号を取り直して台帳を進める。取り直せなければ
        // 以降の委譲を止める(版が合わなくなるので自然にCanDelegate*がfalseになる)。
        this.Workspace.TryGetBufferVersion(FilePath,out int VersionAfter);
        this.TextUndoLedger[Key]=Redo
            ?SceneTextUndoLedgerPolicy.AfterRedo(Entry,VersionAfter)
            :SceneTextUndoLedgerPolicy.AfterUndo(Entry,VersionAfter);

        // M0051 Step 1-6: 表示の追従はこの再解析待ちなので、デバウンスを挟まない。
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

            // 保存前の最後に実解析されたsnapshot(Publish済み)を基準にIDを引き継ぐ。実ソースが
            // pin以降に一切変化していなければ、これはpin時のProjectionと同じ形になる。
            this.Snapshots.TryGetValue(Key,out SceneDocumentSnapshot?LastReal);
            SceneProjection RealProjection=Reconciler.Reconcile(Key,Context.Model,LastReal?.Projection);

            // M0051 Step 1-5: 履歴エントリごとの中間構文木を受け取り、1操作=1 VS Undo単位で
            // 書き戻す。以前はApplyForSave(最終木だけ)を使っていたため、3回編集してから保存すると
            // 保存後のCtrl+Zが3回分まとめて戻ってしまった(確定仕様は「1操作=1 Undo単位」)。
            SceneSourceEditStepsResult Edited=SceneSourceEditor.ApplyForSaveSteps(
                Context.Root,Key,RealProjection,Document.History);
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

            this.AwaitingOwnSaveKey=Key;
            SceneLiveBufferWriteResult Written;
            try {
                Written=await this.Workspace
                    .ApplyTextStepsAsync(Context,WriteSteps,CancellationToken).ConfigureAwait(false);
            } catch {
                // M0051 Step 1-4(バグ修正): 以前はfinallyがSavesInFlightしか戻さず、書き戻しが
                // 例外で終わるとAwaitingOwnSaveKeyが残っていた。すると次に来た
                // 無関係な再解析がTrustOwnSaveと誤判定され、Document.ResetToで未保存の編集が
                // 全て消える。書き戻しが完了しなかった経路では必ずフラグを戻す。
                this.AwaitingOwnSaveKey=null;
                throw;
            }

            if(!Written.Succeeded){
                this.AwaitingOwnSaveKey=null;
                return new SceneEditCommitResult(false,Written.ErrorMessage,WasRebased:false);
            }

            Document.MarkSaved();

            // M0051 Step 1-5: 保存後のCtrl+Zはここで積んだトランザクションへ委譲する。何件積んだかと
            // 積んだ直後のバッファ版番号を控えておき、以降ユーザーがコードエディタで手打ちしたら
            // (版番号が進むので)委譲を止める。
            this.TextUndoLedger.TryGetValue(Key,out SceneTextUndoLedgerEntry PreviousLedger);
            this.TextUndoLedger[Key]=SceneTextUndoLedgerPolicy.AfterWrite(
                PreviousLedger,Written.VersionBeforeWrite,Written.VersionAfterWrite,Written.AppliedUnitCount);

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
        SceneKey?AwaitingOwnSave=this.AwaitingOwnSaveKey;
        this.AwaitingOwnSaveKey=null;

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
                SceneOwnSaveLandingPolicy.IsOwnSaveLanding(AwaitingOwnSave,Value),
                RealContentActuallyChanged,Document.IsDirty);

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
