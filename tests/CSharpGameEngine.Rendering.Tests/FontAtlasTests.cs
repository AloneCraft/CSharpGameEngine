using System.Numerics;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Rendering.Tests;

/// <summary>ADR-0078/M0104: the baked font format and the text layout over it, on a small hand-made font.</summary>
public sealed class FontAtlasTests {
    /// <summary>Ascent 0.8, descent 0.2, no gap (so a line is one em). 'A' advances 0.6 and draws; ' ' advances 0.3 and draws nothing; '?' is the fallback.</summary>
    private static FontAtlas MakeFont(bool WithFallback=true){
        List<FontGlyph>Glyphs=[
            new(' ',0.3f,0f,0f,0f,0f,0f,0f,0f,0f),
            new('A',0.6f,0.1f,0f,0.6f,0.7f,0.25f,0.5f,0.5f,0.75f),
        ];
        if(WithFallback){ Glyphs.Add(new('?',0.5f,0.05f,0f,0.45f,0.7f,0.5f,0.5f,0.75f,0.75f)); }
        byte[]Distance=new byte[8*8];
        for(int Index=0;Index<Distance.Length;Index++){ Distance[Index]=(byte)(Index*3); }
        return new FontAtlas(8,8,0.8f,0.2f,0f,0.125f,Glyphs,Distance);
    }

    [Fact]
    public void ABakedFont_RoundTripsThroughItsFileFormat(){
        FontAtlas Font=MakeFont();

        FontAtlas Read=FontAtlas.Parse(Font.ToBytes());

        Assert.Equal(Font.AtlasWidth,Read.AtlasWidth);
        Assert.Equal(Font.AtlasHeight,Read.AtlasHeight);
        Assert.Equal(Font.Ascent,Read.Ascent);
        Assert.Equal(Font.Descent,Read.Descent);
        Assert.Equal(Font.SpreadEm,Read.SpreadEm);
        Assert.Equal(Font.Glyphs,Read.Glyphs);
        Assert.True(Font.Distance.SequenceEqual(Read.Distance));
        Assert.Equal(Font.ToBytes(),Read.ToBytes());
    }

    [Fact]
    public void Parse_RejectsFilesThatAreNotValidFonts(){
        byte[]Good=MakeFont().ToBytes();

        Assert.Throws<FormatException>(()=>FontAtlas.Parse([]));
        Assert.Throws<FormatException>(()=>FontAtlas.Parse(Good[..20]));
        Assert.Throws<FormatException>(()=>FontAtlas.Parse(Good[..^1]));
        Assert.Throws<FormatException>(()=>FontAtlas.Parse([.. Good,0]));
        byte[]BadMagic=(byte[])Good.Clone();
        BadMagic[7]=(byte)'2';
        Assert.Throws<FormatException>(()=>FontAtlas.Parse(BadMagic));
        byte[]NoGlyphs=(byte[])Good.Clone();
        BitConverter.GetBytes(0).CopyTo(NoGlyphs,32);
        Assert.Throws<FormatException>(()=>FontAtlas.Parse(NoGlyphs));
        byte[]NotANumber=(byte[])Good.Clone();
        BitConverter.GetBytes(float.NaN).CopyTo(NotANumber,16);
        Assert.Throws<FormatException>(()=>FontAtlas.Parse(NotANumber));
        // The second glyph record starts after the header (36) and the first record (40); its code point is first.
        byte[]Unordered=(byte[])Good.Clone();
        BitConverter.GetBytes((int)' ').CopyTo(Unordered,36+40);
        Assert.Throws<FormatException>(()=>FontAtlas.Parse(Unordered));
        byte[]OutsideAtlas=(byte[])Good.Clone();
        BitConverter.GetBytes(1.5f).CopyTo(OutsideAtlas,36+40+4+4+16+8);
        Assert.Throws<FormatException>(()=>FontAtlas.Parse(OutsideAtlas));
    }

    [Fact]
    public void TheConstructor_RejectsInvalidFonts(){
        FontGlyph Glyph=new('A',0.6f,0f,0f,0.5f,0.5f,0f,0f,0.5f,0.5f);

        Assert.Throws<ArgumentException>(()=>new FontAtlas(2,2,0.8f,0.2f,0f,0.1f,[Glyph,Glyph],new byte[4]));
        Assert.Throws<ArgumentException>(()=>new FontAtlas(2,2,0.8f,0.2f,0f,0.1f,[],new byte[4]));
        Assert.Throws<ArgumentException>(()=>new FontAtlas(2,2,0.8f,0.2f,0f,0.1f,[Glyph],new byte[3]));
        Assert.Throws<ArgumentException>(()=>new FontAtlas(2,2,0f,0.2f,0f,0.1f,[Glyph],new byte[4]));
        Assert.Throws<ArgumentException>(()=>new FontAtlas(2,2,0.8f,0.2f,0f,0f,[Glyph],new byte[4]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new FontAtlas(0,2,0.8f,0.2f,0f,0.1f,[Glyph],[]));
        Assert.Throws<ArgumentException>(()=>new FontAtlas(2,2,0.8f,0.2f,0f,0.1f,[Glyph with { Advance=-1f }],new byte[4]));
    }

    [Fact]
    public void TryGetGlyph_FindsAGlyphByCodePoint_AndReportsAMissingOne(){
        FontAtlas Font=MakeFont();

        Assert.True(Font.TryGetGlyph('A',out FontGlyph A));
        Assert.Equal(0.6f,A.Advance);
        Assert.True(Font.TryGetGlyph(' ',out _));
        Assert.True(Font.TryGetGlyph('?',out _));
        Assert.False(Font.TryGetGlyph('B',out _));
        Assert.False(Font.TryGetGlyph(0x1F642,out _));
    }

    [Fact]
    public void TheTexture_IsWhiteWithTheDistanceInAlpha(){
        FontAtlas Font=MakeFont();

        RenderTextureResource Texture=Font.Texture;

        Assert.Same(Texture,Font.Texture);
        Assert.Equal(8,Texture.Width);
        Assert.Equal(8,Texture.Height);
        for(int Index=0;Index<64;Index++){
            Assert.Equal(255,Texture.Pixels[Index*4]);
            Assert.Equal(255,Texture.Pixels[(Index*4)+2]);
            Assert.Equal(Font.Distance[Index],Texture.Pixels[(Index*4)+3]);
        }
    }

    [Theory]
    [InlineData("",0f,1f)]
    [InlineData("A",0.6f,1f)]
    [InlineData("AA A",2.1f,1f)]
    [InlineData("A\nAAA",1.8f,2f)]
    [InlineData("A\r\nA",0.6f,2f)]
    [InlineData("\n\n",0f,3f)]
    public void Measure_IsTheWidestLineAndTheLineCountTimesTheLineHeight(string Text,float Width,float Lines){
        Vector2 Size=ScreenTextLayout.Measure(MakeFont(),Text,10f);

        Assert.Equal(Width*10f,Size.X,3);
        Assert.Equal(Lines*10f,Size.Y,3);
    }

    [Fact]
    public void Append_PlacesAGlyphsQuadFromThePenOnTheBaseline(){
        List<RenderScreenVertex>Quads=[];
        Vector4 Color=new(1f,0.5f,0.25f,1f);

        Vector2 Block=ScreenTextLayout.Append(Quads,MakeFont(),"A",10f,TextAlignment.Left,new Vector2(100f,50f),Color);

        Assert.Equal(new Vector2(6f,10f),Block);
        Assert.Equal(6,Quads.Count);
        // Baseline y = 50 + ascent 8 = 58. Plane x 0.1..0.6 -> 101..106; plane y 0..0.7 up -> 58..51.
        Assert.Equal(new Vector2(101f,51f),Quads[0].Position);
        Assert.Equal(new Vector2(106f,51f),Quads[1].Position);
        Assert.Equal(new Vector2(101f,58f),Quads[2].Position);
        Assert.Equal(new Vector2(106f,58f),Quads[4].Position);
        Assert.Equal(new Vector2(0.25f,0.5f),Quads[0].Uv);
        Assert.Equal(new Vector2(0.5f,0.5f),Quads[1].Uv);
        Assert.Equal(new Vector2(0.25f,0.75f),Quads[2].Uv);
        Assert.Equal(new Vector2(0.5f,0.75f),Quads[4].Uv);
        Assert.All(Quads,Vertex=>Assert.Equal(Color,Vertex.Color));
        // The second triangle repeats two corners of the first.
        Assert.Equal(Quads[1],Quads[3]);
        Assert.Equal(Quads[2],Quads[5]);
    }

    [Fact]
    public void Append_AdvancesThePenByEachGlyph_AndASpaceDrawsNothing(){
        List<RenderScreenVertex>Quads=[];

        ScreenTextLayout.Append(Quads,MakeFont(),"A A",10f,TextAlignment.Left,Vector2.Zero,Vector4.One);

        // Two glyph quads; the second 'A' starts after 0.6 + 0.3 em: pen x 9, quad left 9 + 1 = 10.
        Assert.Equal(12,Quads.Count);
        Assert.Equal(1f,Quads[0].Position.X);
        Assert.Equal(10f,Quads[6].Position.X);
    }

    [Theory]
    [InlineData(TextAlignment.Left,0f)]
    [InlineData(TextAlignment.Center,6f)]
    [InlineData(TextAlignment.Right,12f)]
    public void Append_AlignsEachLineInsideTheBlock(TextAlignment Alignment,float ShortLineOffset){
        List<RenderScreenVertex>Quads=[];

        // Line 1 is 'AAA' (18 wide), line 2 is 'A' (6 wide): the block is 18 wide and the short line starts 0, 6 or 12 in.
        Vector2 Block=ScreenTextLayout.Append(Quads,MakeFont(),"AAA\nA",10f,TextAlignment.Left,Vector2.Zero,Vector4.One);
        Quads.Clear();
        ScreenTextLayout.Append(Quads,MakeFont(),"AAA\nA",10f,Alignment,Vector2.Zero,Vector4.One);

        Assert.Equal(18f,Block.X,3);
        RenderScreenVertex ShortLineFirst=Quads[^6];
        Assert.Equal(ShortLineOffset+1f,ShortLineFirst.Position.X,3);
        // The second line is one line height (10) lower.
        Assert.Equal(10f,ShortLineFirst.Position.Y-Quads[0].Position.Y,3);
    }

    [Fact]
    public void Append_DrawsAMissingGlyphAsTheFallback_OrSkipsItWhenThereIsNone(){
        List<RenderScreenVertex>WithFallback=[];
        List<RenderScreenVertex>WithoutFallback=[];

        ScreenTextLayout.Append(WithFallback,MakeFont(),"B\U0001F642",10f,TextAlignment.Left,Vector2.Zero,Vector4.One);
        ScreenTextLayout.Append(WithoutFallback,MakeFont(WithFallback:false),"B\U0001F642A",10f,TextAlignment.Left,Vector2.Zero,Vector4.One);

        // Both the unknown letter and the emoji (one code point, two UTF-16 units) draw the '?' quad, advancing 0.5 em each.
        Assert.Equal(12,WithFallback.Count);
        Assert.Equal(new Vector2(0.5f,0.5f),WithFallback[0].Uv);
        Assert.Equal(5f+0.5f,WithFallback[6].Position.X,3);
        // Without a fallback they advance nothing and draw nothing, so only the 'A' draws.
        Assert.Equal(6,WithoutFallback.Count);
        Assert.Equal(1f,WithoutFallback[0].Position.X,3);
    }
}
