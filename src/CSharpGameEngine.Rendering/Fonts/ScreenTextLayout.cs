using System.Numerics;
using System.Text;

namespace CSharpGameEngine.Rendering;

/// <summary>Where the lines of a block of text sit inside the block's width.</summary>
public enum TextAlignment:byte {
    Left=0,
    Center=1,
    Right=2,
}

/// <summary>
/// Lays text out with a <see cref="FontAtlas"/> (ADR-0078/M0104): the size of a block of text, and the textured quads of its glyphs. Pure and
/// allocation-light; there is no kerning or shaping, so each glyph advances by its own width. A new line is <c>\n</c> (a <c>\r</c> is ignored);
/// a code point the font lacks is drawn as <c>?</c> when it has one, and skipped otherwise.
/// </summary>
public static class ScreenTextLayout {
    private const int Fallback='?';

    /// <summary>The width and height, in pixels, of <paramref name="Text"/> at <paramref name="Size"/> pixels per em.</summary>
    public static Vector2 Measure(FontAtlas Font,string Text,float Size){
        ArgumentNullException.ThrowIfNull(Font);
        ArgumentNullException.ThrowIfNull(Text);
        float Widest=0f;
        int Lines=1;
        float Width=0f;
        foreach(Rune Rune in Text.EnumerateRunes()){
            if(Rune.Value=='\n'){
                Widest=MathF.Max(Widest,Width);
                Width=0f;
                Lines++;
                continue;
            }
            if(Rune.Value=='\r'){ continue; }
            Width+=AdvanceOf(Font,Rune.Value)*Size;
        }
        Widest=MathF.Max(Widest,Width);
        return new Vector2(Widest,Lines*Font.LineHeight*Size);
    }

    /// <summary>
    /// Appends the glyph quads of <paramref name="Text"/> to <paramref name="Quads"/> (six vertices each, two triangles), with the block's top-left
    /// corner at <paramref name="TopLeft"/>. Returns the size of the block, which is what <see cref="Measure"/> returns.
    /// </summary>
    public static Vector2 Append(List<RenderScreenVertex>Quads,FontAtlas Font,string Text,float Size,TextAlignment Alignment,Vector2 TopLeft,Vector4 Color){
        ArgumentNullException.ThrowIfNull(Quads);
        Vector2 Block=Measure(Font,Text,Size);
        float LineHeight=Font.LineHeight*Size;
        int Line=0;
        float LineWidth=LineWidthOf(Font,Text,0,Size);
        float PenX=TopLeft.X+AlignmentOffset(Alignment,Block.X,LineWidth);
        foreach(Rune Rune in Text.EnumerateRunes()){
            if(Rune.Value=='\n'){
                Line++;
                LineWidth=LineWidthOf(Font,Text,Line,Size);
                PenX=TopLeft.X+AlignmentOffset(Alignment,Block.X,LineWidth);
                continue;
            }
            if(Rune.Value=='\r'){ continue; }
            if(!TryGlyph(Font,Rune.Value,out FontGlyph Glyph)){ continue; }
            if(Glyph.HasQuad){
                float Baseline=TopLeft.Y+(Line*LineHeight)+(Font.Ascent*Size);
                float X0=PenX+(Glyph.PlaneLeft*Size);
                float X1=PenX+(Glyph.PlaneRight*Size);
                float Y0=Baseline-(Glyph.PlaneTop*Size);
                float Y1=Baseline-(Glyph.PlaneBottom*Size);
                RenderScreenVertex TopLeftVertex=new(new Vector2(X0,Y0),new Vector2(Glyph.U0,Glyph.V0),Color);
                RenderScreenVertex TopRightVertex=new(new Vector2(X1,Y0),new Vector2(Glyph.U1,Glyph.V0),Color);
                RenderScreenVertex BottomLeftVertex=new(new Vector2(X0,Y1),new Vector2(Glyph.U0,Glyph.V1),Color);
                RenderScreenVertex BottomRightVertex=new(new Vector2(X1,Y1),new Vector2(Glyph.U1,Glyph.V1),Color);
                Quads.Add(TopLeftVertex);
                Quads.Add(TopRightVertex);
                Quads.Add(BottomLeftVertex);
                Quads.Add(TopRightVertex);
                Quads.Add(BottomRightVertex);
                Quads.Add(BottomLeftVertex);
            }
            PenX+=Glyph.Advance*Size;
        }
        return Block;
    }

    private static float AlignmentOffset(TextAlignment Alignment,float BlockWidth,float LineWidth)=>Alignment switch{
        TextAlignment.Center=>(BlockWidth-LineWidth)*0.5f,
        TextAlignment.Right=>BlockWidth-LineWidth,
        _=>0f,
    };

    /// <summary>The width of line <paramref name="LineIndex"/> (zero based) of <paramref name="Text"/>.</summary>
    private static float LineWidthOf(FontAtlas Font,string Text,int LineIndex,float Size){
        int Line=0;
        float Width=0f;
        foreach(Rune Rune in Text.EnumerateRunes()){
            if(Rune.Value=='\n'){
                if(Line==LineIndex){ return Width; }
                Line++;
                Width=0f;
                continue;
            }
            if(Rune.Value=='\r'||Line!=LineIndex){ continue; }
            Width+=AdvanceOf(Font,Rune.Value)*Size;
        }
        return Line==LineIndex?Width:0f;
    }

    private static float AdvanceOf(FontAtlas Font,int CodePoint)=>TryGlyph(Font,CodePoint,out FontGlyph Glyph)?Glyph.Advance:0f;

    private static bool TryGlyph(FontAtlas Font,int CodePoint,out FontGlyph Glyph)=>
        Font.TryGetGlyph(CodePoint,out Glyph)||Font.TryGetGlyph(Fallback,out Glyph);
}
