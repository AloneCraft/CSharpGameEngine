namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 対象<c>.cs</c>のテキスト側Undo履歴と、Scene Designerの同期状態。
/// <see cref="SceneTextSyncPolicy"/>が遷移を決める。
/// </summary>
internal readonly struct SceneTextSyncState {
    internal SceneTextSyncState(int BufferVersion,int RedoableUnits){
        this.BufferVersion=BufferVersion;
        this.RedoableUnits=RedoableUnits;
    }

    /// <summary>自分が最後にバッファを触った直後の<c>ITextSnapshot.Version.VersionNumber</c>(未書き込みなら-1)。</summary>
    internal int BufferVersion { get; }

    /// <summary>
    /// テキスト側のRedoスタックに残っている、自分が戻したトランザクションの数。
    /// <b>ウォーターマークでは代替できない唯一の情報</b> — 「メモリ内だけ戻した」場合と
    /// 「テキストごと戻した」場合が、どちらも<c>HistoryCount==TextWatermark</c>に見えるため。
    /// </summary>
    internal int RedoableUnits { get; }
}

/// <summary>
/// M0051 Step 1-6: テキスト側のUndo/Redoを自分のものとして実行してよいかの純粋な判定。
///
/// <para>
/// <b>なぜ説明文で判定しないのか(Step 1-5の実機で判明)</b>: VSの<c>ITextUndoHistory</c>実装
/// (<c>Microsoft.VisualStudio.Editor.Implementation.Undo.VsUndoHistory</c>)は
/// <c>LastUndoTransaction</c>/<c>LastRedoTransaction</c>/<c>UndoStack</c>/<c>RedoStack</c>を
/// 実装しておらず<see cref="System.NotSupportedException"/>を投げる。使えるのは
/// <c>CanUndo</c>/<c>CanRedo</c>/<c>Undo(n)</c>/<c>Redo(n)</c>/<c>CreateTransaction</c>だけ。
/// </para>
///
/// <para>
/// <b>版番号で「自分が最後に触った」ことを確かめる。</b> 保存のあとユーザーがコードエディタで
/// 手打ちすると、テキストUndoスタックの一番上はユーザーの編集になる。そこで<c>Undo(1)</c>すると
/// 他人の編集を巻き戻してしまう。<c>ITextSnapshot.Version.VersionNumber</c>は単調増加なので、
/// 自分の操作直後に記録した値と現在値の一致が「以降だれもバッファを触っていない」ことの証明になる。
/// </para>
///
/// <para>
/// <b>Step 1-5からの変更</b>: <c>UndoableUnits</c>は
/// <see cref="SceneEditDocument.TextWatermark"/>と同じ量の別表現だったので削除した。
/// 二重に持つと片方だけずれたときに壊れ方が読めなくなる。
/// </para>
/// </summary>
internal static class SceneTextSyncPolicy {
    internal static readonly SceneTextSyncState Empty=new(BufferVersion:-1,RedoableUnits:0);

    /// <summary>
    /// 保存がテキストへ書き出した直後。新しい編集を積めばテキスト側のRedoスタックは捨てられるので
    /// <see cref="SceneTextSyncState.RedoableUnits"/>は0。
    ///
    /// <para>
    /// 書き込み前の版番号を見る必要は無い — <c>SceneTextBufferApplyGuard</c>が書き込み時点で
    /// バッファ内容が期待どおりであることを既に検証しており、ユーザーの手打ちがあれば
    /// そもそも書き込みが「編集中にC#ソースが変化したため上書きを中止しました」で失敗する。
    /// </para>
    /// </summary>
    internal static SceneTextSyncState AfterWrite(int VersionAfterWrite)=>
        new(VersionAfterWrite,RedoableUnits:0);

    internal static SceneTextSyncState AfterUndo(SceneTextSyncState State,int VersionAfterUndo)=>
        new(VersionAfterUndo,State.RedoableUnits+1);

    internal static SceneTextSyncState AfterRedo(SceneTextSyncState State,int VersionAfterRedo)=>
        new(VersionAfterRedo,State.RedoableUnits>0?State.RedoableUnits-1:0);

    /// <summary>
    /// テキストに触れない新しい編集が積まれたとき。Documentの<c>RedoStack</c>が捨てられるため、
    /// テキスト側に残っているRedo単位はもう<b>対応物が無い</b> — ここで0にしないと、
    /// 「メモリ内だけ戻した編集」をRedoしたときに<b>無関係な古いテキストRedoが走って乖離する</b>。
    /// 版番号は自分が触っていないので保つ。
    /// </summary>
    internal static SceneTextSyncState AfterInMemoryEdit(SceneTextSyncState State)=>
        new(State.BufferVersion,RedoableUnits:0);

    /// <summary>
    /// テキスト側のUndoを実行してよいか。<b>件数は見ない</b> —
    /// 「テキストに自分の単位が残っているか」は<see cref="SceneEditDocument.TextWatermark"/>が持ち、
    /// 呼び出し側の<see cref="SceneTextWatermarkPolicy.DecideUndo"/>が既に確かめている。
    /// </summary>
    internal static bool CanUndoText(SceneTextSyncState State,int CurrentBufferVersion)=>
        State.BufferVersion==CurrentBufferVersion;

    internal static bool CanRedoText(SceneTextSyncState State,int CurrentBufferVersion)=>
        State.RedoableUnits>0&&State.BufferVersion==CurrentBufferVersion;
}
