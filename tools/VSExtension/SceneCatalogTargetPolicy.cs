using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene Designer の selector を所有するプロジェクトを選びます。固定された Scene が表示対象なので、アクティブなコードドキュメントより優先されます。
/// </summary>
internal static class SceneCatalogTargetPolicy {
    internal static string?SelectProjectId(SceneKey?PinnedSceneKey,string?ActiveProjectId)=>
        PinnedSceneKey?.ProjectId??ActiveProjectId;

    /// <summary>
    /// selector の検索は補助 UI です。catalog を失っても、すでに描画済みの Scene を切り離してはいけません。その表示を消せるのは Scene 自体の解析 lifecycle だけです。
    /// </summary>
    internal static bool KeepsPresentedScene(bool HasPinnedScene,bool HasCatalog)=>
        HasPinnedScene||HasCatalog;
}
