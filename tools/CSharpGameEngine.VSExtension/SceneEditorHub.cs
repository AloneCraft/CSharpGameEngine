using System;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 常時表示の「ヒエラルキー」ツールウィンドウ(<see cref="HierarchyToolWindowPane"/>)向けの
/// プロセス内メディエータ。C# ソースが唯一の正本なので、ここで運ぶモデルはライブ Roslyn 解析の
/// 結果(<see cref="SceneModel"/>)であり、`.scene` ファイルではない(ADR-0040)。
///
/// <para>
/// ヒエラルキーでノードを選択すると、Scene デザイナーへの一方向のハイライト要求を発行する
/// (ADR-0020 以来の一方向性。ビューポート側のクリックからヒエラルキーを選択する逆経路は
/// M0020 Phase 2 で <see cref="SceneSelectionService"/> 側へ入る)。
/// </para>
///
/// <para>
/// 旧 `.scene` JSON チャンネル(<c>ActiveScene</c>/<c>RequestReload</c>/<c>ReloadRequested</c>)と、
/// 購読者を持たなかった <c>AllSceneModels</c> 系は M0020 Phase 0 で削除した。Scene デザイナーは
/// <see cref="SceneEditorSessionService"/> の <see cref="SceneDocumentSnapshot"/> から対象を解決する。
/// </para>
/// </summary>
internal static class SceneEditorHub {
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
