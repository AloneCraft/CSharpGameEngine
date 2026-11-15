namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0045: Ctrl+Z/Ctrl+Yの毎フレームポーリング(<see cref="SceneHwndHost"/>)から押しっぱなし連射を
/// 除くための、立ち上がりエッジ判定だけを切り出した純粋関数。
/// </summary>
internal static class SceneUndoRedoKeyPolicy {
    internal static bool IsRisingEdge(bool CurrentlyDown,bool WasDownLastTick)=>
        CurrentlyDown&&!WasDownLastTick;
}
