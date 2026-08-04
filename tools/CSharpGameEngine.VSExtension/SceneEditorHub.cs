using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// In-process mediator connecting the "シーン" document window (<see cref="SceneWindowPane"/>/
/// <see cref="SceneHwndHost"/>) to the always-visible "ヒエラルキー" tool window
/// (<see cref="HierarchyToolWindowPane"/>), ADR-0020: opening a scene publishes its node tree, and
/// selecting a Hierarchy node publishes a one-directional highlight request back to the Scene view
/// (never the reverse - clicking an object in the Scene view does not select it in Hierarchy).
/// Known limitation: only the most-recently-opened scene is tracked ("last one wins") - this repo
/// has a single scene file today, and Docs/EditorSpec.md §6 excludes multi-scene transitions from
/// scope anyway.
/// </summary>
internal static class SceneEditorHub
{
    public static event Action<SceneFile, string>? SceneOpened;

    public static event Action<Guid?>? SelectedNodeChanged;

    public static (SceneFile Scene, string Path)? ActiveScene { get; private set; }

    public static void PublishSceneOpened(SceneFile scene, string path)
    {
        ActiveScene = (scene, path);
        SceneOpened?.Invoke(scene, path);
    }

    public static void PublishSelection(Guid? nodeId) => SelectedNodeChanged?.Invoke(nodeId);
}
