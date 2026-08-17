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

    /// <summary>
    /// M0038: 書き戻しが飛行中のUndoは、テキストUndoと先読みUndoが食い違ってソースと表示がずれるため
    /// 実行させない。ただしSupported(=<see cref="SceneDesignerUndoCommandPolicy.ShouldHandle"/>)は
    /// 落とさないので、Ctrl+Zが別ドキュメントへ流れることはない。
    /// </summary>
    [Theory]
    [InlineData(true,true,false,true)]
    [InlineData(true,true,true,false)]
    [InlineData(false,true,false,false)]
    [InlineData(true,false,false,false)]
    public void CanExecute_IsBlockedWhileASourceWriteIsStillInFlight(
        bool ViewportHasKeyboardFocus,bool HasPinnedSnapshot,bool HasPendingSourceWrite,bool Expected){
        Assert.Equal(
            Expected,
            SceneDesignerUndoCommandPolicy.CanExecute(
                ViewportHasKeyboardFocus,HasPinnedSnapshot,HasPendingSourceWrite));
    }

    [Fact]
    public void CanExecute_NeverWidensShouldHandle(){
        // Supportedを落とさない設計の裏返し: CanExecuteがtrueならShouldHandleも必ずtrueであること。
        foreach(bool Focus in new[]{ true,false }){
            foreach(bool Pinned in new[]{ true,false }){
                foreach(bool Pending in new[]{ true,false }){
                    if(SceneDesignerUndoCommandPolicy.CanExecute(Focus,Pinned,Pending)){
                        Assert.True(SceneDesignerUndoCommandPolicy.ShouldHandle(Focus,Pinned));
                    }
                }
            }
        }
    }
}
