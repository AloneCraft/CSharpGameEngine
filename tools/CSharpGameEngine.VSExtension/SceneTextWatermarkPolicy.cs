namespace CSharpGameEngine.VSExtension;

/// <summary>Ctrl+Z / Ctrl+Y をどう処理するか。<see cref="SceneTextWatermarkPolicy"/>の結果。</summary>
internal enum SceneUndoAction {
    /// <summary>何もしない。</summary>
    None,

    /// <summary>メモリ内スタックだけ動かす(その編集はまだテキストへ書かれていない)。</summary>
    InMemoryOnly,

    /// <summary>メモリ内スタックを動かし、あわせてテキスト側のUndo/Redoも同期実行する。</summary>
    InMemoryWithText,
}

/// <summary>
/// M0051 Step 1-6: Ctrl+Z / Ctrl+Y の行き先を、<b>ウォーターマーク</b>から決める純粋な判定。
///
/// <para>
/// <b>解決する問題</b>: Step 1-5は保存の着地でUndoスタックを捨て、保存後のCtrl+Zを
/// テキストUndoへ<b>委譲</b>していた。委譲は表示を更新しないため、追従はRoslyn再解析待ちになり
/// 「メモリ上のctrl+z,ctrl+yは早い。保存すると遅くなる」という実機報告になった。
/// </para>
///
/// <para>
/// Step 1-6ではメモリ内スタックをセッション全体で保持し、<b>Undo/Redoは常にメモリ内が駆動する</b>。
/// テキストへの反映は「メモリ内Undoの副作用」に格下げする。これで表示は保存の前後を問わず即時になり、
/// C#ソースも同じ瞬間に戻る(ユーザー確認済みの仕様)。
/// </para>
///
/// <para>
/// <b>不変条件</b>: <c>0 ≤ TextWatermark ≤ HistoryCount</c> かつ
/// 「C#テキスト == History[0..TextWatermark) を再生した状態」。
/// この不変条件を破る動作は絶対に許さない — 破ると次の保存が書き出す部分列がずれ、
/// 「保存時に対象Nodeの構文を特定できません」で保存不能になる。
/// </para>
///
/// <para>
/// <b>テキストと同期できないときは何もしない。</b> ユーザーがコードエディタで直接C#を編集した場合、
/// テキスト側の一番上は自分のトランザクションではないので<c>Undo(1)</c>してはいけない。
/// かといってメモリ内だけ戻すと上記の不変条件が壊れる(テキストがメモリより先に進んだ状態は
/// ウォーターマークで表現できない)。前向きのテキスト編集で強制同期する案は、新しい
/// トランザクションを積むことになり「1操作=1 Undo単位」を壊すので採らない。
/// </para>
/// </summary>
internal static class SceneTextWatermarkPolicy {
    /// <summary>
    /// <paramref name="HistoryCount"/>は<see cref="SceneEditDocument.History"/>の件数、
    /// <paramref name="TextWatermark"/>はそのうちテキストへ書き込み済みの件数。
    /// <paramref name="CanSyncText"/>はテキスト側のUndoを自分のものとして実行してよいか
    /// (<see cref="SceneTextSyncPolicy.CanUndoText"/>)。
    /// </summary>
    internal static SceneUndoAction DecideUndo(
        int HistoryCount,int TextWatermark,bool CanSyncText,bool IsSaving){
        if(IsSaving||HistoryCount<=0){
            return SceneUndoAction.None;
        }

        // まだテキストへ書いていない編集を戻すだけなら、テキストは無関係。
        if(HistoryCount>TextWatermark){
            return SceneUndoAction.InMemoryOnly;
        }

        return CanSyncText?SceneUndoAction.InMemoryWithText:SceneUndoAction.None;
    }

    /// <summary>
    /// Redo。<paramref name="CanSyncText"/>には<see cref="SceneTextSyncPolicy.CanRedoText"/>を渡すこと —
    /// 「テキスト側に自分が戻した単位が残っているか」はウォーターマークでは判別できない
    /// (メモリ内だけ戻した場合とテキストごと戻した場合が、どちらも<c>HistoryCount==TextWatermark</c>に
    /// 見えるため)。
    ///
    /// <para>
    /// <b>Undoと非対称に、テキストが追随できなくても進める。</b> Redoでメモリがテキストより先へ出る状態は
    /// 「まだ保存していない編集がある」そのものでウォーターマークで表現できる。逆にUndoでメモリが
    /// テキストより<b>後ろ</b>へ下がる状態は表現できないので、あちらは止めるしかない。
    /// </para>
    /// </summary>
    internal static SceneUndoAction DecideRedo(
        int RedoCount,int HistoryCount,int TextWatermark,bool CanSyncText,bool IsSaving){
        if(IsSaving||RedoCount<=0){
            return SceneUndoAction.None;
        }

        if(HistoryCount>TextWatermark){
            return SceneUndoAction.InMemoryOnly;
        }

        return CanSyncText?SceneUndoAction.InMemoryWithText:SceneUndoAction.InMemoryOnly;
    }
}

/// <summary>保存が書き出す範囲。<see cref="SceneSaveSuffixPolicy"/>の結果。</summary>
internal enum SceneSaveSuffixKind {
    /// <summary>テキストは既にDocumentと同じ位置にある。書き出すものが無い。</summary>
    AlreadyInSync,

    /// <summary>ウォーターマーク以降の履歴を書き出す。</summary>
    WriteSuffix,
}

/// <summary>
/// M0051 Step 1-6: 保存が履歴のどこから書き出すかの純粋な判定。
///
/// <para>
/// <b>これが無いと保存が壊れる</b>: ウォーターマークより下へ潜った(=保存済みの編集をUndoした)状態で
/// Ctrl+Sすると部分列が空になる。空を<see cref="SceneSourceEditor.ApplyForSaveSteps"/>へ渡すと
/// <c>FinalRoot</c>が<see langword="null"/>になり「保存に失敗しました」と表示される。
/// 潜った直後はテキストも一緒に戻っている(<see cref="SceneUndoAction.InMemoryWithText"/>)ので、
/// 実際には<b>既に同期済み</b>であり、保存は成功として何もしないのが正しい。
/// </para>
/// </summary>
internal static class SceneSaveSuffixPolicy {
    internal static SceneSaveSuffixKind Decide(int HistoryCount,int TextWatermark)=>
        HistoryCount<=TextWatermark?SceneSaveSuffixKind.AlreadyInSync:SceneSaveSuffixKind.WriteSuffix;
}

/// <summary>着地した再解析をどう扱うか。<see cref="SceneLandingDecision"/>の結果。</summary>
internal enum SceneLandingAction {
    /// <summary>Documentをそのまま維持する(高速経路)。履歴もウォーターマークも保つ。</summary>
    KeepDocument,

    /// <summary>Documentを実解析結果へ差し替える。履歴とウォーターマークは失われる。</summary>
    Rebase,

    /// <summary>
    /// M0051 Step 1-7: 判断材料が揃っていない(解析エラー中)。<b>何もしない</b>。
    /// ここで直前の良い解析結果へフォールバックして比較すると「一致」と誤判定し、
    /// いま届いたエラー状態を見逃す。次の正常な解析を待つのが正しい。
    /// </summary>
    Defer,
}
