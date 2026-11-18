using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class ImageOutputCreationTests {
    [Fact]
    public void EncodedImageSink_CreatesAnImageOutputWithoutTextureMips(){
        var Output=ImageOutputDrop.NewOutput("CSharpGameEngine.Authoring.ImageAsset",TextureUsage.Color);
        Assert.Equal(ImageOutputKind.Image,Output.Kind);
        Assert.Equal("image",Output.Key);
        Assert.False(Output.Mipmaps);
        Assert.False(Output.IsSrgb);
        new ImageImportSettings{DefaultOutput=Output.Key,Outputs=[Output]}.Validate();
    }
    [Theory]
    [InlineData(TextureUsage.Color,"color",true)]
    [InlineData(TextureUsage.Normal,"normal",false)]
    [InlineData(TextureUsage.Packed,"packed",false)]
    public void TextureSink_PreservesItsUsageAndMipDefaults(TextureUsage Usage,string Key,bool Srgb){
        var Output=ImageOutputDrop.NewOutput("CSharpGameEngine.Authoring.TextureAsset",Usage);
        Assert.Equal(ImageOutputKind.Texture,Output.Kind);
        Assert.Equal(Usage,Output.Usage);
        Assert.Equal(Key,Output.Key);
        Assert.True(Output.Mipmaps);
        Assert.Equal(Srgb,Output.IsSrgb);
        new ImageImportSettings{DefaultOutput=Output.Key,Outputs=[Output]}.Validate();
    }
    [Fact]
    public void UnsupportedSink_IsRejectedBeforeAnyMetadataEdit()=>
        Assert.Throws<ArgumentException>(()=>ImageOutputDrop.NewOutput("CSharpGameEngine.Authoring.ModelAsset",TextureUsage.Color));
}
