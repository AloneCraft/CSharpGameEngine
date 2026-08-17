namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneDesignerUndoCommandPolicyTests {
    [Theory]
    [InlineData(true,true,true)]
    [InlineData(true,false,false)]
    [InlineData(false,true,false)]
    [InlineData(false,false,false)]
    public void ShouldHandle_RequiresBothFocusAndPinnedSnapshot(
        bool ViewportHasKeyboardFocus,bool HasPinnedSnapshot,bool Expected){
        Assert.Equal(
            Expected,
            SceneDesignerUndoCommandPolicy.ShouldHandle(ViewportHasKeyboardFocus,HasPinnedSnapshot));
    }
}
