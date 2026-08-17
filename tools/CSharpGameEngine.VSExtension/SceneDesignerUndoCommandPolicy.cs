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
}
