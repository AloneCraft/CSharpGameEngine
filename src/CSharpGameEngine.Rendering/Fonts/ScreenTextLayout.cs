using System.Numerics;
using System.Text;

namespace CSharpGameEngine.Rendering;

/// <summary>テキストブロックの幅の中で、各行をどこに配置するか。</summary>
public enum TextAlignment:byte {
    Left=0,
    Center=1,
    Right=2,
}

/// <summary>
/// <see cref="FontAtlas"/> を使ってテキストをレイアウトする（ADR-0078/M0104）。テキストブロックのサイズと、グリフを表すテクスチャ付き四角形を求める。状態を持たず、割
/// り当てを抑えた処理。カーニングや字形整形は行わないため、各グリフは自身の幅だけ進む。改行には <c>\n</c> を使い、<c>\r</c> は無視する。フォントにないコードポイントは、
/// <c>?</c> があればそれを描画し、なければ読み飛ばす。
/// </summary>
public static class ScreenTextLayout {
    private const int Fallback='?';

    /// <summary><paramref name="Size"/> ピクセルを em あたりの大きさとして描画した <paramref name="Text"/> の幅と高さ（ピクセル）。</summary>
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
    /// <paramref name="Text"/> のグリフ四角形を <paramref name="Quads"/> に追加する（各グリフ6頂点、三角形2つ）。ブロックの左上角を
    /// <paramref name="TopLeft"/> に置く。ブロックのサイズを返す。これは <see cref="Measure"/> の戻り値と同じ。
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

    /// <summary><paramref name="Text"/> の0始まりの <paramref name="LineIndex"/> 行の幅。</summary>
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
