using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

public class TrianglePixelVerifierTests
{
    private static readonly PixelColor Background = new(5, 5, 20, 255);
    private static readonly PixelColor Foreground = new(220, 30, 30, 255);

    private static PixelReadbackResult BuildFrame(int width, int height, uint extraRowPitchPadding, DXGI_FORMAT format, Func<int, int, bool> isForeground)
    {
        int bytesPerPixel = 4;
        uint rowPitch = (uint)(width * bytesPerPixel) + extraRowPitchPadding;
        byte[] pixels = new byte[rowPitch * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                PixelColor color = isForeground(x, y) ? Foreground : Background;
                long offset = (long)y * rowPitch + (x * bytesPerPixel);

                if (format == DXGI_FORMAT_B8G8R8A8_UNORM)
                {
                    pixels[offset] = color.B;
                    pixels[offset + 1] = color.G;
                    pixels[offset + 2] = color.R;
                    pixels[offset + 3] = color.A;
                }
                else
                {
                    pixels[offset] = color.R;
                    pixels[offset + 1] = color.G;
                    pixels[offset + 2] = color.B;
                    pixels[offset + 3] = color.A;
                }
            }
        }

        return new PixelReadbackResult
        {
            Width = width,
            Height = height,
            RowPitch = rowPitch,
            Format = format,
            Pixels = pixels,
        };
    }

    [Fact]
    public void Verify_CenterFilledCornersBackground_Succeeds()
    {
        PixelReadbackResult frame = BuildFrame(256, 256, extraRowPitchPadding: 0, DXGI_FORMAT_R8G8B8A8_UNORM,
            (x, y) => Math.Abs(x - 128) < 40 && Math.Abs(y - 128) < 40);

        TriangleVerificationResult result = TrianglePixelVerifier.Verify(frame, Background);

        Assert.True(result.Success, result.Details);
        Assert.Equal(Foreground, result.CenterColor);
    }

    [Fact]
    public void Verify_EntirelyBackground_FailsBecauseCenterDoesNotDiffer()
    {
        PixelReadbackResult frame = BuildFrame(256, 256, extraRowPitchPadding: 0, DXGI_FORMAT_R8G8B8A8_UNORM, (_, _) => false);

        TriangleVerificationResult result = TrianglePixelVerifier.Verify(frame, Background);

        Assert.False(result.Success);
        Assert.Contains("nothing appears to have been drawn", result.Details);
    }

    [Fact]
    public void Verify_ForegroundBleedsIntoCorners_Fails()
    {
        PixelReadbackResult frame = BuildFrame(256, 256, extraRowPitchPadding: 0, DXGI_FORMAT_R8G8B8A8_UNORM, (_, _) => true);

        TriangleVerificationResult result = TrianglePixelVerifier.Verify(frame, Background);

        Assert.False(result.Success);
        Assert.Contains("corner", result.Details);
    }

    [Fact]
    public void Verify_HandlesRowPitchPaddingLargerThanTightWidth()
    {
        PixelReadbackResult frame = BuildFrame(17, 13, extraRowPitchPadding: 231, DXGI_FORMAT_R8G8B8A8_UNORM,
            (x, y) => Math.Abs(x - 8) < 3 && Math.Abs(y - 6) < 3);

        TriangleVerificationResult result = TrianglePixelVerifier.Verify(frame, Background);

        Assert.True(result.Success, result.Details);
    }

    [Fact]
    public void Verify_BgraFormat_ReadsChannelsInCorrectOrder()
    {
        PixelReadbackResult frame = BuildFrame(64, 64, extraRowPitchPadding: 0, DXGI_FORMAT_B8G8R8A8_UNORM,
            (x, y) => Math.Abs(x - 32) < 10 && Math.Abs(y - 32) < 10);

        TriangleVerificationResult result = TrianglePixelVerifier.Verify(frame, Background);

        Assert.True(result.Success, result.Details);
        Assert.Equal(Foreground, result.CenterColor);
    }

    [Fact]
    public void Verify_ZeroSizedFrame_FailsCleanly()
    {
        PixelReadbackResult frame = new()
        {
            Width = 0,
            Height = 0,
            RowPitch = 0,
            Format = DXGI_FORMAT_R8G8B8A8_UNORM,
            Pixels = [],
        };

        TriangleVerificationResult result = TrianglePixelVerifier.Verify(frame, Background);

        Assert.False(result.Success);
    }
}
