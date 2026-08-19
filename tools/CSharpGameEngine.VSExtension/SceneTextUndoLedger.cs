namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 対象<c>.cs</c>のテキストUndo履歴に、Scene Designerが積んだトランザクションがいくつ残っているかの台帳。
/// <see cref="SceneTextUndoLedgerPolicy"/>が遷移を決める。
/// </summary>
internal readonly struct SceneTextUndoLedgerEntry {
    internal SceneTextUndoLedgerEntry(int BufferVersion,int UndoableUnits,int RedoableUnits){
        this.BufferVersion=BufferVersion;
        this.UndoableUnits=UndoableUnits;
        this.RedoableUnits=RedoableUnits;
    }

    /// <summary>自分が最後にバッファを触った直後の<c>ITextSnapshot.Version.VersionNumber</c>。</summary>
    internal int BufferVersion { get; }

    /// <summary>まだUndoで戻せる、自分が積んだトランザクションの数。</summary>
    internal int UndoableUnits { get; }

    /// <summary>Undo済みでRedoできる、自分が積んだトランザクションの数。</summary>
    internal int RedoableUnits { get; }
}

/// <summary>
/// M0051 Step 1-5(実機修正): 保存後のCtrl+Zをテキストへ委譲してよいかを、
/// <b>自分が積んだ単位数とバッファの版番号だけ</b>で判定する純粋な状態遷移。
///
/// <para>
/// <b>なぜ説明文で判定しないのか(実機で判明)</b>: 当初は
/// <c>ITextUndoHistory.LastUndoTransaction.Description</c>に接頭辞を付けて所有を判定していたが、
/// VSの実装(<c>Microsoft.VisualStudio.Editor.Implementation.Undo.VsUndoHistory</c>)は
/// <b><c>LastUndoTransaction</c>/<c>LastRedoTransaction</c>/<c>UndoStack</c>/<c>RedoStack</c>を
/// 実装しておらず<see cref="System.NotSupportedException"/>を投げる</b>。
/// このシムで使えるのは<c>CanUndo</c>/<c>CanRedo</c>/<c>Undo(n)</c>/<c>Redo(n)</c>/
/// <c>CreateTransaction</c>だけ。実機ではCtrl+Zのたびに<c>CompositionTarget.Rendering</c>の
/// ハンドラごと例外で落ちていた。
/// (同じ理由で、検討して見送った「<c>UndoStack</c>の深さをフレーム跨ぎで監視して二重Undoを補償する」
/// 案も実現不可能だったことになる。)
/// </para>
///
/// <para>
/// <b>版番号で「自分が最後に触った」ことを確かめる。</b> 単位数だけを数えると、保存のあとユーザーが
/// コードエディタで手打ちしたケースで、スタックの一番上がユーザーの編集なのに自分のものと誤認して
/// 巻き戻してしまう。<c>ITextSnapshot.Version.VersionNumber</c>は単調増加なので、自分の操作直後に
/// 記録した値と現在値が一致することが「以降だれもバッファを触っていない」ことの証明になる。
/// 一致しなければ委譲しない(安全側に倒す — ユーザーはコードエディタ側のCtrl+Zを使える)。
/// </para>
/// </summary>
internal static class SceneTextUndoLedgerPolicy {
    internal static readonly SceneTextUndoLedgerEntry Empty=new(BufferVersion:-1,UndoableUnits:0,RedoableUnits:0);

    /// <summary>
    /// 保存が<paramref name="UnitsWritten"/>件のトランザクションを積んだ直後の状態。
    ///
    /// <para>
    /// <paramref name="VersionBeforeWrite"/>が前回記録した版と食い違う場合は、保存の前に
    /// <b>誰かがバッファを触っている</b>ということ。その編集は自分の古い単位の<b>上</b>に積まれており、
    /// 通り抜けて下の自分の単位へ到達することは許されないので、数え直す(累積しない)。
    /// </para>
    ///
    /// <para>新しい編集を積めばRedoスタックは消えるので<see cref="SceneTextUndoLedgerEntry.RedoableUnits"/>は0。</para>
    /// </summary>
    internal static SceneTextUndoLedgerEntry AfterWrite(
        SceneTextUndoLedgerEntry Previous,int VersionBeforeWrite,int VersionAfterWrite,int UnitsWritten){
        int Carried=Previous.BufferVersion==VersionBeforeWrite?Previous.UndoableUnits:0;
        return new SceneTextUndoLedgerEntry(VersionAfterWrite,Carried+UnitsWritten,RedoableUnits:0);
    }

    internal static bool CanDelegateUndo(SceneTextUndoLedgerEntry Entry,int CurrentBufferVersion)=>
        Entry.UndoableUnits>0&&Entry.BufferVersion==CurrentBufferVersion;

    internal static bool CanDelegateRedo(SceneTextUndoLedgerEntry Entry,int CurrentBufferVersion)=>
        Entry.RedoableUnits>0&&Entry.BufferVersion==CurrentBufferVersion;

    internal static SceneTextUndoLedgerEntry AfterUndo(SceneTextUndoLedgerEntry Entry,int VersionAfterUndo)=>
        new(VersionAfterUndo,Entry.UndoableUnits-1,Entry.RedoableUnits+1);

    internal static SceneTextUndoLedgerEntry AfterRedo(SceneTextUndoLedgerEntry Entry,int VersionAfterRedo)=>
        new(VersionAfterRedo,Entry.UndoableUnits+1,Entry.RedoableUnits-1);
}
