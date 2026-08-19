namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0051 Step 1-4: 再解析(実C#由来)のモデルを<see cref="SceneEditorHub"/>へ流してよいかの純粋な判定。
///
/// <para>
/// <b>解決する不具合</b>: Hierarchyで編集した直後にファイル保存等で再解析が走ると、Hubが
/// <b>C#由来の未編集モデル</b>で上書きされ、Hierarchyの木がその古い内容から再構築されて
/// 編集が消えたように見えていた(実機報告「c#ソースを正本として再構築されて取り消される」の実体)。
/// </para>
///
/// <para>
/// 原因はM0048の配線の取り残し。M0048はDocument更新をHubへ橋渡しする経路
/// (<c>PinnedSnapshotChanged</c>→<c>PublishSceneModel</c>)を追加したが、それ以前からあった
/// <c>SceneWorkspaceService.ModelChanged</c>→<c>PublishSceneModel</c>の直接購読を残したため、
/// 2つの発行元が競合していた。さらに<see cref="SceneExternalChangePolicy"/>が
/// <c>Ignore</c>(テキストは実際には変わっていない)を返す場合は<c>PublishDocument</c>が
/// 呼ばれないため、Hubには古いモデルが残り続けた。
/// </para>
///
/// <para>
/// <b>規則</b>: pin中のSceneと同じものが再解析されてきたときは、Documentが持つ編集済みモデルの方が
/// 新しいので再解析由来のモデルを流さない。別のScene(または未pin)なら従来どおり流す —
/// Hierarchyはpinしていない文書のSceneも表示するため、ここを塞ぐと通常の表示が壊れる。
/// </para>
/// </summary>
internal static class SceneHubPublicationPolicy {
    /// <summary>
    /// <paramref name="AnalyzedClassFullyQualifiedName"/>は再解析されたSceneの完全修飾名
    /// (対象が無ければnull)。<paramref name="PinnedClassFullyQualifiedName"/>は現在pin中のSceneの
    /// 完全修飾名(未pinならnull)。
    /// </summary>
    internal static bool ShouldPublishAnalyzedModel(
        string?AnalyzedClassFullyQualifiedName,
        string?PinnedClassFullyQualifiedName,
        bool PinnedDocumentHasUnsavedEdits){
        if(!PinnedDocumentHasUnsavedEdits||PinnedClassFullyQualifiedName is null){
            return true;
        }

        // 解析対象が無くなった(null)通知も塞ぐ。アクティブ文書が一時的に別ファイルへ移った等で
        // nullが流れると、未保存の編集を持つpin中SceneがHierarchyから消えてしまう。
        if(AnalyzedClassFullyQualifiedName is null){
            return false;
        }

        return !string.Equals(
            AnalyzedClassFullyQualifiedName,PinnedClassFullyQualifiedName,System.StringComparison.Ordinal);
    }
}
