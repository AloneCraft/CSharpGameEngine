namespace CSharpGameEngine.Graphics.D3D12;

public sealed class TriangleRunOptions
{
    public int? Frames { get; init; }

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 720;

    public bool Hidden { get; init; }

    public bool UseWarp { get; init; }

    public bool EnableDebugLayer { get; init; }

    public bool EnableGpuValidation { get; init; }

    public bool Verify { get; init; }
}

public sealed record TriangleOptionsParseResult(bool Success, TriangleRunOptions? Options, string? Error)
{
    public static TriangleOptionsParseResult Ok(TriangleRunOptions options) => new(true, options, null);

    public static TriangleOptionsParseResult Fail(string error) => new(false, null, error);
}

public static class TriangleOptionsParser
{
    public static TriangleOptionsParseResult Parse(IReadOnlyList<string> Args)
    {
        ArgumentNullException.ThrowIfNull(Args);

        int? Frames = null;
        int Width = 1280;
        int Height = 720;
        bool Hidden = false;
        bool UseWarp = false;
        bool EnableDebugLayer = false;
        bool EnableGpuValidation = false;
        bool Verify = false;

        for (int i = 0; i < Args.Count; i++)
        {
            string Arg = Args[i];
            switch (Arg)
            {
                case "--frames":
                {
                    if (!TryReadIntValue(Args, ref i, out int Value))
                    {
                        return TriangleOptionsParseResult.Fail("--frames requires an integer value.");
                    }
                    if (Value <= 0)
                    {
                        return TriangleOptionsParseResult.Fail($"--frames must be a positive integer (got {Value}).");
                    }
                    Frames = Value;
                    break;
                }

                case "--width":
                {
                    if (!TryReadIntValue(Args, ref i, out int value))
                    {
                        return TriangleOptionsParseResult.Fail("--width requires an integer value.");
                    }
                    if (value <= 0)
                    {
                        return TriangleOptionsParseResult.Fail($"--width must be a positive integer (got {value}).");
                    }
                    Width = value;
                    break;
                }

                case "--height":
                {
                    if (!TryReadIntValue(Args, ref i, out int value))
                    {
                        return TriangleOptionsParseResult.Fail("--height requires an integer value.");
                    }
                    if (value <= 0)
                    {
                        return TriangleOptionsParseResult.Fail($"--height must be a positive integer (got {value}).");
                    }
                    Height = value;
                    break;
                }

                case "--hidden":
                    Hidden = true;
                    break;

                case "--warp":
                    UseWarp = true;
                    break;

                case "--debug-layer":
                    EnableDebugLayer = true;
                    break;

                case "--gpu-validation":
                    EnableGpuValidation = true;
                    break;

                case "--verify":
                    Verify = true;
                    break;

                default:
                    return TriangleOptionsParseResult.Fail($"Unknown option: {Arg}");
            }
        }

        TriangleRunOptions Options = new()
        {
            Frames = Frames,
            Width = Width,
            Height = Height,
            Hidden = Hidden,
            UseWarp = UseWarp,
            EnableDebugLayer = EnableDebugLayer,
            EnableGpuValidation = EnableGpuValidation,
            Verify = Verify,
        };

        return TriangleOptionsParseResult.Ok(Options);
    }

    private static bool TryReadIntValue(IReadOnlyList<string> Args, ref int index, out int Value)
    {
        if (index + 1 >= Args.Count || !int.TryParse(Args[index + 1], out Value))
        {
            Value = 0;
            return false;
        }

        index++;
        return true;
    }
}
