using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;
using Camera=CSharpGameEngine.Graphics.D3D12.Camera;
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
    var ParseResult=ViewerOptionsParser.Parse(Args);
    if(!ParseResult.Success){
        Console.WriteLine($"FAILED: {ParseResult.Error}");
        return 2;
    }

    var Options=ParseResult.Options!;
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
        var FbxResult=FbxImporter.Import(ModelPath);
        foreach(var Diagnostic in FbxResult.Diagnostics){
            Console.WriteLine($"  {Diagnostic}");
        }

        if(!FbxResult.Success){
            Console.WriteLine("FAILED: could not import model.");
            return 1;
        }

        var FbxMesh=FbxResult.Mesh!;
        Positions=FbxMesh.Positions;
        Normals=FbxMesh.Normals;
        Uvs=FbxMesh.Uvs;
        Indices=FbxMesh.Indices;
        BaseColorTextureBytes=FbxMesh.BaseColorTextureBytes;
        BaseColorFactor=FbxMesh.BaseColorFactor;
        BoundsCenter=FbxMesh.Bounds.Center;
        BoundsRadius=FbxMesh.Bounds.BoundingSphereRadius;
    } else {
        var GltfResult=GltfImporter.Import(ModelPath);
        foreach(var Diagnostic in GltfResult.Diagnostics){
            Console.WriteLine($"  {Diagnostic}");
        }

        if(!GltfResult.Success){
            Console.WriteLine("FAILED: could not import model.");
            return 1;
        }

        var GltfMesh=GltfResult.Mesh!;
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
    RenderTextureResource?BaseColorTexture;
    try {
        BaseColorTexture=RenderTextureArtifactAdapter.DecodeOptional(
            BaseColorTextureBytes??[]);
    } catch(InvalidDataException Exception){
        Console.WriteLine($"FAILED: could not decode the base-color texture: {Exception.Message}");
        return 1;
    }

    var ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
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

    RenderServer Renderer;
    try {
        StageMaterialCreateInfo Material=new(){
            BaseColorFactor=BaseColorFactor,
            RoughnessFactor=1f,
            MetallicFactor=0f,
            BaseColorTexture=BaseColorTexture,
        };
        (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,
            StageObjectCreateInfo[]Objects)=RenderServer.BuildSingleMeshRoster(
            Positions,Normals,Uvs,Indices,Material);
        Renderer=RenderServer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,
            ShaderArtifactPath,Meshes,Materials,Objects);
        Vector3 Extent=new(BoundsRadius);
        Renderer.SetCamera(Camera.CreateBoundsFit(
            BoundsCenter-Extent,BoundsCenter+Extent));
        Renderer.SetAmbientLight(new Vector3(0.35f));
        Renderer.SetDirectionalLight(
            new CSharpGameEngine.Rendering.DirectionalLightSubmission(Vector3.Normalize(new Vector3(1f,-1f,1f)),new CSharpGameEngine.Lighting.LinearLightColor(1f,1f,1f),new CSharpGameEngine.Lighting.Illuminance(50_000f),new CSharpGameEngine.Lighting.LightShadowSettings(0,new CSharpGameEngine.Lighting.ShadowBias(0.0015f))));
    } catch(Exception Ex){
        Console.WriteLine($"FAILED: could not initialize the D3D12 renderer: {Ex.Message}");
        return 1;
    }

    Window.Resized+=(W,H)=>Renderer.Resize(W,H);

    Console.WriteLine($"Adapter: {Renderer.AdapterDescription} ({(Renderer.IsWarp?"WARP":"Hardware")})");
    Console.WriteLine($"Feature level: {GraphicsDevice.RequiredFeatureLevel}");
    Console.WriteLine($"Back buffer format: R8G8B8A8_UNORM, depth format: D32_FLOAT, {Options.Width}x{Options.Height}");
    Console.WriteLine($"Debug layer: {(Renderer.DebugLayerEnabled?"enabled":"disabled")}{(Renderer.GpuBasedValidationEnabled?" (+GPU-based validation)":string.Empty)}");
    Console.WriteLine($"Shader compilation: {(Renderer.ShaderArtifactLoaded?"OK":"FAILED")} ({Renderer.ShaderArtifactDiagnostics.Count} diagnostic(s))");
    foreach(var Diagnostic in Renderer.ShaderArtifactDiagnostics){
        Console.WriteLine($"  {Diagnostic}");
    }

    Console.WriteLine($"Base color texture: {(BaseColorTexture is not null?"loaded":"fallback (white 1x1)")}");

    var ExitCode=0;
    try {
        if(Options.Frames is int FrameCount){
            for(var I=0;I<FrameCount&&!Window.IsClosed;I++){
                Window.PumpEvents();
                if(Window.IsClosed||Window.IsMinimized){
                    continue;
                }

                var IsLastFrame=I==FrameCount-1;
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
                var Verification=ModelPixelVerifier.Verify(Renderer.LastCapturedFrame,RenderServer.ClearColor);
                Console.WriteLine($"Verification: {(Verification.Success?"PASSED":"FAILED")} - {Verification.Details}");
                if(!Verification.Success){
                    ExitCode=1;
                }
            }
        }

        var ErrorMessageCount=Renderer.ErrorOrWorseMessageCount;
        Console.WriteLine($"D3D12 validation errors: {ErrorMessageCount}");
        if(ErrorMessageCount>0){
            foreach(var Message in Renderer.DebugMessages){
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
    // file, e.g. under UserEcsProject/Assets/Fab/); fall back to the executable's own directory
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
