using CSharpGameEngine.Rendering;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// ゴールデンフレームの証拠と再固定用の補助手段（ADR-0084 §7）。<c>CGE_GOLDEN_DUMP=&lt;dir&gt;</c>を指定すると、描画した各ケースを
/// 生のRGBA8画像とPNGで<c>&lt;dir&gt;/&lt;label&gt;/</c>へ保存する。<c>CGE_GOLDEN_BASELINE=&lt;dir&gt;</c>を指定すると、過去の実行で保存した
/// 生フレームと比較する。固定済みハッシュに変更があった場合、測定差分も確認できる。
/// </summary>
internal static class GoldenFrameDump {
    public static string?DumpDirectory=>System.Environment.GetEnvironmentVariable("CGE_GOLDEN_DUMP");
    public static string?BaselineDirectory=>System.Environment.GetEnvironmentVariable("CGE_GOLDEN_BASELINE");
    public static string Hash(PixelReadbackResult Frame)=>Convert.ToHexString(SHA256.HashData(Frame.Pixels));
    /// <summary>色チャンネルのいずれかが異なるピクセル数と、1チャンネルあたりの最大8ビット差分（アルファチャンネルは比較しない）。</summary>
    public static (int Different,int Largest)Difference(byte[]Expected,byte[]Actual){
        Assert.Equal(Expected.Length,Actual.Length);
        var Largest=0;
        var Different=0;
        for(var Index=0;Index<Expected.Length;Index+=4){
            var PixelLargest=0;
            for(var Channel=0;Channel<3;Channel++){
                PixelLargest=Math.Max(PixelLargest,Math.Abs(Expected[Index+Channel]-Actual[Index+Channel]));
            }
            Largest=Math.Max(Largest,PixelLargest);
            if(PixelLargest>0){ Different++; }
        }
        return (Different,Largest);
    }
    /// <summary><c>CGE_GOLDEN_DUMP</c>が設定されていればフレームを保存し、<c>CGE_GOLDEN_BASELINE</c>があればベースラインと比較した結果を返す（どちらもなければ空文字列）。</summary>
    public static string Record(string Label,string Case,PixelReadbackResult Frame){
        var Rows=Tight(Frame);
        var Directory=DumpDirectory;
        if(Directory!=null){
            var Folder=Path.Combine(Directory,Label);
            System.IO.Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder,Case+".rgba"),Rows);
            File.WriteAllBytes(Path.Combine(Folder,Case+".png"),EncodePng(Frame.Width,Frame.Height,Rows));
        }
        var Baseline=BaselineDirectory;
        if(Baseline==null){
            return "";
        }
        var Path0=Path.Combine(Baseline,Label,Case+".rgba");
        if(!File.Exists(Path0)){
            return $"{Label}/{Case}: no baseline";
        }
        var Old=File.ReadAllBytes(Path0);
        if(Old.Length!=Rows.Length){
            return $"{Label}/{Case}: baseline has another size";
        }
        var (Different,Largest)=Difference(Old,Rows);
        return $"{Label}/{Case}: {Different} of {Rows.Length/4} pixels differ from the baseline, largest channel difference {Largest}";
    }
    /// <summary>マイルストーン文書が引用するパリティ表を書き出す（CGE_GOLDEN_DUMP）。</summary>
    public static void WriteReport(string Name,object Report){
        var Directory=DumpDirectory;
        if(Directory==null){
            return;
        }
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(Path.Combine(Directory,Name),JsonSerializer.Serialize(Report,new JsonSerializerOptions { WriteIndented=true }));
    }
    /// <summary>GPUの行パディングを除いた、フレームの各行データ。</summary>
    private static byte[]Tight(PixelReadbackResult Frame){
        var RowBytes=Frame.Width*4;
        var Result=new byte[RowBytes*Frame.Height];
        for(var Row=0;Row<Frame.Height;Row++){
            Buffer.BlockCopy(Frame.Pixels,Row*(int)Frame.RowPitch,Result,Row*RowBytes,RowBytes);
        }
        return Result;
    }
    private static byte[]EncodePng(int Width,int Height,byte[]Rows){
        using var Output=new MemoryStream();
        Output.Write([0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A]);
        var Header=new byte[13];
        WriteBigEndian(Header,0,(uint)Width);
        WriteBigEndian(Header,4,(uint)Height);
        Header[8]=8;
        Header[9]=6;
        WriteChunk(Output,"IHDR",Header);
        using var Raw=new MemoryStream();
        using(var Deflate=new ZLibStream(Raw,CompressionLevel.Optimal,leaveOpen:true)){
            for(var Row=0;Row<Height;Row++){
                Deflate.WriteByte(0);
                Deflate.Write(Rows,Row*Width*4,Width*4);
            }
        }
        WriteChunk(Output,"IDAT",Raw.ToArray());
        WriteChunk(Output,"IEND",[]);
        return Output.ToArray();
    }
    private static void WriteChunk(Stream Output,string Type,byte[]Data){
        var Length=new byte[4];
        WriteBigEndian(Length,0,(uint)Data.Length);
        Output.Write(Length);
        var Body=new byte[4+Data.Length];
        for(var Index=0;Index<4;Index++){
            Body[Index]=(byte)Type[Index];
        }
        Buffer.BlockCopy(Data,0,Body,4,Data.Length);
        Output.Write(Body);
        var Crc=new byte[4];
        WriteBigEndian(Crc,0,Crc32(Body));
        Output.Write(Crc);
    }
    private static void WriteBigEndian(byte[]Bytes,int Offset,uint Value){
        Bytes[Offset]=(byte)(Value>>24);
        Bytes[Offset+1]=(byte)(Value>>16);
        Bytes[Offset+2]=(byte)(Value>>8);
        Bytes[Offset+3]=(byte)Value;
    }
    private static uint Crc32(byte[]Bytes){
        var Crc=0xFFFFFFFFu;
        foreach(var Value in Bytes){
            Crc^=Value;
            for(var Bit=0;Bit<8;Bit++){
                Crc=(Crc&1)!=0?(Crc>>1)^0xEDB88320u:Crc>>1;
            }
        }
        return ~Crc;
    }
}
