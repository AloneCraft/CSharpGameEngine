namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0051 Step 1-4: Ctrl+SをScene Designerが引き受けてよいかの純粋な判定。
///
/// <para>
/// <b>Undo/Redo(<see cref="SceneDesignerUndoCommandPolicy"/>)より対象範囲が広い</b>のが要点。
/// 実機報告「ヒエラルキーで編集してもctrl+sでc#ソースを正本として再構築されて取り消される」の
/// 直接原因は、Ctrl+Sの検知が<b>3Dビューポートのキーボードフォーカス</b>だけを条件にしていたこと
/// だった。Hierarchyツリーで編集した直後はフォーカスがHierarchy側にあるため、Ctrl+Sは
/// <c>SaveAsync</c>へ一切届かずVS標準の保存が未変更の<c>.cs</c>をそのまま保存していた。
/// ギズモ編集だけが保存できていたのは、その操作の直後はビューポートにフォーカスが残るためで、
/// 報告された非対称性と一致する。
/// </para>
///
/// <para>
/// <b>Undo/Redoの門番は意図的に広げない。</b> Ctrl+Z/YはM0037で「ペインを標準コマンドとして
/// 登録したらVS全体のUndo管理が壊れた」実機回帰を起こし、M0045で<c>GetKeyState</c>ポーリングへ
/// 撤退した経緯がある。誤って別ドキュメントのUndoを走らせる危険があるため、範囲を広げるのは
/// 保存だけに限定する。
/// </para>
/// </summary>
internal static class SceneSaveTriggerPolicy {
    /// <summary>
    /// <paramref name="AnyDesignerSurfaceHasFocus"/>はビューポート・Hierarchy・Inspectorの
    /// いずれかにキーボードフォーカスがあること。<paramref name="IsSaving"/>のときは弾く
    /// (Ctrl+Sの押しっぱなしや連打で保存が多重起動しないようにする)。
    /// </summary>
    internal static bool ShouldSave(
        bool AnyDesignerSurfaceHasFocus,bool HasPinnedSnapshot,bool IsSaving)=>
        AnyDesignerSurfaceHasFocus&&HasPinnedSnapshot&&!IsSaving;
}
