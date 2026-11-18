using System.Numerics;
using CSharpGameEngine.Content.Tiles;
namespace CSharpGameEngine.World.Terrain;
/// <summary>
/// 地形の層の 1 枚（ADR-0100 決定 7、M0135）。点（球は向き、平面は中心からの (x, 0, z)）と、その点の高さ（メートル）と傾き（0 = 平ら、1 = 垂直）から、
/// 下の層の色 <paramref name="Below"/> に重ねた色（リニア RGB、0〜1）を返す。同じ入力に同じ値を返すこと。
/// </summary>
public interface ITerrainColorLayer {
    Vector3 Apply(Vector3 Point,float Height,float Slope,Vector3 Below);
}
/// <summary>色のタイルの階層（RGB、0〜1、sRGB で詰めたもの）を読む層。球は正距円筒（+Y が北極、+X が経度 0、+Z が経度 +90°）、平面は範囲がメートル。値の無い所は下の色のまま。</summary>
public sealed class TileColorLayer(TilePyramid Pyramid,int Level=0):ITerrainColorLayer {
    public int Level { get; }=Math.Clamp(Level,0,Pyramid.Header.LevelCount-1);
    public Vector3 Apply(Vector3 Point,float Height,float Slope,Vector3 Below){
        var Header=Pyramid.Header;
        var (U,V)=TerrainTileAddress.Uv(Header,Point);
        var Width=Header.LevelWidth(this.Level);
        var HeightPixels=Header.LevelHeight(this.Level);
        var Channels=Math.Min(3,Header.Channels);
        var Result=Below;
        for(var Channel=0;Channel<Channels;Channel++){
            var Value=Pyramid.SampleBilinear(this.Level,U*Width,V*HeightPixels,Channel);
            if(float.IsNaN(Value)){ return Below; }
            Result[Channel]=TerrainColor.SrgbToLinear(Value);
        }
        if(Channels==1){ Result=new Vector3(Result.X); }
        return Result;
    }
}
/// <summary>傾きで重ねる層（急な所に岩の色）。<see cref="From"/> から <see cref="To"/> の傾きで 0 から 1 へ混ぜる。</summary>
public sealed record SlopeColorLayer(Vector3 Color,float From=0.35f,float To=0.6f):ITerrainColorLayer {
    public Vector3 Apply(Vector3 Point,float Height,float Slope,Vector3 Below)=>Vector3.Lerp(Below,this.Color,Smooth(this.From,this.To,Slope));
    internal static float Smooth(float From,float To,float Value){
        var T=Math.Clamp((Value-From)/MathF.Max(To-From,1e-6f),0f,1f);
        return T*T*(3f-2f*T);
    }
}
/// <summary>高さで重ねる層（高い所に別の色）。<see cref="From"/> から <see cref="To"/> メートルで 0 から 1 へ混ぜる。</summary>
public sealed record HeightColorLayer(Vector3 Color,float From,float To,float Strength=1f):ITerrainColorLayer {
    public Vector3 Apply(Vector3 Point,float Height,float Slope,Vector3 Below)=>Vector3.Lerp(Below,this.Color,SlopeColorLayer.Smooth(this.From,this.To,Height)*this.Strength);
}
/// <summary>タイルの階層の画素の位置（0〜1）。</summary>
public static class TerrainTileAddress {
    public static (float U,float V)Uv(TilePyramidHeader Header,Vector3 Point){
        if(Header.Projection==TileProjection.Equirectangular){
            var Longitude=MathF.Atan2(Point.Z,Point.X)*180f/MathF.PI;
            var Latitude=MathF.Asin(Math.Clamp(Point.Y,-1f,1f))*180f/MathF.PI;
            var U=(Longitude-Header.West)/(Header.East-Header.West);
            return (U-MathF.Floor(U),(Header.North-Latitude)/(Header.North-Header.South));
        }
        return ((Point.X-Header.West)/(Header.East-Header.West),(Header.North-Point.Z)/(Header.North-Header.South));
    }
}
/// <summary>sRGB とリニアの変換。</summary>
public static class TerrainColor {
    public static float SrgbToLinear(float Value)=>Value<=0.04045f?Value/12.92f:MathF.Pow((Value+0.055f)/1.055f,2.4f);
    public static float LinearToSrgb(float Value){
        Value=Math.Clamp(Value,0f,1f);
        return Value<=0.0031308f?Value*12.92f:1.055f*MathF.Pow(Value,1f/2.4f)-0.055f;
    }
}
/// <summary>
/// 地形の仮想テクスチャのページの作り手（ADR-0100 決定 7、M0135）。マス 1 つに 1 ページ（<see cref="PageTexels"/> × <see cref="PageTexels"/>、周りに 1 テクセルの縁）を、
/// 層を下から順に重ねて作る。ページのキーはマスのキー（面、深さ、x、y）で、高さのタイルと同じ番地。テクセルの点は、マスの格子の分数の位置を、
/// 球なら立方体の点を正規化した向き（float）、平面なら中心からの位置にしたもの。傾きは高さの差から求める。
/// </summary>
public sealed class TerrainVirtualTexture(TerrainSettings Settings,ITerrainHeightField Heights,IReadOnlyList<ITerrainColorLayer>Layers,int PageTexels=64,Vector3?BaseColor=null) {
    public const int Border=1;
    public int PageTexels { get; }=PageTexels>=4?PageTexels:throw new ArgumentOutOfRangeException(nameof(PageTexels));
    /// <summary>縁を含めたページの一辺（テクセル）。</summary>
    public int PageStride=>this.PageTexels+2*Border;
    private readonly Vector3 Base=BaseColor??new Vector3(0.5f);
    /// <summary>マス <paramref name="Key"/> のページ（縁を含む PageStride²、RGBA8、色は sRGB で詰める）。</summary>
    public byte[]ComposePage(TerrainCellKey Key){
        var Stride=this.PageStride;
        var Result=new byte[Stride*Stride*4];
        for(var Y=0;Y<Stride;Y++){
            for(var X=0;X<Stride;X++){
                var Color=this.Texel(Key,(X-Border+0.5f)/this.PageTexels,(Y-Border+0.5f)/this.PageTexels);
                var At=(Y*Stride+X)*4;
                Result[At]=(byte)MathF.Round(TerrainColor.LinearToSrgb(Color.X)*255f);
                Result[At+1]=(byte)MathF.Round(TerrainColor.LinearToSrgb(Color.Y)*255f);
                Result[At+2]=(byte)MathF.Round(TerrainColor.LinearToSrgb(Color.Z)*255f);
                Result[At+3]=255;
            }
        }
        return Result;
    }
    /// <summary>マスの中の分数の位置 (<paramref name="U"/>, <paramref name="V"/>)（0〜1、外へ少しはみ出してよい）の色（リニア RGB）。</summary>
    public Vector3 Texel(TerrainCellKey Key,float U,float V){
        var Point=this.PointAt(Key,U,V);
        var Height=Heights.Height(Point);
        var Slope=this.Slope(Key,U,V,Height);
        var Color=this.Base;
        foreach(var Layer in Layers){ Color=Layer.Apply(Point,Height,Slope,Color); }
        return Color;
    }
    /// <summary>マスの中の分数の位置の点（球は向き、平面は中心からの (x, 0, z)）。</summary>
    public Vector3 PointAt(TerrainCellKey Key,float U,float V){
        var Span=(float)(Settings.LatticeSize>>Key.Depth);
        var I=(Key.X+U)*Span;
        var J=(Key.Y+V)*Span;
        if(Settings.Shape==TerrainShape.Sphere){ return Vector3.Normalize(TerrainCells.CubeDirection(Key.Face,I,J,Settings.LatticeSize)); }
        Settings.SizeMeters.TryToSingle(out var Size);
        return new Vector3(I/Settings.LatticeSize*Size-Size/2f,0f,J/Settings.LatticeSize*Size-Size/2f);
    }
    /// <summary>傾き（0 = 平ら、1 = 垂直）。テクセルの間隔で高さの差を取る。</summary>
    private float Slope(TerrainCellKey Key,float U,float V,float Height){
        var Step=1f/this.PageTexels;
        var Spacing=TerrainCells.EdgeLengthMeters(Settings,Key.Depth)*Step;
        var Du=Heights.Height(this.PointAt(Key,U+Step,V))-Height;
        var Dv=Heights.Height(this.PointAt(Key,U,V+Step))-Height;
        var Normal=Vector3.Normalize(new Vector3(-Du,Spacing,-Dv));
        return 1f-Normal.Y;
    }
}
