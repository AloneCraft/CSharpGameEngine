namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// C#側の定数と .vsct の ID 定義がずれていないことを固定する。.vsct のスロットは
/// OpenSceneDesignerSceneItem00〜31 (0x0110〜0x012F) と溢れ表示 (0x0130) を手で並べているため、
/// C#側だけを変えると実機で初めて壊れる種類の食い違いになる。
/// </summary>
public sealed class SceneDesignerCommandIdsTests {
    [Fact]
    public void SceneItemSlots_MatchTheVsctIdBlock(){
        Assert.Equal(0x0110,OpenSceneDesignerCommand.SceneItemCommandIdFirst);
        Assert.Equal(32,OpenSceneDesignerCommand.SceneItemCommandCount);
        Assert.Equal(0x0130,OpenSceneDesignerCommand.OverflowCommandId);
    }

    /// <summary>
    /// 溢れ表示の ID がスロット領域の直後にあること。スロット数だけを増やすと溢れ表示の ID が
    /// スロットと衝突し、片方が黙って動かなくなる(.vsct を再生成せずに定数だけ変えた場合に起きる)。
    /// </summary>
    [Fact]
    public void OverflowId_DoesNotCollideWithTheSlotBlock(){
        Assert.Equal(
            OpenSceneDesignerCommand.SceneItemCommandIdFirst+OpenSceneDesignerCommand.SceneItemCommandCount,
            OpenSceneDesignerCommand.OverflowCommandId);
    }
}
