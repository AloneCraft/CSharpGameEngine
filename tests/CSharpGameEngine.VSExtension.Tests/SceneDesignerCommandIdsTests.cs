namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneDesignerCommandIdsTests {
    [Fact]
    public void DynamicSceneItems_StartAtTheVsctPlaceholderId(){
        Assert.Equal(0x0110,OpenSceneDesignerCommand.DynamicSceneCommandStart);
    }
}