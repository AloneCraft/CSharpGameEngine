using Xunit;
using System.Numerics;
using CSharpGameEngine.Content.Tiles;
using CSharpGameEngine.Import.Images;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.World.Terrain;
namespace CSharpGameEngine.World.Tests;
/// <summary>
/// 地形の仮想テクスチャのページ（M0135、ADR-0100 決定 7）。ページは同じ入力から同じバイト列で、隣のページとの縁がそろい（つなぎ目が出ない）、
/// 原本の色のタイルとの差が小さい（ページの解像度と 8 ビットに詰める分だけ）ことを確かめる。差の大きさは記録する。
/// </summary>
public sealed class TerrainVirtualTextureTests:IDisposable {
    private readonly string Folder=Path.Combine(Path.GetTempPath(),"cge-rvt-"+Guid.NewGuid().ToString("N"));
    public TerrainVirtualTextureTests()=>Directory.CreateDirectory(this.Folder);
    public void Dispose(){
        try { Directory.Delete(this.Folder,true); } catch(IOException) { }
    }
    private sealed class Ramp(float Amplitude):ITerrainHeightField {
        public float Height(Vector3 Point)=>Amplitude*MathF.Sin(Point.X*0.01f)*MathF.Cos(Point.Z*0.013f);
    }
    /// <summary>なめらかな色（RGB、0〜1）の正方形の画像（平面、範囲 ±1024 m）。</summary>
    private sealed class ColorField(int Size):IRasterSource {
        public int Width=>Size;
        public int Height=>Size;
        public int Channels=>3;
        public string Unit=>"srgb";
        public float?NoData=>null;
        public void ReadRows(int Row,int Rows,Span<float>Destination){
            for(var Y=0;Y<Rows;Y++){
                for(var X=0;X<Size;X++){
                    var At=(Y*Size+X)*3;
                    Destination[At]=0.5f+0.4f*MathF.Sin(X*0.02f);
                    Destination[At+1]=0.5f+0.4f*MathF.Cos((Row+Y)*0.017f);
                    Destination[At+2]=0.3f+0.2f*MathF.Sin((X+Row+Y)*0.01f);
                }
            }
        }
        public void Dispose(){ }
    }
    private TilePyramid Colors(){
        var Index=Path.Combine(this.Folder,"Colors.cgetiles");
        if(!File.Exists(Index)){
            using var Source=new ColorField(1024);
            _=TilePyramidCompiler.Compile(Source,Index,new TilePyramidCompileOptions { TileSize=128,Encoding=TileSampleEncoding.UNorm16,Projection=TileProjection.Planar,West=-1024f,East=1024f,North=1024f,South=-1024f });
        }
        return TilePyramid.Open(Index);
    }
    private static TerrainSettings Planar=>new(TerrainShape.Planar,LogicalVector3.Zero,LogicalScalar.FromInt64(2048),GridQuads:8,MaximumDepth:6);
    [Fact]
    public void PagesAreDeterministicAndNeighbourBordersMatch(){
        using var Pyramid=this.Colors();
        var Texture=new TerrainVirtualTexture(Planar,new Ramp(40f),[new TileColorLayer(Pyramid),new SlopeColorLayer(new Vector3(0.1f))],PageTexels:32);
        var Left=new TerrainCellKey(0,3,2,5);
        var Right=new TerrainCellKey(0,3,3,5);
        var Page=Texture.ComposePage(Left);
        Assert.Equal(Page,Texture.ComposePage(Left));
        var Neighbour=Texture.ComposePage(Right);
        var Stride=Texture.PageStride;
        var Largest=0;
        // 左のページの右の縁（x = Stride − 1）は、右のページの最初の内側の列（x = 1）と同じ点。
        for(var Y=1;Y<Stride-1;Y++){
            for(var Channel=0;Channel<3;Channel++){
                Largest=Math.Max(Largest,Math.Abs(Page[(Y*Stride+Stride-1)*4+Channel]-Neighbour[(Y*Stride+1)*4+Channel]));
                Largest=Math.Max(Largest,Math.Abs(Page[(Y*Stride+Stride-2)*4+Channel]-Neighbour[(Y*Stride)*4+Channel]));
            }
        }
        Assert.True(Largest<=1,$"the borders differ by up to {Largest} of 255");
    }
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void APageStaysCloseToTheSourceColors(int Depth){
        using var Pyramid=this.Colors();
        var Layer=new TileColorLayer(Pyramid);
        var Texture=new TerrainVirtualTexture(Planar,FlatTerrainHeightField.Instance,[Layer],PageTexels:64);
        var Key=new TerrainCellKey(0,Depth,(1<<Depth)/2,(1<<Depth)/3);
        var Page=Texture.ComposePage(Key);
        var Stride=Texture.PageStride;
        var Random=new Random(Depth);
        var Total=0f;
        var Largest=0f;
        const int Samples=2000;
        for(var Sample=0;Sample<Samples;Sample++){
            var U=Random.NextSingle();
            var V=Random.NextSingle();
            // 描画と同じく、ページを双線形で読む（縁の内側、テクセルの中心が整数 + 0.5）。
            var X=TerrainVirtualTexture.Border+U*Texture.PageTexels-0.5f;
            var Y=TerrainVirtualTexture.Border+V*Texture.PageTexels-0.5f;
            var X0=(int)MathF.Floor(X);
            var Y0=(int)MathF.Floor(Y);
            var Tx=X-X0;
            var Ty=Y-Y0;
            for(var Channel=0;Channel<3;Channel++){
                float Read(int Px,int Py)=>TerrainColor.SrgbToLinear(Page[(Py*Stride+Px)*4+Channel]/255f);
                var Fetched=(Read(X0,Y0)*(1f-Tx)+Read(X0+1,Y0)*Tx)*(1f-Ty)+(Read(X0,Y0+1)*(1f-Tx)+Read(X0+1,Y0+1)*Tx)*Ty;
                var Source=Layer.Apply(Texture.PointAt(Key,U,V),0f,0f,Vector3.Zero)[Channel];
                var Error=MathF.Abs(TerrainColor.LinearToSrgb(Fetched)-TerrainColor.LinearToSrgb(Source));
                Total+=Error;
                Largest=MathF.Max(Largest,Error);
            }
        }
        var Mean=Total/(Samples*3);
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"rvt-error.txt"),$"depth {Depth}: mean {Mean*255f:F2}/255, largest {Largest*255f:F2}/255{System.Environment.NewLine}");
        // 深いマス（ページ 1 テクセルが原本の画素より細かい）ほど差は 8 ビットの丸めに近づく。浅いマスは原本の細部を平均するので差が大きい。
        Assert.True(Mean*255f<(Depth>=4?1.5f:12f),$"depth {Depth}: mean error {Mean*255f:F2} of 255");
    }
}
