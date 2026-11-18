namespace CSharpGameEngine.VSExtension.Tests;
public sealed class ImageAssetSourcePathTests {
    [Theory]
    [InlineData("Assets/Photo.png:color","CSharpGameEngine.Authoring.TextureAsset","Assets/Photo.png")]
    [InlineData("Assets/Photo.png:original","CSharpGameEngine.Authoring.ImageAsset","Assets/Photo.png")]
    [InlineData("Assets/Photo.png:color%20space","CSharpGameEngine.Authoring.TextureAsset","Assets/Photo.png")]
    [InlineData("C:\\Game\\Assets\\a.png:color","CSharpGameEngine.Authoring.TextureAsset","C:\\Game\\Assets\\a.png")]
    [InlineData("Assets/Photo.png","CSharpGameEngine.Authoring.TextureAsset","Assets/Photo.png")]
    public void Parse_ReturnsSelectorSourcePath(string? AssetPath,string? AssetTypeName,string? Expected){
        Assert.Equal(Expected,ImageAssetSourcePath.Parse(AssetPath,AssetTypeName));
    }
    [Theory]
    [InlineData(null,"CSharpGameEngine.Authoring.TextureAsset")]
    [InlineData("","CSharpGameEngine.Authoring.TextureAsset")]
    [InlineData(" \t ","CSharpGameEngine.Authoring.TextureAsset")]
    [InlineData("Assets/Photo.png:color","CSharpGameEngine.Authoring.UnknownAsset")]
    [InlineData("Assets/Photo.png:color",null)]
    [InlineData("Assets/Photo.png:color","")]
    [InlineData("Assets/Photo.png:color%2","CSharpGameEngine.Authoring.TextureAsset")]
    [InlineData("Assets/Photo.png:","CSharpGameEngine.Authoring.ImageAsset")]
    [InlineData("asset://texture/abc:color","CSharpGameEngine.Authoring.TextureAsset")]
    public void Parse_ReturnsNullForMissingUnsupportedOrMalformedInput(string? AssetPath,string? AssetTypeName){
        Assert.Null(ImageAssetSourcePath.Parse(AssetPath,AssetTypeName));
    }
}
