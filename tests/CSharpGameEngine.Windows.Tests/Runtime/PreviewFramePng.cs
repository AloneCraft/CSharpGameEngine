using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Windows.Tests.Runtime;
/// <summary>
/// 描画テストのフレームを PNG に保存する診断用ヘルパー。環境変数 <c>CGE_SAVE_PREVIEW_PNG</c> にディレクトリを指定したときだけ書く
/// （通常のテスト実行では何も書かない）。テンプレートの見た目を目で確認するために使う。
/// </summary>
internal static class PreviewFramePng {
    internal static void SaveIfRequested(PixelReadbackResult Frame,string Name){
        var Directory=Environment.GetEnvironmentVariable("CGE_SAVE_PREVIEW_PNG");
        if(string.IsNullOrWhiteSpace(Directory)){ return; }
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllBytes(Path.Combine(Directory,Name+".png"),Encode(Frame));
    }
    internal static byte[]Encode(PixelReadbackResult Frame){
        using var Output=new MemoryStream();
        Output.Write([137,80,78,71,13,10,26,10]);
        var Header=new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(Header,Frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(Header.AsSpan(4),Frame.Height);
        Header[8]=8;Header[9]=2;
        Chunk(Output,"IHDR",Header);
        using var Compressed=new MemoryStream();
        using(var Zlib=new ZLibStream(Compressed,CompressionLevel.Fastest,true)){
            var Row=new byte[Frame.Width*3];
            for(var Y=0;Y<Frame.Height;Y++){
                for(var X=0;X<Frame.Width;X++){
                    var Offset=(long)Y*Frame.RowPitch+X*4;
                    var Bgra=Frame.Format==RenderPixelFormat.Bgra8Unorm;
                    Row[X*3]=Frame.Pixels[Offset+(Bgra?2:0)];
                    Row[X*3+1]=Frame.Pixels[Offset+1];
                    Row[X*3+2]=Frame.Pixels[Offset+(Bgra?0:2)];
                }
                Zlib.WriteByte(0);
                Zlib.Write(Row);
            }
        }
        Chunk(Output,"IDAT",Compressed.ToArray());
        Chunk(Output,"IEND",[]);
        return Output.ToArray();
    }
    private static void Chunk(Stream Output,string Type,byte[]Data){
        Span<byte>Number=stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(Number,Data.Length);
        Output.Write(Number);
        var Body=Encoding.ASCII.GetBytes(Type).Concat(Data).ToArray();
        Output.Write(Body);
        var Crc=uint.MaxValue;
        foreach(var Byte in Body){
            Crc^=Byte;
            for(var Bit=0;Bit<8;Bit++){ Crc=(Crc>>1)^((Crc&1)!=0?0xedb88320u:0u); }
        }
        BinaryPrimitives.WriteUInt32BigEndian(Number,~Crc);
        Output.Write(Number);
    }
}
