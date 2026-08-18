namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0048: 「再解析イベントが来た」という事実だけでDocumentを触ってよい根拠にはならない
/// (VSの背景解析は無関係な理由でも頻繁に発火しうる)ことを固定する。本命は
/// <see cref="Evaluate_ContentUnchanged_AlwaysIgnoresRegardlessOfDirtyState"/> —
/// ここが崩れると、保存直後や単純なUndoでIsDirty==falseになった瞬間に無関係な再解析が
/// Redoスタックを黙って消す(実機で確認された退行)。
/// </summary>
public sealed class SceneExternalChangePolicyTests {
    [Fact]
    public void Evaluate_OwnSaveLanding_AlwaysTrustsRegardlessOfOtherFlags(){
        Assert.Equal(
            SceneExternalChangeAction.TrustOwnSave,
            SceneExternalChangePolicy.Evaluate(IsOwnSaveLanding:true,RealContentActuallyChanged:true,HasUnsavedChanges:true));
        Assert.Equal(
            SceneExternalChangeAction.TrustOwnSave,
            SceneExternalChangePolicy.Evaluate(IsOwnSaveLanding:true,RealContentActuallyChanged:false,HasUnsavedChanges:false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_ContentUnchanged_AlwaysIgnoresRegardlessOfDirtyState(bool HasUnsavedChanges){
        Assert.Equal(
            SceneExternalChangeAction.Ignore,
            SceneExternalChangePolicy.Evaluate(
                IsOwnSaveLanding:false,RealContentActuallyChanged:false,HasUnsavedChanges));
    }

    [Fact]
    public void Evaluate_ContentChangedWithNoUnsavedEdits_FollowsSilently(){
        Assert.Equal(
            SceneExternalChangeAction.FollowSilently,
            SceneExternalChangePolicy.Evaluate(
                IsOwnSaveLanding:false,RealContentActuallyChanged:true,HasUnsavedChanges:false));
    }

    [Fact]
    public void Evaluate_ContentChangedWithUnsavedEdits_AsksUser(){
        Assert.Equal(
            SceneExternalChangeAction.AskUser,
            SceneExternalChangePolicy.Evaluate(
                IsOwnSaveLanding:false,RealContentActuallyChanged:true,HasUnsavedChanges:true));
    }
}
