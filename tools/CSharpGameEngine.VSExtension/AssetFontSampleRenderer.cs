using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
/// <summary>描いた見本の画素（事前乗算済みの BGRA、行は詰める）と、描けなかった文字の数。</summary>
internal sealed record FontSamplePixels(int Width,int Height,byte[]Pixels,int MissingCount);
/// <summary>
/// ベイク済みフォント（<c>.cgefont</c>）の見本を、ゲームの実行時と同じ規則で描く（<c>ScreenTextLayout</c> と同じ）: 字形ごとの送り幅で並べ、
/// カーニングはなく、無い文字は <c>?</c> に置き換え、<c>?</c> も無ければ何も描かない。描けない文字には下線（<c>Missing</c> の色）を引く。
/// 画素を作る部分は WPF に依存しない純粋な処理で、<see cref="ToBitmap"/> だけが WPF の画像にする。
/// </summary>
internal static class AssetFontSampleRenderer {
    private const int Padding=2;
    /// <summary>作る画像の 1 辺の上限（画素）。長い文字列や大きな em で巨大な画像を作らない。</summary>
    internal const int MaximumEdge=4096;
    internal static FontSamplePixels Render(AssetFontPreview Preview,string Text,float PixelsPerEm,Color Foreground,Color Missing){
        if(PixelsPerEm<4f||PixelsPerEm>512f){ throw new ArgumentOutOfRangeException(nameof(PixelsPerEm)); }
        var Glyphs=Preview.Glyphs.ToDictionary(static Glyph=>Glyph.CodePoint);
        var Lines=Text.Replace("\r","").Split('\n');
        var LineHeight=Math.Max(1f,(Preview.Ascent+Preview.Descent+Preview.LineGap)*PixelsPerEm);
        var Placed=new List<(AssetFontGlyph?Glyph,float X,int Line,bool IsMissing,float Advance)>();
        var Widest=0f;
        for(var LineIndex=0;LineIndex<Lines.Length;LineIndex++){
            var Pen=0f;
            foreach(var CodePoint in AssetFontCoverage.CodePoints(Lines[LineIndex])){
                var Drawable=Glyphs.TryGetValue(CodePoint,out var Own)&&(Own.HasQuad||AssetFontCoverage.IsBlank(CodePoint));
                var Glyph=Drawable?Own:(Glyphs.TryGetValue('?',out var Question)?Question:null);
                var Advance=(Glyph?.Advance??0.4f)*PixelsPerEm;
                Placed.Add((Glyph,Pen,LineIndex,!Drawable,Advance));
                Pen+=Advance;
            }
            Widest=Math.Max(Widest,Pen);
        }
        var Width=Math.Max(1,Ceil(Widest)+Padding*2);
        var Height=Math.Max(1,Ceil(LineHeight*Lines.Length)+Padding*2);
        if(Width>MaximumEdge||Height>MaximumEdge){ throw new ArgumentOutOfRangeException(nameof(Text),"The font sample would be too large."); }
        var Pixels=new byte[Width*Height*4];
        var Distance=Preview.Distance;
        var Scale=Preview.SpreadEm*PixelsPerEm/127f;
        var MissingCount=0;
        foreach(var (Glyph,X,Line,IsMissing,Advance) in Placed){
            var Baseline=Padding+Line*LineHeight+Preview.Ascent*PixelsPerEm;
            if(IsMissing){ MissingCount++; }
            if(Glyph is not null&&Glyph.HasQuad){
                var X0=Padding+X+Glyph.PlaneLeft*PixelsPerEm;var X1=Padding+X+Glyph.PlaneRight*PixelsPerEm;
                var Y0=Baseline-Glyph.PlaneTop*PixelsPerEm;var Y1=Baseline-Glyph.PlaneBottom*PixelsPerEm;
                var Color=IsMissing?Missing:Foreground;
                for(var Y=Math.Max(0,Floor(Y0));Y<Math.Min(Height,Ceil(Y1));Y++){
                    var V=Glyph.V0+((Y+0.5f-Y0)/(Y1-Y0))*(Glyph.V1-Glyph.V0);
                    for(var Px=Math.Max(0,Floor(X0));Px<Math.Min(Width,Ceil(X1));Px++){
                        var U=Glyph.U0+((Px+0.5f-X0)/(X1-X0))*(Glyph.U1-Glyph.U0);
                        var Alpha=Math.Max(0f,Math.Min(1f,0.5f+(Sample(Distance,Preview.Width,Preview.Height,U,V)-128f)*Scale));
                        if(Alpha>0f){ Blend(Pixels,(Y*Width+Px)*4,Color,Alpha); }
                    }
                }
            }
            if(IsMissing){
                // 描けない文字の下に下線を引く（置き換えた ? の幅、? も無ければ 0.4 em）。
                var Left=Floor(Padding+X);var Right=Ceil(Padding+X+Math.Max(Advance,PixelsPerEm*0.3f));
                var Row=Math.Min(Height-1,Floor(Baseline+Preview.Descent*PixelsPerEm*0.35f+0.5f));
                for(var Thickness=0;Thickness<Math.Max(1,(int)(PixelsPerEm/16f));Thickness++){
                    for(var Px=Math.Max(0,Left);Px<Math.Min(Width,Right);Px++){ Blend(Pixels,(Math.Min(Height-1,Row+Thickness)*Width+Px)*4,Missing,1f); }
                }
            }
        }
        return new FontSamplePixels(Width,Height,Pixels,MissingCount);
    }
    private static int Floor(float Value){ var Whole=(int)Value;return Whole>Value?Whole-1:Whole; }
    private static int Ceil(float Value){ var Whole=(int)Value;return Whole<Value?Whole+1:Whole; }
    /// <summary>距離画像を双一次補間で読む（座標は 0..1 の正規化座標、範囲外は端の値）。</summary>
    internal static float Sample(byte[]Distance,int Width,int Height,float U,float V){
        var X=Math.Max(0f,Math.Min(Width-1f,U*Width-0.5f));
        var Y=Math.Max(0f,Math.Min(Height-1f,V*Height-0.5f));
        var X0=Floor(X);var Y0=Floor(Y);
        var X1=Math.Min(Width-1,X0+1);var Y1=Math.Min(Height-1,Y0+1);
        var Fx=X-X0;var Fy=Y-Y0;
        var Top=Distance[Y0*Width+X0]*(1f-Fx)+Distance[Y0*Width+X1]*Fx;
        var Bottom=Distance[Y1*Width+X0]*(1f-Fx)+Distance[Y1*Width+X1]*Fx;
        return Top*(1f-Fy)+Bottom*Fy;
    }
    private static void Blend(byte[]Pixels,int Offset,Color Source,float Alpha){
        var A=Alpha*Source.A/255f;
        var Keep=1f-A;
        Pixels[Offset]=(byte)Math.Min(255f,Source.B*A+Pixels[Offset]*Keep);
        Pixels[Offset+1]=(byte)Math.Min(255f,Source.G*A+Pixels[Offset+1]*Keep);
        Pixels[Offset+2]=(byte)Math.Min(255f,Source.R*A+Pixels[Offset+2]*Keep);
        Pixels[Offset+3]=(byte)Math.Min(255f,255f*A+Pixels[Offset+3]*Keep);
    }
    internal static BitmapSource ToBitmap(FontSamplePixels Sample){
        var Bitmap=BitmapSource.Create(Sample.Width,Sample.Height,96,96,PixelFormats.Pbgra32,null,Sample.Pixels,Sample.Width*4);
        Bitmap.Freeze();
        return Bitmap;
    }
}
