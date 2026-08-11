using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// In-process mediator connecting the "シーン" document window (<see cref="SceneWindowPane"/>/
/// <see cref="SceneHwndHost"/>) to the always-visible "ヒエラルキー" tool window
/// (<see cref="HierarchyToolWindowPane"/>) and the "シェイプ" palette
/// (<see cref="ShapePaletteToolWindowPane"/>/<see cref="PrimitiveSpawner"/>), ADR-0020/ADR-0021:
/// opening a scene publishes its node tree, selecting a Hierarchy node publishes a one-directional
/// highlight request back to the Scene view (never the reverse - clicking an object in the Scene
/// view does not select it in Hierarchy), and spawning a primitive from the palette requests the
/// active Scene view reload the file it just wrote to disk. Known limitation: only the
/// most-recently-opened scene is tracked ("last one wins") - this repo has a single scene file
/// today, and Docs/EditorSpec.md §6 excludes multi-scene transitions from scope anyway.
/// </summary>
internal static class SceneEditorHub {
    public static event Action<SceneFile,string>?SceneOpened;

    public static event Action<Guid?>?SelectedNodeChanged;

    /// <summary>Raised after <see cref="ActiveScene"/>'s file has been rewritten on disk (e.g. a primitive was appended, ADR-0021) - the Scene view re-reads its own path in response.</summary>
    public static event Action?ReloadRequested;

    public static(SceneFile Scene,string Path)?ActiveScene { get;private set;}

    public static void PublishSceneOpened(SceneFile Scene,string Path){
        ActiveScene=(Scene,Path);
        SceneOpened?.Invoke(Scene,Path);
    }

    public static void PublishSelection(Guid?NodeId)=>SelectedNodeChanged?.Invoke(NodeId);

    public static void RequestReload()=>ReloadRequested?.Invoke();
}
