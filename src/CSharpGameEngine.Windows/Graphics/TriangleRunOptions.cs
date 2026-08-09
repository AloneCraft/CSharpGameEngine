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
    public static TriangleOptionsParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        int? frames = null;
        int width = 1280;
        int height = 720;
        bool hidden = false;
        bool useWarp = false;
        bool enableDebugLayer = false;
        bool enableGpuValidation = false;
        bool verify = false;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--frames":
                {
                    if (!TryReadIntValue(args, ref i, out int value))
                    {
                        return TriangleOptionsParseResult.Fail("--frames requires an integer value.");
                    }
                    if (value <= 0)
                    {
                        return TriangleOptionsParseResult.Fail($"--frames must be a positive integer (got {value}).");
                    }
                    frames = value;
                    break;
                }

                case "--width":
                {
                    if (!TryReadIntValue(args, ref i, out int value))
                    {
                        return TriangleOptionsParseResult.Fail("--width requires an integer value.");
                    }
                    if (value <= 0)
                    {
                        return TriangleOptionsParseResult.Fail($"--width must be a positive integer (got {value}).");
                    }
                    width = value;
                    break;
                }

                case "--height":
                {
                    if (!TryReadIntValue(args, ref i, out int value))
                    {
                        return TriangleOptionsParseResult.Fail("--height requires an integer value.");
                    }
                    if (value <= 0)
                    {
                        return TriangleOptionsParseResult.Fail($"--height must be a positive integer (got {value}).");
                    }
                    height = value;
                    break;
                }

                case "--hidden":
                    hidden = true;
                    break;

                case "--warp":
                    useWarp = true;
                    break;

                case "--debug-layer":
                    enableDebugLayer = true;
                    break;

                case "--gpu-validation":
                    enableGpuValidation = true;
                    break;

                case "--verify":
                    verify = true;
                    break;

                default:
                    return TriangleOptionsParseResult.Fail($"Unknown option: {arg}");
            }
        }

        TriangleRunOptions options = new()
        {
            Frames = frames,
            Width = width,
            Height = height,
            Hidden = hidden,
            UseWarp = useWarp,
            EnableDebugLayer = enableDebugLayer,
            EnableGpuValidation = enableGpuValidation,
            Verify = verify,
        };

        return TriangleOptionsParseResult.Ok(options);
    }

    private static bool TryReadIntValue(IReadOnlyList<string> args, ref int index, out int value)
    {
        if (index + 1 >= args.Count || !int.TryParse(args[index + 1], out value))
        {
            value = 0;
            return false;
        }

        index++;
        return true;
    }
}
