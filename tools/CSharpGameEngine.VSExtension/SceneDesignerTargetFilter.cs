namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Whether a Scene Designer shows a scene: every scene when it follows the session, only its own
/// when it is a Scene Editor tab (ADR-0071).
/// </summary>
internal static class SceneDesignerTargetFilter {
    internal static bool Accepts(SceneKey?FixedSceneKey,SceneKey?Incoming)=>
        FixedSceneKey is not { }Fixed||Incoming is { }Key&&Key.Equals(Fixed);
    /// <summary>
    /// Whether closing a Scene Editor tab unpins the session's scene: only when the tab shows the pinned
    /// scene, so Hierarchy and Inspector never keep editing a closed document (M0095 1-4).
    /// </summary>
    internal static bool ReleasesPinOnClose(SceneKey?TabSceneKey,SceneKey?PinnedSceneKey)=>
        TabSceneKey is { }Tab&&PinnedSceneKey is { }Pinned&&Tab.Equals(Pinned);
}
