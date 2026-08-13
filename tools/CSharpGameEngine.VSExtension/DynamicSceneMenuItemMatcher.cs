namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VS の DynamicItemStart 契約を VS Shell 型から切り離した小さな状態変換です。
/// root プレースホルダーは OleMenuCommand が DynamicItemMatch を呼ばないため matched ID 0 のまま
/// QueryStatus へ来ることがあり、その場合も先頭の項目として扱います。
/// </summary>
internal static class DynamicSceneMenuItemMatcher {
    /// <summary>
    /// root は先頭 Scene を表すため、動的 ID としては root の次から最後の Scene までだけを一致させる。
    /// 最後の次の ID で false を返すことが VS の動的リスト終了通知になる。
    /// </summary>
    internal static bool TryMatch(
        int FirstCommandId,int CandidateCommandId,int SceneCount,out int MatchedCommandId){
        int ItemIndex=CandidateCommandId-FirstCommandId;
        if(SceneCount>1&&ItemIndex>0&&ItemIndex<SceneCount){
            MatchedCommandId=CandidateCommandId;
            return true;
        }

        MatchedCommandId=0;
        return false;
    }

    internal static int GetItemIndex(int FirstCommandId,int MatchedCommandId)=>
        MatchedCommandId==0?0:MatchedCommandId-FirstCommandId;
}