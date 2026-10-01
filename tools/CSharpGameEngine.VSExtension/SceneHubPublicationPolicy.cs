namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0051 Step 1-4: 再解析(シーン JSON 由来、ADR-0070)のモデルを<see cref="SceneEditorHub"/>へ流してよいかの純粋な判定。
///
/// <para>
/// <b>解決する不具合</b>: Hierarchyで編集した直後にファイル保存等で再解析が走ると、Hubが
/// <b>C#由来の未編集モデル</b>で上書きされ、Hierarchyの木がその古い内容から再構築されて
/// 編集が消えたように見えていた(実機報告「c#ソースを正本として再構築されて取り消される」の実体)。
/// </para>
///
/// <para>
    /// 原因は同じHubにWorkspaceとSessionの2経路から異なる世代のmodelを発行していたこと。
    /// 現在はSessionだけがpin中sceneのprojectionを発行し、WorkspaceのRoslyn更新をそこで直列化する。
/// </para>
///
/// <para>
/// M0053: 判定の軸を「未保存の編集を守るか」から<b>「pin中Sceneの表示を守るか」</b>へ組み替えた。
/// 以前は<c>PinnedDocumentHasUnsavedEdits</c>が偽なら無条件に流していたため、
/// <b>シーン デザイナーでSceneを開いた直後(まだ何も編集していない)にコードエディタのタブを
/// Sceneを宣言しない.csへ切り替えるだけでHierarchyが空になっていた</b>(実機報告)。
/// <c>SceneWorkspaceService</c>は文書切替のたびに無条件で<c>ModelChanged(null)</c>を発行するので、
/// そのnullがそのままHubへ届いていたのが実体。表示を消してよいかは「pin中かどうか」で決まり、
/// 編集の有無とは無関係だった。
/// </para>
///
/// <para>
/// <b>規則</b>: 未pinなら全部流す(Sceneファイルを開いただけでHierarchyに出る入口を維持する)。
/// pin中は、シーン デザイナーの表示とHierarchyを常に一致させるため<b>pin中Sceneを見せ続ける</b> —
/// 解析対象が無くなったnull通知も、別Sceneの解析も流さない。同じSceneの再解析だけは、
/// 未保存の編集がなければ流す(保存直後や外部編集の追従がここを通る)。
/// </para>
///
/// <para>
/// pinの解除は<c>SceneEditorSessionService.ClearPin</c>だけが行い、それはシーン デザイナー内の
/// 明示的な操作(対象の選択解除・解決不能・例外)と、pin中Sceneのタブを閉じたとき(M0095)だけ呼ばれる。
/// pin中はHubのモデルが常にpin中Sceneなので、Hierarchy/Inspectorの編集(<c>EnsurePinned</c>)が
/// 別Sceneへpinを移すことはない。よって
/// 「シーン デザイナーを閉じたらHierarchyも空になる」はこの規則でも保たれる。
/// </para>
/// </summary>
internal static class SceneHubPublicationPolicy {
    /// <summary>
    /// <paramref name="AnalyzedClassFullyQualifiedName"/>は再解析されたSceneの完全修飾名
    /// (対象が無ければnull)。<paramref name="PinnedClassFullyQualifiedName"/>は現在pin中のSceneの
    /// 完全修飾名(未pinならnull)。<paramref name="PinnedDocumentHasUnsavedEdits"/>は
    /// <b>pin中Sceneと同じものが再解析されてきた場合にだけ</b>効く。
    /// </summary>
    internal static bool ShouldPublishAnalyzedModel(
        string?AnalyzedClassFullyQualifiedName,
        string?PinnedClassFullyQualifiedName,
        bool PinnedDocumentHasUnsavedEdits){
        if(PinnedClassFullyQualifiedName is null){
            return true;
        }

        // 解析対象が無くなった(null)通知は、pin中である限り常に塞ぐ。アクティブ文書が
        // Sceneを宣言しない.csへ移っただけでpin中SceneがHierarchyから消えてしまうため。
        if(AnalyzedClassFullyQualifiedName is null){
            return false;
        }

        // 別Sceneの解析も流さない。Hierarchyとシーン デザイナーが別のSceneを指す状態を作らない
        // (選択・Inspector・ギズモの整合が崩れる)。別Sceneへ移るのは明示的なpin切替だけ。
        if(!string.Equals(
            AnalyzedClassFullyQualifiedName,PinnedClassFullyQualifiedName,System.StringComparison.Ordinal)){
            return false;
        }

        // 同じSceneの再解析。未保存の編集があるならDocumentが持つ編集済みモデルの方が新しい
        // (M0051 Step 1-4: Hierarchyで編集した直後の再解析で編集が消えたように見えた不具合)。
        return !PinnedDocumentHasUnsavedEdits;
    }
}
