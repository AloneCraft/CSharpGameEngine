namespace CSharpGameEngine.VSExtension.Tests;

public sealed class ActiveDocumentPathSelectorTests {
    [Fact]
    public void ToolWindowWithoutFileMoniker_PreservesPreviousCodeDocument(){
        string Previous=@"G:\Repo\Scenes\MainScene.cs";

        string?Result=ActiveDocumentPathSelector.Select(null,null,Previous);

        Assert.Equal(Previous,Result);
    }

    [Fact]
    public void ToolWindowMoniker_UsesLastTextViewInsteadOfClearingTheDocument(){
        string TextView=@"G:\Repo\Scenes\MainScene.cs";

        string?Result=ActiveDocumentPathSelector.Select("Solution Explorer",TextView,null);

        Assert.Equal(TextView,Result);
    }

    [Fact]
    public void RealNonCSharpDocument_ReplacesThePreviousCodeDocument(){
        string Asset=@"G:\Repo\Assets\Box.glb";

        string?Result=ActiveDocumentPathSelector.Select(Asset,@"G:\Repo\Scenes\MainScene.cs",null);

        Assert.Equal(Asset,Result);
    }
}
