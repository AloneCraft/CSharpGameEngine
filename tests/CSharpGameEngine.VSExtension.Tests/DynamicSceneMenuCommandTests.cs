namespace CSharpGameEngine.VSExtension.Tests;

public sealed class DynamicSceneMenuCommandTests {
    [Fact]
    public void RootPlaceholder_UsesTheFirstSceneIndex(){
        Assert.Equal(0,DynamicSceneMenuItemMatcher.GetItemIndex(0x0110,MatchedCommandId:0));
    }

    [Fact]
    public void SecondScene_MatchesTheFirstDynamicId(){
        bool Matched=DynamicSceneMenuItemMatcher.TryMatch(0x0110,0x0111,SceneCount:2,out int MatchedCommandId);

        Assert.True(Matched);
        Assert.Equal(0x0111,MatchedCommandId);
        Assert.Equal(1,DynamicSceneMenuItemMatcher.GetItemIndex(0x0110,MatchedCommandId));
    }

    [Fact]
    public void IdImmediatelyAfterTheLastScene_EndsTheDynamicList(){
        bool Matched=DynamicSceneMenuItemMatcher.TryMatch(0x0110,0x0112,SceneCount:2,out int MatchedCommandId);

        Assert.False(Matched);
        Assert.Equal(0,MatchedCommandId);
    }

    [Fact]
    public void OneScene_HasNoDynamicChildAfterTheRootPlaceholder(){
        Assert.False(DynamicSceneMenuItemMatcher.TryMatch(0x0110,0x0111,SceneCount:1,out _));
    }
}