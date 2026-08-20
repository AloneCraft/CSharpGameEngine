namespace CSharpGameEngine.VSExtension.Tests;

public sealed class ActiveDocumentPathSelectorTests {
    [Fact]
    public void ToolWindowWithoutFileMoniker_PreservesPreviousCodeDocument(){
        var Previous=@"G:\Repo\Scenes\MainScene.cs";

        var Result=ActiveDocumentPathSelector.Select(null,null,Previous);

        Assert.Equal(Previous,Result);
    }

    [Fact]
    public void ToolWindowMoniker_UsesLastTextViewInsteadOfClearingTheDocument(){
        var TextView=@"G:\Repo\Scenes\MainScene.cs";

        var Result=ActiveDocumentPathSelector.Select("Solution Explorer",TextView,null);

        Assert.Equal(TextView,Result);
    }

    [Fact]
    public void RealNonCSharpDocument_ReplacesThePreviousCodeDocument(){
        var Asset=@"G:\Repo\Assets\Box.glb";

        var Result=ActiveDocumentPathSelector.Select(Asset,@"G:\Repo\Scenes\MainScene.cs",null);

        Assert.Equal(Asset,Result);
    }
}
