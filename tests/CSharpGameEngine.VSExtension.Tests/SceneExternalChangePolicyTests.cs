namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0048: 「再解析イベントが来た」という事実だけでDocumentを触ってよい根拠にはならない
/// (VSの背景解析は無関係な理由でも頻繁に発火しうる)ことを固定する。本命は
/// <see cref="Evaluate_ContentUnchanged_AlwaysIgnoresRegardlessOfDirtyState"/> —
/// ここが崩れると、保存直後や単純なUndoでIsDirty==falseになった瞬間に無関係な再解析が
/// Redoスタックを黙って消す(実機で確認された退行)。
///
/// <para>
/// M0051 Step 1-6: <b>自分の書き込みの着地はここでは扱わない</b>。保存の書き戻しや
/// Undo/Redoのテキスト同期が着地したときの判断は「外部変更かどうか」ではなく
/// 「Documentと実解析のリネージが一致しているか」なので、
/// <see cref="SceneLandingDecision"/>へ分離した(テストは<c>SceneLandingDecisionTests</c>)。
/// </para>
/// </summary>
public sealed class SceneExternalChangePolicyTests {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_ContentUnchanged_AlwaysIgnoresRegardlessOfDirtyState(bool HasUnsavedChanges){
        Assert.Equal(
            SceneExternalChangeAction.Ignore,
            SceneExternalChangePolicy.Evaluate(RealContentActuallyChanged:false,HasUnsavedChanges));
    }

    [Fact]
    public void Evaluate_ContentChangedWithNoUnsavedEdits_FollowsSilently(){
        Assert.Equal(
            SceneExternalChangeAction.FollowSilently,
            SceneExternalChangePolicy.Evaluate(RealContentActuallyChanged:true,HasUnsavedChanges:false));
    }

    [Fact]
    public void Evaluate_ContentChangedWithUnsavedEdits_AsksUser(){
        Assert.Equal(
            SceneExternalChangeAction.AskUser,
            SceneExternalChangePolicy.Evaluate(RealContentActuallyChanged:true,HasUnsavedChanges:true));
    }
}
