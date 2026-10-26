using System.Buffers.Binary;
using System.IO.Compression;
namespace CSharpGameEngine.Import.Images;
/// <summary>圧縮されていない埋め込み FBX テクセルの決定論的なソース表現。</summary>
internal static class PngSourceEncoder {
    internal static byte[]Encode(int Width,int Height,byte[]Pixels){
        CSharpGameEngine.Content.ImageArtifactCodec.ValidateSize(Width,Height);
        if(Pixels.Length!=checked(Width*Height*4)){ throw new InvalidDataException("Invalid embedded RGBA image."); }
        using var Result=new MemoryStream();Result.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string Type,byte[]Data){
            Span<byte>Length=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(Length,Data.Length);Result.Write(Length);
            var Body=System.Text.Encoding.ASCII.GetBytes(Type).Concat(Data).ToArray();Result.Write(Body);var Crc=uint.MaxValue;
            foreach(var B in Body){ Crc^=B;for(var I=0;I<8;I++){ Crc=(Crc>>1)^((Crc&1)!=0?0xedb88320u:0u); } }
            BinaryPrimitives.WriteUInt32BigEndian(Length,~Crc);Result.Write(Length);
        }
        var Header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(Header,Width);BinaryPrimitives.WriteInt32BigEndian(Header.AsSpan(4),Height);Header[8]=8;Header[9]=6;Chunk("IHDR",Header);
        using var Compressed=new MemoryStream();
        using(var Zlib=new ZLibStream(Compressed,CompressionLevel.SmallestSize,true)){
            for(var Y=0;Y<Height;Y++){ Zlib.WriteByte(0);Zlib.Write(Pixels,Y*Width*4,Width*4); }
        }
        Chunk("IDAT",Compressed.ToArray());Chunk("IEND",[]);return Result.ToArray();
    }
}
