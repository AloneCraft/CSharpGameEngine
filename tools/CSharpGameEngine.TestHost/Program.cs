using CSharpGameEngine.Compiler.Hlsl;
using CSharpGameEngine.Core;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;

const string ProductDisplayName = "C# Game Engine";
const string InternalName = "CSharpGameEngine";

if (args.Length == 0 || IsHelp(args[0]))
{
    PrintHelp();
    return;
}

int exitCode = args[0] switch
{
    "self-test" => RunSelfTest(),
    "triangle" => RunTriangle(args[1..]),
    _ => RunUnknownCommand(args[0]),
};

Environment.ExitCode = exitCode;

static bool IsHelp(string command) =>
    string.Equals(command, "--help", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(command, "-h", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(command, "help", StringComparison.OrdinalIgnoreCase);

static int RunSelfTest()
{
    Console.WriteLine($"[{ProductDisplayName}] self-test started.");

    try
    {
        StableId stableId = StableId.New();
        if (!stableId.IsValid)
        {
            Console.WriteLine("FAILED: StableId generation returned an invalid ID.");
            return 1;
        }

        DiagnosticBag bag = new();
        bag.Add(new Diagnostic("TH0001", DiagnosticSeverity.Info, "Self-test diagnostic emitted.", SourceLocation.Unknown));

        Console.WriteLine($"StableId: {stableId}");
        foreach (Diagnostic diagnostic in bag)
        {
            Console.WriteLine(diagnostic.ToString());
        }

        Console.WriteLine($"SUCCESS: {InternalName} core self-test completed.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED: self-test crashed: {ex.Message}");
        return 1;
    }
}

static int RunTriangle(string[] triangleArgs)
{
    TriangleOptionsParseResult parseResult = TriangleOptionsParser.Parse(triangleArgs);
    if (!parseResult.Success)
    {
        Console.WriteLine($"FAILED: {parseResult.Error}");
        return 2;
    }

    TriangleRunOptions options = parseResult.Options!;
    string shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Triangle.hlsl");

    Console.WriteLine($"Product: {ProductDisplayName} ({InternalName})");
    Console.WriteLine($"Mode: {(options.Hidden ? "hidden" : "windowed")}, frames={(options.Frames is int f ? f.ToString() : "until closed")}, verify={options.Verify}");

    using Win32Window window = new(new Win32WindowOptions
    {
        Title = $"{ProductDisplayName} - Triangle",
        Width = options.Width,
        Height = options.Height,
        Visible = !options.Hidden,
    });

    GraphicsDeviceOptions deviceOptions = new()
    {
        UseWarp = options.UseWarp,
        EnableDebugLayer = options.EnableDebugLayer,
        EnableGpuBasedValidation = options.EnableGpuValidation,
    };

    TriangleRenderer renderer;
    try
    {
        renderer = TriangleRenderer.Create(window.Handle, window.ClientWidth, window.ClientHeight, deviceOptions, shaderPath);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED: could not initialize the D3D12 renderer: {ex.Message}");
        return 1;
    }

    window.Resized += (w, h) => renderer.Resize(w, h);

    Console.WriteLine($"Adapter: {renderer.AdapterDescription} ({(renderer.IsWarp ? "WARP" : "Hardware")})");
    Console.WriteLine($"Feature level: {GraphicsDevice.RequiredFeatureLevel}");
    Console.WriteLine($"Back buffer format: {TriangleRenderer.BackBufferFormat}, {options.Width}x{options.Height}");
    Console.WriteLine($"Debug layer: {(renderer.DebugLayerEnabled ? "enabled" : "disabled")}{(renderer.GpuBasedValidationEnabled ? " (+GPU-based validation)" : string.Empty)}");
    Console.WriteLine($"Shader compilation: {(renderer.ShaderCompilationSucceeded ? "OK" : "FAILED")} ({renderer.ShaderDiagnostics.Count} diagnostic(s))");
    foreach (HlslDiagnostic diagnostic in renderer.ShaderDiagnostics)
    {
        Console.WriteLine($"  {diagnostic}");
    }

    int exitCode = 0;
    try
    {
        if (options.Frames is int frameCount)
        {
            for (int i = 0; i < frameCount && !window.IsClosed; i++)
            {
                window.PumpEvents();
                if (window.IsClosed || window.IsMinimized)
                {
                    continue;
                }

                bool isLastFrame = i == frameCount - 1;
                renderer.RenderFrame(captureForVerification: options.Verify && isLastFrame);
            }
        }
        else
        {
            while (!window.IsClosed)
            {
                window.PumpEvents();
                if (!window.IsClosed && !window.IsMinimized)
                {
                    renderer.RenderFrame();
                }
            }
        }

        Console.WriteLine($"Frames rendered: {renderer.FramesRendered}");

        if (options.Verify)
        {
            if (renderer.LastCapturedFrame is null)
            {
                Console.WriteLine("Verification: FAILED (no frame was captured for readback).");
                exitCode = 1;
            }
            else
            {
                TriangleVerificationResult verification = TrianglePixelVerifier.Verify(renderer.LastCapturedFrame, TriangleRenderer.ClearColor);
                Console.WriteLine($"Verification: {(verification.Success ? "PASSED" : "FAILED")} - {verification.Details}");
                if (!verification.Success)
                {
                    exitCode = 1;
                }
            }
        }

        int errorMessageCount = renderer.ErrorOrWorseMessageCount;
        Console.WriteLine($"D3D12 validation errors: {errorMessageCount}");
        if (errorMessageCount > 0)
        {
            foreach (D3D12DebugMessage message in renderer.DebugMessages)
            {
                if (message.IsErrorOrWorse)
                {
                    Console.WriteLine($"  {message}");
                }
            }

            exitCode = 1;
        }
    }
    finally
    {
        renderer.Dispose();
    }

    Console.WriteLine(exitCode == 0
        ? $"SUCCESS: {InternalName} triangle finished."
        : $"FAILED: {InternalName} triangle finished with errors.");

    return exitCode;
}

static int RunUnknownCommand(string command)
{
    Console.WriteLine($"Unknown command: {command}");
    PrintHelp();
    return 2;
}

static void PrintHelp()
{
    Console.WriteLine($"{ProductDisplayName} ({InternalName}) TestHost");
    Console.WriteLine("Usage:");
    Console.WriteLine("  --help                Show this help.");
    Console.WriteLine("  self-test             Run minimal core self-test.");
    Console.WriteLine("  triangle [options]    Run the Windows + Direct3D 12 triangle vertical slice.");
    Console.WriteLine();
    Console.WriteLine("triangle options:");
    Console.WriteLine("  --frames <count>      Render exactly <count> frames, then exit.");
    Console.WriteLine("  --width <pixels>      Client width (default 1280).");
    Console.WriteLine("  --height <pixels>     Client height (default 720).");
    Console.WriteLine("  --hidden              Do not show the window (for automated verification).");
    Console.WriteLine("  --warp                Use the WARP software adapter.");
    Console.WriteLine("  --debug-layer         Enable the D3D12 debug layer.");
    Console.WriteLine("  --gpu-validation      Enable GPU-based validation (requires --debug-layer).");
    Console.WriteLine("  --verify              Read back the rendered frame and verify the triangle was drawn.");
}
