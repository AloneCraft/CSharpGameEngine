using System.Numerics;
using CSharpGameEngine.Compiler.Hlsl;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using GltfImporter = CSharpGameEngine.Import.Gltf.GltfImporter;
using FbxImporter = CSharpGameEngine.Import.Fbx.FbxImporter;

const string ProductDisplayName = "C# Game Engine";
const string InternalName = "CSharpGameEngine";

if (args.Length == 0 || IsHelp(args[0]))
{
    PrintHelp();
    return;
}

Environment.ExitCode = RunViewer(args);

static bool IsHelp(string arg) =>
    string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase);

static int RunViewer(string[] args)
{
    ViewerOptionsParseResult parseResult = ViewerOptionsParser.Parse(args);
    if (!parseResult.Success)
    {
        Console.WriteLine($"FAILED: {parseResult.Error}");
        return 2;
    }

    ViewerRunOptions options = parseResult.Options!;
    string modelPath = ResolveModelPath(options.ModelPath);

    Console.WriteLine($"Product: {ProductDisplayName} ({InternalName}) Viewer");
    Console.WriteLine($"Model: {modelPath}");

    IReadOnlyList<Vector3> positions;
    IReadOnlyList<Vector3> normals;
    IReadOnlyList<Vector2> uvs;
    IReadOnlyList<uint> indices;
    byte[]? baseColorTextureBytes;
    Vector4 baseColorFactor;
    Vector3 boundsCenter;
    float boundsRadius;

    string extension = Path.GetExtension(modelPath);
    if (string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase))
    {
        CSharpGameEngine.Import.Fbx.FbxImportResult fbxResult = FbxImporter.Import(modelPath);
        foreach (CSharpGameEngine.Import.Fbx.FbxImportDiagnostic diagnostic in fbxResult.Diagnostics)
        {
            Console.WriteLine($"  {diagnostic}");
        }

        if (!fbxResult.Success)
        {
            Console.WriteLine("FAILED: could not import model.");
            return 1;
        }

        CSharpGameEngine.Import.Fbx.ImportedMesh fbxMesh = fbxResult.Mesh!;
        positions = fbxMesh.Positions;
        normals = fbxMesh.Normals;
        uvs = fbxMesh.Uvs;
        indices = fbxMesh.Indices;
        baseColorTextureBytes = fbxMesh.BaseColorTextureBytes;
        baseColorFactor = fbxMesh.BaseColorFactor;
        boundsCenter = fbxMesh.Bounds.Center;
        boundsRadius = fbxMesh.Bounds.BoundingSphereRadius;
    }
    else
    {
        CSharpGameEngine.Import.Gltf.GltfImportResult gltfResult = GltfImporter.Import(modelPath);
        foreach (CSharpGameEngine.Import.Gltf.GltfImportDiagnostic diagnostic in gltfResult.Diagnostics)
        {
            Console.WriteLine($"  {diagnostic}");
        }

        if (!gltfResult.Success)
        {
            Console.WriteLine("FAILED: could not import model.");
            return 1;
        }

        CSharpGameEngine.Import.Gltf.ImportedMesh gltfMesh = gltfResult.Mesh!;
        positions = gltfMesh.Positions;
        normals = gltfMesh.Normals;
        uvs = gltfMesh.Uvs;
        indices = gltfMesh.Indices;
        baseColorTextureBytes = gltfMesh.BaseColorTextureBytes;
        baseColorFactor = gltfMesh.BaseColorFactor;
        boundsCenter = gltfMesh.Bounds.Center;
        boundsRadius = gltfMesh.Bounds.BoundingSphereRadius;
    }

    Console.WriteLine($"Mesh: {positions.Count} vertices, {indices.Count / 3} triangles, bounds radius={boundsRadius:F3}");

    string shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Model.hlsl");
    Console.WriteLine($"Mode: {(options.Hidden ? "hidden" : "windowed")}, frames={(options.Frames is int f ? f.ToString() : "until closed")}, verify={options.Verify}");

    using Win32Window window = new(new Win32WindowOptions
    {
        Title = $"{ProductDisplayName} - Viewer",
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

    ModelRenderer renderer;
    try
    {
        renderer = ModelRenderer.Create(
            window.Handle, window.ClientWidth, window.ClientHeight, deviceOptions, shaderPath,
            positions, normals, uvs, indices, baseColorTextureBytes, baseColorFactor,
            boundsCenter, boundsRadius);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED: could not initialize the D3D12 renderer: {ex.Message}");
        return 1;
    }

    window.Resized += (w, h) => renderer.Resize(w, h);

    Console.WriteLine($"Adapter: {renderer.AdapterDescription} ({(renderer.IsWarp ? "WARP" : "Hardware")})");
    Console.WriteLine($"Feature level: {GraphicsDevice.RequiredFeatureLevel}");
    Console.WriteLine($"Back buffer format: {ModelRenderer.BackBufferFormat}, depth format: {ModelRenderer.DepthBufferFormat}, {options.Width}x{options.Height}");
    Console.WriteLine($"Debug layer: {(renderer.DebugLayerEnabled ? "enabled" : "disabled")}{(renderer.GpuBasedValidationEnabled ? " (+GPU-based validation)" : string.Empty)}");
    Console.WriteLine($"Shader compilation: {(renderer.ShaderCompilationSucceeded ? "OK" : "FAILED")} ({renderer.ShaderDiagnostics.Count} diagnostic(s))");
    foreach (HlslDiagnostic diagnostic in renderer.ShaderDiagnostics)
    {
        Console.WriteLine($"  {diagnostic}");
    }

    Console.WriteLine($"Base color texture: {(renderer.TextureLoaded ? "loaded" : "fallback (white 1x1)")}{(renderer.TextureLoadError is string error ? $" - {error}" : string.Empty)}");

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
                ModelVerificationResult verification = ModelPixelVerifier.Verify(renderer.LastCapturedFrame, ModelRenderer.ClearColor);
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
        ? $"SUCCESS: {InternalName} viewer finished."
        : $"FAILED: {InternalName} viewer finished with errors.");

    return exitCode;
}

static string ResolveModelPath(string modelPath)
{
    // Resolve relative to the current directory first (matches `--model` pointing at any real
    // file, e.g. under UserGameProject/Assets/Fab/); fall back to the executable's own directory
    // so the bundled Samples/Box.glb works whether invoked via `dotnet run` from the repo root or
    // by running the built .exe directly from its output directory.
    if (File.Exists(modelPath) || Path.IsPathRooted(modelPath))
    {
        return modelPath;
    }

    string besideExecutable = Path.Combine(AppContext.BaseDirectory, modelPath);
    return File.Exists(besideExecutable) ? besideExecutable : modelPath;
}

static void PrintHelp()
{
    Console.WriteLine($"{ProductDisplayName} ({InternalName}) Extension/Viewer");
    Console.WriteLine("Usage:");
    Console.WriteLine("  --help                Show this help.");
    Console.WriteLine("  --model <path>        (required) Path to a .gltf, .glb, or .fbx file to view.");
    Console.WriteLine("  --frames <count>      Render exactly <count> frames, then exit.");
    Console.WriteLine("  --width <pixels>      Client width (default 1280).");
    Console.WriteLine("  --height <pixels>     Client height (default 720).");
    Console.WriteLine("  --hidden              Do not show the window (for automated verification).");
    Console.WriteLine("  --warp                Use the WARP software adapter.");
    Console.WriteLine("  --debug-layer         Enable the D3D12 debug layer.");
    Console.WriteLine("  --gpu-validation      Enable GPU-based validation (requires --debug-layer).");
    Console.WriteLine("  --verify              Read back the rendered frame and verify the mesh was drawn.");
    Console.WriteLine();
    Console.WriteLine("Example:");
    Console.WriteLine("  dotnet run --project tools/CSharpGameEngine.Extension -- --model Samples/Box.glb");
}
