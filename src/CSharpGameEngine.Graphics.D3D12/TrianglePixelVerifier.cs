using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;

namespace CSharpGameEngine.Graphics.D3D12;

public sealed record TriangleVerificationResult(bool Success, PixelColor CenterColor, IReadOnlyList<PixelColor> CornerColors, string Details);

/// <summary>
/// Pure, GPU-free pixel checks for the triangle vertical slice: the frame center must differ
/// from the background clear color (something was drawn) and the corners must not (nothing
/// bleeds outside the triangle). No GPU access required, so this is fully unit-testable.
/// </summary>
public static class TrianglePixelVerifier
{
    public const byte DefaultTolerance = 24;

    public static TriangleVerificationResult Verify(PixelReadbackResult readback, PixelColor backgroundColor, byte tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(readback);

        if (readback.Width <= 0 || readback.Height <= 0)
        {
            return new TriangleVerificationResult(false, default, [], "Readback has no pixels (width or height is zero).");
        }

        const double edgeInset = 0.05;
        int insetX = Math.Max(1, (int)(readback.Width * edgeInset));
        int insetY = Math.Max(1, (int)(readback.Height * edgeInset));

        PixelColor center = ReadPixel(readback, readback.Width / 2, readback.Height / 2);

        (int X, int Y)[] cornerPositions =
        [
            (insetX, insetY),
            (readback.Width - 1 - insetX, insetY),
            (insetX, readback.Height - 1 - insetY),
            (readback.Width - 1 - insetX, readback.Height - 1 - insetY),
        ];

        PixelColor[] corners = new PixelColor[cornerPositions.Length];
        for (int i = 0; i < cornerPositions.Length; i++)
        {
            corners[i] = ReadPixel(readback, cornerPositions[i].X, cornerPositions[i].Y);
        }

        bool centerDiffersFromBackground = !IsClose(center, backgroundColor, tolerance);

        List<string> cornerFailures = [];
        for (int i = 0; i < corners.Length; i++)
        {
            if (!IsClose(corners[i], backgroundColor, tolerance))
            {
                cornerFailures.Add($"corner[{i}] at {cornerPositions[i]} = {corners[i]} is not close to background {backgroundColor}");
            }
        }

        bool success = centerDiffersFromBackground && cornerFailures.Count == 0;

        string details = success
            ? $"center={center} differs from background={backgroundColor}; all corners match background within tolerance {tolerance}."
            : BuildFailureDetails(centerDiffersFromBackground, center, backgroundColor, cornerFailures);

        return new TriangleVerificationResult(success, center, corners, details);
    }

    private static string BuildFailureDetails(bool centerDiffersFromBackground, PixelColor center, PixelColor backgroundColor, List<string> cornerFailures)
    {
        List<string> parts = [];
        if (!centerDiffersFromBackground)
        {
            parts.Add($"center={center} is too close to background={backgroundColor}; nothing appears to have been drawn.");
        }
        parts.AddRange(cornerFailures);
        return string.Join(" ", parts);
    }

    private static bool IsClose(PixelColor a, PixelColor b, byte tolerance) =>
        Math.Abs(a.R - b.R) <= tolerance &&
        Math.Abs(a.G - b.G) <= tolerance &&
        Math.Abs(a.B - b.B) <= tolerance;

    private static PixelColor ReadPixel(PixelReadbackResult readback, int x, int y)
    {
        const int bytesPerPixel = 4;
        long offset = (long)y * readback.RowPitch + (x * bytesPerPixel);

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
