namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Whether a Scene Designer shows a scene: every scene when it follows the session, only its own
/// when it is a Scene Editor tab (ADR-0071).
/// </summary>
internal static class SceneDesignerTargetFilter {
    internal static bool Accepts(SceneKey?FixedSceneKey,SceneKey?Incoming)=>
        FixedSceneKey is not { }Fixed||Incoming is { }Key&&Key.Equals(Fixed);
}
