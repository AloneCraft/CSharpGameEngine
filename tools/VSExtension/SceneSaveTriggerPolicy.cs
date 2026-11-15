namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0051 Step 1-4: Ctrl+SをScene Designerが引き受けてよいかの純粋な判定。
/// <see cref="SceneDesignerInputRoutingPolicy"/>がフォーカスだけで決める一段目を通したあと、
/// <b>Session側の状態</b>を加えるのがここ(<see cref="SceneDesignerUndoCommandPolicy"/>と同じ二段構成)。
///
/// <para>
/// 実機報告「ヒエラルキーで編集してもctrl+sでc#ソースを正本として再構築されて取り消される」の
/// 直接原因は、Ctrl+Sの検知が<b>3Dビューポートのキーボードフォーカス</b>だけを条件にしていたこと
/// だった。Hierarchyツリーで編集した直後はフォーカスがHierarchy側にあるため、Ctrl+Sは
/// <c>SaveAsync</c>へ一切届かずVS標準の保存が未変更の<c>.cs</c>をそのまま保存していた。
/// ギズモ編集だけが保存できていたのは、その操作の直後はビューポートにフォーカスが残るためで、
/// 報告された非対称性と一致する。
/// </para>
///
/// <para>
/// <b>M0051 Step 1-5での訂正</b>: Step 1-4はここに「Undo/Redoの門番は意図的に広げない。M0037で
/// VS全体のUndo管理が壊れた回帰があるため」と書いたが、その根拠は誤りだった。M0037の回帰の原因は
/// 一貫して<b>標準コマンドIDを<c>OleMenuCommandService</c>へ登録したこと</b>という手段に帰属しており、
/// <c>GetKeyState</c>ポーリングの門番範囲が原因だったという記述はどこにも無い
/// (<c>Docs/Conformance.md</c>はむしろ「M0045は今回の真因ではなかった」と記している)。
/// Step 1-5でUndo/Redoも同じ範囲へ揃えた。<b>3つのキーの範囲は今後も揃えて扱うこと。</b>
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
