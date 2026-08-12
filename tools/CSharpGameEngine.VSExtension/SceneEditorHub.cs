using System;
using System.Collections.Generic;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// In-process mediator connecting the "シーン デザイナー" tool window (<see cref="SceneDesignerToolWindowPane"/>/
/// <see cref="SceneHwndHost"/>) to the always-visible "ヒエラルキー" tool window
/// (<see cref="HierarchyToolWindowPane"/>) and the "シェイプ" palette
/// (<see cref="ShapePaletteToolWindowPane"/>/<see cref="PrimitiveSpawner"/>), ADR-0020/ADR-0021/
/// ADR-0040 Phase 9: selecting a Hierarchy node publishes a one-directional highlight request back
/// to the Scene Designer (never the reverse - clicking an object in the Scene Designer does not
/// select it in Hierarchy).
///
/// <para>
/// <see cref="ActiveScene"/>/<see cref="RequestReload"/> are the one remaining piece of the old
/// <c>.scene</c>-JSON channel (ADR-0020/ADR-0021), kept only because <see cref="SceneNodeAdder"/>'s
/// drag-and-drop/Shape-Palette write path still targets a <c>.scene</c> file (Phase 10's job to move
/// onto Roslyn-based source edits). Nothing publishes to <see cref="ActiveScene"/> any more now that
/// Scene Designer reads from <see cref="SceneModel"/> instead of a <c>.scene</c> document - so it
/// stays permanently null and that write path shows its "open a scene file first" error until
/// Phase 10 lands. This is the documented, accepted Phase 9 regression, not a bug.
/// </para>
/// </summary>
internal static class SceneEditorHub {
    /// <summary>Raised after <see cref="ActiveScene"/>'s file has been rewritten on disk (e.g. a primitive was appended, ADR-0021).</summary>
    public static event Action?ReloadRequested;

    public static(SceneFile Scene,string Path)?ActiveScene { get;private set;}

    public static void RequestReload()=>ReloadRequested?.Invoke();

    // ------------------------------------------------------------------------------------------
    // ADR-0040: the Scene DSL channel. C# source is the single source of truth, so the model here
    // comes from live Roslyn rather than a .scene file. This is the only channel Scene Designer
    // (SceneHwndHost) uses as of Phase 9 - the .scene-JSON read side above is gone.
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

    /// <summary>
    /// Every <c>IScene</c> found in the active document, not just the first (ADR-0040 Phase 9) - the
    /// Scene Designer tool window needs this to keep showing the specific class it was targeted at
    /// (e.g. one of several classes in the same .cs file) as the user keeps editing.
    /// </summary>
    public static event Action<IReadOnlyList<SceneModel>>?AllSceneModelsChanged;

    public static IReadOnlyList<SceneModel>ActiveAllSceneModels { get;private set;}=[];

    public static void PublishAllSceneModels(IReadOnlyList<SceneModel>Models){
        ActiveAllSceneModels=Models;
        AllSceneModelsChanged?.Invoke(Models);
    }

    public static void PublishSelectedNodePath(NodePath?Path){
        SelectedNodePath=Path;
        SelectedNodePathChanged?.Invoke(Path);
    }
}
