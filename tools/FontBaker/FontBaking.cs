using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Numerics;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.FontBaker;

/// <summary>
/// TrueType フォントを <see cref="FontAtlas"/> にベイクする。GDI+ で各グリフの輪郭を大きくアンチエイリアスなしで描き、正確な距離変換で各ピクセルから輪郭までの距離を測り、アトラス解像度へ縮小する。同じ Windows バージョンで同じフォント、サイズ、設定を使えば、同じバイト列になる。
/// </summary>
internal static class FontBaking {
    internal const int DefaultSize=64;
    internal const int DefaultSpread=8;
    internal const int DefaultSupersample=4;
    internal const int AtlasWidth=1024;
    private const int PackingGap=1;

    /// <summary>指定がない場合にベイクする文字：印字可能な ASCII、Latin-1、および一般的な組版用句読点。</summary>
    internal static IReadOnlyList<int>DefaultCodePoints(){
        List<int>CodePoints=[];
        for(int CodePoint=0x20;CodePoint<=0x7E;CodePoint++){ CodePoints.Add(CodePoint); }
        for(int CodePoint=0xA0;CodePoint<=0xFF;CodePoint++){ CodePoints.Add(CodePoint); }
        CodePoints.AddRange([0x2013,0x2014,0x2018,0x2019,0x201C,0x201D,0x2022,0x2026,0x20AC]);
        return CodePoints;
    }

    internal static FontAtlas Bake(string FontPath,IReadOnlyList<int>CodePoints,int Size,int Spread,int Supersample){
        if(Size<8){ throw new ArgumentOutOfRangeException(nameof(Size)); }
        using PrivateFontCollection Collection=new();
        Collection.AddFontFile(Path.GetFullPath(FontPath));
        FontFamily Family=Collection.Families[0];
        const FontStyle Style=FontStyle.Regular;
        float EmHeight=Family.GetEmHeight(Style);
        float AscentEm=Family.GetCellAscent(Style)/EmHeight;
        float DescentEm=Family.GetCellDescent(Style)/EmHeight;
        float LineGapEm=Math.Max(0f,(Family.GetLineSpacing(Style)-Family.GetCellAscent(Style)-Family.GetCellDescent(Style))/EmHeight);
        float EmHi=Size*Supersample;
        int SpreadHi=Spread*Supersample;

        List<(int CodePoint,float Advance,float PlaneLeft,float PlaneTop,int CellWidth,int CellHeight,byte[]Cell)>Baked=[];
        foreach(int CodePoint in CodePoints.Distinct().Order()){
            string Text=char.ConvertFromUtf32(CodePoint);
            float Advance=MeasureAdvance(Text,Family,Style,EmHi)/EmHi;
            using GraphicsPath Path=new(FillMode.Winding);
            Path.AddString(Text,Family,(int)Style,EmHi,PointF.Empty,StringFormat.GenericTypographic);
            if(Path.PointCount==0||Path.GetBounds() is { Width:<=0f }){
                Baked.Add((CodePoint,Advance,0f,0f,0,0,[]));
                continue;
            }
            RectangleF Bounds=Path.GetBounds();
            int OriginX=(int)MathF.Floor(Bounds.Left-SpreadHi);
            int OriginY=(int)MathF.Floor(Bounds.Top-SpreadHi);
            int CellWidth=(int)MathF.Ceiling((MathF.Ceiling(Bounds.Right+SpreadHi)-OriginX)/Supersample);
            int CellHeight=(int)MathF.Ceiling((MathF.Ceiling(Bounds.Bottom+SpreadHi)-OriginY)/Supersample);
            bool[]Inside=Rasterize(Path,OriginX,OriginY,CellWidth*Supersample,CellHeight*Supersample);
            byte[]Cell=GlyphField.ToCell(Inside,CellWidth*Supersample,CellHeight*Supersample,Supersample,Spread,out _,out _);
            float BaselineHi=AscentEm*EmHi;
            Baked.Add((CodePoint,Advance,OriginX/EmHi,(BaselineHi-OriginY)/EmHi,CellWidth,CellHeight,Cell));
        }

        (int X,int Y)[]Positions=ShelfPacker.Pack([.. Baked.Select(static Glyph=>(Glyph.CellWidth,Glyph.CellHeight))],AtlasWidth,PackingGap,out int AtlasHeight);
        byte[]Distance=new byte[AtlasWidth*AtlasHeight];
        // すべてのグリフの外側では、距離場は「遠く離れた外側」を表す 0 となる。
        List<FontGlyph>Glyphs=[];
        for(int Index=0;Index<Baked.Count;Index++){
            var Glyph=Baked[Index];
            if(Glyph.CellWidth==0){
                Glyphs.Add(new FontGlyph(Glyph.CodePoint,Glyph.Advance,0f,0f,0f,0f,0f,0f,0f,0f));
                continue;
            }
            (int X,int Y)=Positions[Index];
            for(int Row=0;Row<Glyph.CellHeight;Row++){
                Array.Copy(Glyph.Cell,Row*Glyph.CellWidth,Distance,((Y+Row)*AtlasWidth)+X,Glyph.CellWidth);
            }
            float Width=Glyph.CellWidth*Supersample/EmHi;
            float Height=Glyph.CellHeight*Supersample/EmHi;
            Glyphs.Add(new FontGlyph(Glyph.CodePoint,Glyph.Advance,
                Glyph.PlaneLeft,Glyph.PlaneTop-Height,Glyph.PlaneLeft+Width,Glyph.PlaneTop,
                (float)X/AtlasWidth,(float)Y/AtlasHeight,(float)(X+Glyph.CellWidth)/AtlasWidth,(float)(Y+Glyph.CellHeight)/AtlasHeight));
        }
        return new FontAtlas(AtlasWidth,AtlasHeight,AscentEm,DescentEm,LineGapEm,(float)Spread/Size,Glyphs,Distance);
    }

    private static float MeasureAdvance(string Text,FontFamily Family,FontStyle Style,float EmSize){
        using Bitmap Scratch=new(1,1);
        using Graphics Graphics=Graphics.FromImage(Scratch);
        using Font Font=new(Family,EmSize,Style,GraphicsUnit.Pixel);
        using StringFormat Format=new(StringFormat.GenericTypographic);
        Format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces;
        return Graphics.MeasureString(Text,Font,PointF.Empty,Format).Width;
    }

    /// <summary>
    /// <paramref name="Path"/> を、パス空間で (<paramref name="OriginX"/>, <paramref name="OriginY"/>) にある左上ピクセルを持
    /// つマスクへアンチエイリアスなしで塗りつぶす。
    /// </summary>
    private static bool[]Rasterize(GraphicsPath Path,int OriginX,int OriginY,int Width,int Height){
        using Bitmap Mask=new(Width,Height,PixelFormat.Format32bppArgb);
        using(Graphics Graphics=Graphics.FromImage(Mask)){
            Graphics.SmoothingMode=SmoothingMode.None;
            Graphics.PixelOffsetMode=PixelOffsetMode.None;
            Graphics.Clear(Color.Transparent);
            Graphics.TranslateTransform(-OriginX,-OriginY);
            Graphics.FillPath(Brushes.White,Path);
        }
        BitmapData Data=Mask.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try {
            bool[]Inside=new bool[Width*Height];
            byte[]Row=new byte[Math.Abs(Data.Stride)];
            for(int Y=0;Y<Height;Y++){
                System.Runtime.InteropServices.Marshal.Copy(Data.Scan0+(Y*Data.Stride),Row,0,Math.Abs(Data.Stride));
                for(int X=0;X<Width;X++){ Inside[(Y*Width)+X]=Row[(X*4)+3]>127; }
            }
            return Inside;
        } finally {
            Mask.UnlockBits(Data);
        }
    }
}
