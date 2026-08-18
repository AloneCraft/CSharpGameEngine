namespace CSharpGameEngine.VSExtension;

internal enum SceneExternalChangeAction { Ignore, FollowSilently, AskUser, TrustOwnSave }

/// <summary>
/// M0048: <see cref="SceneEditorSessionService.OnWorkspaceModelsChanged"/>が再解析イベントを
/// どう扱うべきかの純粋判定。VSの背景解析は対象Sceneの内容と無関係な理由でも頻繁に発火しうるため、
/// 「再解析イベントが来た」という事実だけでは<see cref="SceneEditDocument"/>を触ってよい根拠に
/// ならない — <c>RealContentActuallyChanged</c>(実投影を比較して本当に変わったか)を必ず経由する。
/// </summary>
internal static class SceneExternalChangePolicy {
    internal static SceneExternalChangeAction Evaluate(
        bool IsOwnSaveLanding,bool RealContentActuallyChanged,bool HasUnsavedChanges){
        if(IsOwnSaveLanding){
            return SceneExternalChangeAction.TrustOwnSave;
        }
        if(!RealContentActuallyChanged){
            return SceneExternalChangeAction.Ignore;
        }
        return HasUnsavedChanges?SceneExternalChangeAction.AskUser:SceneExternalChangeAction.FollowSilently;
    }
}
