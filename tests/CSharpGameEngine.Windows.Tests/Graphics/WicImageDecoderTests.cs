using CSharpGameEngine.Import.Gltf;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>WICデコードにはD3D12デバイスが不要。そのためWindows専用テストだが、GPUカテゴリには含めない。</summary>
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
    /// 1つの配列に収まらないピクセル数をヘッダーに申告する画像（破損または悪意のあるファイル）は、他の不正画像と同様に失敗する。
    /// ピクセルバッファーサイズを検査なしの32ビット演算で計算していたため、65535x65535では値がラップし、
    /// デコーダー内での割り当てが例外になっていた。
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

    /// <summary>
    /// デコード画像はDirect3D 12テクスチャとなり、各辺の上限は16384。これを超えるとピクセル読み込み前にテクスチャサイズ超過として失敗する。
    /// ヘッダーが20000 x 20000と申告する画像では、先にピクセル用として1.6 GBを確保していた。
    /// </summary>
    [WindowsOnlyTheory]
    [InlineData(16385u,1u)]
    [InlineData(1u,16385u)]
    [InlineData(20000u,20000u)]
    public void Decode_ImageBeyondTheTextureLimit_FailsAsTooLarge(uint Width,uint Height){
        var Before=GC.GetAllocatedBytesForCurrentThread();
        var Result=WicImageDecoder.Decode(Png(Width,Height));
        var Allocated=GC.GetAllocatedBytesForCurrentThread()-Before;

        Assert.False(Result.Success);
        Assert.Contains("16384",Result.Error,StringComparison.Ordinal);
        Assert.True(Allocated<1<<20,$"allocated {Allocated} bytes");
    }

    /// <summary><paramref name="Width"/> x <paramref name="Height"/>をIHDRに記載し、IDATが空のRGBA PNG。</summary>
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
