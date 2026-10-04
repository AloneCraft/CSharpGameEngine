using CSharpGameEngine.Import.Gltf;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>WIC decode needs no D3D12 device, so these are Windows-only, not GPU-category tests.</summary>
public class WicImageDecoderTests {
    private static string FixturePath(string FileName)=>Path.Combine(AppContext.BaseDirectory,"Fixtures",FileName);

    [WindowsOnlyFact]
    public void Decode_RealPngFromBoxTextured_Succeeds(){
        var ImportResult=GltfImporter.Import(FixturePath("BoxTextured.glb"));
        Assert.True(ImportResult.Success,string.Join(Environment.NewLine,ImportResult.Diagnostics));
        var TextureBytes=ImportResult.Mesh!.BaseColorTextureBytes;
        Assert.NotNull(TextureBytes);

        var Result=WicImageDecoder.Decode(TextureBytes!);

        Assert.True(Result.Success,Result.Error);
        Assert.NotNull(Result.Image);
        Assert.True(Result.Image!.Width>0);
        Assert.True(Result.Image.Height>0);
        Assert.Equal(Result.Image.Width*Result.Image.Height*4,Result.Image.Pixels.Length);
    }

    /// <summary>
    /// An image whose header claims more pixels than one array holds (a broken or hostile file) fails like any other bad
    /// image. The pixel buffer size was computed in unchecked 32-bit arithmetic, so 65535x65535 wrapped around and the
    /// allocation threw out of the decoder.
    /// </summary>
    [WindowsOnlyTheory]
    [InlineData(65535u,65535u)]
    [InlineData(32768u,32768u)]
    [InlineData(1073741825u,1u)]
    public void Decode_HeaderClaimingAHugeImage_FailsNotException(uint Width,uint Height){
        var Result=WicImageDecoder.Decode(Png(Width,Height));

        Assert.False(Result.Success);
        Assert.Null(Result.Image);
        Assert.NotNull(Result.Error);
    }

    /// <summary>An RGBA PNG whose IHDR claims <paramref name="Width"/> x <paramref name="Height"/> and whose IDAT is empty.</summary>
    private static byte[]Png(uint Width,uint Height){
        static void BigEndian(List<byte>Bytes,uint Value){ Bytes.Add((byte)(Value>>24));Bytes.Add((byte)(Value>>16));Bytes.Add((byte)(Value>>8));Bytes.Add((byte)Value); }
        static uint Crc(IEnumerable<byte>Bytes){
            var Crc=0xFFFFFFFFu;
            foreach(var Byte in Bytes){
                Crc^=Byte;
                for(var Bit=0;Bit<8;Bit++){ Crc=(Crc&1)!=0?(Crc>>1)^0xEDB88320u:Crc>>1; }
            }
            return ~Crc;
        }
        static void Chunk(List<byte>Png,string Type,byte[]Data){
            BigEndian(Png,(uint)Data.Length);
            var Typed=new List<byte>(System.Text.Encoding.ASCII.GetBytes(Type));
            Typed.AddRange(Data);
            Png.AddRange(Typed);
            BigEndian(Png,Crc(Typed));
        }
        var Png=new List<byte>{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A};
        var Header=new List<byte>();
        BigEndian(Header,Width);
        BigEndian(Header,Height);
        Header.AddRange(new byte[]{8,6,0,0,0});
        Chunk(Png,"IHDR",[.. Header]);
        Chunk(Png,"IDAT",[0x78,0x9C,0x03,0x00,0x00,0x00,0x00,0x01]);
        Chunk(Png,"IEND",[]);
        return [.. Png];
    }

    [WindowsOnlyFact]
    public void Decode_EmptyBytes_FailsCleanly(){
        var Result=WicImageDecoder.Decode([]);

        Assert.False(Result.Success);
        Assert.Null(Result.Image);
        Assert.NotNull(Result.Error);
    }

    [WindowsOnlyFact]
    public void Decode_GarbageBytes_FailsNotException(){
        var Garbage="NOT_A_VALID_IMAGE_FILE_JUST_GARBAGE_BYTES_FOR_TESTING"u8.ToArray();

        var Result=WicImageDecoder.Decode(Garbage);

        Assert.False(Result.Success);
        Assert.Null(Result.Image);
        Assert.NotNull(Result.Error);
    }

    [WindowsOnlyFact]
    public void Decode_NullBytes_Throws(){
        Assert.Throws<ArgumentNullException>(()=>WicImageDecoder.Decode(null!));
    }
}
