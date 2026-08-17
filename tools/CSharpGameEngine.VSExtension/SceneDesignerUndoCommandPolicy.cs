namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0037: <see cref="SceneDesignerToolWindowPane"/>がEdit.Undo/Redoコマンドを自分のものとして
/// 名乗り出てよいかどうかの純粋な判定。ビューポートに実際のWPFキーボードフォーカスがあり、かつ
/// 対象Sceneがpinされている場合だけ引き受ける — それ以外(コードエディタ側でのCtrl+Z等)は
/// <c>Supported=false</c>を返しシェルの通常ルーティングへ戻す。
/// </summary>
internal static class SceneDesignerUndoCommandPolicy {
    internal static bool ShouldHandle(bool ViewportHasKeyboardFocus,bool HasPinnedSnapshot)=>
        ViewportHasKeyboardFocus&&HasPinnedSnapshot;

    /// <summary>
    /// M0038: 実際にUndo/Redoを走らせてよいか。<see cref="ShouldHandle"/>に加えて
    /// <b>ソース書き戻しが飛行中でないこと</b>を要求する。
    ///
    /// <para>
    /// M0038で編集は予測を配信した時点でローカルには確定するが、C#ソースへの書き戻しは背後で続く。
    /// その窓でCtrl+Zを通すと、テキストUndo(まだ今回の編集が積まれていない履歴を1つ戻す)と
    /// 先読みUndo(今回の編集を表示上戻す)が食い違い、ソースと表示がずれる。ここで弾いておけば
    /// 書き戻し完了後の押し直しで正しく動く。
    /// </para>
    ///
    /// <para>
    /// <see cref="ShouldHandle"/>(=<c>Supported</c>)は落とさない。落とすとCtrl+Zがシェルの通常
    /// ルーティングへ戻り、<b>別のドキュメントのUndoが走ってしまう</b> — 無効化するだけに留める。
    /// </para>
    /// </summary>
    internal static bool CanExecute(
        bool ViewportHasKeyboardFocus,bool HasPinnedSnapshot,bool HasPendingSourceWrite)=>
        ShouldHandle(ViewportHasKeyboardFocus,HasPinnedSnapshot)&&!HasPendingSourceWrite;
}
