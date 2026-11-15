namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene DesignerがEdit.Undo / Edit.Redoを引き受けてよいかの純粋な判定。
/// キーの検知自体は<see cref="SceneHwndHost.TickUndoRedoIfNeeded"/>のポーリングと
/// <see cref="SceneDesignerInputRoutingPolicy"/>が担い、ここは<b>Session側の状態</b>
/// (pin中Sceneの有無・書き戻しの飛行中)を加えた最終判定を行う二段目。
///
/// <para>
/// <b>M0051 Step 1-5でビューポート限定をやめた。</b> 以前は3Dビューポートにキーボードフォーカスが
/// あるときだけ引き受けていたため、Hierarchyツリーで編集した直後のCtrl+Zがどこにも届かなかった
/// (実機報告「ヒエラルキーは取り消しできない」の直接原因 — 編集履歴自体はラベル付きで
/// 正しく積まれており、欠けていたのは入力経路だけだった)。判定の広さは
/// <see cref="SceneDesignerInputRoutingPolicy"/>および<see cref="SceneSaveTriggerPolicy"/>と揃える。
/// </para>
///
/// <para>
/// M0037で試した「ペイン自身を<c>OleMenuCommandService</c>へ標準コマンドとして登録する」方式は
/// VS全体のUndo管理を壊す実機回帰を招き、M0045で全廃した。<b>その全廃は維持する</b> —
/// 今回広げたのはポーリングの門番の範囲だけで、標準コマンドテーブルには依然として一切触れない。
/// なお登録方式を前提にした<c>Supported</c>/<c>QueryStatus</c>の概念はもう存在しないため、
/// 「Supportedを落とすと別ドキュメントのUndoが走る」というM0038当時の記述は削除した。
/// </para>
/// </summary>
internal static class SceneDesignerUndoCommandPolicy {
    /// <summary>
    /// <paramref name="AnyDesignerSurfaceHasFocus"/>は3Dビューポート・Hierarchy・Inspectorの
    /// いずれかにキーボードフォーカスがあること(テキスト入力中の除外は
    /// <see cref="SceneDesignerInputRoutingPolicy"/>が既に済ませている)。
    /// </summary>
    internal static bool ShouldHandle(bool AnyDesignerSurfaceHasFocus,bool HasPinnedSnapshot)=>
        AnyDesignerSurfaceHasFocus&&HasPinnedSnapshot;

    /// <summary>
    /// M0038: 実際にUndo/Redoを走らせてよいか。<see cref="ShouldHandle"/>に加えて
    /// <b>ソース書き戻しが飛行中でないこと</b>を要求する。
    ///
    /// <para>
    /// 編集は予測を配信した時点でローカルには確定するが、C#ソースへの書き戻しは背後で続く。
    /// その窓でCtrl+Zを通すと、テキストUndo(まだ今回の編集が積まれていない履歴を1つ戻す)と
    /// メモリ内Undo(今回の編集を戻す)が食い違い、ソースと表示がずれる。ここで弾いておけば
    /// 書き戻し完了後の押し直しで正しく動く。同じ理由から
    /// <see cref="SceneTextUndoDelegationPolicy.Decide"/>も飛行中は<c>None</c>を返す。
    /// </para>
    /// </summary>
    internal static bool CanExecute(
        bool AnyDesignerSurfaceHasFocus,bool HasPinnedSnapshot,bool HasPendingSourceWrite)=>
        ShouldHandle(AnyDesignerSurfaceHasFocus,HasPinnedSnapshot)&&!HasPendingSourceWrite;

    /// <summary>
    /// M0095 1-2：この designer がキーボードの Undo、Redo、Save を所有するかどうか。開いているシーン tab はすべてキーを監視し、Hierarchy または Inspector にフォーカスがある場合も全 tab が対象になります。そのため各 tab が同じキー入力に応答すると、固定中のシーンが tab の数だけ進んでしまいます。固定中のシーンを表示する designer だけがキー操作を所有します。固定されたシーンのない designer（tool window）は pin に追従します。
    /// </summary>
    internal static bool IsKeyboardOwner(SceneKey?FixedSceneKey,SceneKey?PinnedSceneKey)=>
        PinnedSceneKey is { }Pinned&&(FixedSceneKey is not { }Fixed||Fixed.Equals(Pinned));
}
