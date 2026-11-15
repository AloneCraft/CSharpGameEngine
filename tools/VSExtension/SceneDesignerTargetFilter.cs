namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene Designer がシーンを表示する条件。session に追従する場合はすべてのシーン、Scene Editor tab の場合は自身のシーンだけです（ADR-0071）。
/// </summary>
internal static class SceneDesignerTargetFilter {
    internal static bool Accepts(SceneKey?FixedSceneKey,SceneKey?Incoming)=>
        FixedSceneKey is not { }Fixed||Incoming is { }Key&&Key.Equals(Fixed);
    /// <summary>
    /// Scene Editor tab を閉じたときに session の Scene の pin を解除する条件。tab が固定中の Scene を表示している場合だけです。これにより Hierarchy と Inspector が閉じたドキュメントを編集し続けるのを防ぎます（M0095 1-4）。
    /// </summary>
    internal static bool ReleasesPinOnClose(SceneKey?TabSceneKey,SceneKey?PinnedSceneKey)=>
        TabSceneKey is { }Tab&&PinnedSceneKey is { }Pinned&&Tab.Equals(Pinned);
}
