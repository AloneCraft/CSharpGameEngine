using CSharpGameEngine.Compiler.Hlsl;
using CSharpGameEngine.Core;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;

const string ProductDisplayName="C# Game Engine";
const string InternalName="CSharpGameEngine";

if(args.Length==0||IsHelp(args[0])){
    PrintHelp();
    return;
}

var ExitCode=args[0]switch {
    "self-test"=>RunSelfTest(),
    "triangle"=>RunTriangle(args[1..]),
    _ =>RunUnknownCommand(args[0]),
};

Environment.ExitCode=ExitCode;

static bool IsHelp(string Command)=>
    string.Equals(Command,"--help",StringComparison.OrdinalIgnoreCase)||
    string.Equals(Command,"-h",StringComparison.OrdinalIgnoreCase)||
    string.Equals(Command,"help",StringComparison.OrdinalIgnoreCase);

static int RunSelfTest(){
    Console.WriteLine($"[{ProductDisplayName}] self-test started.");

    try {
        StableId StableId=StableId.New();
        if(!StableId.IsValid){
            Console.WriteLine("FAILED: StableId generation returned an invalid ID.");
            return 1;
        }

        DiagnosticBag Bag=new();
        Bag.Add(new Diagnostic("TH0001",DiagnosticSeverity.Info,"Self-test diagnostic emitted.",SourceLocation.Unknown));

        Console.WriteLine($"StableId: {StableId}");
        foreach(Diagnostic Diagnostic in Bag){
            Console.WriteLine(Diagnostic.ToString());
        }

        Console.WriteLine($"SUCCESS: {InternalName} core self-test completed.");
        return 0;
    } catch(Exception Ex){
        Console.WriteLine($"FAILED: self-test crashed: {Ex.Message}");
        return 1;
    }
}

static int RunTriangle(string[]TriangleArgs){
    TriangleOptionsParseResult ParseResult=TriangleOptionsParser.Parse(TriangleArgs);
    if(!ParseResult.Success){
        Console.WriteLine($"FAILED: {ParseResult.Error}");
        return 2;
    }

    TriangleRunOptions Options=ParseResult.Options!;
    var ShaderPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Triangle.hlsl");

    Console.WriteLine($"Product: {ProductDisplayName} ({InternalName})");
    Console.WriteLine($"Mode: {(Options.Hidden?"hidden":"windowed")}, frames={(Options.Frames is int F?F.ToString():"until closed")}, verify={Options.Verify}");

    using Win32Window Window=new(new Win32WindowOptions {
        Title= $"{ProductDisplayName} - Triangle",
        Width=Options.Width,
        Height=Options.Height,
        Visible=!Options.Hidden,
    });

    GraphicsDeviceOptions DeviceOptions=new(){
        UseWarp=Options.UseWarp,
        EnableDebugLayer=Options.EnableDebugLayer,
        EnableGpuBasedValidation=Options.EnableGpuValidation,
    };

    TriangleRenderer Renderer;
    try {
        Renderer=TriangleRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath);
    } catch(Exception Ex){
        Console.WriteLine($"FAILED: could not initialize the D3D12 renderer: {Ex.Message}");
        return 1;
    }

    Window.Resized+=(W,H)=>Renderer.Resize(W,H);

    Console.WriteLine($"Adapter: {Renderer.AdapterDescription} ({(Renderer.IsWarp?"WARP":"Hardware")})");
    Console.WriteLine($"Feature level: {GraphicsDevice.RequiredFeatureLevel}");
    Console.WriteLine($"Back buffer format: {TriangleRenderer.BackBufferFormat}, {Options.Width}x{Options.Height}");
    Console.WriteLine($"Debug layer: {(Renderer.DebugLayerEnabled?"enabled":"disabled")}{(Renderer.GpuBasedValidationEnabled?" (+GPU-based validation)":string.Empty)}");
    Console.WriteLine($"Shader compilation: {(Renderer.ShaderCompilationSucceeded?"OK":"FAILED")} ({Renderer.ShaderDiagnostics.Count} diagnostic(s))");
    foreach(HlslDiagnostic Diagnostic in Renderer.ShaderDiagnostics){
        Console.WriteLine($"  {Diagnostic}");
    }

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
                TriangleVerificationResult Verification=TrianglePixelVerifier.Verify(Renderer.LastCapturedFrame,TriangleRenderer.ClearColor);
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
        ? $"SUCCESS: {InternalName} triangle finished."
        : $"FAILED: {InternalName} triangle finished with errors.");

    return ExitCode;
}

static int RunUnknownCommand(string Command){
    Console.WriteLine($"Unknown command: {Command}");
    PrintHelp();
    return 2;
}

static void PrintHelp(){
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
