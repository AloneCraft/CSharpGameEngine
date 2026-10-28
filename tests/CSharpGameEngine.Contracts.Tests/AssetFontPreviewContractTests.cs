using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Contracts.Tests;
public sealed class AssetFontPreviewContractTests {
    private static AssetFontPreview Valid()=>new(){
        SourceWidth=1024,SourceHeight=2,Width=4,Height=2,GlyphCount=2,Distance=new byte[8],Ascent=0.9f,Descent=0.25f,LineGap=0f,SpreadEm=0.125f,
        Glyphs=[new(){ CodePoint=32,Advance=0.25f },new(){ CodePoint=65,Advance=0.6f,PlaneRight=0.5f,PlaneTop=0.7f,U1=0.5f,V1=0.5f }],
    };
    [Fact]
    public void AWellFormedPreviewIsAccepted(){
        Assert.Null(AssetFontPreview.Validate(Valid()));
        Assert.False(Valid().Glyphs[0].HasQuad);
        Assert.True(Valid().Glyphs[1].HasQuad);
    }
    [Fact]
    public void ASurvivingJsonRoundTripKeepsTheGlyphs(){
        var Back=System.Text.Json.JsonSerializer.Deserialize<AssetFontPreview>(System.Text.Json.JsonSerializer.Serialize(Valid()))!;
        Assert.Null(AssetFontPreview.Validate(Back));
        Assert.Equal(new[]{ 32,65 },Back.Glyphs.Select(static Glyph=>Glyph.CodePoint));
        Assert.Equal(0.9f,Back.Ascent);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("width")]
    [InlineData("distance")]
    [InlineData("count")]
    [InlineData("order")]
    [InlineData("duplicate")]
    [InlineData("codepoint")]
    [InlineData("nan-advance")]
    [InlineData("uv")]
    [InlineData("metrics")]
    [InlineData("nullglyph")]
    public void ABadPreviewIsRejectedWithAReason(string Mutation){
        var Preview=Valid();
        switch(Mutation){
            case "null": Assert.NotNull(AssetFontPreview.Validate(null)); return;
            case "width": Preview.Width=513; break;
            case "distance": Preview.Distance=new byte[3]; break;
            case "count": Preview.GlyphCount=3; break;
            case "order": Preview.Glyphs=[Preview.Glyphs[1],Preview.Glyphs[0]]; break;
            case "duplicate": Preview.Glyphs[1].CodePoint=32; break;
            case "codepoint": Preview.Glyphs[1].CodePoint=0x110000; break;
            case "nan-advance": Preview.Glyphs[1].Advance=float.NaN; break;
            case "uv": Preview.Glyphs[1].U1=1.5f; break;
            case "metrics": Preview.Ascent=float.PositiveInfinity; break;
            case "nullglyph": Preview.Glyphs[1]=null!; break;
        }
        Assert.NotNull(AssetFontPreview.Validate(Preview));
    }
}
