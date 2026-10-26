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
        foreach(var Focus in new[]{ true,false }){
            foreach(var Pinned in new[]{ true,false }){
                foreach(var Pending in new[]{ true,false }){
                    if(SceneDesignerUndoCommandPolicy.CanExecute(Focus,Pinned,Pending)){
                        Assert.True(SceneDesignerUndoCommandPolicy.ShouldHandle(Focus,Pinned));
                    }
                }
            }
        }
    }
    /// <summary>
    /// M0095 1-2：開いている各シーンタブがキーボードをポーリングするため、Hierarchy または Inspector にフォーカスがあると、すべてのタブが同じ Ctrl+Z を受け取り、固定されたシーンがタブの数だけ巻き戻っていた。固定シーンを表示している唯一のデザイナーだけが、キーボードによる Undo、Redo、Save を処理する。
    /// </summary>
    [Fact]
    public void KeyboardOwner_IsExactlyTheTabShowingThePinnedScene(){
        var A=new SceneKey("project","Game.Scenes.A");
        var B=new SceneKey("project","Game.Scenes.B");
        var C=new SceneKey("project","Game.Scenes.C");
        var Tabs=new[]{ A,B,C };
        foreach(var Pinned in Tabs){
            Assert.Equal(new[]{ Pinned },Tabs.Where(Tab=>SceneDesignerUndoCommandPolicy.IsKeyboardOwner(Tab,Pinned)).ToArray());
        }
        Assert.DoesNotContain(Tabs,Tab=>SceneDesignerUndoCommandPolicy.IsKeyboardOwner(Tab,null));
    }
    /// <summary>Scene Designer ツールウィンドウ（固定シーンなし）はピン留めに追従するため、シーンが固定されている間は常にキー操作を処理する。</summary>
    [Fact]
    public void KeyboardOwner_DesignerWithoutAFixedScene_OwnsThePinnedScene(){
        Assert.True(SceneDesignerUndoCommandPolicy.IsKeyboardOwner(null,new SceneKey("project","Game.Scenes.A")));
        Assert.False(SceneDesignerUndoCommandPolicy.IsKeyboardOwner(null,null));
    }
}
