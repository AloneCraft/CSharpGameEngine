using System;
using CSharpGameEngine.SceneAnalysis;

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

    // ------------------------------------------------------------------------------------------
    // ADR-0040: the Scene DSL channel. C# source is the single source of truth, so the model here
    // comes from live Roslyn rather than a .scene file.
    //
    // This runs alongside the SceneFile channel above rather than replacing it outright: the 3D
    // Scene view still renders from .scene until it is moved onto SceneModel too. Keeping both
    // channels for exactly one step means neither the Hierarchy nor the Scene view is ever broken
    // mid-migration. The SceneFile channel and everything above this line go away with that step.
    // ------------------------------------------------------------------------------------------

    /// <summary>Raised whenever the analysed scene changes - including to null, when the active document declares none.</summary>
    public static event Action<SceneModel?>?SceneModelChanged;

    /// <summary>Selection is keyed by <see cref="NodePath"/>: names are optional and not unique, and text spans move on every edit.</summary>
    public static event Action<NodePath?>?SelectedNodePathChanged;

    public static SceneModel?ActiveSceneModel { get;private set;}

    public static NodePath?SelectedNodePath { get;private set;}

    public static void PublishSceneModel(SceneModel?Model){
        ActiveSceneModel=Model;
        SceneModelChanged?.Invoke(Model);
    }

    public static void PublishSelectedNodePath(NodePath?Path){
        SelectedNodePath=Path;
        SelectedNodePathChanged?.Invoke(Path);
    }
}
