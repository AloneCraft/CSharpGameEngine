namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0051 Step 1-5: Ctrl+Z / Ctrl+Y / Ctrl+S を Scene Designer が引き受けてよいかの、
/// <b>キーボードフォーカスだけから決まる</b>純粋な判定。
///
/// <para>
/// これらのキーはVS標準のグローバルキーバインド(Edit.Undo / Edit.Redo / File.SaveSelectedItems)に
/// 割り当て済みで、シェルのアクセラレータテーブルが先に消費するためWPFの<c>KeyDown</c>には届かない。
/// 検知は<see cref="SceneHwndHost.TickUndoRedoIfNeeded"/>の<c>GetKeyState</c>ポーリング1本だけで行う。
/// </para>
///
/// <para>
/// <b>M0051 Step 1-5でUndo/RedoもSaveと同じ範囲へ広げた。</b> Step 1-4ではSaveだけを広げ、Undo/Redoは
/// 「M0037の実機回帰があるため広げない」と判断したが、その根拠は誤りだった。M0037の回帰の原因は一貫して
/// <b>標準コマンドIDを<c>OleMenuCommandService</c>へ登録したこと</b>という手段に帰属しており
/// (<c>Docs/Milestones/M0045-…</c>、<see cref="SceneDesignerToolWindowPane"/>のクラスコメント)、
/// <c>Docs/Conformance.md</c>は「M0045は今回の真因ではなかった」とまで記している。
/// <c>GetKeyState</c>ポーリングの<b>門番の範囲</b>が回帰の原因だったという記述はどこにも無い。
/// ポーリングは標準コマンドテーブルに一切触れないため、範囲を広げても回帰は再燃しない。
/// </para>
///
/// <para>
/// <b>テキスト入力中の扱いはUndo/RedoとSaveで異なる。</b> Hierarchyのインラインリネームも
/// Inspectorの数値編集も<c>TextBox</c>を使い、補助サーフェスのフォーカス判定は
/// <c>IsKeyboardFocusWithin</c>ベースなので入力中もtrueになる。
/// <list type="bullet">
/// <item>Ctrl+Z/Yは<b>除外する</b> — <c>TextBox</c>自身が文字単位のUndoを持っており、
/// 除外しないとリネーム途中のCtrl+Zが「文字の取り消し」ではなく「シーン全体のUndo」になる。</item>
/// <item>Ctrl+Sは<b>除外しない</b> — <c>TextBox</c>に「保存」の意味は無く、ここで降りると
/// VS標準の保存が未反映の<c>.cs</c>をそのまま保存してしまい、Step 1-4で直した不具合に戻る。</item>
/// </list>
/// </para>
/// </summary>
internal static class SceneDesignerInputRoutingPolicy {
    /// <summary>
    /// <paramref name="ViewportFocused"/>は3Dビューポート(<c>RenderHostGrid</c>)、
    /// <paramref name="AuxiliarySurfaceFocused"/>はHierarchy / Inspector
    /// (<see cref="SceneEditorHub.AnyAuxiliarySurfaceHasFocus"/>)。
    /// <paramref name="TextEntryFocused"/>は現在のフォーカス要素が<c>TextBoxBase</c>であること。
    /// </summary>
    internal static bool ShouldRouteUndoRedoToDesigner(
        bool ViewportFocused,bool AuxiliarySurfaceFocused,bool TextEntryFocused)=>
        !TextEntryFocused&&ShouldRouteSaveToDesigner(ViewportFocused,AuxiliarySurfaceFocused);

    /// <summary>
    /// Ctrl+Sはテキスト入力中でも引き受ける(上記の理由)。範囲そのものはUndo/Redoと同一で、
    /// <b>3つのキーで揃える</b>のがStep 1-5の方針。
    /// </summary>
    internal static bool ShouldRouteSaveToDesigner(bool ViewportFocused,bool AuxiliarySurfaceFocused)=>
        ViewportFocused||AuxiliarySurfaceFocused;
}
