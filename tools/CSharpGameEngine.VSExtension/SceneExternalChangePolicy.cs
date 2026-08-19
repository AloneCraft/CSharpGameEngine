namespace CSharpGameEngine.VSExtension;

internal enum SceneExternalChangeAction { Ignore, FollowSilently, AskUser }

/// <summary>
/// M0048: <see cref="SceneEditorSessionService.OnWorkspaceModelsChanged"/>が
/// <b>外部由来の</b>再解析イベントをどう扱うべきかの純粋判定。VSの背景解析は対象Sceneの内容と
/// 無関係な理由でも頻繁に発火しうるため、「再解析イベントが来た」という事実だけでは
/// <see cref="SceneEditDocument"/>を触ってよい根拠にならない —
/// <c>RealContentActuallyChanged</c>(実投影を比較して本当に変わったか)を必ず経由する。
///
/// <para>
/// M0051 Step 1-6で<c>IsOwnSaveLanding</c>/<c>TrustOwnSave</c>を外した。<b>自分の書き込み</b>
/// (保存の書き戻し、Undo/Redoのテキスト同期)の着地は、外部変更かどうかではなく
/// <b>Documentと実解析のリネージが一致しているか</b>で扱いが決まるため、
/// <see cref="SceneDocumentLineagePolicy"/>という別の判定へ分離した。
/// </para>
/// </summary>
internal static class SceneExternalChangePolicy {
    internal static SceneExternalChangeAction Evaluate(
        bool RealContentActuallyChanged,bool HasUnsavedChanges){
        if(!RealContentActuallyChanged){
            return SceneExternalChangeAction.Ignore;
        }

        return HasUnsavedChanges?SceneExternalChangeAction.AskUser:SceneExternalChangeAction.FollowSilently;
    }
}
