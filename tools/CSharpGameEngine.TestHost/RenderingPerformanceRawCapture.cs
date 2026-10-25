using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using CSharpGameEngine.Core;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;

internal static class RenderingPerformanceRawCapture {
    private const int WarmupCount=5;
    private const int SampleCount=30;
    private const int Width=1280;
    private const int Height=720;
    private const int VisibilityCount=100_000;
    private const string Schema="csharp-game-engine-rendering-raw-performance-v2";
    private static readonly StableId NamespaceId=StableId.Parse("e2929ca8-42d1-5c2a-9249-37269784d11d");
    private static readonly RenderMeshId MeshId=new(StableId.Parse("8069221e-08e8-5e84-9df1-5ceaba6e85ae"));
    private static readonly RenderMaterialId MaterialId=new(StableId.Parse("4c0a4059-da61-58ea-bafd-b31e4a73682c"));

    public static int Run(string[]Args){
        if(!TryParse(Args,out RawOptions?Options,out string?Error)){
            Console.Error.WriteLine($"FAILED: {Error}");
            return 2;
        }
        try {
            RawWorkload Visibility=MeasureVisibility();
            (RawWorkload Frame,string Adapter,string Description,string ShaderArtifactSha256,
                int DebugErrors,bool PixelVerified)=
                MeasureFrames(Options!);
            var Report=new RawReport(
                Schema,Options!.Configuration,Adapter,Description,RuntimeMode(),
                ShaderArtifactSha256,Width,Height,
                DebugErrors,PixelVerified,[Frame,Visibility]);
            string OutputPath=Path.GetFullPath(Options.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            byte[]Json=JsonSerializer.SerializeToUtf8Bytes(Report,new JsonSerializerOptions {
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                WriteIndented=true,
            });
            byte[]Canonical=new byte[Json.Length+1];
            Json.CopyTo(Canonical,0);
            Canonical[^1]=(byte)'\n';
            File.WriteAllBytes(OutputPath,Canonical);
            Console.WriteLine($"Rendering performance raw report: {OutputPath}");
            return 0;
        } catch(Exception Ex){
            Console.Error.WriteLine($"FAILED: rendering performance capture: {Ex.Message}");
            return 1;
        }
    }

    private static RawWorkload MeasureVisibility(){
        RenderLodLevel[]Lods=[new(MeshId,10_000f)];
        var Instances=new RenderInstanceRecord[VisibilityCount];
        for(int Index=0;Index<Instances.Length;Index++){
            Instances[Index]=new(
                StableId.FromName(NamespaceId,Index.ToString(CultureInfo.InvariantCulture)),
                MaterialId,Matrix4x4.Identity,new Vector3(Index%100,0f,Index/100),
                0.25f,0,1);
        }
        RenderWorldSnapshot Snapshot=RenderWorldSnapshot.Create(1,Instances,Lods);
        var CurrentStates=new RenderLodState[VisibilityCount];
        var NextStates=new RenderLodState[VisibilityCount];
        var Visible=new VisibleRenderInstance[VisibilityCount];
        var Order=new int[VisibilityCount];
        var Batches=new RenderInstanceBatch[VisibilityCount];
        RenderView View=new(RenderFrustum.CreateAxisAligned(new Vector3(-1f),new Vector3(2000f)),0.1f);
        bool HasState=false;

        void RunOnce(){
            RenderVisibilityQueryResult Result=RenderVisibility.Select(
                Snapshot,View,HasState?CurrentStates:[],NextStates,Visible);
            int BatchCount=RenderVisibility.BuildBatches(
                Visible.AsSpan(0,Result.VisibleCount),Order,Batches);
            if(Result.VisibleCount!=VisibilityCount||BatchCount!=1){
                throw new InvalidDataException(
                    $"Visibility oracle failed: visible={Result.VisibleCount}, batches={BatchCount}.");
            }
            (CurrentStates,NextStates)=(NextStates,CurrentStates);
            HasState=true;
        }

        for(int Index=0;Index<WarmupCount;Index++){ RunOnce(); }
        var Samples=new RawSample[SampleCount];
        for(int Index=0;Index<Samples.Length;Index++){
            long AllocationStart=GC.GetAllocatedBytesForCurrentThread();
            long TimestampStart=Stopwatch.GetTimestamp();
            RunOnce();
            long TimestampEnd=Stopwatch.GetTimestamp();
            long Allocated=GC.GetAllocatedBytesForCurrentThread()-AllocationStart;
            Samples[Index]=new(Index,ToNanoseconds(TimestampEnd-TimestampStart),Allocated);
        }
        return new RawWorkload("rendering.visibility-100k",WarmupCount,Samples);
    }

    private static (RawWorkload Workload,string Adapter,string Description,
        string ShaderArtifactSha256,int DebugErrors,bool PixelVerified)
        MeasureFrames(RawOptions Options){
        string ShaderPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
        string ShaderArtifactSha256=Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(ShaderPath))).ToLowerInvariant();
        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Rendering Performance",
            Width=Width,
            Height=Height,
            Visible=false,
        });
        StageMaterialCreateInfo Material=new(){
            BaseColorFactor=new Vector4(0.9f,0.25f,0.15f,1f),
            RoughnessFactor=1f,
            MetallicFactor=0f,
        };
        (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,
            StageObjectCreateInfo[]Objects)=RenderServer.BuildSingleMeshRoster(
            [new Vector3(0f,0.6f,0f),new Vector3(-0.6f,-0.6f,0f),new Vector3(0.6f,-0.6f,0f)],
            [Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ],
            [new Vector2(0.5f,0f),new Vector2(0f,1f),new Vector2(1f,1f)],
            [0u,1u,2u],Material);
        using RenderServer Renderer=RenderServer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,
            new GraphicsDeviceOptions {
                UseWarp=Options.Adapter=="warp",
                EnableDebugLayer=true,
            },ShaderPath,Meshes,Materials,Objects);
        Renderer.SetCamera(Camera.CreateDefault(Vector3.Zero));
        Renderer.SetAmbientLight(Vector3.One);
        for(int Index=0;Index<WarmupCount;Index++){
            Window.PumpEvents();
            Renderer.RenderFrame();
        }
        var Samples=new RawSample[SampleCount];
        for(int Index=0;Index<Samples.Length;Index++){
            Window.PumpEvents();
            long AllocationStart=GC.GetAllocatedBytesForCurrentThread();
            long TimestampStart=Stopwatch.GetTimestamp();
            Renderer.RenderFrame();
            long TimestampEnd=Stopwatch.GetTimestamp();
            long Allocated=GC.GetAllocatedBytesForCurrentThread()-AllocationStart;
            Samples[Index]=new(Index,ToNanoseconds(TimestampEnd-TimestampStart),Allocated);
        }
        Renderer.RenderFrame(CaptureForVerification:true);
        bool PixelVerified=Renderer.LastCapturedFrame is not null
            &&TrianglePixelVerifier.Verify(
                Renderer.LastCapturedFrame,RenderServer.ClearColor).Success;
        return (
            new RawWorkload("rendering.frame-1280x720",WarmupCount,Samples),
            Renderer.IsWarp?"warp":"hardware",Renderer.AdapterDescription,
            ShaderArtifactSha256,Renderer.ErrorOrWorseMessageCount,PixelVerified);
    }

    private static long ToNanoseconds(long CounterDelta){
        if(CounterDelta<=0){ return 0; }
        Int128 Nanoseconds=(Int128)CounterDelta*1_000_000_000L/Stopwatch.Frequency;
        return Nanoseconds>long.MaxValue?long.MaxValue:(long)Nanoseconds;
    }

    private static string RuntimeMode()=>
        Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")=="0"
        &&Environment.GetEnvironmentVariable("COMPlus_TieredCompilation")=="0"
            ?"tiered-compilation-disabled":"runtime-default";

    private static bool TryParse(
        IReadOnlyList<string>Args,out RawOptions?Options,out string?Error){
        string?Configuration=null;
        string?Adapter=null;
        string?Output=null;
        for(int Index=0;Index<Args.Count;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Count){
                Options=null;Error=$"{Argument} requires a value.";return false;
            }
            string Value=Args[++Index];
            switch(Argument){
                case "--configuration" when Value is "Debug" or "Release":
                    Configuration=Value;break;
                case "--adapter" when Value is "warp" or "hardware":
                    Adapter=Value;break;
                case "--output" when !string.IsNullOrWhiteSpace(Value):
                    Output=Value;break;
                default:
                    Options=null;Error=$"Unknown or invalid rendering performance option: {Argument} {Value}.";return false;
            }
        }
        if(Configuration is null||Adapter is null||Output is null){
            Options=null;
            Error="rendering-performance requires --configuration, --adapter, and --output.";
            return false;
        }
        Options=new RawOptions(Configuration,Adapter,Output);
        Error=null;
        return true;
    }

    private sealed record RawOptions(string Configuration,string Adapter,string OutputPath);
    private sealed record RawSample(int Index,long ElapsedNanoseconds,long AllocatedBytes);
    private sealed record RawWorkload(string Id,int WarmupCount,IReadOnlyList<RawSample>Samples);
    private sealed record RawReport(
        string Schema,string Configuration,string Adapter,string AdapterDescription,
        string RuntimeMode,string ShaderArtifactSha256,
        int Width,int Height,int DebugErrorCount,bool PixelVerified,
        IReadOnlyList<RawWorkload>Workloads);
}
