using System.Numerics;
using CSharpGameEngine.Compiler.Hlsl;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using FbxImporter=CSharpGameEngine.Import.Fbx.FbxImporter;
using GltfImporter=CSharpGameEngine.Import.Gltf.GltfImporter;

const string ProductDisplayName="C# Game Engine";
const string InternalName="CSharpGameEngine";

if(args.Length==0||IsHelp(args[0])){
    PrintHelp();
    return;
}

Environment.ExitCode=RunViewer(args);

static bool IsHelp(string Arg)=>
    string.Equals(Arg,"--help",StringComparison.OrdinalIgnoreCase)||
    string.Equals(Arg,"-h",StringComparison.OrdinalIgnoreCase)||
    string.Equals(Arg,"help",StringComparison.OrdinalIgnoreCase);

static int RunViewer(string[]Args){
    ViewerOptionsParseResult ParseResult=ViewerOptionsParser.Parse(Args);
    if(!ParseResult.Success){
        Console.WriteLine($"FAILED: {ParseResult.Error}");
        return 2;
    }

    ViewerRunOptions Options=ParseResult.Options!;
    var ModelPath=ResolveModelPath(Options.ModelPath);

    Console.WriteLine($"Product: {ProductDisplayName} ({InternalName}) Viewer");
    Console.WriteLine($"Model: {ModelPath}");

    IReadOnlyList<Vector3>Positions;
    IReadOnlyList<Vector3>Normals;
    IReadOnlyList<Vector2>Uvs;
    IReadOnlyList<uint>Indices;
    byte[]?BaseColorTextureBytes;
    Vector4 BaseColorFactor;
    Vector3 BoundsCenter;
    float BoundsRadius;

    var Extension=Path.GetExtension(ModelPath);
    if(string.Equals(Extension,".fbx",StringComparison.OrdinalIgnoreCase)){
        CSharpGameEngine.Import.Fbx.FbxImportResult FbxResult=FbxImporter.Import(ModelPath);
        foreach(CSharpGameEngine.Import.Fbx.FbxImportDiagnostic Diagnostic in FbxResult.Diagnostics){
            Console.WriteLine($"  {Diagnostic}");
        }

        if(!FbxResult.Success){
            Console.WriteLine("FAILED: could not import model.");
            return 1;
        }

        CSharpGameEngine.Import.Fbx.ImportedMesh FbxMesh=FbxResult.Mesh!;
        Positions=FbxMesh.Positions;
        Normals=FbxMesh.Normals;
        Uvs=FbxMesh.Uvs;
        Indices=FbxMesh.Indices;
        BaseColorTextureBytes=FbxMesh.BaseColorTextureBytes;
        BaseColorFactor=FbxMesh.BaseColorFactor;
        BoundsCenter=FbxMesh.Bounds.Center;
        BoundsRadius=FbxMesh.Bounds.BoundingSphereRadius;
    } else {
        CSharpGameEngine.Import.Gltf.GltfImportResult GltfResult=GltfImporter.Import(ModelPath);
        foreach(CSharpGameEngine.Import.Gltf.GltfImportDiagnostic diagnostic in GltfResult.Diagnostics){
            Console.WriteLine($"  {diagnostic}");
        }

        if(!GltfResult.Success){
            Console.WriteLine("FAILED: could not import model.");
            return 1;
        }

        CSharpGameEngine.Import.Gltf.ImportedMesh GltfMesh=GltfResult.Mesh!;
        Positions=GltfMesh.Positions;
        Normals=GltfMesh.Normals;
        Uvs=GltfMesh.Uvs;
        Indices=GltfMesh.Indices;
        BaseColorTextureBytes=GltfMesh.BaseColorTextureBytes;
        BaseColorFactor=GltfMesh.BaseColorFactor;
        BoundsCenter=GltfMesh.Bounds.Center;
        BoundsRadius=GltfMesh.Bounds.BoundingSphereRadius;
    }

    Console.WriteLine($"Mesh: {Positions.Count} vertices, {Indices.Count/3} triangles, bounds radius={BoundsRadius:F3}");

    var ShaderPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Model.hlsl");
    Console.WriteLine($"Mode: {(Options.Hidden?"hidden":"windowed")}, frames={(Options.Frames is int F?F.ToString():"until closed")}, verify={Options.Verify}");

    using Win32Window Window=new(new Win32WindowOptions {
        Title= $"{ProductDisplayName} - Viewer",
        Width=Options.Width,
        Height=Options.Height,
        Visible=!Options.Hidden,
    });

    GraphicsDeviceOptions DeviceOptions=new(){
        UseWarp=Options.UseWarp,
        EnableDebugLayer=Options.EnableDebugLayer,
        EnableGpuBasedValidation=Options.EnableGpuValidation,
    };

    ModelRenderer Renderer;
    try {
        Renderer=ModelRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            Positions,Normals,Uvs,Indices,BaseColorTextureBytes,BaseColorFactor,
            BoundsCenter,BoundsRadius);
    } catch(Exception Ex){
        Console.WriteLine($"FAILED: could not initialize the D3D12 renderer: {Ex.Message}");
        return 1;
    }

    Window.Resized+=(W,H)=>Renderer.Resize(W,H);

    Console.WriteLine($"Adapter: {Renderer.AdapterDescription} ({(Renderer.IsWarp?"WARP":"Hardware")})");
    Console.WriteLine($"Feature level: {GraphicsDevice.RequiredFeatureLevel}");
    Console.WriteLine($"Back buffer format: {ModelRenderer.BackBufferFormat}, depth format: {ModelRenderer.DepthBufferFormat}, {Options.Width}x{Options.Height}");
    Console.WriteLine($"Debug layer: {(Renderer.DebugLayerEnabled?"enabled":"disabled")}{(Renderer.GpuBasedValidationEnabled?" (+GPU-based validation)":string.Empty)}");
    Console.WriteLine($"Shader compilation: {(Renderer.ShaderCompilationSucceeded?"OK":"FAILED")} ({Renderer.ShaderDiagnostics.Count} diagnostic(s))");
    foreach(HlslDiagnostic diagnostic in Renderer.ShaderDiagnostics){
        Console.WriteLine($"  {diagnostic}");
    }

    Console.WriteLine($"Base color texture: {(Renderer.TextureLoaded?"loaded":"fallback (white 1x1)")}{(Renderer.TextureLoadError is string Error? $" - {Error}" :string.Empty)}");

    var ExitCode=0;
    try {
        if(Options.Frames is int FrameCount){
            for(var i=0;i<FrameCount&&!Window.IsClosed;i++){
                Window.PumpEvents();
                if(Window.IsClosed||Window.IsMinimized){
                    continue;
                }

                var IsLastFrame=i==FrameCount-1;
                Renderer.RenderFrame(CaptureForVerification:Options.Verify&&IsLastFrame);
            }
        } else {
            while(!Window.IsClosed){
                Window.PumpEvents();
                if(!Window.IsClosed&&!Window.IsMinimized){
                    Renderer.RenderFrame();
                }
            }
        }

        Console.WriteLine($"Frames rendered: {Renderer.FramesRendered}");

        if(Options.Verify){
            if(Renderer.LastCapturedFrame is null){
                Console.WriteLine("Verification: FAILED (no frame was captured for readback).");
                ExitCode=1;
            } else {
                ModelVerificationResult Verification=ModelPixelVerifier.Verify(Renderer.LastCapturedFrame,ModelRenderer.ClearColor);
                Console.WriteLine($"Verification: {(Verification.Success?"PASSED":"FAILED")} - {Verification.Details}");
                if(!Verification.Success){
                    ExitCode=1;
                }
            }
        }

        var ErrorMessageCount=Renderer.ErrorOrWorseMessageCount;
        Console.WriteLine($"D3D12 validation errors: {ErrorMessageCount}");
        if(ErrorMessageCount>0){
            foreach(D3D12DebugMessage Message in Renderer.DebugMessages){
                if(Message.IsErrorOrWorse){
                    Console.WriteLine($"  {Message}");
                }
            }

            ExitCode=1;
        }
    } finally {
        Renderer.Dispose();
    }

    Console.WriteLine(ExitCode==0
        ? $"SUCCESS: {InternalName} viewer finished."
        : $"FAILED: {InternalName} viewer finished with errors.");

    return ExitCode;
}

static string ResolveModelPath(string ModelPath){
    // Resolve relative to the current directory first (matches `--model` pointing at any real
    // file, e.g. under UserGameProject/Assets/Fab/); fall back to the executable's own directory
    // so the bundled Samples/Box.glb works whether invoked via `dotnet run` from the repo root or
    // by running the built .exe directly from its output directory.
    if(File.Exists(ModelPath)||Path.IsPathRooted(ModelPath)){
        return ModelPath;
    }

    var BesideExecutable=Path.Combine(AppContext.BaseDirectory,ModelPath);
    return File.Exists(BesideExecutable)?BesideExecutable:ModelPath;
}

static void PrintHelp(){
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
