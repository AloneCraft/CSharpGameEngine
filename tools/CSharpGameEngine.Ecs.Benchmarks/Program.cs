using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.Ecs.Benchmarks;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;
using CSharpGameEngine.Runtime.Simulation;

return EcsPerformanceRawCapture.Run(args);

internal static class EcsPerformanceRawCapture {
    private const int WarmupCount=5;
    private const int SampleCount=30;
    private const int EntityCount=100_000;
    private const string Schema="csharp-game-engine-ecs-raw-performance-v1";

    public static int Run(string[]Args){
        if(!TryParse(Args,out RawOptions?Options,out string?Error)){
            Console.Error.WriteLine($"FAILED: {Error}");
            return 2;
        }
        try {
            string BuildConfiguration=GetBuildConfiguration();
            if(Options!.Configuration!=BuildConfiguration){
                throw new InvalidDataException(
                    $"Requested configuration {Options.Configuration} does not match compiled configuration {BuildConfiguration}.");
            }
            (string GeneratedSourceSha256,bool ContainsHighWord)=ReadGeneratedSource(
                Options.RepositoryRoot,BuildConfiguration);
            int ExpectedMaskWords=ComponentTypeCount<=64?1:2;
            if(ContainsHighWord!=(ExpectedMaskWords==2)){
                throw new InvalidDataException(
                    $"Generated mask shape is invalid for {ComponentTypeCount} component types.");
            }

            World World=new();
            Entity First=default;
            Entity Last=default;
            for(int Index=0;Index<EntityCount;Index++){
                Entity Created=BenchmarkWorldFactory.Create(World,Index);
                if(Index==0){ First=Created; }
                Last=Created;
            }
            State State=new(World){Enabled=true};
            HotSystem System=new();
            FixedTickScheduler Scheduler=new();
            for(int Index=0;Index<WarmupCount;Index++){
                RunOnce(ref State,ref System,ref Scheduler);
            }
            var Samples=new RawSample[SampleCount];
            for(int Index=0;Index<Samples.Length;Index++){
                long AllocationStart=GC.GetAllocatedBytesForCurrentThread();
                long TimestampStart=Stopwatch.GetTimestamp();
                RunOnce(ref State,ref System,ref Scheduler);
                long TimestampEnd=Stopwatch.GetTimestamp();
                long Allocated=GC.GetAllocatedBytesForCurrentThread()-AllocationStart;
                Samples[Index]=new(
                    Index,ToNanoseconds(TimestampEnd-TimestampStart),Allocated);
            }

            int ExpectedCalls=checked(EntityCount*(WarmupCount+SampleCount));
            int FirstValue=BenchmarkWorldFactory.ReadHot(World,First);
            int LastValue=BenchmarkWorldFactory.ReadHot(World,Last);
            int ExpectedLast=checked(EntityCount-1+WarmupCount+SampleCount);
            WorldStatistics Statistics=World.GetStatistics();
            if(System.Calls!=ExpectedCalls
                ||FirstValue!=WarmupCount+SampleCount
                ||LastValue!=ExpectedLast
                ||Statistics.AliveEntityCount!=EntityCount
                ||Statistics.EntitySlotCount!=EntityCount){
                throw new InvalidDataException(
                    $"ECS state oracle failed: calls={System.Calls}, first={FirstValue}, last={LastValue}, alive={Statistics.AliveEntityCount}, slots={Statistics.EntitySlotCount}.");
            }
            string StateSha256=Sha256(
                $"{ComponentTypeCount}|{EntityCount}|{System.Calls}|{FirstValue}|{LastValue}|{Statistics.AliveEntityCount}|{Statistics.EntitySlotCount}");
            var Report=new RawReport(
                Schema,BuildConfiguration,RuntimeMode(),ComponentTypeCount,
                $"C{ComponentTypeCount-1}",EntityCount,1,
                GeneratedSourceSha256,StateSha256,ExpectedMaskWords,ContainsHighWord,
                new RawWorkload(
                    $"ecs.generated-query-{ComponentTypeCount}",WarmupCount,Samples));
            string OutputPath=Path.GetFullPath(Options.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            byte[]Json=JsonSerializer.SerializeToUtf8Bytes(
                Report,new JsonSerializerOptions {
                    PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                    WriteIndented=true,
                });
            byte[]Canonical=new byte[Json.Length+1];
            Json.CopyTo(Canonical,0);
            Canonical[^1]=(byte)'\n';
            string TemporaryPath=OutputPath+".tmp-"+Guid.NewGuid().ToString("N");
            try {
                File.WriteAllBytes(TemporaryPath,Canonical);
                File.Move(TemporaryPath,OutputPath,true);
            } finally {
                if(File.Exists(TemporaryPath)){ File.Delete(TemporaryPath); }
            }
            Console.WriteLine(
                $"ECS performance raw report: count={ComponentTypeCount} path={OutputPath}");
            return 0;
        } catch(Exception Ex){
            Console.Error.WriteLine($"FAILED: ECS performance capture: {Ex.Message}");
            return 1;
        }
    }

    private static void RunOnce(
        ref State State,ref HotSystem System,ref FixedTickScheduler Scheduler)=>
        GeneratedGameLoop.RunFrame(ref State,ref System,ref Scheduler,1,60);

    private static (string Sha256,bool ContainsHighWord)ReadGeneratedSource(
        string RepositoryRoot,string Configuration){
        string Root=Path.Combine(
            RepositoryRoot,"tools","CSharpGameEngine.Ecs.Benchmarks","obj",
            Configuration,"net10.0","generated",$"ecs-{ComponentTypeCount}");
        if(!Directory.Exists(Root)){
            throw new DirectoryNotFoundException(
                $"Generated source directory is missing: {Root}");
        }
        string[]Files=Directory.GetFiles(Root,"*.g.cs",SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToArray();
        if(Files.Length==0){
            throw new InvalidDataException(
                $"Generated source directory contains no .g.cs files: {Root}");
        }
        using IncrementalHash Hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        bool ContainsHighWord=false;
        foreach(string FilePath in Files){
            string Relative=Path.GetRelativePath(Root,FilePath).Replace('\\','/');
            byte[]RelativeBytes=Encoding.UTF8.GetBytes(Relative);
            Hash.AppendData(RelativeBytes);
            Hash.AppendData([0]);
            byte[]Bytes=File.ReadAllBytes(FilePath);
            Hash.AppendData(Bytes);
            Hash.AppendData([0]);
            if(Encoding.UTF8.GetString(Bytes).Contains(
                "ComponentEnabledHigh",StringComparison.Ordinal)){
                ContainsHighWord=true;
            }
        }
        return (Convert.ToHexString(Hash.GetHashAndReset()).ToLowerInvariant(),ContainsHighWord);
    }

    private static string RuntimeMode()=>
        Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")=="0"
        &&Environment.GetEnvironmentVariable("COMPlus_TieredCompilation")=="0"
            ?"tiered-compilation-disabled":"runtime-default";

    private static long ToNanoseconds(long CounterDelta){
        if(CounterDelta<=0){ return 0; }
        Int128 Nanoseconds=(Int128)CounterDelta*1_000_000_000L/Stopwatch.Frequency;
        return Nanoseconds>long.MaxValue?long.MaxValue:(long)Nanoseconds;
    }

    private static string Sha256(string Value)=>Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Value))).ToLowerInvariant();

    private static int ComponentTypeCount {
        get {
#if ECS_COMPONENT_COUNT_1
            return 1;
#elif ECS_COMPONENT_COUNT_64
            return 64;
#elif ECS_COMPONENT_COUNT_65
            return 65;
#elif ECS_COMPONENT_COUNT_128
            return 128;
#else
#error EcsComponentCount must be 1, 64, 65, or 128.
#endif
        }
    }

    private static string GetBuildConfiguration(){
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static bool TryParse(
        IReadOnlyList<string>Args,out RawOptions?Options,out string?Error){
        string?RepositoryRoot=null;
        string?Configuration=null;
        string?Output=null;
        for(int Index=0;Index<Args.Count;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Count){
                Options=null;Error=$"{Argument} requires a value.";return false;
            }
            string Value=Args[++Index];
            switch(Argument){
                case "--repository-root" when Directory.Exists(Value):
                    RepositoryRoot=Path.GetFullPath(Value);break;
                case "--configuration" when Value is "Debug" or "Release":
                    Configuration=Value;break;
                case "--output" when !string.IsNullOrWhiteSpace(Value):
                    Output=Value;break;
                default:
                    Options=null;Error=$"Unknown or invalid ECS performance option: {Argument} {Value}.";return false;
            }
        }
        if(RepositoryRoot is null||Configuration is null||Output is null){
            Options=null;
            Error="ECS performance capture requires --repository-root, --configuration, and --output.";
            return false;
        }
        Options=new RawOptions(RepositoryRoot,Configuration,Output);
        Error=null;
        return true;
    }

    private sealed record RawOptions(
        string RepositoryRoot,string Configuration,string OutputPath);
    private sealed record RawSample(
        int Index,long ElapsedNanoseconds,long AllocatedBytes);
    private sealed record RawWorkload(
        string Id,int WarmupCount,IReadOnlyList<RawSample>Samples);
    private sealed record RawReport(
        string Schema,string Configuration,string RuntimeMode,
        int ComponentTypeCount,string QueryComponent,int EntityCount,int WorkerCount,
        string GeneratedSourceSha256,string StateSha256,int MaskWordCount,
        bool GeneratedRuntimeContainsHighWord,RawWorkload Workload);
}
