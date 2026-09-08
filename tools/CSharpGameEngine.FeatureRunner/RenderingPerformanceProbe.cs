using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace CSharpGameEngine.FeatureStages;

public sealed record RenderingRawWorkload(
    string Id,int WarmupCount,IReadOnlyList<PerformanceSample>Samples);
public sealed record RenderingRawPerformanceReport(
    string Schema,string Configuration,string Adapter,string AdapterDescription,
    string RuntimeMode,string ShaderArtifactSha256,
    int Width,int Height,int DebugErrorCount,bool PixelVerified,
    IReadOnlyList<RenderingRawWorkload>Workloads);

public static class RenderingPerformanceProbe {
    public const string RawSchema="csharp-game-engine-rendering-raw-performance-v2";
    public const string RequiredRuntimeMode="tiered-compilation-disabled";
    private const string FrameWorkloadId="rendering.frame-1280x720";
    private const string VisibilityWorkloadId="rendering.visibility-100k";
    private static readonly IReadOnlyDictionary<string,long>MaximumSampleAllocation=
        new Dictionary<string,long>(StringComparer.Ordinal){
            [FrameWorkloadId]=1_000_000,
            [VisibilityWorkloadId]=0,
        };

    public static IReadOnlyList<string>ValidateRawReport(
        RenderingRawPerformanceReport Report,string ExpectedAdapter="warp",
        string?ExpectedConfiguration=null){
        ArgumentNullException.ThrowIfNull(Report);
        var Errors=new List<string>();
        if(Report.Schema!=RawSchema){ Errors.Add($"Rendering raw schema must be {RawSchema}."); }
        if(Report.Configuration is not("Debug" or "Release")){ Errors.Add("Rendering configuration must be Debug or Release."); }
        if(ExpectedConfiguration is not null&&Report.Configuration!=ExpectedConfiguration){ Errors.Add($"Rendering configuration must be {ExpectedConfiguration}."); }
        if(ExpectedAdapter is not("warp" or "hardware")){ throw new ArgumentException("Expected adapter must be warp or hardware.",nameof(ExpectedAdapter)); }
        if(Report.Adapter!=ExpectedAdapter){ Errors.Add($"Rendering adapter must be {ExpectedAdapter}."); }
        if(string.IsNullOrWhiteSpace(Report.AdapterDescription)){ Errors.Add("Rendering adapter description is required."); }
        if(Report.RuntimeMode!=RequiredRuntimeMode){ Errors.Add($"Rendering performance requires tiered compilation to be disabled and recorded as {RequiredRuntimeMode}."); }
        if(!IsSha256(Report.ShaderArtifactSha256)){ Errors.Add("Rendering performance requires a valid Shader Artifact SHA-256."); }
        if(Report.Width!=1280||Report.Height!=720){ Errors.Add("Rendering performance resolution must be 1280x720."); }
        if(Report.DebugErrorCount!=0){ Errors.Add("Rendering performance requires zero D3D12 debug errors."); }
        if(!Report.PixelVerified){ Errors.Add("Rendering performance requires a verified output pixel oracle."); }
        RenderingRawWorkload[]Workloads=Report.Workloads?.ToArray()??[];
        foreach(string Duplicate in Workloads.GroupBy(static Workload=>Workload.Id,StringComparer.Ordinal).Where(static Group=>Group.Count()>1).Select(static Group=>Group.Key)){
            Errors.Add($"Duplicate rendering workload: {Duplicate}.");
        }
        foreach((string Id,long MaximumAllocation) in MaximumSampleAllocation){
            RenderingRawWorkload?Workload=Workloads.FirstOrDefault(Workload=>Workload.Id==Id);
            if(Workload is null){ Errors.Add($"Missing rendering workload: {Id}.");continue; }
            if(Workload.WarmupCount!=PerformanceMeasurementFramework.RequiredWarmupCount){
                Errors.Add($"{Id} requires exactly {PerformanceMeasurementFramework.RequiredWarmupCount} warm-up iterations.");
            }
            PerformanceSample[]Samples=Workload.Samples?.ToArray()??[];
            if(Samples.Length!=PerformanceMeasurementFramework.RequiredSampleCount){
                Errors.Add($"{Id} requires exactly {PerformanceMeasurementFramework.RequiredSampleCount} measured samples.");
            }
            foreach(int Duplicate in Samples.GroupBy(static Sample=>Sample.Index).Where(static Group=>Group.Count()>1).Select(static Group=>Group.Key)){
                Errors.Add($"Duplicate sample index {Duplicate} in {Id}.");
            }
            if(!Samples.Select(static Sample=>Sample.Index).Order().SequenceEqual(
                Enumerable.Range(0,PerformanceMeasurementFramework.RequiredSampleCount))){
                Errors.Add($"{Id} sample indices must be the exact zero-through-29 sequence.");
            }
            if(Samples.Any(static Sample=>Sample.ElapsedNanoseconds<=0)){
                Errors.Add($"{Id} contains a sample with no measurable elapsed time.");
            }
            if(Samples.Any(static Sample=>Sample.AllocatedBytes<0)){
                Errors.Add($"{Id} contains a negative allocation sample.");
            }
            if(Samples.Any(Sample=>Sample.AllocatedBytes>MaximumAllocation)){
                Errors.Add($"{Id} exceeds its per-sample allocation ceiling of {MaximumAllocation} bytes.");
            }
        }
        foreach(string Extra in Workloads.Select(static Workload=>Workload.Id).Except(MaximumSampleAllocation.Keys,StringComparer.Ordinal)){
            Errors.Add($"Unregistered rendering workload: {Extra}.");
        }
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public static PerformanceMeasurementPlan CreatePlan(
        RenderingRawPerformanceReport Report,RenderingRawWorkload Workload,
        string RawReportSha256){
        ArgumentNullException.ThrowIfNull(Report);
        ArgumentNullException.ThrowIfNull(Workload);
        if(string.IsNullOrWhiteSpace(RawReportSha256)){ throw new ArgumentException("Raw report SHA-256 is required.",nameof(RawReportSha256)); }
        bool IsFrame=Workload.Id==FrameWorkloadId;
        if(!IsFrame&&Workload.Id!=VisibilityWorkloadId){ throw new ArgumentException($"Unknown rendering workload: {Workload.Id}.",nameof(Workload)); }
        PerformanceAbsoluteBudget Budget=Report.Configuration=="Debug"
            ?new PerformanceAbsoluteBudget(
                long.MaxValue,long.MaxValue,IsFrame?1_000_000:0)
            :IsFrame
                ?FrameBudget(Report.Adapter)
                :new PerformanceAbsoluteBudget(6_000_000,8_330_000,0);
        string Id=IsFrame?$"rendering.{Report.Adapter}-frame":VisibilityWorkloadId;
        var Metadata=new SortedDictionary<string,string>(StringComparer.Ordinal){
            ["adapter"]=Report.Adapter,["adapterDescription"]=Report.AdapterDescription,
            ["debugLayer"]="enabled",["pixelOracle"]="triangle-readback",
            ["rawReportSha256"]=RawReportSha256,["resolution"]=$"{Report.Width}x{Report.Height}",
            ["runtimeMode"]=Report.RuntimeMode,["shaderArtifactSha256"]=Report.ShaderArtifactSha256,
            ["timingGate"]=Report.Configuration=="Debug"
                ||IsFrame&&Report.Adapter=="warp"?"correctness-only":"absolute-budget",
        };
        if(!IsFrame){
            Metadata["activeInstances"]="100000";
            Metadata["expectedBatches"]="1";
        }
        return new PerformanceMeasurementPlan(
            Id,Report.Configuration,"nanoseconds",Metadata,Workload.WarmupCount,
            Workload.Samples.Count,Budget,null);
    }

    public static async Task<int>RunAsync(
        string RepositoryRoot,string Configuration,string Adapter,
        TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        string Root=Path.GetFullPath(RepositoryRoot);
        string RawDirectory=Path.Combine(Root,".artifacts","RenderingPerformance");
        Directory.CreateDirectory(RawDirectory);
        string RawPath=Path.Combine(
            RawDirectory,$"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Configuration.ToLowerInvariant()}-{Adapter}-{Guid.NewGuid():N}.raw.json");
        ProcessResult Process=await RunCaptureAsync(
            Root,Configuration,Adapter,RawPath,CancellationToken).ConfigureAwait(false);
        if(Process.ExitCode!=0){
            await Error.WriteLineAsync($"Rendering performance capture exited with code {Process.ExitCode}.").ConfigureAwait(false);
            if(!string.IsNullOrWhiteSpace(Process.StandardError)){ await Error.WriteLineAsync(Process.StandardError.Trim()).ConfigureAwait(false); }
            return 1;
        }
        if(!string.IsNullOrWhiteSpace(Process.StandardOutput)){ await Output.WriteLineAsync(Process.StandardOutput.Trim()).ConfigureAwait(false); }
        byte[]RawBytes;
        RenderingRawPerformanceReport?Report;
        try {
            RawBytes=await File.ReadAllBytesAsync(RawPath,CancellationToken).ConfigureAwait(false);
            Report=JsonSerializer.Deserialize<RenderingRawPerformanceReport>(
                RawBytes,FeatureRunResultStore.CreateJsonOptions());
        } catch(Exception Ex)when(Ex is IOException or UnauthorizedAccessException or JsonException){
            await Error.WriteLineAsync($"Rendering raw report cannot be read: {Ex.Message}").ConfigureAwait(false);
            return 1;
        }
        if(Report is null){ await Error.WriteLineAsync("Rendering raw report is empty.").ConfigureAwait(false);return 1; }
        IReadOnlyList<string>RawErrors=ValidateRawReport(Report,Adapter,Configuration);
        foreach(string ValidationError in RawErrors){ await Error.WriteLineAsync(ValidationError).ConfigureAwait(false); }
        if(RawErrors.Count!=0){ return 1; }

        string RawHash=Convert.ToHexString(SHA256.HashData(RawBytes)).ToLowerInvariant();
        (string Commit,bool IsDirty)Provenance;
        try { Provenance=await GitChangedPathProvider.ReadProvenanceAsync(Root,CancellationToken).ConfigureAwait(false); }
        catch(Exception Ex)when(Ex is not OperationCanceledException){
            Provenance=("unknown",true);
            await Error.WriteLineAsync($"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false);
        }
        PerformanceMachineFingerprint Machine=PerformanceMeasurementFramework.CaptureMachineFingerprint();
        var IntegrityErrors=new List<string>();
        var AllPassed=true;
        foreach(RenderingRawWorkload Workload in Report.Workloads.OrderBy(static Workload=>Workload.Id,StringComparer.Ordinal)){
            PerformanceMeasurementPlan Plan=CreatePlan(Report,Workload,RawHash);
            PerformanceAnalysis Analysis=PerformanceMeasurementFramework.Analyze(Plan,Workload.Samples);
            var Payload=new PerformanceReportPayload(
                PerformanceFrameworkProbe.ReportSchema,DateTimeOffset.UtcNow,Plan,
                Workload.Samples,Analysis,Machine,Provenance.Commit,Provenance.IsDirty);
            string ReportPath=await PerformanceReportStore.SaveAsync(
                Root,Payload,CancellationToken).ConfigureAwait(false);
            IntegrityErrors.AddRange(PerformanceReportStore.Validate(ReportPath));
            await Output.WriteLineAsync(
                $"{Plan.Id} samples={Plan.SampleCount} medianNs={Analysis.ElapsedNanoseconds.Median} "
                +$"p95Ns={Analysis.ElapsedNanoseconds.P95} madNs={Analysis.ElapsedNanoseconds.Mad} "
                +$"allocationP95={Analysis.AllocatedBytes.P95} passed={Analysis.Passed} report={ReportPath}")
                .ConfigureAwait(false);
            if(!Analysis.Passed){
                await Error.WriteLineAsync($"{Plan.Id}: {Analysis.AbsoluteBudget.Detail}").ConfigureAwait(false);
                AllPassed=false;
            }
        }
        foreach(string IntegrityError in IntegrityErrors){ await Error.WriteLineAsync(IntegrityError).ConfigureAwait(false); }
        return AllPassed&&IntegrityErrors.Count==0?0:1;
    }

    private static PerformanceAbsoluteBudget FrameBudget(string Adapter)=>
        Adapter=="warp"
            ?new PerformanceAbsoluteBudget(long.MaxValue,long.MaxValue,1_000_000)
            :new PerformanceAbsoluteBudget(16_670_000,16_670_000,1_000_000);

    private static bool IsSha256(string?Value)=>
        Value is {Length:64}&&Value.All(static Character=>
            Character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static async Task<ProcessResult>RunCaptureAsync(
        string RepositoryRoot,string Configuration,string Adapter,string RawPath,
        CancellationToken CancellationToken){
        var StartInfo=new ProcessStartInfo(){
            FileName="dotnet",WorkingDirectory=RepositoryRoot,UseShellExecute=false,
            RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,
        };
        foreach(string Argument in new[]{
            "run","--project","tools/CSharpGameEngine.TestHost/CSharpGameEngine.TestHost.csproj",
            "--configuration",Configuration,"--","rendering-performance",
            "--configuration",Configuration,"--adapter",Adapter,"--output",RawPath,
        }){ StartInfo.ArgumentList.Add(Argument); }
        StartInfo.Environment["DOTNET_TieredCompilation"]="0";
        StartInfo.Environment["COMPlus_TieredCompilation"]="0";
        using var Process=new Process(){StartInfo=StartInfo};
        if(!Process.Start()){ throw new InvalidOperationException("Could not start rendering performance TestHost."); }
        Task<string>OutputTask=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        Task<string>ErrorTask=Process.StandardError.ReadToEndAsync(CancellationToken);
        try { await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false); }
        catch(OperationCanceledException){
            if(!Process.HasExited){ try { Process.Kill(true); } catch(InvalidOperationException){ } }
            throw;
        }
        return new ProcessResult(
            Process.ExitCode,await OutputTask.ConfigureAwait(false),await ErrorTask.ConfigureAwait(false));
    }

    private sealed record ProcessResult(int ExitCode,string StandardOutput,string StandardError);
}
