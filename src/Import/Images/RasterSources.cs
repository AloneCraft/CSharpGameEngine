using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
namespace CSharpGameEngine.Import.Images;
/// <summary>
/// 大きな画像を、行の帯ごとに少しずつ読む入口（ADR-0100 決定 7、M0134）。全体をメモリに載せない。
/// 値は float で返し、<see cref="Scale"/> と <see cref="Offset"/> を掛けた後の値（標高ならメートル）。帯の中は行優先・画素の中はチャンネル順。
/// </summary>
public interface IRasterSource:IDisposable {
    int Width { get; }
    int Height { get; }
    int Channels { get; }
    /// <summary>値の意味（記録用。例: 「height-meters」「srgb-reflectance」）。</summary>
    string Unit { get; }
    /// <summary>値が無い所の印（あれば）。換算した後の値で比べる。</summary>
    float?NoData { get; }
    /// <summary>行 <paramref name="Row"/> から <paramref name="Rows"/> 行を読む。<paramref name="Destination"/> は Width × Rows × Channels。</summary>
    void ReadRows(int Row,int Rows,Span<float>Destination);
}
/// <summary>生の画素の型。</summary>
public enum RasterSampleType { UInt8,Int16,UInt16,Int32,UInt32,Float32,Float64 }
/// <summary>
/// ラベルの無い生の画像の説明（NASA PDS の IMG のように、ラベルが別にあって手元に無いとき）。値 = 生の値 × Scale + Offset。
/// </summary>
public sealed record RawRasterDescription(int Width,int Height,int Channels,RasterSampleType SampleType,bool LittleEndian,float Scale=1f,float Offset=0f,long HeaderBytes=0,float?NoData=null,string Unit="");
/// <summary>画素の型の大きさと読み方。</summary>
internal static class RasterSamples {
    internal static int Bytes(RasterSampleType Type)=>Type switch {
        RasterSampleType.UInt8=>1,
        RasterSampleType.Int16 or RasterSampleType.UInt16=>2,
        RasterSampleType.Int32 or RasterSampleType.UInt32 or RasterSampleType.Float32=>4,
        _=>8,
    };
    internal static float Read(ReadOnlySpan<byte>Bytes,RasterSampleType Type,bool LittleEndian)=>Type switch {
        RasterSampleType.UInt8=>Bytes[0],
        RasterSampleType.Int16=>LittleEndian?BinaryPrimitives.ReadInt16LittleEndian(Bytes):BinaryPrimitives.ReadInt16BigEndian(Bytes),
        RasterSampleType.UInt16=>LittleEndian?BinaryPrimitives.ReadUInt16LittleEndian(Bytes):BinaryPrimitives.ReadUInt16BigEndian(Bytes),
        RasterSampleType.Int32=>LittleEndian?BinaryPrimitives.ReadInt32LittleEndian(Bytes):BinaryPrimitives.ReadInt32BigEndian(Bytes),
        RasterSampleType.UInt32=>LittleEndian?BinaryPrimitives.ReadUInt32LittleEndian(Bytes):BinaryPrimitives.ReadUInt32BigEndian(Bytes),
        RasterSampleType.Float32=>LittleEndian?BinaryPrimitives.ReadSingleLittleEndian(Bytes):BinaryPrimitives.ReadSingleBigEndian(Bytes),
        _=>throw new NotSupportedException("64-bit floating point samples are not supported (the engine keeps raster values in float)."),
    };
}
/// <summary>
/// 生の画像（NASA PDS3 の IMG など）。付いているラベル（先頭の <c>PDS_VERSION_ID</c>）か、同じ名前の <c>.LBL</c> か、<see cref="RawRasterDescription"/> で形を知る。
/// 行は先頭から順に並ぶ（チャンネルは画素ごとに並ぶ。PDS の BAND_STORAGE_TYPE = BAND_SEQUENTIAL は 1 チャンネルだけ受け付ける）。
/// </summary>
public sealed class RawRasterSource:IRasterSource {
    private readonly FileStream Stream;
    private readonly RawRasterDescription Description;
    private byte[]Buffer=[];
    public RawRasterSource(string Path,RawRasterDescription Description){
        ArgumentNullException.ThrowIfNull(Description);
        if(Description.Width<=0||Description.Height<=0||Description.Channels<=0||!float.IsFinite(Description.Scale)||!float.IsFinite(Description.Offset)||Description.HeaderBytes<0){
            throw new ArgumentException("The raw raster description is invalid.",nameof(Description));
        }
        this.Description=Description;
        this.Stream=new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.Read,1<<16,FileOptions.SequentialScan);
        var Expected=Description.HeaderBytes+(long)Description.Width*Description.Height*Description.Channels*RasterSamples.Bytes(Description.SampleType);
        var Length=this.Stream.Length;
        if(Length<Expected){
            this.Stream.Dispose();
            throw new InvalidDataException($"'{Path}' has {Length} bytes; the description needs {Expected}.");
        }
    }
    /// <summary>
    /// PDS3 のラベル（付いているか、同じ名前の .LBL）から開く。ラベルが無ければ <see cref="InvalidDataException"/>（呼び出し側が説明を渡す）。
    /// </summary>
    public static RawRasterSource OpenPds(string Path){
        var Label=ReadAttachedLabel(Path,out var RecordBytes);
        var LabelPath=Path[..^System.IO.Path.GetExtension(Path).Length];
        if(Label is null){
            foreach(var Candidate in new[]{ LabelPath+".LBL",LabelPath+".lbl" }){
                if(File.Exists(Candidate)){ Label=File.ReadAllText(Candidate,Encoding.ASCII); break; }
            }
            RecordBytes=0;
        }
        if(Label is null){ throw new InvalidDataException($"'{Path}' has no PDS label; describe it with RawRasterDescription."); }
        return new RawRasterSource(Path,ParsePdsLabel(Label,RecordBytes));
    }
    /// <summary>PDS3 のラベルの IMAGE の部分から形を読む（試験と、ラベルだけを確かめる道具のため公開）。</summary>
    public static RawRasterDescription ParsePdsLabel(string Label,long AttachedRecordBytes){
        var Values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var InImage=false;
        string?ImagePointer=null;
        foreach(var RawLine in Label.Split('\n')){
            var Line=RawLine.Trim();
            var Equals=Line.IndexOf('=');
            if(Equals<0){ continue; }
            var Key=Line[..Equals].Trim();
            var Value=Line[(Equals+1)..].Trim().Trim('"');
            if(Key.Equals("^IMAGE",StringComparison.OrdinalIgnoreCase)){ ImagePointer=Value; continue; }
            if(Key.Equals("OBJECT",StringComparison.OrdinalIgnoreCase)&&Value.Equals("IMAGE",StringComparison.OrdinalIgnoreCase)){ InImage=true; continue; }
            if(Key.Equals("END_OBJECT",StringComparison.OrdinalIgnoreCase)&&InImage){ InImage=false; continue; }
            if(InImage||Key.Equals("RECORD_BYTES",StringComparison.OrdinalIgnoreCase)){ Values[Key]=Value; }
        }
        static int Integer(Dictionary<string,string>Values,string Key,int Default)=>Values.TryGetValue(Key,out var Text)&&int.TryParse(Text.Split(' ')[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out var Value)?Value:Default;
        static float Number(Dictionary<string,string>Values,string Key,float Default)=>Values.TryGetValue(Key,out var Text)&&float.TryParse(Text.Split(' ')[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var Value)?Value:Default;
        var Width=Integer(Values,"LINE_SAMPLES",0);
        var Height=Integer(Values,"LINES",0);
        var Bands=Integer(Values,"BANDS",1);
        var Bits=Integer(Values,"SAMPLE_BITS",0);
        Values.TryGetValue("SAMPLE_TYPE",out var TypeText);
        TypeText=(TypeText??string.Empty).ToUpperInvariant();
        var Little=TypeText.StartsWith("LSB",StringComparison.Ordinal)||TypeText.StartsWith("PC_",StringComparison.Ordinal)||TypeText.StartsWith("VAX",StringComparison.Ordinal);
        var Real=TypeText.Contains("REAL",StringComparison.Ordinal);
        var Unsigned=TypeText.Contains("UNSIGNED",StringComparison.Ordinal);
        var Type=(Bits,Real,Unsigned) switch {
            (8,false,_)=>RasterSampleType.UInt8,
            (16,false,true)=>RasterSampleType.UInt16,
            (16,false,false)=>RasterSampleType.Int16,
            (32,false,true)=>RasterSampleType.UInt32,
            (32,false,false)=>RasterSampleType.Int32,
            (32,true,_)=>RasterSampleType.Float32,
            (64,true,_)=>RasterSampleType.Float64,
            _=>throw new InvalidDataException($"The PDS sample type '{TypeText}' with {Bits} bits is not supported."),
        };
        if(Width<=0||Height<=0){ throw new InvalidDataException("The PDS label has no LINES or LINE_SAMPLES."); }
        if(Bands!=1&&Values.TryGetValue("BAND_STORAGE_TYPE",out var Storage)&&!Storage.Contains("SAMPLE_INTERLEAVED",StringComparison.OrdinalIgnoreCase)){
            throw new InvalidDataException($"The PDS band storage '{Storage}' is not supported; only one band or sample-interleaved bands are read.");
        }
        long Header=0;
        if(ImagePointer is not null){
            // ^IMAGE = 3（レコードの番号、1 から）か ("FILE.IMG", 3) か 2048 <BYTES>。
            var Pointer=ImagePointer.Trim('(',')');
            var Parts=Pointer.Split(',');
            var Last=Parts[^1].Trim();
            if(Last.EndsWith("<BYTES>",StringComparison.OrdinalIgnoreCase)){
                Header=long.Parse(Last[..^7].Trim(),CultureInfo.InvariantCulture)-1;
            } else if(long.TryParse(Last,NumberStyles.Integer,CultureInfo.InvariantCulture,out var Record)&&Parts.Length==1){
                var RecordBytes=AttachedRecordBytes>0?AttachedRecordBytes:Integer(Values,"RECORD_BYTES",0);
                Header=(Record-1)*RecordBytes;
            }
        }
        float?NoData=Values.ContainsKey("MISSING_CONSTANT")?Number(Values,"MISSING_CONSTANT",0f)*Number(Values,"SCALING_FACTOR",1f)+Number(Values,"OFFSET",0f):null;
        Values.TryGetValue("UNIT",out var Unit);
        return new RawRasterDescription(Width,Height,Bands,Type,Little,Number(Values,"SCALING_FACTOR",1f),Number(Values,"OFFSET",0f),Header,NoData,Unit?.Trim('<','>')??string.Empty);
    }
    private static string?ReadAttachedLabel(string Path,out long RecordBytes){
        RecordBytes=0;
        using var File=new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.Read);
        var Head=new byte[Math.Min(65536,File.Length)];
        File.ReadExactly(Head);
        var Text=Encoding.ASCII.GetString(Head);
        if(!Text.StartsWith("PDS_VERSION_ID",StringComparison.Ordinal)&&!Text.StartsWith("ODL_VERSION_ID",StringComparison.Ordinal)){ return null; }
        var End=Text.IndexOf("\nEND",StringComparison.Ordinal);
        var Label=End>0?Text[..End]:Text;
        foreach(var Line in Label.Split('\n')){
            var Parts=Line.Split('=');
            if(Parts.Length==2&&Parts[0].Trim().Equals("RECORD_BYTES",StringComparison.OrdinalIgnoreCase)){ _=long.TryParse(Parts[1].Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out RecordBytes); }
        }
        return Label;
    }
    public int Width=>this.Description.Width;
    public int Height=>this.Description.Height;
    public int Channels=>this.Description.Channels;
    public string Unit=>this.Description.Unit;
    public float?NoData=>this.Description.NoData;
    public void ReadRows(int Row,int Rows,Span<float>Destination){
        ArgumentOutOfRangeException.ThrowIfNegative(Row);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Row+Rows,this.Height);
        var Samples=this.Width*this.Channels;
        var SampleBytes=RasterSamples.Bytes(this.Description.SampleType);
        var Bytes=checked(Samples*Rows*SampleBytes);
        if(this.Buffer.Length<Bytes){ this.Buffer=new byte[Bytes]; }
        this.Stream.Position=this.Description.HeaderBytes+(long)Row*Samples*SampleBytes;
        this.Stream.ReadExactly(this.Buffer,0,Bytes);
        for(var Index=0;Index<Samples*Rows;Index++){
            Destination[Index]=RasterSamples.Read(this.Buffer.AsSpan(Index*SampleBytes,SampleBytes),this.Description.SampleType,this.Description.LittleEndian)*this.Description.Scale+this.Description.Offset;
        }
    }
    public void Dispose()=>this.Stream.Dispose();
}
/// <summary>
/// TIFF と BigTIFF（最初の画像だけ）。帯（strip）かタイル、圧縮なし・LZW・Deflate、水平の予測（Predictor 2）、画素ごとか面ごとのチャンネル、
/// 8/16/32 ビットの整数と 32/64 ビットの浮動小数を読む。それ以外は <see cref="NotSupportedException"/>（読めないものは黙って変えずにはっきり拒む）。
/// </summary>
public sealed class TiffRasterSource:IRasterSource {
    private readonly FileStream Stream;
    private readonly bool Little;
    private readonly int BitsPerSample;
    private readonly RasterSampleType SampleType;
    private readonly int Compression;
    private readonly int Predictor;
    private readonly bool Planar;
    private readonly int ChunkWidth;
    private readonly int ChunkHeight;
    private readonly bool Tiled;
    private readonly long[]ChunkOffsets;
    private readonly long[]ChunkBytes;
    private int CachedChunk=-1;
    private byte[]Chunk=[];
    public TiffRasterSource(string Path,float Scale=1f,float Offset=0f,string Unit=""){
        this.Stream=new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.Read,1<<16);
        this.Scale=Scale;
        this.Offset=Offset;
        this.Unit=Unit;
        try {
            Span<byte>Head=stackalloc byte[16];
            if(this.Stream.Length<16){ throw new InvalidDataException($"'{Path}' is not a TIFF file."); }
            this.Stream.ReadExactly(Head);
            this.Little=Head[0]==(byte)'I';
            if(Head[0]!=Head[1]||Head[0]!=(byte)'I'&&Head[0]!=(byte)'M'){ throw new InvalidDataException($"'{Path}' is not a TIFF file."); }
            var Magic=this.U16(Head[2..]);
            var Big=Magic==43;
            if(Magic!=42&&!Big){ throw new InvalidDataException($"'{Path}' is not a TIFF file."); }
            var Directory=Big?(long)this.U64(Head[8..]):this.U32(Head[4..]);
            var Tags=this.ReadDirectory(Directory,Big);
            long Tag(int Id,long Default=-1)=>Tags.TryGetValue(Id,out var Values)&&Values.Length>0?Values[0]:Default;
            this.Width=checked((int)Tag(256));
            this.Height=checked((int)Tag(257));
            this.Channels=checked((int)Tag(277,1));
            this.BitsPerSample=checked((int)Tag(258,1));
            if(Tags.TryGetValue(258,out var AllBits)&&AllBits.Any(Value=>Value!=this.BitsPerSample)){ throw new NotSupportedException("TIFF channels with different bit depths are not supported."); }
            this.Compression=checked((int)Tag(259,1));
            this.Predictor=checked((int)Tag(317,1));
            this.Planar=Tag(284,1)==2&&this.Channels>1;
            var Format=Tag(339,1);
            this.SampleType=(Format,this.BitsPerSample) switch {
                (1,8)=>RasterSampleType.UInt8,
                (1,16)=>RasterSampleType.UInt16,
                (2,16)=>RasterSampleType.Int16,
                (1,32)=>RasterSampleType.UInt32,
                (2,32)=>RasterSampleType.Int32,
                (3,32)=>RasterSampleType.Float32,
                (3,64)=>RasterSampleType.Float64,
                (2,8)=>throw new NotSupportedException("Signed 8-bit TIFF samples are not supported."),
                _=>throw new NotSupportedException($"TIFF sample format {Format} with {this.BitsPerSample} bits is not supported."),
            };
            if(this.Compression is not (1 or 5 or 8 or 32946)){ throw new NotSupportedException($"TIFF compression {this.Compression} is not supported (none, LZW and Deflate are)."); }
            if(this.Predictor is not (1 or 2)){ throw new NotSupportedException($"TIFF predictor {this.Predictor} is not supported."); }
            this.Tiled=Tags.ContainsKey(322);
            this.ChunkWidth=this.Tiled?checked((int)Tag(322)):this.Width;
            this.ChunkHeight=this.Tiled?checked((int)Tag(323)):checked((int)Math.Min(Tag(278,this.Height),this.Height));
            this.ChunkOffsets=Tags[this.Tiled?324:273];
            this.ChunkBytes=Tags[this.Tiled?325:279];
            if(this.NoDataText is{ }NoDataText&&float.TryParse(NoDataText,NumberStyles.Float,CultureInfo.InvariantCulture,out var NoDataValue)){ this.NoData=NoDataValue*Scale+Offset; }
        } catch {
            this.Stream.Dispose();
            throw;
        }
    }
    private string?NoDataText;
    public int Width { get; }
    public int Height { get; }
    public int Channels { get; }
    public string Unit { get; }
    public float?NoData { get; }
    private float Scale { get; }
    private float Offset { get; }
    private int SampleBytes=>this.BitsPerSample/8;
    private int ChunksAcross=>(this.Width+this.ChunkWidth-1)/this.ChunkWidth;
    private int ChunksDown=>(this.Height+this.ChunkHeight-1)/this.ChunkHeight;
    public void ReadRows(int Row,int Rows,Span<float>Destination){
        ArgumentOutOfRangeException.ThrowIfNegative(Row);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Row+Rows,this.Height);
        var Planes=this.Planar?this.Channels:1;
        var Interleaved=this.Planar?1:this.Channels;
        for(var Y=Row;Y<Row+Rows;Y++){
            var ChunkRow=Y/this.ChunkHeight;
            var InChunk=Y%this.ChunkHeight;
            for(var Plane=0;Plane<Planes;Plane++){
                for(var Across=0;Across<this.ChunksAcross;Across++){
                    var Index=(Plane*this.ChunksDown+ChunkRow)*this.ChunksAcross+Across;
                    var Data=this.LoadChunk(Index);
                    var X0=Across*this.ChunkWidth;
                    var Count=Math.Min(this.ChunkWidth,this.Width-X0);
                    var RowStart=InChunk*this.ChunkWidth*Interleaved*this.SampleBytes;
                    for(var X=0;X<Count;X++){
                        for(var Channel=0;Channel<Interleaved;Channel++){
                            var Source=RowStart+(X*Interleaved+Channel)*this.SampleBytes;
                            var Value=RasterSamples.Read(Data.AsSpan(Source,this.SampleBytes),this.SampleType,this.Little)*this.Scale+this.Offset;
                            Destination[((Y-Row)*this.Width+X0+X)*this.Channels+Plane+Channel]=Value;
                        }
                    }
                }
            }
        }
    }
    /// <summary>塊を読んで伸ばし、予測を戻す（塊は帯かタイル。最後に読んだ塊を覚えておく）。</summary>
    private byte[]LoadChunk(int Index){
        if(Index==this.CachedChunk){ return this.Chunk; }
        var Interleaved=this.Planar?1:this.Channels;
        var Expected=this.ChunkWidth*this.ChunkHeight*Interleaved*this.SampleBytes;
        var Raw=new byte[this.ChunkBytes[Index]];
        this.Stream.Position=this.ChunkOffsets[Index];
        this.Stream.ReadExactly(Raw);
        var Data=this.Compression switch {
            1=>Raw,
            5=>TiffLzw.Decode(Raw,Expected),
            _=>Inflate(Raw,Expected),
        };
        if(Data.Length<Expected){ Array.Resize(ref Data,Expected); }
        if(this.Predictor==2){ this.UndoPredictor(Data,Interleaved); }
        this.Chunk=Data;
        this.CachedChunk=Index;
        return Data;
    }
    private static byte[]Inflate(byte[]Raw,int Expected){
        using var Input=new ZLibStream(new MemoryStream(Raw),CompressionMode.Decompress);
        var Result=new byte[Expected];
        var Read=0;
        while(Read<Expected){
            var Count=Input.Read(Result,Read,Expected-Read);
            if(Count==0){ break; }
            Read+=Count;
        }
        return Result;
    }
    /// <summary>水平の予測: 同じ行の左の画素との差を足し戻す（8/16/32 ビットの整数）。</summary>
    private void UndoPredictor(byte[]Data,int Interleaved){
        var Stride=this.ChunkWidth*Interleaved;
        for(var Y=0;Y<this.ChunkHeight;Y++){
            for(var X=Interleaved;X<Stride;X++){
                var At=(Y*Stride+X)*this.SampleBytes;
                var Left=(Y*Stride+X-Interleaved)*this.SampleBytes;
                switch(this.SampleBytes){
                    case 1:Data[At]=(byte)(Data[At]+Data[Left]);break;
                    case 2:{
                        var Value=(ushort)(this.U16(Data.AsSpan(At))+this.U16(Data.AsSpan(Left)));
                        if(this.Little){ BinaryPrimitives.WriteUInt16LittleEndian(Data.AsSpan(At),Value); } else { BinaryPrimitives.WriteUInt16BigEndian(Data.AsSpan(At),Value); }
                        break;
                    }
                    case 4:{
                        if(this.SampleType==RasterSampleType.Float32){ throw new NotSupportedException("Horizontal prediction of floating point TIFF samples is not supported."); }
                        var Value=this.U32(Data.AsSpan(At))+this.U32(Data.AsSpan(Left));
                        if(this.Little){ BinaryPrimitives.WriteUInt32LittleEndian(Data.AsSpan(At),Value); } else { BinaryPrimitives.WriteUInt32BigEndian(Data.AsSpan(At),Value); }
                        break;
                    }
                    default:throw new NotSupportedException("Horizontal prediction of 64-bit TIFF samples is not supported.");
                }
            }
        }
    }
    private Dictionary<int,long[]>ReadDirectory(long Offset,bool Big){
        var Result=new Dictionary<int,long[]>();
        this.Stream.Position=Offset;
        Span<byte>Count=stackalloc byte[8];
        this.Stream.ReadExactly(Count[..(Big?8:2)]);
        var Entries=Big?(long)this.U64(Count):this.U16(Count);
        var EntryBytes=Big?20:12;
        var Table=new byte[Entries*EntryBytes];
        this.Stream.ReadExactly(Table);
        for(var Entry=0;Entry<Entries;Entry++){
            var At=Table.AsSpan(Entry*EntryBytes,EntryBytes);
            int Tag=this.U16(At);
            int Type=this.U16(At[2..]);
            var Values=Big?(long)this.U64(At[4..]):this.U32(At[4..]);
            var Size=Type switch { 1 or 2 or 6 or 7=>1,3 or 8=>2,4 or 9 or 11 or 13=>4,_=>8 };
            var Inline=Big?8:4;
            var Bytes=checked((int)(Values*Size));
            byte[]Data;
            if(Bytes<=Inline){
                Data=At.Slice(Big?12:8,Bytes).ToArray();
            } else {
                var Where=Big?(long)this.U64(At[12..]):this.U32(At[8..]);
                var Back=this.Stream.Position;
                this.Stream.Position=Where;
                Data=new byte[Bytes];
                this.Stream.ReadExactly(Data);
                this.Stream.Position=Back;
            }
            if(Tag==42113&&Type==2){ this.NoDataText=Encoding.ASCII.GetString(Data).TrimEnd('\0').Trim(); continue; }
            if(Type is not (3 or 4 or 16 or 1)){ continue; }
            var Parsed=new long[Values];
            for(var Index=0;Index<Values;Index++){
                Parsed[Index]=Type switch {
                    1=>Data[Index],
                    3=>this.U16(Data.AsSpan(Index*2)),
                    4=>this.U32(Data.AsSpan(Index*4)),
                    _=>(long)this.U64(Data.AsSpan(Index*8)),
                };
            }
            Result[Tag]=Parsed;
        }
        return Result;
    }
    private ushort U16(ReadOnlySpan<byte>Bytes)=>this.Little?BinaryPrimitives.ReadUInt16LittleEndian(Bytes):BinaryPrimitives.ReadUInt16BigEndian(Bytes);
    private uint U32(ReadOnlySpan<byte>Bytes)=>this.Little?BinaryPrimitives.ReadUInt32LittleEndian(Bytes):BinaryPrimitives.ReadUInt32BigEndian(Bytes);
    private ulong U64(ReadOnlySpan<byte>Bytes)=>this.Little?BinaryPrimitives.ReadUInt64LittleEndian(Bytes):BinaryPrimitives.ReadUInt64BigEndian(Bytes);
    public void Dispose()=>this.Stream.Dispose();
}
/// <summary>TIFF の LZW（MSB が先、9〜12 ビットの符号、256 = 表を空に、257 = 終わり。符号の長さは表が 511・1023・2047 で 1 つ早く伸びる）。</summary>
internal static class TiffLzw {
    internal static byte[]Decode(ReadOnlySpan<byte>Input,int Expected){
        var Output=new byte[Expected];
        var Written=0;
        var Prefix=new int[4096];
        var Suffix=new byte[4096];
        var Length=new int[4096];
        for(var Code=0;Code<256;Code++){ Suffix[Code]=(byte)Code;Length[Code]=1;Prefix[Code]=-1; }
        var Next=258;
        var Width=9;
        var Previous=-1;
        long Bits=0;
        var BitCount=0;
        var Position=0;
        Span<byte>Scratch=stackalloc byte[4096];
        while(true){
            while(BitCount<Width){
                if(Position>=Input.Length){ return Output; }
                Bits=Bits<<8|Input[Position++];
                BitCount+=8;
            }
            var Code=(int)(Bits>>(BitCount-Width)&((1<<Width)-1));
            BitCount-=Width;
            Bits&=(1L<<BitCount)-1;
            if(Code==257){ break; }
            if(Code==256){
                Next=258;
                Width=9;
                Previous=-1;
                continue;
            }
            int First;
            if(Code<Next&&(Code<256||Length[Code]>0)){
                First=Emit(Code,Prefix,Suffix,Length,Scratch,Output,ref Written);
            } else if(Code==Next&&Previous>=0){
                First=Emit(Previous,Prefix,Suffix,Length,Scratch,Output,ref Written);
                if(Written<Output.Length){ Output[Written++]=(byte)First; }
            } else {
                throw new InvalidDataException("The TIFF LZW stream is corrupt.");
            }
            if(Previous>=0&&Next<4096){
                Prefix[Next]=Previous;
                Suffix[Next]=(byte)First;
                Length[Next]=Length[Previous]+1;
                Next++;
            }
            Previous=Code;
            if(Next+1>=1<<Width&&Width<12){ Width++; }
        }
        return Output;
    }
    /// <summary>符号 <paramref name="Code"/> の列を書き、その先頭の値を返す。</summary>
    private static int Emit(int Code,int[]Prefix,byte[]Suffix,int[]Length,Span<byte>Scratch,byte[]Output,ref int Written){
        var Count=Length[Code];
        var At=Code;
        for(var Index=Count-1;Index>=0;Index--){
            Scratch[Index]=Suffix[At];
            At=Prefix[At];
        }
        var Copy=Math.Min(Count,Output.Length-Written);
        Scratch[..Copy].CopyTo(Output.AsSpan(Written));
        Written+=Copy;
        return Scratch[0];
    }
}
