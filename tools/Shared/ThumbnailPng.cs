using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace CSharpGameEngine.Tools.Imaging;
internal static class ThumbnailPng {
    /// <summary>Texture Editor の派生プレビュー（最大 2048px）も出力するため、上限は AssetThumbnailRequest.MaximumImageEdge と同じ。ジオメトリのサムネイルは要求側が 128px に固定している。</summary>
    private const int Edge=2048;
    internal static byte[]Encode(int Width,int Height,byte[]Pixels){
        if(Width is <1 or >Edge||Height is <1 or >Edge||Pixels.Length!=checked(Width*Height*4)){ throw new InvalidDataException("Thumbnail pixels are invalid."); }
        using var Output=new MemoryStream();Output.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string Type,byte[]Data){
            Span<byte>Number=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(Number,Data.Length);Output.Write(Number);
            var Body=Encoding.ASCII.GetBytes(Type).Concat(Data).ToArray();Output.Write(Body);var Crc=uint.MaxValue;
            foreach(var Byte in Body){ Crc^=Byte;for(var Bit=0;Bit<8;Bit++){ Crc=(Crc>>1)^((Crc&1)!=0?0xedb88320u:0u); } }
            BinaryPrimitives.WriteUInt32BigEndian(Number,~Crc);Output.Write(Number);
        }
        var Header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(Header,Width);BinaryPrimitives.WriteInt32BigEndian(Header.AsSpan(4),Height);Header[8]=8;Header[9]=6;Chunk("IHDR",Header);
        using var Compressed=new MemoryStream();
        using(var Zlib=new ZLibStream(Compressed,CompressionLevel.Fastest,true)){
            for(var Y=0;Y<Height;Y++){ Zlib.WriteByte(0);Zlib.Write(Pixels,Y*Width*4,Width*4); }
        }
        Chunk("IDAT",Compressed.ToArray());Chunk("IEND",[]);return Output.ToArray();
    }
}