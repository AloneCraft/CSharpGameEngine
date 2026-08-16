using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Chooses the project that owns the Scene Designer's selector. A pinned Scene is the displayed
/// subject, so it must take precedence over whichever code document happens to be active.
/// </summary>
internal static class SceneCatalogTargetPolicy {
    internal static string?SelectProjectId(SceneKey?PinnedSceneKey,string?ActiveProjectId)=>
        PinnedSceneKey?.ProjectId??ActiveProjectId;

    /// <summary>
    /// Selector lookup is auxiliary UI. Losing its catalog must not detach an already rendered
    /// Scene; only the Scene's own analysis lifecycle may clear that presentation.
    /// </summary>
    internal static bool KeepsPresentedScene(bool HasPinnedScene,bool HasCatalog)=>
        HasPinnedScene||HasCatalog;
}
