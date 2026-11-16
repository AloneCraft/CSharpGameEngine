using System.Windows.Media;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetFontSampleRendererTests {
    private static readonly Color Ink=Color.FromRgb(240,240,240);
    private static readonly Color Alert=Color.FromRgb(255,60,60);
    /// <summary>全面が「内側」（255）の 4×4 の距離画像で、A・B・? と空白だけを持つ小さなフォント。</summary>
    private static AssetFontPreview Font(params int[]CodePoints){
        AssetFontGlyph Glyph(int CodePoint)=>CodePoint==' '
            ?new(){ CodePoint=CodePoint,Advance=0.3f }
            :new(){ CodePoint=CodePoint,Advance=0.6f,PlaneLeft=0.05f,PlaneBottom=0f,PlaneRight=0.55f,PlaneTop=0.7f,U0=0f,V0=0f,U1=1f,V1=1f };
        var Distance=Enumerable.Repeat((byte)255,16).ToArray();
        return new(){ SourceWidth=4,SourceHeight=4,Width=4,Height=4,Distance=Distance,Ascent=0.8f,Descent=0.2f,LineGap=0f,SpreadEm=0.125f,
            GlyphCount=CodePoints.Length,Glyphs=[..CodePoints.OrderBy(static CodePoint=>CodePoint).Select(Glyph)] };
    }
    private static (byte B,byte G,byte R,byte A)Pixel(FontSamplePixels Image,int X,int Y){
        var Offset=(Y*Image.Width+X)*4;
        return (Image.Pixels[Offset],Image.Pixels[Offset+1],Image.Pixels[Offset+2],Image.Pixels[Offset+3]);
    }
    private static bool Inked(FontSamplePixels Image,int X,int Y)=>Pixel(Image,X,Y).A>200;
    [Fact]
    public void AGlyphIsDrawnInsideItsQuadWithTheRequestedColourAndAdvancesTheNextOne(){
        var Image=AssetFontSampleRenderer.Render(Font('A','B',' '),"AB",100f,Ink,Alert);
        Assert.Equal(0,Image.MissingCount);
        // 送り幅 0.6 em × 2 + 余白。
        Assert.InRange(Image.Width,124,125);
        Assert.InRange(Image.Height,104,105);
        // A の矩形（x 5..55、y は基線 80 から上に 70）の中央は塗られ、色は前景。
        Assert.True(Inked(Image,2+30,2+50));
        var Centre=Pixel(Image,2+30,2+50);
        Assert.Equal((240,240,240),(Centre.R,Centre.G,Centre.B));
        // 2 文字目は 60px 右。矩形の外（A と B の間の字間）は透明。
        Assert.True(Inked(Image,2+60+30,2+50));
        Assert.Equal(0,Pixel(Image,2+57,2+50).A);
    }
    [Fact]
    public void ACharacterTheFontLacksIsReplacedByAQuestionMarkAndUnderlined(){
        var Image=AssetFontSampleRenderer.Render(Font('A','?'),"AZ",100f,Ink,Alert);
        Assert.Equal(1,Image.MissingCount);
        // Z の位置に ? の字形が警告色で描かれる。
        var Replaced=Pixel(Image,2+60+30,2+50);
        Assert.True(Replaced.A>200);
        Assert.Equal((255,60,60),(Replaced.R,Replaced.G,Replaced.B));
        // 下線が基線より少し下に引かれる（? の送り幅のあいだ）。
        var Underline=Enumerable.Range(2+60,60).Count(X=>Enumerable.Range(2+80,16).Any(Y=>Pixel(Image,X,Y).A>200&&Pixel(Image,X,Y).R==255));
        Assert.True(Underline>30,$"{Underline} underlined columns");
    }
    [Fact]
    public void WithoutAQuestionMarkAMissingCharacterDrawsOnlyTheUnderline(){
        var Image=AssetFontSampleRenderer.Render(Font('A'),"AZ",100f,Ink,Alert);
        Assert.Equal(1,Image.MissingCount);
        Assert.False(Inked(Image,2+60+30,2+50));
        var Underlined=Enumerable.Range(2+60,Image.Width-2-60).Count(X=>Enumerable.Range(0,Image.Height).Any(Y=>Pixel(Image,X,Y).R==255&&Pixel(Image,X,Y).A>200));
        Assert.NotEqual(0,Underlined);
    }
    [Fact]
    public void TheEdgeBlendsByTheDistanceValueInsteadOfAHardThreshold(){
        var Preview=Font('A');
        // 128 は輪郭ちょうど（半透明）、255 は内側、0 は外側。
        Assert.True(AssetFontSampleRenderer.Sample(Preview.Distance,4,4,0.5f,0.5f)>=254f);
        var Half=Enumerable.Repeat((byte)128,16).ToArray();
        Assert.Equal(128f,AssetFontSampleRenderer.Sample(Half,4,4,0.3f,0.7f),3);
        Preview.Distance=Half;
        var Image=AssetFontSampleRenderer.Render(Preview,"A",100f,Ink,Alert);
        var Alpha=Pixel(Image,2+30,2+50).A;
        Assert.InRange((int)Alpha,100,150);
        Preview.Distance=new byte[16];
        Assert.Equal(0,Pixel(AssetFontSampleRenderer.Render(Preview,"A",100f,Ink,Alert),2+30,2+50).A);
    }
    [Fact]
    public void LinesBreakAtNewlinesAndEmptyOrOutOfRangeInputIsHandled(){
        var One=AssetFontSampleRenderer.Render(Font('A'),"A",50f,Ink,Alert);
        var Two=AssetFontSampleRenderer.Render(Font('A'),"A\nA",50f,Ink,Alert);
        Assert.Equal(One.Width,Two.Width);
        Assert.Equal(One.Height+50,Two.Height);
        var Empty=AssetFontSampleRenderer.Render(Font('A'),"",50f,Ink,Alert);
        Assert.Equal(0,Empty.MissingCount);
        Assert.Equal(Empty.Width*Empty.Height*4,Empty.Pixels.Length);
        Assert.Throws<ArgumentOutOfRangeException>(()=>AssetFontSampleRenderer.Render(Font('A'),"A",1f,Ink,Alert));
        // 上限を超える画像は作らない（大きすぎる em）。
        Assert.Throws<ArgumentOutOfRangeException>(()=>AssetFontSampleRenderer.Render(Font('A'),"A",5000f,Ink,Alert));
    }
}
