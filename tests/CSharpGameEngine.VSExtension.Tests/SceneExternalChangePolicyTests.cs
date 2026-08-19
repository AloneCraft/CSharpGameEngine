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
/// <see cref="SceneDocumentLineagePolicy"/>へ分離した。
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

    // ---- 自分の書き込みの着地(M0051 Step 1-6) ----

    /// <summary>
    /// 高速経路。Documentは既に正しい内容を持っているので、リネージが一致していれば何もしない。
    /// ここでResetToを呼ぶと履歴とウォーターマークが消え、保存後のUndoが再び遅い経路へ落ちる。
    /// </summary>
    [Fact]
    public void Lineage_KeepsTheDocumentWhenProjectionsMatch(){
        Assert.Equal(
            SceneLandingAction.KeepDocument,
            SceneDocumentLineagePolicy.Decide(IsOwnLanding:true,ProjectionsMatch:true));
    }

    /// <summary>
    /// 安全側の縮退。Name+Component構成が同一の兄弟はIDが払い直され、Documentの履歴が参照する
    /// EditorNodeIdを実投影上で解決できなくなる。そのまま進むと次の保存が
    /// 「対象Nodeの構文を特定できません」で失敗するので、実解析結果へ戻す。
    /// </summary>
    [Fact]
    public void Lineage_RebasesWhenProjectionsDiverge(){
        Assert.Equal(
            SceneLandingAction.Rebase,
            SceneDocumentLineagePolicy.Decide(IsOwnLanding:true,ProjectionsMatch:false));
    }

    /// <summary>自分の書き込みでない着地は、一致していてもDocumentを維持する根拠にならない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Lineage_AlwaysRebasesForForeignLandings(bool ProjectionsMatch){
        Assert.Equal(
            SceneLandingAction.Rebase,
            SceneDocumentLineagePolicy.Decide(IsOwnLanding:false,ProjectionsMatch));
    }
}
