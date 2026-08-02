using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;

namespace CSharpGameEngine.Graphics.D3D12;

public sealed record ModelVerificationResult(bool Success, PixelColor CenterColor, double ForegroundRatio, string Details);

/// <summary>
/// Pure, GPU-free pixel checks for the model viewer vertical slice. Unlike
/// <see cref="TrianglePixelVerifier"/> (which knows a small triangle sits dead-center with
/// background at every edge), an arbitrary auto-framed mesh's silhouette isn't a fixed shape, so
/// this scans the whole frame and checks the fraction of non-background pixels falls in a
/// plausible band - proving real geometry was rasterized without covering the entire frame.
/// </summary>
public static class ModelPixelVerifier
{
    public const byte DefaultTolerance = 24;
    public const double DefaultMinForegroundRatio = 0.05;
    public const double DefaultMaxForegroundRatio = 0.98;

    public static ModelVerificationResult Verify(
        PixelReadbackResult readback,
        PixelColor backgroundColor,
        byte tolerance = DefaultTolerance,
        double minForegroundRatio = DefaultMinForegroundRatio,
        double maxForegroundRatio = DefaultMaxForegroundRatio)
    {
        ArgumentNullException.ThrowIfNull(readback);

        if (readback.Width <= 0 || readback.Height <= 0)
        {
            return new ModelVerificationResult(false, default, 0.0, "Readback has no pixels (width or height is zero).");
        }

        PixelColor center = ReadPixel(readback, readback.Width / 2, readback.Height / 2);

        long foregroundCount = 0;
        long totalCount = (long)readback.Width * readback.Height;

        for (int y = 0; y < readback.Height; y++)
        {
            for (int x = 0; x < readback.Width; x++)
            {
                if (!IsClose(ReadPixel(readback, x, y), backgroundColor, tolerance))
                {
                    foregroundCount++;
                }
            }
        }

        double foregroundRatio = (double)foregroundCount / totalCount;
        bool centerDiffersFromBackground = !IsClose(center, backgroundColor, tolerance);
        bool ratioInBand = foregroundRatio >= minForegroundRatio && foregroundRatio <= maxForegroundRatio;

        bool success = centerDiffersFromBackground && ratioInBand;

        string details = success
            ? $"center={center} differs from background={backgroundColor}; foreground ratio={foregroundRatio:P2} is within [{minForegroundRatio:P0},{maxForegroundRatio:P0}]."
            : BuildFailureDetails(centerDiffersFromBackground, ratioInBand, center, backgroundColor, foregroundRatio, minForegroundRatio, maxForegroundRatio);

        return new ModelVerificationResult(success, center, foregroundRatio, details);
    }

    private static string BuildFailureDetails(bool centerDiffersFromBackground, bool ratioInBand, PixelColor center, PixelColor backgroundColor, double foregroundRatio, double minForegroundRatio, double maxForegroundRatio)
    {
        List<string> parts = [];
        if (!centerDiffersFromBackground)
        {
            parts.Add($"center={center} is too close to background={backgroundColor}; nothing appears to have been drawn there.");
        }
        if (!ratioInBand)
        {
            parts.Add($"foreground ratio={foregroundRatio:P2} is outside the plausible band [{minForegroundRatio:P0},{maxForegroundRatio:P0}].");
        }
        return string.Join(" ", parts);
    }

    private static bool IsClose(PixelColor a, PixelColor b, byte tolerance) =>
        Math.Abs(a.R - b.R) <= tolerance &&
        Math.Abs(a.G - b.G) <= tolerance &&
        Math.Abs(a.B - b.B) <= tolerance;

    private static PixelColor ReadPixel(PixelReadbackResult readback, int x, int y)
    {
        const int bytesPerPixel = 4;
        long offset = ((long)y * readback.RowPitch) + (x * bytesPerPixel);

        byte b0 = readback.Pixels[offset];
        byte b1 = readback.Pixels[offset + 1];
        byte b2 = readback.Pixels[offset + 2];
        byte b3 = readback.Pixels[offset + 3];

        return readback.Format switch
        {
            DXGI_FORMAT_B8G8R8A8_UNORM => new PixelColor(b2, b1, b0, b3),
            _ => new PixelColor(b0, b1, b2, b3),
        };
    }
}
