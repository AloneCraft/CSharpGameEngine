using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CSharpGameEngine.FeatureStages;

public enum PerformanceGateStatus { Passed,Failed,NotEvaluated,Unavailable }

public sealed record PerformanceSample(int Index,long ElapsedNanoseconds,long AllocatedBytes);
public sealed record PerformanceStatistics(long Minimum,long Median,long P95,long Mad,long Maximum);
public sealed record PerformanceAbsoluteBudget(long MaximumMedianNanoseconds,long MaximumP95Nanoseconds,long MaximumP95AllocatedBytes);
/// <summary>A comparison baseline. A null <see cref="P95Nanoseconds"/> compares the median only.</summary>
public sealed record PerformanceBaseline(long MedianNanoseconds,long? P95Nanoseconds,long P95AllocatedBytes,int MaximumRegressionBasisPoints);
public sealed record PerformanceGateResult(PerformanceGateStatus Status,string Detail);
public sealed record PerformanceAnalysis(PerformanceStatistics ElapsedNanoseconds,PerformanceStatistics AllocatedBytes,PerformanceGateResult Measurement,PerformanceGateResult AbsoluteBudget,PerformanceGateResult BaselineDelta,bool Passed);
public sealed record PerformanceMeasurementPlan(string Id,string Configuration,string Unit,IReadOnlyDictionary<string,string>Workload,int WarmupCount,int SampleCount,PerformanceAbsoluteBudget?AbsoluteBudget,PerformanceBaseline?Baseline);
public sealed record PerformanceMachineFingerprint(string FingerprintSha256,string OsDescription,string OsArchitecture,string ProcessArchitecture,string ProcessorIdentifier,int ProcessorCount,long AvailableMemoryBytes,string RuntimeDescription,long StopwatchFrequency,bool ServerGc,string GcLatencyMode);
public sealed record PerformanceReportPayload(string Schema,DateTimeOffset CreatedUtc,PerformanceMeasurementPlan Plan,IReadOnlyList<PerformanceSample>Samples,PerformanceAnalysis Analysis,PerformanceMachineFingerprint Machine,string Commit,bool IsDirty);
public sealed record PerformanceReport(string Schema,string ReportSha256,DateTimeOffset CreatedUtc,PerformanceMeasurementPlan Plan,IReadOnlyList<PerformanceSample>Samples,PerformanceAnalysis Analysis,PerformanceMachineFingerprint Machine,string Commit,bool IsDirty);

public static class PerformanceMeasurementFramework {
    public const int RequiredWarmupCount=5;
    public const int RequiredSampleCount=30;

    public static (IReadOnlyList<PerformanceSample>Samples,PerformanceAnalysis Analysis)Measure(PerformanceMeasurementPlan Plan,Action<int>Workload){
        ValidatePlan(Plan);
        ArgumentNullException.ThrowIfNull(Workload);
        for(int Index=0;Index<Plan.WarmupCount;Index++){ Workload(Index); }
        var Samples=new PerformanceSample[Plan.SampleCount];
        for(int Index=0;Index<Samples.Length;Index++){
            // Workloads are synchronous and execute on this thread. A process-wide counter would
            // attribute unrelated test/host allocations to the sample whenever the runner is
            // active in parallel, making the same workload pass or fail based on ambient work.
            long AllocationStart=GC.GetAllocatedBytesForCurrentThread();
            long TimestampStart=Stopwatch.GetTimestamp();
            Workload(Index);
            long TimestampEnd=Stopwatch.GetTimestamp();
            long AllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-AllocationStart;
            long ElapsedNanoseconds=ToNanoseconds(TimestampEnd-TimestampStart,Stopwatch.Frequency);
            Samples[Index]=new PerformanceSample(Index,ElapsedNanoseconds,AllocatedBytes);
        }
        return (Samples,Analyze(Plan,Samples));
    }

    public static PerformanceAnalysis Analyze(PerformanceMeasurementPlan Plan,IReadOnlyList<PerformanceSample>Samples){
        ValidatePlan(Plan);
        ArgumentNullException.ThrowIfNull(Samples);
        string?Unavailable=ValidateSamples(Plan,Samples);
        if(Unavailable is not null){
            var Empty=new PerformanceStatistics(0,0,0,0,0);
            var Result=new PerformanceGateResult(PerformanceGateStatus.Unavailable,Unavailable);
            return new PerformanceAnalysis(Empty,Empty,Result,Result,Result,false);
        }
        PerformanceStatistics Elapsed=Calculate(Samples.Select(static Sample=>Sample.ElapsedNanoseconds));
        PerformanceStatistics Allocated=Calculate(Samples.Select(static Sample=>Sample.AllocatedBytes));
        var Measurement=new PerformanceGateResult(PerformanceGateStatus.Passed,$"Captured {Samples.Count} post-warm-up samples.");
        PerformanceGateResult Absolute=EvaluateAbsolute(Plan.AbsoluteBudget,Elapsed,Allocated);
        PerformanceGateResult Baseline=EvaluateBaseline(Plan.Baseline,Elapsed,Allocated);
        bool Passed=(Absolute.Status is PerformanceGateStatus.Passed or PerformanceGateStatus.NotEvaluated)&&(Baseline.Status is PerformanceGateStatus.Passed or PerformanceGateStatus.NotEvaluated);
        return new PerformanceAnalysis(Elapsed,Allocated,Measurement,Absolute,Baseline,Passed);
    }

    public static PerformanceMachineFingerprint CaptureMachineFingerprint(){
        string Os=RuntimeInformation.OSDescription;
        string OsArchitecture=RuntimeInformation.OSArchitecture.ToString();
        string ProcessArchitecture=RuntimeInformation.ProcessArchitecture.ToString();
        string ProcessorIdentifier=Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")??"unknown";
        long AvailableMemoryBytes=GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        string Runtime=RuntimeInformation.FrameworkDescription;
        string Latency=GCSettings.LatencyMode.ToString();
        string Hash=HashFingerprint(Os,OsArchitecture,ProcessArchitecture,ProcessorIdentifier,Environment.ProcessorCount,AvailableMemoryBytes,Runtime,Stopwatch.Frequency,GCSettings.IsServerGC,Latency);
        return new PerformanceMachineFingerprint(Hash,Os,OsArchitecture,ProcessArchitecture,ProcessorIdentifier,Environment.ProcessorCount,AvailableMemoryBytes,Runtime,Stopwatch.Frequency,GCSettings.IsServerGC,Latency);
    }

    public static string HashFingerprint(PerformanceMachineFingerprint Fingerprint)=>HashFingerprint(Fingerprint.OsDescription,Fingerprint.OsArchitecture,Fingerprint.ProcessArchitecture,Fingerprint.ProcessorIdentifier,Fingerprint.ProcessorCount,Fingerprint.AvailableMemoryBytes,Fingerprint.RuntimeDescription,Fingerprint.StopwatchFrequency,Fingerprint.ServerGc,Fingerprint.GcLatencyMode);

    private static void ValidatePlan(PerformanceMeasurementPlan Plan){
        ArgumentNullException.ThrowIfNull(Plan);
        if(string.IsNullOrWhiteSpace(Plan.Id)){ throw new ArgumentException("Performance measurement ID is required.",nameof(Plan)); }
        if(Plan.Configuration is not("Debug" or "Release")){ throw new ArgumentException("Performance configuration must be Debug or Release.",nameof(Plan)); }
        if(Plan.Unit!="nanoseconds"){ throw new ArgumentException("Performance elapsed-time unit must be nanoseconds.",nameof(Plan)); }
        if(Plan.Workload is null||Plan.Workload.Count==0||Plan.Workload.Any(Pair=>string.IsNullOrWhiteSpace(Pair.Key)||string.IsNullOrWhiteSpace(Pair.Value))){ throw new ArgumentException("Performance workload metadata must be explicit and non-empty.",nameof(Plan)); }
        if(Plan.WarmupCount!=RequiredWarmupCount||Plan.SampleCount!=RequiredSampleCount){ throw new ArgumentException($"Performance measurements require exactly {RequiredWarmupCount} warm-ups and {RequiredSampleCount} samples.",nameof(Plan)); }
        if(Plan.AbsoluteBudget is { }Absolute&&(Absolute.MaximumMedianNanoseconds<0||Absolute.MaximumP95Nanoseconds<0||Absolute.MaximumP95AllocatedBytes<0)){ throw new ArgumentOutOfRangeException(nameof(Plan),"Absolute budgets cannot be negative."); }
        if(Plan.Baseline is { }Baseline&&(Baseline.MedianNanoseconds<0||Baseline.P95Nanoseconds<0||Baseline.P95AllocatedBytes<0||Baseline.MaximumRegressionBasisPoints<0)){ throw new ArgumentOutOfRangeException(nameof(Plan),"Baseline values cannot be negative."); }
    }

    private static string?ValidateSamples(PerformanceMeasurementPlan Plan,IReadOnlyList<PerformanceSample>Samples){
        if(Samples.Count!=Plan.SampleCount){ return $"Expected {Plan.SampleCount} samples, observed {Samples.Count}."; }
        var Indices=new HashSet<int>();
        foreach(PerformanceSample Sample in Samples){
            if(!Indices.Add(Sample.Index)){ return $"Duplicate sample index {Sample.Index}."; }
            if(Sample.ElapsedNanoseconds<=0){ return $"Sample {Sample.Index} has no measurable elapsed time."; }
            if(Sample.AllocatedBytes<0){ return $"Sample {Sample.Index} has a negative allocation count."; }
        }
        return null;
    }

    private static PerformanceStatistics Calculate(IEnumerable<long>Values){
        long[]Sorted=[.. Values.Order()];
        long Median=Midpoint(Sorted[(Sorted.Length-1)/2],Sorted[Sorted.Length/2]);
        int P95Index=(Sorted.Length*95+99)/100-1;
        long[]Deviations=[.. Sorted.Select(Value=>Value>=Median?Value-Median:Median-Value).Order()];
        long Mad=Midpoint(Deviations[(Deviations.Length-1)/2],Deviations[Deviations.Length/2]);
        return new PerformanceStatistics(Sorted[0],Median,Sorted[P95Index],Mad,Sorted[^1]);
    }

    private static PerformanceGateResult EvaluateAbsolute(PerformanceAbsoluteBudget?Budget,PerformanceStatistics Elapsed,PerformanceStatistics Allocated){
        if(Budget is null){ return new PerformanceGateResult(PerformanceGateStatus.NotEvaluated,"No absolute budget was configured."); }
        var Failures=new List<string>();
        if(Elapsed.Median>Budget.MaximumMedianNanoseconds){ Failures.Add($"median {Elapsed.Median}>{Budget.MaximumMedianNanoseconds}"); }
        if(Elapsed.P95>Budget.MaximumP95Nanoseconds){ Failures.Add($"p95 {Elapsed.P95}>{Budget.MaximumP95Nanoseconds}"); }
        if(Allocated.P95>Budget.MaximumP95AllocatedBytes){ Failures.Add($"allocation p95 {Allocated.P95}>{Budget.MaximumP95AllocatedBytes}"); }
        return Failures.Count==0?new PerformanceGateResult(PerformanceGateStatus.Passed,"Absolute budget passed."):new PerformanceGateResult(PerformanceGateStatus.Failed,$"Absolute budget failed: {string.Join(", ",Failures)}.");
    }

    private static PerformanceGateResult EvaluateBaseline(PerformanceBaseline?Baseline,PerformanceStatistics Elapsed,PerformanceStatistics Allocated){
        if(Baseline is null){ return new PerformanceGateResult(PerformanceGateStatus.NotEvaluated,"No comparison baseline was configured."); }
        var Failures=new List<string>();
        if(IsRegression(Elapsed.Median,Baseline.MedianNanoseconds,Baseline.MaximumRegressionBasisPoints)){ Failures.Add("median"); }
        if(Baseline.P95Nanoseconds is { }P95&&IsRegression(Elapsed.P95,P95,Baseline.MaximumRegressionBasisPoints)){ Failures.Add("p95"); }
        if(IsRegression(Allocated.P95,Baseline.P95AllocatedBytes,Baseline.MaximumRegressionBasisPoints)){ Failures.Add("allocation p95"); }
        return Failures.Count==0?new PerformanceGateResult(PerformanceGateStatus.Passed,"Baseline delta passed."):new PerformanceGateResult(PerformanceGateStatus.Failed,$"Baseline regression exceeded {Baseline.MaximumRegressionBasisPoints} basis points for {string.Join(", ",Failures)}.");
    }

    private static bool IsRegression(long Current,long Baseline,int MaximumRegressionBasisPoints)=>(Int128)Current*10000>(Int128)Baseline*((Int128)10000+MaximumRegressionBasisPoints);
    private static long Midpoint(long Lower,long Upper)=>Lower+(Upper-Lower)/2;
    private static long ToNanoseconds(long CounterDelta,long Frequency){
        if(CounterDelta<=0||Frequency<=0){ return 0; }
        Int128 Value=(Int128)CounterDelta*1_000_000_000/Frequency;
        return Value>long.MaxValue?long.MaxValue:(long)Value;
    }
    private static string HashFingerprint(string Os,string OsArchitecture,string ProcessArchitecture,string ProcessorIdentifier,int ProcessorCount,long AvailableMemoryBytes,string Runtime,long Frequency,bool ServerGc,string Latency){
        string Source=string.Join((char)10,Os,OsArchitecture,ProcessArchitecture,ProcessorIdentifier,ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),AvailableMemoryBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),Runtime,Frequency.ToString(System.Globalization.CultureInfo.InvariantCulture),ServerGc.ToString(),Latency);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Source))).ToLowerInvariant();
    }
}

public static class PerformanceReportStore {
    public static async Task<string>SaveAsync(string RepositoryRoot,PerformanceReportPayload Payload,CancellationToken CancellationToken){
        JsonSerializerOptions Options=FeatureRunResultStore.CreateJsonOptions();
        byte[]PayloadBytes=JsonSerializer.SerializeToUtf8Bytes(Payload,Options);
        string Hash=Convert.ToHexString(SHA256.HashData(PayloadBytes)).ToLowerInvariant();
        var Report=new PerformanceReport(Payload.Schema,Hash,Payload.CreatedUtc,Payload.Plan,Payload.Samples,Payload.Analysis,Payload.Machine,Payload.Commit,Payload.IsDirty);
        string DirectoryPath=Path.Combine(Path.GetFullPath(RepositoryRoot),".artifacts","Performance");
        Directory.CreateDirectory(DirectoryPath);
        string PathValue=Path.Combine(DirectoryPath,$"{Payload.CreatedUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.performance.json");
        string TemporaryPath=PathValue+".tmp";
        try {
            await File.WriteAllTextAsync(TemporaryPath,JsonSerializer.Serialize(Report,Options)+Environment.NewLine,new UTF8Encoding(false),CancellationToken).ConfigureAwait(false);
            File.Move(TemporaryPath,PathValue,true);
        } finally { if(File.Exists(TemporaryPath)){ File.Delete(TemporaryPath); } }
        return PathValue;
    }

    public static IReadOnlyList<string>Validate(string ReportPath){
        var Errors=new List<string>();
        JsonSerializerOptions Options=FeatureRunResultStore.CreateJsonOptions();
        PerformanceReport?Report;
        try { Report=JsonSerializer.Deserialize<PerformanceReport>(File.ReadAllText(ReportPath),Options); }
        catch(Exception Ex)when(Ex is JsonException or IOException or UnauthorizedAccessException){ return [$"Performance report cannot be read: {Ex.Message}"]; }
        if(Report is null){ return ["Performance report is empty."]; }
        if(Report.Schema!=PerformanceFrameworkProbe.ReportSchema){ Errors.Add($"Performance report schema must be {PerformanceFrameworkProbe.ReportSchema}."); }
        var Payload=new PerformanceReportPayload(Report.Schema,Report.CreatedUtc,Report.Plan,Report.Samples,Report.Analysis,Report.Machine,Report.Commit,Report.IsDirty);
        string ExpectedHash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Payload,Options))).ToLowerInvariant();
        if(!string.Equals(ExpectedHash,Report.ReportSha256,StringComparison.OrdinalIgnoreCase)){ Errors.Add("Performance report SHA-256 mismatch."); }
        if(!string.Equals(PerformanceMeasurementFramework.HashFingerprint(Report.Machine),Report.Machine.FingerprintSha256,StringComparison.OrdinalIgnoreCase)){ Errors.Add("Performance machine fingerprint SHA-256 mismatch."); }
        try {
            PerformanceAnalysis Expected=PerformanceMeasurementFramework.Analyze(Report.Plan,Report.Samples);
            if(Expected!=Report.Analysis){ Errors.Add("Performance report statistics or gate verdict do not match its samples and plan."); }
        } catch(Exception Ex)when(Ex is ArgumentException or ArgumentOutOfRangeException){ Errors.Add($"Performance report plan is invalid: {Ex.Message}"); }
        return Errors;
    }
}

public static class PerformanceFrameworkProbe {
    public const string ReportSchema="csharp-game-engine-performance-v1";
    private static long Sink;

    public static async Task<int>RunAsync(string RepositoryRoot,string Configuration,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        var Plan=new PerformanceMeasurementPlan(
            "verification.framework-probe",Configuration,"nanoseconds",
            new SortedDictionary<string,string>(StringComparer.Ordinal){["algorithm"]="integer-mix-v1",["operationsPerSample"]="250000"},
            PerformanceMeasurementFramework.RequiredWarmupCount,PerformanceMeasurementFramework.RequiredSampleCount,
            new PerformanceAbsoluteBudget(1_000_000_000,2_000_000_000,4096),null);
        (IReadOnlyList<PerformanceSample>Samples,PerformanceAnalysis Analysis)=PerformanceMeasurementFramework.Measure(Plan,RunWorkload);
        (string Commit,bool IsDirty)Provenance;
        try { Provenance=await GitChangedPathProvider.ReadProvenanceAsync(RepositoryRoot,CancellationToken).ConfigureAwait(false); }
        catch(Exception Ex)when(Ex is not OperationCanceledException){ Provenance=("unknown",true);await Error.WriteLineAsync($"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false); }
        var CreatedUtc=DateTimeOffset.UtcNow;
        var Machine=PerformanceMeasurementFramework.CaptureMachineFingerprint();
        var Payload=new PerformanceReportPayload(ReportSchema,CreatedUtc,Plan,Samples,Analysis,Machine,Provenance.Commit,Provenance.IsDirty);
        string ReportPath=await PerformanceReportStore.SaveAsync(RepositoryRoot,Payload,CancellationToken).ConfigureAwait(false);
        IReadOnlyList<string>IntegrityErrors=PerformanceReportStore.Validate(ReportPath);
        await Output.WriteLineAsync($"samples={Samples.Count} medianNs={Analysis.ElapsedNanoseconds.Median} p95Ns={Analysis.ElapsedNanoseconds.P95} madNs={Analysis.ElapsedNanoseconds.Mad} allocationP95={Analysis.AllocatedBytes.P95}").ConfigureAwait(false);
        await Output.WriteLineAsync(ReportPath).ConfigureAwait(false);
        foreach(string IntegrityError in IntegrityErrors){ await Error.WriteLineAsync(IntegrityError).ConfigureAwait(false); }
        if(!Analysis.Passed){ await Error.WriteLineAsync($"Performance framework probe failed: {Analysis.AbsoluteBudget.Detail} {Analysis.BaselineDelta.Detail}").ConfigureAwait(false); }
        return Analysis.Passed&&IntegrityErrors.Count==0?0:1;
    }

    private static void RunWorkload(int SampleIndex){
        long Value=SampleIndex+1;
        for(int Index=0;Index<250000;Index++){ Value=unchecked((Value*6364136223846793005L)^Index); }
        Volatile.Write(ref Sink,Value);
    }

}
