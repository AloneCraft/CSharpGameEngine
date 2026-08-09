using CSharpGameEngine.Import.Gltf;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>WIC decode needs no D3D12 device, so these are Windows-only, not GPU-category tests.</summary>
public class WicImageDecoderTests
{
    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    [WindowsOnlyFact]
    public void Decode_RealPngFromBoxTextured_Succeeds()
    {
        GltfImportResult importResult = GltfImporter.Import(FixturePath("BoxTextured.glb"));
        Assert.True(importResult.Success, string.Join(Environment.NewLine, importResult.Diagnostics));
        byte[]? textureBytes = importResult.Mesh!.BaseColorTextureBytes;
        Assert.NotNull(textureBytes);

        WicDecodeResult result = WicImageDecoder.Decode(textureBytes!);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Image);
        Assert.True(result.Image!.Width > 0);
        Assert.True(result.Image.Height > 0);
        Assert.Equal(result.Image.Width * result.Image.Height * 4, result.Image.Pixels.Length);
    }

    [WindowsOnlyFact]
    public void Decode_EmptyBytes_FailsCleanly()
    {
        WicDecodeResult result = WicImageDecoder.Decode([]);

        Assert.False(result.Success);
        Assert.Null(result.Image);
        Assert.NotNull(result.Error);
    }

    [WindowsOnlyFact]
    public void Decode_GarbageBytes_FailsNotException()
    {
        byte[] garbage = "NOT_A_VALID_IMAGE_FILE_JUST_GARBAGE_BYTES_FOR_TESTING"u8.ToArray();

        WicDecodeResult result = WicImageDecoder.Decode(garbage);

        Assert.False(result.Success);
        Assert.Null(result.Image);
        Assert.NotNull(result.Error);
    }

    [WindowsOnlyFact]
    public void Decode_NullBytes_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WicImageDecoder.Decode(null!));
    }
}
