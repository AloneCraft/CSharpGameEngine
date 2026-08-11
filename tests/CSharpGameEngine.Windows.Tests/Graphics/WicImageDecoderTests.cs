using CSharpGameEngine.Import.Gltf;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>WIC decode needs no D3D12 device, so these are Windows-only, not GPU-category tests.</summary>
public class WicImageDecoderTests
{
    private static string FixturePath(string FileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", FileName);

    [WindowsOnlyFact]
    public void Decode_RealPngFromBoxTextured_Succeeds()
    {
        GltfImportResult ImportResult = GltfImporter.Import(FixturePath("BoxTextured.glb"));
        Assert.True(ImportResult.Success, string.Join(Environment.NewLine, ImportResult.Diagnostics));
        byte[]? TextureBytes = ImportResult.Mesh!.BaseColorTextureBytes;
        Assert.NotNull(TextureBytes);

        WicDecodeResult Result = WicImageDecoder.Decode(TextureBytes!);

        Assert.True(Result.Success, Result.Error);
        Assert.NotNull(Result.Image);
        Assert.True(Result.Image!.Width > 0);
        Assert.True(Result.Image.Height > 0);
        Assert.Equal(Result.Image.Width * Result.Image.Height * 4, Result.Image.Pixels.Length);
    }

    [WindowsOnlyFact]
    public void Decode_EmptyBytes_FailsCleanly()
    {
        WicDecodeResult Result = WicImageDecoder.Decode([]);

        Assert.False(Result.Success);
        Assert.Null(Result.Image);
        Assert.NotNull(Result.Error);
    }

    [WindowsOnlyFact]
    public void Decode_GarbageBytes_FailsNotException()
    {
        byte[] Garbage = "NOT_A_VALID_IMAGE_FILE_JUST_GARBAGE_BYTES_FOR_TESTING"u8.ToArray();

        WicDecodeResult Result = WicImageDecoder.Decode(Garbage);

        Assert.False(Result.Success);
        Assert.Null(Result.Image);
        Assert.NotNull(Result.Error);
    }

    [WindowsOnlyFact]
    public void Decode_NullBytes_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WicImageDecoder.Decode(null!));
    }
}
