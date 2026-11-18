using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace CSharpGameEngine.Content.Tiles;
/// <summary>タイルの値の詰め方。</summary>
public enum TileSampleEncoding:byte {
    /// <summary>32 ビットの浮動小数（標高など）。</summary>
    Float32=0,
    /// <summary>0〜1 を 16 ビットに（色）。</summary>
    UNorm16=1,
    /// <summary>0〜1 を 8 ビットに（色）。</summary>
    UNorm8=2,
}
/// <summary>画像の並べ方（地図の投影）。</summary>
public enum TileProjection:byte {
    /// <summary>平らな面（x と y がそのまま位置）。</summary>
    Planar=0,
    /// <summary>経緯度の等間隔（正距円筒）。列は西から東、行は北から南。画素の中心で登録。</summary>
    Equirectangular=1,
}
/// <summary>
/// タイルの階層の見出し（ADR-0100 決定 7、M0134）。段 0 が元の解像度で、段 L は各辺が 2^L 分の 1（切り上げ）。いちばん粗い段は 1 枚のタイルに収まる。
/// <see cref="West"/>〜<see cref="South"/> は正距円筒なら度（画素の外の縁）、平らなら位置の範囲（メートル）。
/// </summary>
public sealed record TilePyramidHeader(int Width,int Height,int Channels,int TileSize,int LevelCount,TileSampleEncoding Encoding,TileProjection Projection,
    float West,float East,float North,float South,float?NoData,string Unit) {
    public int LevelWidth(int Level)=>Math.Max(1,(int)(((long)this.Width+(1L<<Level)-1)>>Level));
    public int LevelHeight(int Level)=>Math.Max(1,(int)(((long)this.Height+(1L<<Level)-1)>>Level));
    public int TilesAcross(int Level)=>(this.LevelWidth(Level)+this.TileSize-1)/this.TileSize;
    public int TilesDown(int Level)=>(this.LevelHeight(Level)+this.TileSize-1)/this.TileSize;
}
/// <summary>タイル 1 枚の場所。</summary>
public readonly record struct TileKey(int Level,int X,int Y);
/// <summary>
/// タイルの階層の読み手（ADR-0100 決定 7、M0134）。置き場は「索引 1 つ＋まとめファイル 1 つ」で、1 タイル 1 ファイルにしない（数百万件になるため）。
/// <list type="bullet">
/// <item>索引 <c>*.cgetiles</c>: 印 <c>CGETILE1</c>、版、見出し、タイルの数、各タイルの (段, x, y, まとめファイルの中の位置, 長さ)。キーの順（段、y、x）に並ぶ。リトルエンディアン。</item>
/// <item>まとめファイル <c>*.cgetilepack</c>: タイルごとに Deflate で縮めた値を続けて並べる。タイル 1 枚は TileSize × TileSize × Channels の値（端のタイルも同じ大きさで、外は端の値を繰り返す）。</item>
/// </list>
/// 同じ入力から同じバイト列になる（前処理の約束）。
/// </summary>
public sealed class TilePyramid:IDisposable {
    public const string IndexExtension=".cgetiles";
    public const string PackExtension=".cgetilepack";
    public const int FormatVersion=1;
    private static ReadOnlySpan<byte>Magic=>"CGETILE1"u8;
    private readonly FileStream Pack;
    private readonly Dictionary<TileKey,(long Offset,int Length)>Index;
    private readonly Dictionary<TileKey,float[]>Cache=[];
    private readonly LinkedList<TileKey>CacheOrder=[];
    private TilePyramid(TilePyramidHeader Header,Dictionary<TileKey,(long Offset,int Length)>Index,FileStream Pack){
        this.Header=Header;
        this.Index=Index;
        this.Pack=Pack;
    }
    public TilePyramidHeader Header { get; }
    public int TileCount=>this.Index.Count;
    /// <summary>覚えておくタイルの数（使っていないのが最も古いものから捨てる）。</summary>
    public int CacheCapacity { get;set; }=256;
    /// <summary>索引のパスから開く（まとめファイルは同じ名前で拡張子が <see cref="PackExtension"/>）。</summary>
    public static TilePyramid Open(string IndexPath){
        using var Reader=new BinaryReader(File.OpenRead(IndexPath),Encoding.UTF8);
        if(!Reader.ReadBytes(8).AsSpan().SequenceEqual(Magic)){ throw new InvalidDataException($"'{IndexPath}' is not a tile pyramid index."); }
        var Version=Reader.ReadInt32();
        if(Version!=FormatVersion){ throw new InvalidDataException($"'{IndexPath}' has tile pyramid version {Version}; {FormatVersion} is read."); }
        var Header=ReadHeader(Reader);
        var Count=Reader.ReadInt32();
        var Index=new Dictionary<TileKey,(long,int)>(Count);
        for(var Entry=0;Entry<Count;Entry++){
            var Key=new TileKey(Reader.ReadInt32(),Reader.ReadInt32(),Reader.ReadInt32());
            Index[Key]=(Reader.ReadInt64(),Reader.ReadInt32());
        }
        var PackPath=Path.ChangeExtension(IndexPath,PackExtension);
        return new TilePyramid(Header,Index,new FileStream(PackPath,FileMode.Open,FileAccess.Read,FileShare.Read));
    }
    public bool Contains(TileKey Key)=>this.Index.ContainsKey(Key);
    /// <summary>タイル 1 枚の値（TileSize × TileSize × Channels、行優先）。無いタイルは null。</summary>
    public float[]?ReadTile(TileKey Key){
        if(this.Cache.TryGetValue(Key,out var Cached)){
            this.CacheOrder.Remove(Key);
            this.CacheOrder.AddLast(Key);
            return Cached;
        }
        if(!this.Index.TryGetValue(Key,out var Where)){ return null; }
        var Compressed=new byte[Where.Length];
        lock(this.Pack){
            this.Pack.Position=Where.Offset;
            this.Pack.ReadExactly(Compressed);
        }
        var Samples=this.Header.TileSize*this.Header.TileSize*this.Header.Channels;
        var Result=DecodeTile(Compressed,Samples,this.Header.Encoding);
        this.Cache[Key]=Result;
        this.CacheOrder.AddLast(Key);
        while(this.Cache.Count>this.CacheCapacity){
            this.Cache.Remove(this.CacheOrder.First!.Value);
            this.CacheOrder.RemoveFirst();
        }
        return Result;
    }
    /// <summary>
    /// 段 <paramref name="Level"/> の画素の位置 (<paramref name="X"/>, <paramref name="Y"/>)（画素の中心が整数 + 0.5）の、チャンネル <paramref name="Channel"/> の双線形の値。
    /// 外は端の値。正距円筒なら x は東西に回り込む。値の無い所（<see cref="TilePyramidHeader.NoData"/>）は混ぜない（全部無ければ NaN）。
    /// </summary>
    public float SampleBilinear(int Level,float X,float Y,int Channel=0){
        var Width=this.Header.LevelWidth(Level);
        var Height=this.Header.LevelHeight(Level);
        var Fx=X-0.5f;
        var Fy=Y-0.5f;
        var X0=(int)MathF.Floor(Fx);
        var Y0=(int)MathF.Floor(Fy);
        var Tx=Fx-X0;
        var Ty=Fy-Y0;
        var Sum=0f;
        var Weight=0f;
        for(var Dy=0;Dy<2;Dy++){
            for(var Dx=0;Dx<2;Dx++){
                var W=(Dx==0?1f-Tx:Tx)*(Dy==0?1f-Ty:Ty);
                if(W<=0f){ continue; }
                var Value=this.Texel(Level,X0+Dx,Y0+Dy,Width,Height,Channel);
                if(float.IsNaN(Value)){ continue; }
                Sum+=Value*W;
                Weight+=W;
            }
        }
        return Weight>0f?Sum/Weight:float.NaN;
    }
    /// <summary>段 <paramref name="Level"/> の 1 画素（外は端、正距円筒は東西に回り込む）。値が無い所は NaN。</summary>
    public float Texel(int Level,int X,int Y,int Width,int Height,int Channel=0){
        X=this.Header.Projection==TileProjection.Equirectangular?((X%Width)+Width)%Width:Math.Clamp(X,0,Width-1);
        Y=Math.Clamp(Y,0,Height-1);
        var Size=this.Header.TileSize;
        var Tile=this.ReadTile(new TileKey(Level,X/Size,Y/Size));
        if(Tile is null){ return float.NaN; }
        var Value=Tile[((Y%Size)*Size+X%Size)*this.Header.Channels+Channel];
        return this.Header.NoData is{ }NoData&&Value==NoData?float.NaN:Value;
    }
    internal static float[]DecodeTile(byte[]Compressed,int Samples,TileSampleEncoding Encoding){
        var Bytes=Samples*BytesPerSample(Encoding);
        var Raw=new byte[Bytes];
        using(var Input=new DeflateStream(new MemoryStream(Compressed),CompressionMode.Decompress)){
            Input.ReadExactly(Raw);
        }
        var Result=new float[Samples];
        for(var Index=0;Index<Samples;Index++){
            Result[Index]=Encoding switch {
                TileSampleEncoding.Float32=>BinaryPrimitives.ReadSingleLittleEndian(Raw.AsSpan(Index*4)),
                TileSampleEncoding.UNorm16=>BinaryPrimitives.ReadUInt16LittleEndian(Raw.AsSpan(Index*2))/65535f,
                _=>Raw[Index]/255f,
            };
        }
        return Result;
    }
    /// <summary>タイル 1 枚を詰めて縮める（前処理が使う）。同じ値から同じバイト列。</summary>
    public static byte[]EncodeTile(ReadOnlySpan<float>Samples,TileSampleEncoding Encoding){
        var Raw=new byte[Samples.Length*BytesPerSample(Encoding)];
        for(var Index=0;Index<Samples.Length;Index++){
            switch(Encoding){
                case TileSampleEncoding.Float32:BinaryPrimitives.WriteSingleLittleEndian(Raw.AsSpan(Index*4),Samples[Index]);break;
                case TileSampleEncoding.UNorm16:BinaryPrimitives.WriteUInt16LittleEndian(Raw.AsSpan(Index*2),(ushort)MathF.Round(Math.Clamp(Samples[Index],0f,1f)*65535f));break;
                default:Raw[Index]=(byte)MathF.Round(Math.Clamp(Samples[Index],0f,1f)*255f);break;
            }
        }
        using var Output=new MemoryStream();
        using(var Deflate=new DeflateStream(Output,CompressionLevel.Optimal,leaveOpen:true)){
            Deflate.Write(Raw);
        }
        return Output.ToArray();
    }
    public static int BytesPerSample(TileSampleEncoding Encoding)=>Encoding switch { TileSampleEncoding.Float32=>4,TileSampleEncoding.UNorm16=>2,_=>1 };
    /// <summary>索引を書く（前処理が使う）。<paramref name="Entries"/> はキーの順に並べて渡す。</summary>
    public static void WriteIndex(string IndexPath,TilePyramidHeader Header,IReadOnlyList<(TileKey Key,long Offset,int Length)>Entries){
        using var Writer=new BinaryWriter(File.Create(IndexPath),Encoding.UTF8);
        Writer.Write(Magic);
        Writer.Write(FormatVersion);
        Writer.Write(Header.Width);
        Writer.Write(Header.Height);
        Writer.Write(Header.Channels);
        Writer.Write(Header.TileSize);
        Writer.Write(Header.LevelCount);
        Writer.Write((byte)Header.Encoding);
        Writer.Write((byte)Header.Projection);
        Writer.Write(Header.West);
        Writer.Write(Header.East);
        Writer.Write(Header.North);
        Writer.Write(Header.South);
        Writer.Write(Header.NoData.HasValue);
        Writer.Write(Header.NoData??0f);
        Writer.Write(Header.Unit);
        Writer.Write(Entries.Count);
        foreach(var (Key,Offset,Length) in Entries){
            Writer.Write(Key.Level);
            Writer.Write(Key.X);
            Writer.Write(Key.Y);
            Writer.Write(Offset);
            Writer.Write(Length);
        }
    }
    private static TilePyramidHeader ReadHeader(BinaryReader Reader){
        var Width=Reader.ReadInt32();
        var Height=Reader.ReadInt32();
        var Channels=Reader.ReadInt32();
        var TileSize=Reader.ReadInt32();
        var Levels=Reader.ReadInt32();
        var Encoding=(TileSampleEncoding)Reader.ReadByte();
        var Projection=(TileProjection)Reader.ReadByte();
        var West=Reader.ReadSingle();
        var East=Reader.ReadSingle();
        var North=Reader.ReadSingle();
        var South=Reader.ReadSingle();
        var HasNoData=Reader.ReadBoolean();
        var NoData=Reader.ReadSingle();
        var Unit=Reader.ReadString();
        if(Width<=0||Height<=0||Channels<=0||TileSize<=0||Levels<=0){ throw new InvalidDataException("The tile pyramid header is invalid."); }
        return new TilePyramidHeader(Width,Height,Channels,TileSize,Levels,Encoding,Projection,West,East,North,South,HasNoData?NoData:null,Unit);
    }
    public void Dispose()=>this.Pack.Dispose();
}
