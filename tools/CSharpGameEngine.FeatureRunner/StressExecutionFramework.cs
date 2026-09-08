using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CSharpGameEngine.FeatureStages;

public enum StressOutcome { Passed,Failed,TimedOut,Cancelled }
public sealed record StressPlan(string Id,string Configuration,string Adapter,uint Seed,int TickCount,int TimeoutSeconds,IReadOnlyDictionary<string,string>Workload,string ReproductionCommand);
public sealed record StressTickResult(bool Passed,string StateHash,string EventHash,string?FailureSignature,string?Detail){
    public static StressTickResult Success(string StateHash,string EventHash)=>new(true,StateHash,EventHash,null,null);
    public static StressTickResult Failure(string StateHash,string EventHash,string Signature,string Detail)=>new(false,StateHash,EventHash,Signature,Detail);
}
public sealed record StressFailure(int Tick,string Signature,string Detail);
public sealed record StressExecutionResult(StressOutcome Outcome,int CompletedTicks,string StateHash,string EventHash,StressFailure?Failure,bool Passed);
public sealed record StressReportPayload(string Schema,DateTimeOffset CreatedUtc,StressPlan Plan,StressExecutionResult Result,PerformanceMachineFingerprint Machine,string Commit,bool IsDirty);
public sealed record StressReport(string Schema,string ReportSha256,DateTimeOffset CreatedUtc,StressPlan Plan,StressExecutionResult Result,PerformanceMachineFingerprint Machine,string Commit,bool IsDirty);

public static class StressExecutionFramework {
    public const int MaximumTimeoutSeconds=28800;

    public static async Task<StressExecutionResult>RunAsync(StressPlan Plan,Func<int,uint,CancellationToken,ValueTask<StressTickResult>>Workload,CancellationToken CancellationToken){
        ValidatePlan(Plan);
        ArgumentNullException.ThrowIfNull(Workload);
        using var Deadline=new CancellationTokenSource(TimeSpan.FromTicks((long)Plan.TimeoutSeconds*TimeSpan.TicksPerSecond));
        using var Linked=CancellationTokenSource.CreateLinkedTokenSource(CancellationToken,Deadline.Token);
        using IncrementalHash State=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using IncrementalHash Events=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int Completed=0;
        StressFailure?Failure=null;
        StressOutcome Outcome=StressOutcome.Passed;
        for(int Tick=0;Tick<Plan.TickCount;Tick++){
            StressTickResult TickResult;
            try { TickResult=await Workload(Tick,Plan.Seed,Linked.Token).ConfigureAwait(false); }
            catch(OperationCanceledException)when(Deadline.IsCancellationRequested&&!CancellationToken.IsCancellationRequested){ Outcome=StressOutcome.TimedOut;Failure=new StressFailure(Tick,"timeout",$"Stress workload exceeded {Plan.TimeoutSeconds} seconds.");break; }
            catch(OperationCanceledException)when(CancellationToken.IsCancellationRequested){ Outcome=StressOutcome.Cancelled;Failure=new StressFailure(Tick,"cancelled","Stress workload was externally cancelled.");break; }
            catch(Exception Ex){ Outcome=StressOutcome.Failed;Failure=new StressFailure(Tick,$"exception:{Ex.GetType().FullName}",Ex.Message);break; }
            if(CancellationToken.IsCancellationRequested){ Outcome=StressOutcome.Cancelled;Failure=new StressFailure(Tick,"cancelled","Stress workload was externally cancelled.");break; }
            if(Deadline.IsCancellationRequested){ Outcome=StressOutcome.TimedOut;Failure=new StressFailure(Tick,"timeout",$"Stress workload exceeded {Plan.TimeoutSeconds} seconds.");break; }
            try { ValidateTickResult(Tick,TickResult); }
            catch(InvalidDataException Ex){ Outcome=StressOutcome.Failed;Failure=new StressFailure(Tick,"workload-contract",Ex.Message);break; }
            Append(State,Tick,TickResult.StateHash);
            Append(Events,Tick,TickResult.EventHash);
            Completed++;
            if(!TickResult.Passed){ Outcome=StressOutcome.Failed;Failure=new StressFailure(Tick,TickResult.FailureSignature!,TickResult.Detail!);break; }
        }
        string StateHash=Convert.ToHexString(State.GetHashAndReset()).ToLowerInvariant();
        string EventHash=Convert.ToHexString(Events.GetHashAndReset()).ToLowerInvariant();
        bool Passed=Outcome==StressOutcome.Passed&&Completed==Plan.TickCount&&Failure is null;
        return new StressExecutionResult(Outcome,Completed,StateHash,EventHash,Failure,Passed);
    }

    public static void ValidatePlan(StressPlan Plan){
        ArgumentNullException.ThrowIfNull(Plan);
        if(string.IsNullOrWhiteSpace(Plan.Id)){ throw new ArgumentException("Stress plan ID is required.",nameof(Plan)); }
        if(Plan.Configuration is not("Debug" or "Release")){ throw new ArgumentException("Stress configuration must be Debug or Release.",nameof(Plan)); }
        if(Plan.Adapter is not("Warp" or "Hardware" or "None")){ throw new ArgumentException("Stress adapter must be Warp, Hardware, or None.",nameof(Plan)); }
        if(Plan.Seed==0){ throw new ArgumentOutOfRangeException(nameof(Plan),"Stress seed must be nonzero."); }
        if(Plan.TickCount<=0){ throw new ArgumentOutOfRangeException(nameof(Plan),"Stress tickCount must be positive."); }
        if(Plan.TimeoutSeconds<=0||Plan.TimeoutSeconds>MaximumTimeoutSeconds){ throw new ArgumentOutOfRangeException(nameof(Plan),$"Stress timeout must be between 1 and {MaximumTimeoutSeconds} seconds."); }
        if(Plan.Workload is null||Plan.Workload.Count==0||Plan.Workload.Any(Pair=>string.IsNullOrWhiteSpace(Pair.Key)||string.IsNullOrWhiteSpace(Pair.Value))){ throw new ArgumentException("Stress workload metadata must be explicit and non-empty.",nameof(Plan)); }
        if(!Plan.Workload.ContainsKey("faultInjection")){ throw new ArgumentException("Stress workload metadata must declare faultInjection.",nameof(Plan)); }
        if(string.IsNullOrWhiteSpace(Plan.ReproductionCommand)){ throw new ArgumentException("Stress reproduction command is required.",nameof(Plan)); }
    }

    private static void ValidateTickResult(int Tick,StressTickResult Result){
        ArgumentNullException.ThrowIfNull(Result);
        if(!IsSha256(Result.StateHash)||!IsSha256(Result.EventHash)){ throw new InvalidDataException($"Stress tick {Tick} must return canonical SHA-256 state and event hashes."); }
        if(Result.Passed&&(Result.FailureSignature is not null||Result.Detail is not null)){ throw new InvalidDataException($"Passing stress tick {Tick} cannot carry failure data."); }
        if(!Result.Passed&&(string.IsNullOrWhiteSpace(Result.FailureSignature)||string.IsNullOrWhiteSpace(Result.Detail))){ throw new InvalidDataException($"Failing stress tick {Tick} requires signature and detail."); }
    }

    private static void Append(IncrementalHash Hash,int Tick,string Value){
        Span<byte>TickBytes=stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(TickBytes,Tick);
        Hash.AppendData(TickBytes);
        Span<byte>HashBytes=stackalloc byte[64];
        for(int Index=0;Index<Value.Length;Index++){ HashBytes[Index]=(byte)Value[Index]; }
        Hash.AppendData(HashBytes);
    }
    private static bool IsSha256(string?Value)=>Value is {Length:64}&&Value.All(static Character=>(Character>='0'&&Character<='9')||(Character>='a'&&Character<='f'));
}

public static class StressReportStore {
    public const string Schema="csharp-game-engine-stress-v1";
    public static async Task<string>SaveAsync(string RepositoryRoot,StressReportPayload Payload,CancellationToken CancellationToken){
        JsonSerializerOptions Options=FeatureRunResultStore.CreateJsonOptions();
        string Hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Payload,Options))).ToLowerInvariant();
        var Report=new StressReport(Payload.Schema,Hash,Payload.CreatedUtc,Payload.Plan,Payload.Result,Payload.Machine,Payload.Commit,Payload.IsDirty);
        string DirectoryPath=Path.Combine(Path.GetFullPath(RepositoryRoot),".artifacts","Stress");
        Directory.CreateDirectory(DirectoryPath);
        string PathValue=Path.Combine(DirectoryPath,$"{Payload.CreatedUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.stress.json");
        string TemporaryPath=PathValue+".tmp";
        try {
            await File.WriteAllTextAsync(TemporaryPath,JsonSerializer.Serialize(Report,Options)+Environment.NewLine,new UTF8Encoding(false),CancellationToken).ConfigureAwait(false);
            File.Move(TemporaryPath,PathValue,true);
        } finally { if(File.Exists(TemporaryPath)){ File.Delete(TemporaryPath); } }
        return PathValue;
    }

    public static IReadOnlyList<string>Validate(string ReportPath){
        JsonSerializerOptions Options=FeatureRunResultStore.CreateJsonOptions();
        StressReport?Report;
        try { Report=JsonSerializer.Deserialize<StressReport>(File.ReadAllText(ReportPath),Options); }
        catch(Exception Ex)when(Ex is JsonException or IOException or UnauthorizedAccessException){ return [$"Stress report cannot be read: {Ex.Message}"]; }
        if(Report is null){ return ["Stress report is empty."]; }
        var Errors=new List<string>();
        if(Report.Schema!=Schema){ Errors.Add($"Stress report schema must be {Schema}."); }
        try { StressExecutionFramework.ValidatePlan(Report.Plan); }
        catch(ArgumentException Ex){ Errors.Add($"Stress report plan is invalid: {Ex.Message}"); }
        var Payload=new StressReportPayload(Report.Schema,Report.CreatedUtc,Report.Plan,Report.Result,Report.Machine,Report.Commit,Report.IsDirty);
        string Expected=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Payload,Options))).ToLowerInvariant();
        if(!string.Equals(Expected,Report.ReportSha256,StringComparison.OrdinalIgnoreCase)){ Errors.Add("Stress report SHA-256 mismatch."); }
        if(!string.Equals(PerformanceMeasurementFramework.HashFingerprint(Report.Machine),Report.Machine.FingerprintSha256,StringComparison.OrdinalIgnoreCase)){ Errors.Add("Stress machine fingerprint SHA-256 mismatch."); }
        if(Report.Result.CompletedTicks<0||Report.Result.CompletedTicks>Report.Plan.TickCount){ Errors.Add("Stress report completedTicks is outside the plan."); }
        if(Report.Result.Failure is { }Failure&&(Failure.Tick<0||Failure.Tick>=Report.Plan.TickCount)){ Errors.Add("Stress report failure tick is outside the plan."); }
        if(Report.Result.Failure is { }RecordedFailure){
            bool BoundaryMatches=Report.Result.Outcome is StressOutcome.TimedOut or StressOutcome.Cancelled
                ?Report.Result.CompletedTicks==RecordedFailure.Tick
                :Report.Result.CompletedTicks==RecordedFailure.Tick||Report.Result.CompletedTicks==RecordedFailure.Tick+1;
            if(!BoundaryMatches){ Errors.Add("Stress report completedTicks does not identify the first failure boundary."); }
        }
        bool ExpectedPass=Report.Result.Outcome==StressOutcome.Passed&&Report.Result.CompletedTicks==Report.Plan.TickCount&&Report.Result.Failure is null;
        if(Report.Result.Passed!=ExpectedPass){ Errors.Add("Stress report passed verdict contradicts outcome, ticks, or failure."); }
        if(Report.Result.Outcome!=StressOutcome.Passed&&Report.Result.Failure is null){ Errors.Add("Non-passing stress report requires a reproduction failure."); }
        if(!IsSha256(Report.Result.StateHash)||!IsSha256(Report.Result.EventHash)){ Errors.Add("Stress report aggregate hashes are invalid."); }
        return Errors;
    }
    private static bool IsSha256(string?Value)=>Value is {Length:64}&&Value.All(static Character=>(Character>='0'&&Character<='9')||(Character>='a'&&Character<='f'));
}

public static class StressFrameworkProbe {
    public static async Task<int>RunAsync(string RepositoryRoot,string Configuration,uint Seed,int Ticks,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        var Plan=new StressPlan("verification.framework-probe",Configuration,"None",Seed,Ticks,StressExecutionFramework.MaximumTimeoutSeconds,new SortedDictionary<string,string>(StringComparer.Ordinal){["algorithm"]="integer-state-v1",["faultInjection"]="none"},$"dotnet run --project tools/CSharpGameEngine.FeatureRunner/CSharpGameEngine.FeatureRunner.csproj --configuration {Configuration} -- verify-stress-contract --configuration {Configuration} --seed {Seed} --ticks {Ticks}");
        StressExecutionResult Result=await StressExecutionFramework.RunAsync(Plan,RunTick,CancellationToken).ConfigureAwait(false);
        (string Commit,bool IsDirty)Provenance;
        try { Provenance=await GitChangedPathProvider.ReadProvenanceAsync(RepositoryRoot,CancellationToken).ConfigureAwait(false); }
        catch(Exception Ex)when(Ex is not OperationCanceledException){ Provenance=("unknown",true);await Error.WriteLineAsync($"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false); }
        var Payload=new StressReportPayload(StressReportStore.Schema,DateTimeOffset.UtcNow,Plan,Result,PerformanceMeasurementFramework.CaptureMachineFingerprint(),Provenance.Commit,Provenance.IsDirty);
        string PathValue=await StressReportStore.SaveAsync(RepositoryRoot,Payload,CancellationToken).ConfigureAwait(false);
        IReadOnlyList<string>IntegrityErrors=StressReportStore.Validate(PathValue);
        await Output.WriteLineAsync($"outcome={Result.Outcome} ticks={Result.CompletedTicks}/{Ticks} state={Result.StateHash} events={Result.EventHash}").ConfigureAwait(false);
        await Output.WriteLineAsync(PathValue).ConfigureAwait(false);
        foreach(string IntegrityError in IntegrityErrors){ await Error.WriteLineAsync(IntegrityError).ConfigureAwait(false); }
        return Result.Passed&&IntegrityErrors.Count==0?0:1;
    }

    private static ValueTask<StressTickResult>RunTick(int Tick,uint Seed,CancellationToken CancellationToken){
        CancellationToken.ThrowIfCancellationRequested();
        Span<byte>Bytes=stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(Bytes,Tick);
        BinaryPrimitives.WriteUInt32LittleEndian(Bytes[4..],Seed);
        BinaryPrimitives.WriteUInt32LittleEndian(Bytes[8..],unchecked((uint)Tick*2654435761u)^Seed);
        string State=Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
        Bytes[0]^=0x5a;
        string Events=Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
        return ValueTask.FromResult(StressTickResult.Success(State,Events));
    }
}
