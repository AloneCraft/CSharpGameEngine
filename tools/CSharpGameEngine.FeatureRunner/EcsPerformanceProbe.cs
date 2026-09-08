using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CSharpGameEngine.FeatureStages;

public sealed record EcsRawWorkload(
    string Id,int WarmupCount,IReadOnlyList<PerformanceSample>Samples);
public sealed record EcsRawPerformanceReport(
    string Schema,string Configuration,string RuntimeMode,
    int ComponentTypeCount,string QueryComponent,int EntityCount,int WorkerCount,
    string GeneratedSourceSha256,string StateSha256,int MaskWordCount,
    bool GeneratedRuntimeContainsHighWord,EcsRawWorkload Workload);

public static class EcsPerformanceProbe {
    public const string RawSchema="csharp-game-engine-ecs-raw-performance-v1";
    public const string RequiredRuntimeMode="tiered-compilation-disabled";
    public const int RequiredEntityCount=100_000;
    private static readonly int[]RequiredComponentTypeCounts=[1,64,65,128];

    public static IReadOnlyList<string>ValidateRawReports(
        IReadOnlyList<EcsRawPerformanceReport>Reports,
        string?ExpectedConfiguration=null){
        ArgumentNullException.ThrowIfNull(Reports);
        var Errors=new List<string>();
        EcsRawPerformanceReport[]Values=Reports.Where(static Report=>Report is not null)
            .ToArray();
        foreach(int Duplicate in Values
            .GroupBy(static Report=>Report.ComponentTypeCount)
            .Where(static Group=>Group.Count()>1)
            .Select(static Group=>Group.Key)){
            Errors.Add($"Duplicate ECS raw component boundary: {Duplicate}.");
        }
        foreach(int Required in RequiredComponentTypeCounts.Except(
            Values.Select(static Report=>Report.ComponentTypeCount))){
            Errors.Add($"Missing ECS raw component boundary: {Required}.");
        }
        foreach(int Extra in Values.Select(static Report=>Report.ComponentTypeCount)
            .Except(RequiredComponentTypeCounts)){
            Errors.Add($"Unregistered ECS raw component boundary: {Extra}.");
        }
        foreach(EcsRawPerformanceReport Report in Values){
            string Prefix=$"ECS {Report.ComponentTypeCount}-type";
            if(Report.Schema!=RawSchema){
                Errors.Add($"{Prefix} raw schema must be {RawSchema}.");
            }
            if(Report.Configuration is not("Debug" or "Release")){
                Errors.Add($"{Prefix} configuration must be Debug or Release.");
            }
            if(ExpectedConfiguration is not null
                &&Report.Configuration!=ExpectedConfiguration){
                Errors.Add($"{Prefix} configuration must be {ExpectedConfiguration}.");
            }
            if(Report.RuntimeMode!=RequiredRuntimeMode){
                Errors.Add(
                    $"{Prefix} performance requires Tiered Compilation to be disabled and recorded as {RequiredRuntimeMode}.");
            }
            if(Report.EntityCount!=RequiredEntityCount){
                Errors.Add($"{Prefix} requires exactly {RequiredEntityCount} active entities.");
            }
            if(Report.WorkerCount!=1){
                Errors.Add($"{Prefix} direct generated query requires exactly one worker.");
            }
            string ExpectedQuery=$"C{Report.ComponentTypeCount-1}";
            if(Report.QueryComponent!=ExpectedQuery){
                Errors.Add($"{Prefix} query component must be {ExpectedQuery}.");
            }
            if(!IsSha256(Report.GeneratedSourceSha256)){
                Errors.Add($"{Prefix} generated source SHA-256 is invalid.");
            }
            if(!IsSha256(Report.StateSha256)){
                Errors.Add($"{Prefix} state SHA-256 is invalid.");
            } else if(RequiredComponentTypeCounts.Contains(Report.ComponentTypeCount)
                &&Report.StateSha256!=ComputeExpectedStateSha256(
                    Report.ComponentTypeCount)){
                Errors.Add($"{Prefix} state SHA-256 does not match the deterministic state oracle.");
            }
            int ExpectedMaskWords=Report.ComponentTypeCount<=64?1:2;
            bool ExpectedHigh=ExpectedMaskWords==2;
            if(Report.MaskWordCount!=ExpectedMaskWords){
                Errors.Add(
                    $"{Prefix} generated mask word count must be {ExpectedMaskWords}.");
            }
            if(Report.GeneratedRuntimeContainsHighWord!=ExpectedHigh){
                Errors.Add(
                    $"{Prefix} generated runtime high-word shape does not match its boundary.");
            }
            EcsRawWorkload?Workload=Report.Workload;
            string ExpectedWorkload=$"ecs.generated-query-{Report.ComponentTypeCount}";
            if(Workload is null){
                Errors.Add($"{Prefix} workload is missing.");
                continue;
            }
            if(Workload.Id!=ExpectedWorkload){
                Errors.Add($"{Prefix} workload id must be {ExpectedWorkload}.");
            }
            if(Workload.WarmupCount!=
                PerformanceMeasurementFramework.RequiredWarmupCount){
                Errors.Add(
                    $"{Prefix} requires exactly {PerformanceMeasurementFramework.RequiredWarmupCount} warm-up iterations.");
            }
            PerformanceSample[]Samples=Workload.Samples?.ToArray()??[];
            if(Samples.Length!=PerformanceMeasurementFramework.RequiredSampleCount){
                Errors.Add(
                    $"{Prefix} requires exactly {PerformanceMeasurementFramework.RequiredSampleCount} measured samples.");
            }
            foreach(int Duplicate in Samples.GroupBy(static Sample=>Sample.Index)
                .Where(static Group=>Group.Count()>1)
                .Select(static Group=>Group.Key)){
                Errors.Add($"{Prefix} has Duplicate sample index {Duplicate}.");
            }
            if(!Samples.Select(static Sample=>Sample.Index).Order()
                .SequenceEqual(Enumerable.Range(
                    0,PerformanceMeasurementFramework.RequiredSampleCount))){
                Errors.Add($"{Prefix} sample indices must be the exact zero-through-29 sequence.");
            }
            if(Samples.Any(static Sample=>Sample.ElapsedNanoseconds<=0)){
                Errors.Add($"{Prefix} contains a sample with no measurable elapsed time.");
            }
            if(Samples.Any(static Sample=>Sample.AllocatedBytes<0)){
                Errors.Add($"{Prefix} contains a negative allocation sample.");
            }
            if(Samples.Any(static Sample=>Sample.AllocatedBytes!=0)){
                Errors.Add($"{Prefix} generated query must have zero managed allocation.");
            }
        }
        if(Values.Length==RequiredComponentTypeCounts.Length){
            if(Values.Select(static Report=>Report.GeneratedSourceSha256)
                .Distinct(StringComparer.Ordinal).Count()!=Values.Length){
                Errors.Add(
                    "Each ECS component boundary requires a distinct generated source SHA-256.");
            }
            if(Values.Select(static Report=>Report.StateSha256)
                .Distinct(StringComparer.Ordinal).Count()!=Values.Length){
                Errors.Add(
                    "Each ECS component boundary requires a distinct state SHA-256.");
            }
        }
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static string ComputeExpectedStateSha256(int ComponentTypeCount){
        if(!RequiredComponentTypeCounts.Contains(ComponentTypeCount)){
            throw new ArgumentOutOfRangeException(nameof(ComponentTypeCount));
        }
        const int Executions=PerformanceMeasurementFramework.RequiredWarmupCount
            +PerformanceMeasurementFramework.RequiredSampleCount;
        int Calls=checked(RequiredEntityCount*Executions);
        int Last=checked(RequiredEntityCount-1+Executions);
        string Canonical=$"{ComponentTypeCount}|{RequiredEntityCount}|{Calls}|{Executions}|{Last}|{RequiredEntityCount}|{RequiredEntityCount}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical)))
            .ToLowerInvariant();
    }

    public static (string Sha256,bool ContainsHighWord)ReadGeneratedSourceEvidence(
        string RepositoryRoot,string Configuration,int ComponentTypeCount){
        if(Configuration is not("Debug" or "Release")){
            throw new ArgumentException(
                "Configuration must be Debug or Release.",nameof(Configuration));
        }
        if(!RequiredComponentTypeCounts.Contains(ComponentTypeCount)){
            throw new ArgumentOutOfRangeException(nameof(ComponentTypeCount));
        }
        string Root=Path.Combine(
            Path.GetFullPath(RepositoryRoot),"tools","CSharpGameEngine.Ecs.Benchmarks",
            "obj",Configuration,"net10.0","generated",$"ecs-{ComponentTypeCount}");
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
            Hash.AppendData(Encoding.UTF8.GetBytes(Relative));
            Hash.AppendData([0]);
            byte[]Bytes=File.ReadAllBytes(FilePath);
            Hash.AppendData(Bytes);
            Hash.AppendData([0]);
            if(Encoding.UTF8.GetString(Bytes).Contains(
                "ComponentEnabledHigh",StringComparison.Ordinal)){
                ContainsHighWord=true;
            }
        }
        return (
            Convert.ToHexString(Hash.GetHashAndReset()).ToLowerInvariant(),
            ContainsHighWord);
    }

    public static PerformanceMeasurementPlan CreatePlan(
        EcsRawPerformanceReport Report,string RawReportSha256,
        PerformanceBaseline?Baseline=null){
        ArgumentNullException.ThrowIfNull(Report);
        if(!IsSha256(RawReportSha256)){
            throw new ArgumentException(
                "Raw report SHA-256 is required.",nameof(RawReportSha256));
        }
        PerformanceAbsoluteBudget Budget=Report.Configuration=="Debug"
            ?new PerformanceAbsoluteBudget(long.MaxValue,long.MaxValue,0)
            :new PerformanceAbsoluteBudget(6_000_000,8_330_000,0);
        var Metadata=new SortedDictionary<string,string>(StringComparer.Ordinal){
            ["activeEntities"]=Report.EntityCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["componentTypeCount"]=Report.ComponentTypeCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["generatedRuntimeContainsHighWord"]=
                Report.GeneratedRuntimeContainsHighWord?"true":"false",
            ["generatedSourceSha256"]=Report.GeneratedSourceSha256,
            ["maskWordCount"]=Report.MaskWordCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["queryShape"]=$"write:{Report.QueryComponent}",
            ["rawReportSha256"]=RawReportSha256,
            ["runtimeMode"]=Report.RuntimeMode,
            ["stateSha256"]=Report.StateSha256,
            ["timingGate"]=Report.Configuration=="Debug"
                ?"diagnostic-only":"fixed-simulation-budget",
            ["workerCount"]=Report.WorkerCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
        };
        return new PerformanceMeasurementPlan(
            Report.Workload.Id,Report.Configuration,"nanoseconds",Metadata,
            Report.Workload.WarmupCount,Report.Workload.Samples.Count,
            Budget,Report.Configuration=="Release"?Baseline:null);
    }

    public static async Task<int>RunAsync(
        string RepositoryRoot,string Configuration,TextWriter Output,
        TextWriter Error,CancellationToken CancellationToken){
        string Root=Path.GetFullPath(RepositoryRoot);
        string RawDirectory=Path.Combine(Root,".artifacts","EcsPerformance");
        Directory.CreateDirectory(RawDirectory);
        var Captures=new List<(string Path,byte[]Bytes,EcsRawPerformanceReport Report)>();
        foreach(int ComponentCount in RequiredComponentTypeCounts){
            string RawPath=Path.Combine(
                RawDirectory,
                $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Configuration.ToLowerInvariant()}-{ComponentCount}-{Guid.NewGuid():N}.raw.json");
            ProcessResult Process=await RunCaptureAsync(
                Root,Configuration,ComponentCount,RawPath,CancellationToken)
                .ConfigureAwait(false);
            if(Process.ExitCode!=0){
                await Error.WriteLineAsync(
                    $"ECS performance capture {ComponentCount} exited with code {Process.ExitCode}.")
                    .ConfigureAwait(false);
                if(!string.IsNullOrWhiteSpace(Process.StandardError)){
                    await Error.WriteLineAsync(Process.StandardError.Trim())
                        .ConfigureAwait(false);
                }
                return 1;
            }
            if(!string.IsNullOrWhiteSpace(Process.StandardOutput)){
                await Output.WriteLineAsync(Process.StandardOutput.Trim())
                    .ConfigureAwait(false);
            }
            try {
                byte[]Bytes=await File.ReadAllBytesAsync(
                    RawPath,CancellationToken).ConfigureAwait(false);
                EcsRawPerformanceReport?Report=
                    JsonSerializer.Deserialize<EcsRawPerformanceReport>(
                        Bytes,FeatureRunResultStore.CreateJsonOptions());
                if(Report is null){
                    await Error.WriteLineAsync(
                        $"ECS raw report {ComponentCount} is empty.").ConfigureAwait(false);
                    return 1;
                }
                Captures.Add((RawPath,Bytes,Report));
            } catch(Exception Ex)when(
                Ex is IOException or UnauthorizedAccessException or JsonException){
                await Error.WriteLineAsync(
                    $"ECS raw report {ComponentCount} cannot be read: {Ex.Message}")
                    .ConfigureAwait(false);
                return 1;
            }
        }

        IReadOnlyList<string>RawErrors=ValidateRawReports(
            Captures.Select(static Capture=>Capture.Report).ToArray(),Configuration);
        var SourceErrors=new List<string>();
        foreach(var Capture in Captures){
            try {
                (string Sha256,bool ContainsHighWord)Evidence=
                    ReadGeneratedSourceEvidence(
                        Root,Configuration,Capture.Report.ComponentTypeCount);
                if(Evidence.Sha256!=Capture.Report.GeneratedSourceSha256){
                    SourceErrors.Add(
                        $"ECS {Capture.Report.ComponentTypeCount}-type raw generated source SHA-256 does not match the independently hashed output.");
                }
                if(Evidence.ContainsHighWord!=
                    Capture.Report.GeneratedRuntimeContainsHighWord){
                    SourceErrors.Add(
                        $"ECS {Capture.Report.ComponentTypeCount}-type raw high-word shape does not match the independently inspected output.");
                }
            } catch(Exception Ex)when(
                Ex is IOException or UnauthorizedAccessException
                    or InvalidDataException or DirectoryNotFoundException){
                SourceErrors.Add(
                    $"ECS {Capture.Report.ComponentTypeCount}-type generated source cannot be independently validated: {Ex.Message}");
            }
        }
        RawErrors=[.. RawErrors.Concat(SourceErrors).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        foreach(string ValidationError in RawErrors){
            await Error.WriteLineAsync(ValidationError).ConfigureAwait(false);
        }
        if(RawErrors.Count!=0){ return 1; }

        (string Commit,bool IsDirty)Provenance;
        try {
            Provenance=await GitChangedPathProvider.ReadProvenanceAsync(
                Root,CancellationToken).ConfigureAwait(false);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Provenance=("unknown",true);
            await Error.WriteLineAsync(
                $"Warning: git provenance is unavailable: {Ex.Message}")
                .ConfigureAwait(false);
        }
        PerformanceMachineFingerprint Machine=
            PerformanceMeasurementFramework.CaptureMachineFingerprint();
        var IntegrityErrors=new List<string>();
        var AllPassed=true;
        PerformanceAnalysis?SingleTypeAnalysis=null;
        foreach(var Capture in Captures.OrderBy(
            static Capture=>Capture.Report.ComponentTypeCount)){
            string RawHash=Convert.ToHexString(
                SHA256.HashData(Capture.Bytes)).ToLowerInvariant();
            PerformanceBaseline?Baseline=
                Configuration=="Release"
                &&Capture.Report.ComponentTypeCount==64
                &&SingleTypeAnalysis is not null
                    ?new PerformanceBaseline(
                        SingleTypeAnalysis.ElapsedNanoseconds.Median,
                        SingleTypeAnalysis.ElapsedNanoseconds.P95,0,2000)
                    :null;
            PerformanceMeasurementPlan Plan=CreatePlan(
                Capture.Report,RawHash,Baseline);
            PerformanceAnalysis Analysis=PerformanceMeasurementFramework.Analyze(
                Plan,Capture.Report.Workload.Samples);
            if(Capture.Report.ComponentTypeCount==1){
                SingleTypeAnalysis=Analysis;
            }
            var Payload=new PerformanceReportPayload(
                PerformanceFrameworkProbe.ReportSchema,DateTimeOffset.UtcNow,
                Plan,Capture.Report.Workload.Samples,Analysis,Machine,
                Provenance.Commit,Provenance.IsDirty);
            string ReportPath=await PerformanceReportStore.SaveAsync(
                Root,Payload,CancellationToken).ConfigureAwait(false);
            IntegrityErrors.AddRange(PerformanceReportStore.Validate(ReportPath));
            await Output.WriteLineAsync(
                $"{Plan.Id} samples={Plan.SampleCount} medianNs={Analysis.ElapsedNanoseconds.Median} "
                +$"p95Ns={Analysis.ElapsedNanoseconds.P95} madNs={Analysis.ElapsedNanoseconds.Mad} "
                +$"allocationP95={Analysis.AllocatedBytes.P95} passed={Analysis.Passed} "
                +$"raw={Capture.Path} report={ReportPath}").ConfigureAwait(false);
            if(!Analysis.Passed){
                await Error.WriteLineAsync(
                    $"{Plan.Id}: absolute={Analysis.AbsoluteBudget.Detail}; baseline={Analysis.BaselineDelta.Detail}")
                    .ConfigureAwait(false);
                AllPassed=false;
            }
        }
        foreach(string IntegrityError in IntegrityErrors){
            await Error.WriteLineAsync(IntegrityError).ConfigureAwait(false);
        }
        return AllPassed&&IntegrityErrors.Count==0?0:1;
    }

    private static bool IsSha256(string?Value)=>
        Value is {Length:64}&&Value.All(static Character=>
            Character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static async Task<ProcessResult>RunCaptureAsync(
        string RepositoryRoot,string Configuration,int ComponentTypeCount,
        string RawPath,CancellationToken CancellationToken){
        var StartInfo=new ProcessStartInfo(){
            FileName="dotnet",WorkingDirectory=RepositoryRoot,
            UseShellExecute=false,RedirectStandardOutput=true,
            RedirectStandardError=true,CreateNoWindow=true,
        };
        foreach(string Argument in new[]{
            "run","--project",
            "tools/CSharpGameEngine.Ecs.Benchmarks/CSharpGameEngine.Ecs.Benchmarks.csproj",
            "--configuration",Configuration,
            $"-p:EcsComponentCount={ComponentTypeCount}","--no-restore","--",
            "--repository-root",RepositoryRoot,
            "--configuration",Configuration,"--output",RawPath,
        }){
            StartInfo.ArgumentList.Add(Argument);
        }
        StartInfo.Environment["DOTNET_TieredCompilation"]="0";
        StartInfo.Environment["COMPlus_TieredCompilation"]="0";
        using var Process=new Process(){StartInfo=StartInfo};
        if(!Process.Start()){
            throw new InvalidOperationException(
                "Could not start ECS performance capture.");
        }
        Task<string>OutputTask=Process.StandardOutput.ReadToEndAsync(
            CancellationToken);
        Task<string>ErrorTask=Process.StandardError.ReadToEndAsync(
            CancellationToken);
        try {
            await Process.WaitForExitAsync(CancellationToken)
                .ConfigureAwait(false);
        } catch(OperationCanceledException){
            if(!Process.HasExited){
                try { Process.Kill(true); }
                catch(InvalidOperationException){ }
            }
            throw;
        }
        return new ProcessResult(
            Process.ExitCode,await OutputTask.ConfigureAwait(false),
            await ErrorTask.ConfigureAwait(false));
    }

    private sealed record ProcessResult(
        int ExitCode,string StandardOutput,string StandardError);
}
