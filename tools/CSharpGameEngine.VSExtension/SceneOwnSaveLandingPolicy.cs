namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0051 Step 1-5: 再解析イベントが<b>自分の保存の着地</b>かどうかの純粋な判定。
///
/// <para>
/// <b>解決する不具合</b>: 以前は<c>AwaitingOwnSaveReanalysis</c>という<b>bool</b>で「保存待ち」を表現して
/// いた。ところが<see cref="SceneEditorSessionService"/>の再解析ハンドラは1イベントで<b>複数のSceneModel</b>を
/// 受け取ってループするため、boolだと保存した1つだけでなく<b>同じイベントに乗った無関係なScene</b>まで
/// <c>TrustOwnSave</c>と判定され、<see cref="SceneEditDocument.ResetTo"/>で未保存の編集とUndo履歴を
/// 消してしまっていた。保存したSceneの<see cref="SceneKey"/>を保持して照合する。
/// </para>
///
/// <para>
/// この潜在バグは、Step 1-5で「保存後もUndoできる」ようにすると害が確実に顕在化する — 2つ目のSceneを
/// 開いたまま片方を保存しただけで、もう片方の履歴が黙って消えるため。
/// </para>
/// </summary>
internal static class SceneOwnSaveLandingPolicy {
    /// <summary>
    /// <paramref name="AwaitingKey"/>は保存の着地を待っているSceneのキー(待っていなければnull)。
    /// <paramref name="CandidateKey"/>は今まさに再解析されてきたSceneのキー。
    /// </summary>
    internal static bool IsOwnSaveLanding(SceneKey?AwaitingKey,SceneKey CandidateKey)=>
        AwaitingKey is { }Awaiting&&Awaiting.Equals(CandidateKey);

    /// <summary>
    /// M0051 Step 1-7: 着地待ちフラグを<b>このイベントで消費してよいか</b>。
    ///
    /// <para>
    /// 以前は再解析イベントの先頭で無条件にnull化していたため、対象Sceneのモデルが載っていない
    /// イベントでも消費されていた。消費されると本当の着地が外部変更として扱われ、
    /// 保存直後は<c>IsDirty==false</c>なので<c>FollowSilently</c>→<c>ResetTo</c>で
    /// <b>ウォーターマークと履歴が消える</b>。そのイベントに対象Sceneが実際に載っているときだけ消費する。
    /// </para>
    /// </summary>
    internal static bool ShouldConsumeAwaitingKey(SceneKey?AwaitingKey,bool EventContainsTargetScene)=>
        AwaitingKey is not null&&EventContainsTargetScene;
}
