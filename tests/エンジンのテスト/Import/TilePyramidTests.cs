using Xunit;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using CSharpGameEngine.Content.Tiles;
using CSharpGameEngine.Import.Images;
namespace CSharpGameEngine.Import.Tests;
/// <summary>
/// 大きな画像の読み手とタイルの階層（M0134 単位 1・2、ADR-0100 決定 7）。PDS の IMG（ラベル付き、ラベルなし）と TIFF（圧縮なし・LZW・Deflate、帯とタイル、
/// 予測あり、チャンネルの並べ方 2 通り）を試験の中で作って読み、作ったタイルの階層を読み戻して、段 0 が元の値と一致し、段 L+1 が段 L の 2×2 の平均になることを確かめる。
/// </summary>
public sealed class TilePyramidTests:IDisposable {
    private readonly string Folder=Path.Combine(Path.GetTempPath(),"cge-tiles-"+Guid.NewGuid().ToString("N"));
    public TilePyramidTests()=>Directory.CreateDirectory(this.Folder);
    public void Dispose(){
        try { Directory.Delete(this.Folder,true); } catch(IOException) { }
    }
    private static float Expected(int X,int Y,int Channel)=>(X*7+Y*13+Channel*101)%997;
    [Fact]
    public void ThePdsLabelDescribesTheImageAndTheReaderAppliesScaleAndOffset(){
        const int Width=37;
        const int Height=11;
        const int RecordBytes=Width*2;
        var Label=new StringBuilder();
        Label.Append("PDS_VERSION_ID = PDS3\r\nRECORD_TYPE = FIXED_LENGTH\r\nRECORD_BYTES = ").Append(RecordBytes).Append("\r\nLABEL_RECORDS = 8\r\n^IMAGE = 9\r\n")
            .Append("OBJECT = IMAGE\r\n  LINES = ").Append(Height).Append("\r\n  LINE_SAMPLES = ").Append(Width)
            .Append("\r\n  SAMPLE_TYPE = LSB_INTEGER\r\n  SAMPLE_BITS = 16\r\n  SCALING_FACTOR = 0.5\r\n  OFFSET = 1737400\r\n  MISSING_CONSTANT = -32768\r\n  UNIT = METER\r\nEND_OBJECT = IMAGE\r\nEND\r\n");
        var Header=Encoding.ASCII.GetBytes(Label.ToString());
        Assert.True(Header.Length<=8*RecordBytes,$"the label has {Header.Length} bytes");
        var Bytes=new byte[8*RecordBytes+Width*Height*2];
        Header.CopyTo(Bytes,0);
        for(var Y=0;Y<Height;Y++){
            for(var X=0;X<Width;X++){ BinaryPrimitives.WriteInt16LittleEndian(Bytes.AsSpan(8*RecordBytes+(Y*Width+X)*2),(short)(X-Y*100)); }
        }
        var Path=System.IO.Path.Combine(this.Folder,"DEM.IMG");
        File.WriteAllBytes(Path,Bytes);
        using var Source=RawRasterSource.OpenPds(Path);
        Assert.Equal((Width,Height,1),(Source.Width,Source.Height,Source.Channels));
        Assert.Equal(-32768f*0.5f+1737400f,Source.NoData);
        Assert.Equal("METER",Source.Unit);
        var Rows=new float[Width*3];
        Source.ReadRows(4,3,Rows);
        Assert.Equal((0-400)*0.5f+1737400f,Rows[0]);
        Assert.Equal((36-600)*0.5f+1737400f,Rows[^1]);
    }
    [Fact]
    public void AHeaderlessImageIsReadWithAnExplicitDescription(){
        const int Width=23;
        const int Height=9;
        var Bytes=new byte[Width*Height*4];
        for(var Index=0;Index<Width*Height;Index++){ BinaryPrimitives.WriteSingleBigEndian(Bytes.AsSpan(Index*4),Index*0.25f); }
        var Path=System.IO.Path.Combine(this.Folder,"Raw.img");
        File.WriteAllBytes(Path,Bytes);
        Assert.Throws<InvalidDataException>(()=>RawRasterSource.OpenPds(Path));
        using var Source=new RawRasterSource(Path,new RawRasterDescription(Width,Height,1,RasterSampleType.Float32,LittleEndian:false,Scale:2f,Offset:-1f));
        var Row=new float[Width];
        Source.ReadRows(Height-1,1,Row);
        Assert.Equal(((Height-1)*Width+5)*0.25f*2f-1f,Row[5]);
        Assert.Throws<InvalidDataException>(()=>new RawRasterSource(Path,new RawRasterDescription(Width,Height+1,1,RasterSampleType.Float32,false)));
    }
    [Theory]
    [InlineData(1,false,false,1,8)]
    [InlineData(5,false,false,1,8)]
    [InlineData(5,false,true,1,8)]
    [InlineData(5,false,false,3,16)]
    [InlineData(1,false,false,3,16)]
    [InlineData(8,true,false,1,32)]
    [InlineData(5,true,true,2,16)]
    [InlineData(1,false,false,3,8,true)]
    public void TiffStripsAndTilesInEveryCompressionReadTheSameValues(int Compression,bool Tiled,bool Predictor,int Channels,int Bits,bool PlanarBands=false){
        const int Width=150;
        const int Height=47;
        var Path=System.IO.Path.Combine(this.Folder,$"Image{Compression}{Tiled}{Predictor}{Channels}{Bits}{PlanarBands}.tif");
        TestTiffWriter.Write(Path,Width,Height,Channels,Bits,Compression,Tiled,Predictor,PlanarBands,(X,Y,Channel)=>Bits==32?Expected(X,Y,Channel)+0.5f:Expected(X,Y,Channel)%(Bits==8?256:65536));
        using var Source=new TiffRasterSource(Path);
        Assert.Equal((Width,Height,Channels),(Source.Width,Source.Height,Source.Channels));
        var Rows=new float[Width*Height*Channels];
        Source.ReadRows(0,Height,Rows);
        for(var Y=0;Y<Height;Y++){
            for(var X=0;X<Width;X++){
                for(var Channel=0;Channel<Channels;Channel++){
                    var Want=Bits==32?Expected(X,Y,Channel)+0.5f:Expected(X,Y,Channel)%(Bits==8?256:65536);
                    Assert.True(Want==Rows[(Y*Width+X)*Channels+Channel],$"({X}, {Y}) channel {Channel}: {Rows[(Y*Width+X)*Channels+Channel]} instead of {Want}");
                }
            }
        }
    }
    [Fact]
    public void UnsupportedTiffsAreRejectedClearly(){
        var Path=System.IO.Path.Combine(this.Folder,"Jpeg.tif");
        TestTiffWriter.Write(Path,8,8,1,8,7,false,false,false,static (_,_,_)=>0f);
        Assert.Throws<NotSupportedException>(()=>new TiffRasterSource(Path));
        File.WriteAllBytes(Path,"not a tiff"u8.ToArray());
        Assert.Throws<InvalidDataException>(()=>new TiffRasterSource(Path));
    }
    [Theory]
    [InlineData(1000,500,1,64,TileSampleEncoding.Float32)]
    [InlineData(333,129,2,32,TileSampleEncoding.Float32)]
    [InlineData(200,100,3,64,TileSampleEncoding.UNorm16)]
    public void ThePyramidKeepsLevelZeroAndAveragesEachLevelAbove(int Width,int Height,int Channels,int TileSize,TileSampleEncoding Encoding){
        var Normal=Encoding!=TileSampleEncoding.Float32;
        float Value(int X,int Y,int Channel)=>Normal?Expected(X,Y,Channel)/997f:Expected(X,Y,Channel);
        using var Source=new FunctionRasterSource(Width,Height,Channels,Value);
        var Index=Path.Combine(this.Folder,$"Pyramid{Width}{Channels}.cgetiles");
        var Report=TilePyramidCompiler.Compile(Source,Index,new TilePyramidCompileOptions { TileSize=TileSize,Encoding=Encoding,RowsPerRead=7 });
        using var Pyramid=TilePyramid.Open(Index);
        var Header=Pyramid.Header;
        Assert.Equal(Report.Levels,Header.LevelCount);
        Assert.Equal(1,Header.TilesAcross(Header.LevelCount-1));
        Assert.Equal(1,Header.TilesDown(Header.LevelCount-1));
        var Tiles=0;
        for(var Level=0;Level<Header.LevelCount;Level++){ Tiles+=Header.TilesAcross(Level)*Header.TilesDown(Level); }
        Assert.Equal(Tiles,Report.Tiles);
        Assert.Equal(Tiles,Pyramid.TileCount);
        var Tolerance=Encoding==TileSampleEncoding.UNorm16?1f/65535f:0f;
        for(var Y=0;Y<Height;Y+=3){
            for(var X=0;X<Width;X+=5){
                for(var Channel=0;Channel<Channels;Channel++){
                    Assert.InRange(Pyramid.Texel(0,X,Y,Width,Height,Channel),Value(X,Y,Channel)-Tolerance,Value(X,Y,Channel)+Tolerance);
                }
            }
        }
        for(var Level=0;Level+1<Header.LevelCount;Level++){
            var LevelWidth=Header.LevelWidth(Level);
            var LevelHeight=Header.LevelHeight(Level);
            for(var Y=0;Y<Header.LevelHeight(Level+1);Y+=2){
                for(var X=0;X<Header.LevelWidth(Level+1);X+=3){
                    for(var Channel=0;Channel<Channels;Channel++){
                        var Sum=0f;
                        foreach(var (Dx,Dy) in new[]{ (0,0),(1,0),(0,1),(1,1) }){
                            Sum+=Pyramid.Texel(Level,Math.Min(2*X+Dx,LevelWidth-1),Math.Min(2*Y+Dy,LevelHeight-1),LevelWidth,LevelHeight,Channel);
                        }
                        var Above=Pyramid.Texel(Level+1,X,Y,Header.LevelWidth(Level+1),Header.LevelHeight(Level+1),Channel);
                        Assert.InRange(Above,Sum/4f-2f*Tolerance-1e-3f,Sum/4f+2f*Tolerance+1e-3f);
                    }
                }
            }
        }
        // 同じ入力から同じバイト列。
        var Again=Path.Combine(this.Folder,$"Again{Width}{Channels}.cgetiles");
        _=TilePyramidCompiler.Compile(new FunctionRasterSource(Width,Height,Channels,Value),Again,new TilePyramidCompileOptions { TileSize=TileSize,Encoding=Encoding,RowsPerRead=64 });
        Assert.Equal(File.ReadAllBytes(Index),File.ReadAllBytes(Again));
        Assert.Equal(File.ReadAllBytes(Path.ChangeExtension(Index,TilePyramid.PackExtension)),File.ReadAllBytes(Path.ChangeExtension(Again,TilePyramid.PackExtension)));
    }
    [Fact]
    public void MissingValuesAreNotMixedIntoCoarserLevels(){
        using var Source=new FunctionRasterSource(64,64,1,static (X,Y,_)=>X<32?-9999f:10f,NoData:-9999f);
        var Index=Path.Combine(this.Folder,"Missing.cgetiles");
        _=TilePyramidCompiler.Compile(Source,Index,new TilePyramidCompileOptions { TileSize=16,Projection=TileProjection.Planar });
        using var Pyramid=TilePyramid.Open(Index);
        Assert.True(float.IsNaN(Pyramid.Texel(0,3,3,64,64)));
        Assert.Equal(10f,Pyramid.Texel(0,40,3,64,64));
        // 段 1 の x = 15 は、元の x = 30・31（無い）を混ぜた画素ではなく無い。x = 16 は 10。
        Assert.True(float.IsNaN(Pyramid.Texel(1,15,3,32,32)));
        Assert.Equal(10f,Pyramid.Texel(1,16,3,32,32));
        // 双線形は無い所を混ぜない。
        Assert.Equal(10f,Pyramid.SampleBilinear(0,32.2f,10.5f));
    }
    /// <summary>関数で値を返す画像（試験用）。</summary>
    private sealed class FunctionRasterSource(int Width,int Height,int Channels,Func<int,int,int,float>Value,float?NoData=null):IRasterSource {
        public int Width { get; }=Width;
        public int Height { get; }=Height;
        public int Channels { get; }=Channels;
        public string Unit=>"test";
        public float?NoData { get; }=NoData;
        public void ReadRows(int Row,int Rows,Span<float>Destination){
            for(var Y=0;Y<Rows;Y++){
                for(var X=0;X<this.Width;X++){
                    for(var Channel=0;Channel<this.Channels;Channel++){ Destination[(Y*this.Width+X)*this.Channels+Channel]=Value(X,Row+Y,Channel); }
                }
            }
        }
        public void Dispose(){ }
    }
}
/// <summary>試験用の TIFF の書き手（帯かタイル、圧縮なし・LZW・Deflate、水平の予測、チャンネルを画素ごとか面ごとに）。</summary>
internal static class TestTiffWriter {
    internal static void Write(string Path,int Width,int Height,int Channels,int Bits,int Compression,bool Tiled,bool Predictor,bool Planar,Func<int,int,int,float>Value){
        var SampleBytes=Bits/8;
        var ChunkWidth=Tiled?32:Width;
        var ChunkHeight=Tiled?16:5;
        var Across=(Width+ChunkWidth-1)/ChunkWidth;
        var Down=(Height+ChunkHeight-1)/ChunkHeight;
        var Planes=Planar?Channels:1;
        var Interleaved=Planar?1:Channels;
        var Chunks=new List<byte[]>();
        for(var Plane=0;Plane<Planes;Plane++){
            for(var ChunkY=0;ChunkY<Down;ChunkY++){
                for(var ChunkX=0;ChunkX<Across;ChunkX++){
                    var Rows=Tiled?ChunkHeight:Math.Min(ChunkHeight,Height-ChunkY*ChunkHeight);
                    var Raw=new byte[ChunkWidth*Rows*Interleaved*SampleBytes];
                    for(var Y=0;Y<Rows;Y++){
                        for(var X=0;X<ChunkWidth;X++){
                            for(var Channel=0;Channel<Interleaved;Channel++){
                                var SourceX=Math.Min(ChunkX*ChunkWidth+X,Width-1);
                                var SourceY=Math.Min(ChunkY*ChunkHeight+Y,Height-1);
                                var V=Value(SourceX,SourceY,Plane+Channel);
                                var At=((Y*ChunkWidth+X)*Interleaved+Channel)*SampleBytes;
                                switch(Bits){
                                    case 8:Raw[At]=(byte)V;break;
                                    case 16:BinaryPrimitives.WriteUInt16LittleEndian(Raw.AsSpan(At),(ushort)V);break;
                                    default:BinaryPrimitives.WriteSingleLittleEndian(Raw.AsSpan(At),V);break;
                                }
                            }
                        }
                    }
                    if(Predictor){ ApplyPredictor(Raw,ChunkWidth,Rows,Interleaved,SampleBytes); }
                    Chunks.Add(Compression switch { 5=>Lzw(Raw),8=>Deflate(Raw),_=>Raw });
                }
            }
        }
        using var Stream=new MemoryStream();
        using var Writer=new BinaryWriter(Stream);
        Writer.Write("II"u8);
        Writer.Write((ushort)42);
        Writer.Write(0u);
        var Offsets=new List<uint>();
        foreach(var Chunk in Chunks){
            Offsets.Add((uint)Stream.Position);
            Writer.Write(Chunk);
            if(Stream.Position%2==1){ Writer.Write((byte)0); }
        }
        var OffsetsAt=(uint)Stream.Position;
        foreach(var Offset in Offsets){ Writer.Write(Offset); }
        var CountsAt=(uint)Stream.Position;
        foreach(var Chunk in Chunks){ Writer.Write((uint)Chunk.Length); }
        var BitsAt=(uint)Stream.Position;
        for(var Channel=0;Channel<Channels;Channel++){ Writer.Write((ushort)Bits); }
        var Entries=new List<(ushort Tag,ushort Type,uint Count,uint Value)>{
            (256,4,1,(uint)Width),(257,4,1,(uint)Height),(258,3,(uint)Channels,Channels==1?(uint)Bits:Channels==2?(uint)Bits|(uint)Bits<<16:BitsAt),(259,3,1,(uint)Compression),(262,3,1,Channels==3?2u:1u),
            (277,3,1,(uint)Channels),(284,3,1,Planar?2u:1u),(317,3,1,Predictor?2u:1u),(339,3,1,Bits==32?3u:1u),
        };
        if(Tiled){
            Entries.Add((322,3,1,(uint)ChunkWidth));
            Entries.Add((323,3,1,(uint)ChunkHeight));
            Entries.Add((324,4,(uint)Chunks.Count,Chunks.Count==1?Offsets[0]:OffsetsAt));
            Entries.Add((325,4,(uint)Chunks.Count,Chunks.Count==1?(uint)Chunks[0].Length:CountsAt));
        } else {
            Entries.Add((273,4,(uint)Chunks.Count,Chunks.Count==1?Offsets[0]:OffsetsAt));
            Entries.Add((278,3,1,(uint)ChunkHeight));
            Entries.Add((279,4,(uint)Chunks.Count,Chunks.Count==1?(uint)Chunks[0].Length:CountsAt));
        }
        Entries.Sort(static (A,B)=>A.Tag.CompareTo(B.Tag));
        var DirectoryAt=(uint)Stream.Position;
        Writer.Write((ushort)Entries.Count);
        foreach(var (Tag,Type,Count,Field) in Entries){
            Writer.Write(Tag);
            Writer.Write(Type);
            Writer.Write(Count);
            if(Type==3&&Count==1){ Writer.Write((ushort)Field);Writer.Write((ushort)0); } else { Writer.Write(Field); }
        }
        Writer.Write(0u);
        Stream.Position=4;
        Writer.Write(DirectoryAt);
        File.WriteAllBytes(Path,Stream.ToArray());
    }
    private static void ApplyPredictor(byte[]Raw,int Width,int Rows,int Interleaved,int SampleBytes){
        var Stride=Width*Interleaved;
        for(var Y=0;Y<Rows;Y++){
            for(var X=Stride-1;X>=Interleaved;X--){
                var At=(Y*Stride+X)*SampleBytes;
                var Left=(Y*Stride+X-Interleaved)*SampleBytes;
                if(SampleBytes==1){ Raw[At]=(byte)(Raw[At]-Raw[Left]); }
                else { BinaryPrimitives.WriteUInt16LittleEndian(Raw.AsSpan(At),(ushort)(BinaryPrimitives.ReadUInt16LittleEndian(Raw.AsSpan(At))-BinaryPrimitives.ReadUInt16LittleEndian(Raw.AsSpan(Left)))); }
            }
        }
    }
    private static byte[]Deflate(byte[]Raw){
        using var Output=new MemoryStream();
        using(var Zlib=new ZLibStream(Output,CompressionLevel.Optimal,leaveOpen:true)){ Zlib.Write(Raw); }
        return Output.ToArray();
    }
    /// <summary>TIFF の LZW（MSB が先、表が 511・1023・2047 になる 1 つ前で符号を伸ばす）。表がいっぱいになったら空にする。</summary>
    private static byte[]Lzw(byte[]Raw){
        var Output=new List<byte>();
        long Bits=0;
        var Count=0;
        var Width=9;
        void Put(int Code){
            Bits=Bits<<Width|(uint)Code;
            Count+=Width;
            while(Count>=8){
                Output.Add((byte)(Bits>>(Count-8)));
                Count-=8;
                Bits&=(1L<<Count)-1;
            }
        }
        var Table=new Dictionary<(int,byte),int>();
        var Next=258;
        Put(256);
        var Current=-1;
        foreach(var Byte in Raw){
            if(Current<0){ Current=Byte;continue; }
            if(Table.TryGetValue((Current,Byte),out var Code)){ Current=Code;continue; }
            Put(Current);
            Table[(Current,Byte)]=Next++;
            // 読み手は表を 1 つ遅れて作るので、書き手は表が 512・1024・2048 になってから符号を伸ばす。
            if(Next>=1<<Width&&Width<12){ Width++; }
            if(Next>=4094){
                Put(256);
                Table.Clear();
                Next=258;
                Width=9;
            }
            Current=Byte;
        }
        if(Current>=0){ Put(Current); }
        // 終わりの符号の前に、表へ 1 つ足した分の伸びを数える（読み手と同じ）。
        if(Current>=0&&Next+1>=1<<Width&&Width<12){ Width++; }
        Put(257);
        if(Count>0){ Output.Add((byte)(Bits<<(8-Count))); }
        return [..Output];
    }
}
