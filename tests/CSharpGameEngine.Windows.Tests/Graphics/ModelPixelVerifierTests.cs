using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

public class ModelPixelVerifierTests
{
    private static readonly PixelColor Background = new(5, 5, 20, 255);
    private static readonly PixelColor Foreground = new(200, 150, 100, 255);

    private static PixelReadbackResult BuildFrame(int Width, int Height, uint ExtraRowPitchPadding, DXGI_FORMAT Format, Func<int, int, bool> IsForeground)
    {
        int BytesPerPixel = 4;
        uint RowPitch = (uint)(Width * BytesPerPixel) + ExtraRowPitchPadding;
        byte[] Pixels = new byte[RowPitch * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                PixelColor Color = IsForeground(x, y) ? Foreground : Background;
                long Offset = ((long)y * RowPitch) + (x * BytesPerPixel);

                if (Format == DXGI_FORMAT_B8G8R8A8_UNORM)
                {
                    Pixels[Offset] = Color.B;
                    Pixels[Offset + 1] = Color.G;
                    Pixels[Offset + 2] = Color.R;
                    Pixels[Offset + 3] = Color.A;
                }
                else
                {
                    Pixels[Offset] = Color.R;
                    Pixels[Offset + 1] = Color.G;
                    Pixels[Offset + 2] = Color.B;
                    Pixels[Offset + 3] = Color.A;
                }
            }
        }

        return new PixelReadbackResult
        {
            Width = Width,
            Height = Height,
            RowPitch = RowPitch,
            Format = Format,
            Pixels = Pixels,
        };
    }

    [Fact]
    public void Verify_PlausibleCoverage_Succeeds()
    {
        // A centered disc covering roughly 20% of a 100x100 frame.
        PixelReadbackResult Frame = BuildFrame(100, 100, ExtraRowPitchPadding: 0, DXGI_FORMAT_R8G8B8A8_UNORM,
            (X, Y) => (X - 50) * (X - 50) + (Y - 50) * (Y - 50) < 32 * 32);

        ModelVerificationResult Result = ModelPixelVerifier.Verify(Frame, Background);

        Assert.True(Result.Success, Result.Details);
        Assert.Equal(Foreground, Result.CenterColor);
        Assert.InRange(Result.ForegroundRatio, 0.05, 0.98);
    }

    [Fact]
    public void Verify_EntirelyBackground_Fails()
    {
        PixelReadbackResult Frame = BuildFrame(64, 64, ExtraRowPitchPadding: 0, DXGI_FORMAT_R8G8B8A8_UNORM, (_, _) => false);

        ModelVerificationResult Result = ModelPixelVerifier.Verify(Frame, Background);

        Assert.False(Result.Success);
        Assert.Equal(0.0, Result.ForegroundRatio);
        Assert.Contains("nothing appears to have been drawn", Result.Details);
    }

    [Fact]
    public void Verify_EntirelyForeground_Fails()
    {
        PixelReadbackResult Frame = BuildFrame(64, 64, ExtraRowPitchPadding: 0, DXGI_FORMAT_R8G8B8A8_UNORM, (_, _) => true);

        ModelVerificationResult Result = ModelPixelVerifier.Verify(Frame, Background);

        Assert.False(Result.Success);
        Assert.Equal(1.0, Result.ForegroundRatio);
        Assert.Contains("plausible band", Result.Details);
    }

    [Fact]
    public void Verify_HandlesRowPitchPaddingLargerThanTightWidth()
    {
        PixelReadbackResult Frame = BuildFrame(17, 13, ExtraRowPitchPadding: 231, DXGI_FORMAT_R8G8B8A8_UNORM,
            (X, Y) => Math.Abs(X - 8) < 4 && Math.Abs(Y - 6) < 4);

        ModelVerificationResult Result = ModelPixelVerifier.Verify(Frame, Background);

        Assert.True(Result.Success, Result.Details);
    }

    [Fact]
    public void Verify_BgraFormat_ReadsChannelsInCorrectOrder()
    {
        PixelReadbackResult Frame = BuildFrame(64, 64, ExtraRowPitchPadding: 0, DXGI_FORMAT_B8G8R8A8_UNORM,
            (X, Y) => Math.Abs(X - 32) < 10 && Math.Abs(Y - 32) < 10);

        ModelVerificationResult Result = ModelPixelVerifier.Verify(Frame, Background);

        Assert.True(Result.Success, Result.Details);
        Assert.Equal(Foreground, Result.CenterColor);
    }

    [Fact]
    public void Verify_ZeroSizedFrame_FailsCleanly()
    {
        PixelReadbackResult Frame = new()
        {
            Width = 0,
            Height = 0,
            RowPitch = 0,
            Format = DXGI_FORMAT_R8G8B8A8_UNORM,
            Pixels = [],
        };

        ModelVerificationResult Result = ModelPixelVerifier.Verify(Frame, Background);

        Assert.False(Result.Success);
    }
}
