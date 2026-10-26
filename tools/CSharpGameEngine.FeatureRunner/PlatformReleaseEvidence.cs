using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CSharpGameEngine.FeatureStages;

public sealed record PlatformReleaseInput(string Role,string Path,string Sha256,long ByteLength);
public sealed record PlatformReleaseModes(string Timestamp,string Input,string Audio,string Graphics);
public sealed record PlatformReleaseOptionalEvidence(string Id,string Owner,string Status,string?Evidence,string?ObservedAtUtc,string?ExpiresAtUtc);
public sealed record PlatformReleaseStageEvidence(string ReportPath,string FileSha256,string ResultArtifactSha256,DateTimeOffset FinishedUtc,string Profile,string Adapter);
public sealed record PlatformReleasePayload(string Schema,DateTimeOffset CreatedUtc,string Configuration,string Commit,bool IsDirty,string TargetOs,string MinimumOsVersion,string Architecture,PlatformReleaseModes RequiredModes,PerformanceMachineFingerprint Machine,PlatformReleaseStageEvidence Compatibility,IReadOnlyList<PlatformReleaseOptionalEvidence>OptionalHardware,IReadOnlyList<PlatformReleaseInput>Inputs);
public sealed record PlatformReleaseReport(string Schema,string ReportSha256,DateTimeOffset CreatedUtc,string Configuration,string Commit,bool IsDirty,string TargetOs,string MinimumOsVersion,string Architecture,PlatformReleaseModes RequiredModes,PerformanceMachineFingerprint Machine,PlatformReleaseStageEvidence Compatibility,IReadOnlyList<PlatformReleaseOptionalEvidence>OptionalHardware,IReadOnlyList<PlatformReleaseInput>Inputs);

/// <summary>
/// 既存の Platform の事実をトラック単位の 1 つのレポートにまとめる。後続のリリースステージで使う証拠であり、別のパッケージ、デバイス所有者、製品全体のリリースマニフェストではない。
/// </summary>
public static class PlatformReleaseEvidence {
    public const string ReportSchema="csharp-game-engine-platform-release-v1";
    public const string ReportFileName="platform-release.json";
    private static readonly TimeSpan MaximumCompatibilityAge=TimeSpan.FromHours(2);
    private static readonly PlatformReleaseModes RequiredModes=new("QpcStopwatchCounter","SyntheticWin32Events","Silent","Warp");
    private static readonly InputDefinition[]InputDefinitions=[
        new("platform.matrix","Docs/Verification/Platform-Compatibility-Matrix.json"),
        new("ci.matrix","Docs/Verification/CI-Matrix.json"),
        new("ci.workflow",".github/workflows/ci.yml"),
        new("manual.ledger","Docs/Verification/Manual-Evidence.json"),
        new("toolchain","global.json"),
        new("input.contract","src/CSharpGameEngine.Core/InputContracts.cs"),
        new("timestamp.input-adapter","src/CSharpGameEngine.Windows/Platform/Win32InputEventAdapter.cs"),
        new("input.server","src/CSharpGameEngine.Windows/Input/InputServer.cs"),
        new("audio.contract","src/CSharpGameEngine.Core/AudioContracts.cs"),
        new("audio.mixer","src/CSharpGameEngine.Audio/AudioMixer.cs"),
        new("audio.spatial","src/CSharpGameEngine.Audio/AudioSpatialization.cs"),
        new("audio.device","src/CSharpGameEngine.Windows/Audio/AudioDeviceServer.cs"),
        new("runtime.host","src/CSharpGameEngine.Runtime.Windows/EngineHost.cs"),
        new("compatibility.validator","tools/CSharpGameEngine.FeatureRunner/PlatformCompatibilityMatrixValidator.cs"),
        new("release.evidence","tools/CSharpGameEngine.FeatureRunner/PlatformReleaseEvidence.cs"),
        new("stage.catalog","tools/CSharpGameEngine.FeatureRunner/FeatureStageCatalog.cs"),
    ];
    private static readonly string[]OptionalEvidenceIds=["hardware-audio","hardware-d3d12"];
    private static readonly JsonSerializerOptions JsonOptions=FeatureRunResultStore.CreateJsonOptions();

    public static IEnumerable<string>RequiredRepositoryInputPaths=>InputDefinitions.Select(static Definition=>Definition.Path);

    public static async Task<int>RunAsync(string RepositoryRoot,string Configuration,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        string Root=Path.GetFullPath(RepositoryRoot);
        ValidateConfiguration(Configuration);
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();
        IReadOnlyList<string>ContractErrors=PlatformCompatibilityMatrixValidator.Validate(Root,Catalog);
        if(ContractErrors.Count!=0){
            foreach(string ContractError in ContractErrors){ await Error.WriteLineAsync(ContractError).ConfigureAwait(false); }
            return 1;
        }
        DateTimeOffset CreatedUtc=DateTimeOffset.UtcNow;
        (string Commit,bool IsDirty)Provenance;
        try { Provenance=await GitChangedPathProvider.ReadProvenanceAsync(Root,CancellationToken).ConfigureAwait(false); }
        catch(Exception Ex)when(Ex is not OperationCanceledException){
            Provenance=("unknown",true);
            await Error.WriteLineAsync($"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false);
        }
        string EvidenceRoot=Path.Combine(Root,".artifacts","PlatformRelease",$"{CreatedUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        string ReportPath=Path.Combine(EvidenceRoot,ReportFileName);
        bool Completed=false;
        try {
            PlatformReleaseStageEvidence Stage=FindCompatibilityEvidence(Root,Configuration,Provenance,CreatedUtc,Catalog);
            PlatformReleaseInput[]Inputs=ReadInputs(Root);
            PlatformReleaseOptionalEvidence[]Optional=ReadOptionalEvidence(Root);
            PerformanceMachineFingerprint Machine=PerformanceMeasurementFramework.CaptureMachineFingerprint();
            var Payload=new PlatformReleasePayload(ReportSchema,CreatedUtc,Configuration,Provenance.Commit,Provenance.IsDirty,"Windows","10.0.19041.0","x64",RequiredModes,Machine,Stage,Optional,Inputs);
            string Hash=Sha256(JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions));
            var Report=new PlatformReleaseReport(Payload.Schema,Hash,Payload.CreatedUtc,Payload.Configuration,Payload.Commit,Payload.IsDirty,Payload.TargetOs,Payload.MinimumOsVersion,Payload.Architecture,Payload.RequiredModes,Payload.Machine,Payload.Compatibility,Payload.OptionalHardware,Payload.Inputs);
            Directory.CreateDirectory(EvidenceRoot);
            await WriteCanonicalAsync(ReportPath,Report,CancellationToken).ConfigureAwait(false);
            IReadOnlyList<string>Errors=await ValidateAsync(Root,ReportPath,CancellationToken).ConfigureAwait(false);
            foreach(string ValidationError in Errors){ await Error.WriteLineAsync(ValidationError).ConfigureAwait(false); }
            if(Errors.Count!=0){ return 1; }
            Completed=true;
            await Output.WriteLineAsync($"platformRelease inputs={Inputs.Length} requiredModes=4 optionalHardware={Optional.Length} machine={Machine.FingerprintSha256}").ConfigureAwait(false);
            await Output.WriteLineAsync(ReportPath).ConfigureAwait(false);
            return 0;
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            await Error.WriteLineAsync($"Platform release verification failed: {Ex.Message}").ConfigureAwait(false);
            return 1;
        } finally {
            if(!Completed&&Directory.Exists(EvidenceRoot)){ Directory.Delete(EvidenceRoot,true); }
        }
    }

    public static async Task<IReadOnlyList<string>>ValidateAsync(string RepositoryRoot,string ReportPath,CancellationToken CancellationToken=default){
        var Errors=new List<string>();
        string Root;
        string FullReportPath;
        byte[]Bytes;
        PlatformReleaseReport?Report;
        try {
            Root=Path.GetFullPath(RepositoryRoot);
            FullReportPath=Path.GetFullPath(ReportPath);
            if(!IsContained(Path.Combine(Root,".artifacts","PlatformRelease"),FullReportPath)){ return ["Platform release report must be inside .artifacts/PlatformRelease."]; }
            EnsureNoReparsePoints(Root,FullReportPath);
            Bytes=await File.ReadAllBytesAsync(FullReportPath,CancellationToken).ConfigureAwait(false);
            Report=JsonSerializer.Deserialize<PlatformReleaseReport>(Bytes,JsonOptions);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){ return [$"Platform release report cannot be read: {Ex.Message}"]; }
        if(Report is null){ return ["Platform release report is empty."]; }
        try {
            ValidateConfiguration(Report.Configuration);
            if(!Bytes.AsSpan().SequenceEqual(CanonicalBytes(Report))){ Errors.Add("Platform release report is not canonically encoded."); }
            if(Report.Schema!=ReportSchema){ Errors.Add($"Platform release report schema must be {ReportSchema}."); }
            var Payload=new PlatformReleasePayload(Report.Schema,Report.CreatedUtc,Report.Configuration,Report.Commit,Report.IsDirty,Report.TargetOs,Report.MinimumOsVersion,Report.Architecture,Report.RequiredModes,Report.Machine,Report.Compatibility,Report.OptionalHardware,Report.Inputs);
            if(!FixedHashEquals(Sha256(JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions)),Report.ReportSha256)){ Errors.Add("Platform release report SHA-256 mismatch."); }
            if(Report.TargetOs!="Windows"||Report.MinimumOsVersion!="10.0.19041.0"||Report.Architecture!="x64"){ Errors.Add("Platform release target must be Windows 10.0.19041.0+ x64."); }
            if(Report.RequiredModes!=RequiredModes){ Errors.Add("Platform release required adapter modes are not canonical."); }
            if(!FixedHashEquals(PerformanceMeasurementFramework.HashFingerprint(Report.Machine),Report.Machine.FingerprintSha256)){ Errors.Add("Platform release machine fingerprint mismatch."); }
            if(Report.Machine.OsArchitecture!="X64"||Report.Machine.ProcessArchitecture!="X64"){ Errors.Add("Platform release evidence must be captured by an x64 OS and process."); }
            foreach(string ContractError in PlatformCompatibilityMatrixValidator.Validate(Root,FeatureStageCatalog.CreateDefault())){ Errors.Add($"Platform compatibility: {ContractError}"); }
            ValidateInputs(Root,Report.Inputs,Errors);
            ValidateOptionalEvidence(Root,Report.OptionalHardware,Errors);
            ValidateCompatibilityEvidence(Root,Report,Errors);
            string[]Files=Directory.EnumerateFiles(Path.GetDirectoryName(FullReportPath)!,"*",SearchOption.AllDirectories).ToArray();
            if(Files.Length!=1||!string.Equals(Path.GetFullPath(Files[0]),FullReportPath,StringComparison.OrdinalIgnoreCase)){ Errors.Add("Platform release evidence directory contains missing or extra files."); }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){ Errors.Add($"Platform release evidence validation failed: {Ex.Message}"); }
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static PlatformReleaseStageEvidence FindCompatibilityEvidence(string Root,string Configuration,(string Commit,bool IsDirty)Provenance,DateTimeOffset CreatedUtc,FeatureStageCatalog Catalog){
        string StageRoot=Path.Combine(Root,".artifacts","FeatureStages");
        if(!Directory.Exists(StageRoot)){ throw new InvalidDataException("Platform release requires a recent passing platform.compatibility Stage result."); }
        var Candidates=new List<(string Path,FeatureStageRunResult Result)>();
        foreach(string PathValue in Directory.EnumerateFiles(StageRoot,"*.run-result.json",SearchOption.TopDirectoryOnly)){
            try {
                EnsureNoReparsePoints(Root,PathValue);
                FeatureRunResultDocument?Document=JsonSerializer.Deserialize<FeatureRunResultDocument>(File.ReadAllBytes(PathValue),JsonOptions);
                if(Document?.SchemaVersion!=FeatureRunResultDocument.CurrentSchemaVersion){ continue; }
                Candidates.AddRange(Document.Results.Where(Result=>Result.StageId=="platform.compatibility"&&Result.Configuration==Configuration&&Result.Profile=="platform-release-prerequisite"&&Result.Status==FeatureStageRunStatus.Passed&&Result.Adapter=="Warp"&&Result.GitCommit==Provenance.Commit&&Result.IsDirty==Provenance.IsDirty&&Result.FinishedUtc<=CreatedUtc&&CreatedUtc-Result.FinishedUtc<=MaximumCompatibilityAge&&ValidateStageResult(Result,Catalog).Count==0).Select(Result=>(PathValue,Result)));
            } catch(Exception Ex)when(Ex is not OperationCanceledException&&Ex is not OutOfMemoryException&&Ex is not StackOverflowException){ continue; }
        }
        (string Path,FeatureStageRunResult Result)Match=Candidates.OrderByDescending(static Candidate=>Candidate.Result.FinishedUtc).ThenByDescending(static Candidate=>Candidate.Path,StringComparer.Ordinal).FirstOrDefault();
        if(Match.Result is null){ throw new InvalidDataException($"Platform release requires a recent passing platform.compatibility result for {Configuration} with platform-release-prerequisite profile, matching Git provenance, and Warp adapter."); }
        return new PlatformReleaseStageEvidence(Relative(Root,Match.Path),Sha256(File.ReadAllBytes(Match.Path)),Match.Result.ArtifactSha256,Match.Result.FinishedUtc,Match.Result.Profile,Match.Result.Adapter);
    }

    private static void ValidateCompatibilityEvidence(string Root,PlatformReleaseReport Report,List<string>Errors){
        try {
            string PathValue=ResolveRelative(Root,Report.Compatibility.ReportPath);
            if(!IsContained(Path.Combine(Root,".artifacts","FeatureStages"),PathValue)){ throw new InvalidDataException("Compatibility result escaped .artifacts/FeatureStages."); }
            EnsureNoReparsePoints(Root,PathValue);
            byte[]Bytes=File.ReadAllBytes(PathValue);
            if(!FixedHashEquals(Sha256(Bytes),Report.Compatibility.FileSha256)){ Errors.Add("Platform compatibility result file SHA-256 mismatch.");return; }
            FeatureRunResultDocument?Document=JsonSerializer.Deserialize<FeatureRunResultDocument>(Bytes,JsonOptions);
            if(Document?.SchemaVersion!=FeatureRunResultDocument.CurrentSchemaVersion){ Errors.Add("Platform compatibility result schema mismatch.");return; }
            FeatureStageRunResult?Result=Document.Results.FirstOrDefault(Result=>Result.StageId=="platform.compatibility"&&Result.ArtifactSha256==Report.Compatibility.ResultArtifactSha256);
            if(Result is null){ Errors.Add("Bound platform compatibility result is missing.");return; }
            Errors.AddRange(ValidateStageResult(Result,FeatureStageCatalog.CreateDefault()));
            if(Result.Configuration!=Report.Configuration||Result.GitCommit!=Report.Commit||Result.IsDirty!=Report.IsDirty||Result.FinishedUtc!=Report.Compatibility.FinishedUtc||Result.Profile!="platform-release-prerequisite"||Result.Profile!=Report.Compatibility.Profile||Result.Adapter!=Report.Compatibility.Adapter||Result.FinishedUtc>Report.CreatedUtc||Report.CreatedUtc-Result.FinishedUtc>MaximumCompatibilityAge){ Errors.Add("Platform compatibility result provenance does not match the release report."); }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){ Errors.Add($"Platform compatibility result cannot be validated: {Ex.Message}"); }
    }

    private static IReadOnlyList<string>ValidateStageResult(FeatureStageRunResult Result,FeatureStageCatalog Catalog){
        var Errors=new List<string>();
        FeatureStageDefinition Stage=Catalog.GetStage("platform.compatibility");
        if(Result.Status!=FeatureStageRunStatus.Passed||Result.Pack!=FeatureStagePack.Foundation.ToString()||Result.Adapter!="Warp"||Result.FeatureIds.Count!=1||Result.FeatureIds[0]!="F-L7-034"||Result.Errors.Count!=0||Result.Assertions.Count!=Stage.Commands.Count||Result.Assertions.Any(static Assertion=>!Assertion.Passed)){ Errors.Add("Bound platform.compatibility result is not a complete passing Foundation/Warp verdict."); }
        if(!FixedHashEquals(ComputeStageArtifactHash(Result),Result.ArtifactSha256)){ Errors.Add("Bound platform.compatibility result artifact SHA-256 mismatch."); }
        if(Result.FinishedUtc<Result.StartedUtc||Result.DurationMilliseconds<0){ Errors.Add("Bound platform.compatibility result has invalid timing."); }
        return Errors;
    }

    private static PlatformReleaseInput[]ReadInputs(string Root)=>[.. InputDefinitions.Select(Definition=>{
        string PathValue=ResolveRelative(Root,Definition.Path);
        EnsureNoReparsePoints(Root,PathValue);
        byte[]Bytes=File.ReadAllBytes(PathValue);
        return new PlatformReleaseInput(Definition.Role,Definition.Path,Sha256(Bytes),Bytes.LongLength);
    })];

    private static void ValidateInputs(string Root,IReadOnlyList<PlatformReleaseInput>?Inputs,List<string>Errors){
        if(Inputs is null||Inputs.Count!=InputDefinitions.Length){ Errors.Add("Platform release input closure is incomplete.");return; }
        for(int Index=0;Index<InputDefinitions.Length;Index++){
            InputDefinition Definition=InputDefinitions[Index];
            PlatformReleaseInput Entry=Inputs[Index];
            if(Entry.Role!=Definition.Role||Entry.Path!=Definition.Path){ Errors.Add($"Platform release input {Index} is not canonical.");continue; }
            try {
                string PathValue=ResolveRelative(Root,Entry.Path);
                EnsureNoReparsePoints(Root,PathValue);
                byte[]Bytes=File.ReadAllBytes(PathValue);
                if(Entry.ByteLength!=Bytes.LongLength||!FixedHashEquals(Sha256(Bytes),Entry.Sha256)){ Errors.Add($"Platform release input changed: {Entry.Path}."); }
            } catch(Exception Ex)when(Ex is not OperationCanceledException){ Errors.Add($"Platform release input cannot be validated: {Entry.Path}: {Ex.Message}"); }
        }
    }

    private static PlatformReleaseOptionalEvidence[]ReadOptionalEvidence(string Root){
        ManualDocument Document=JsonSerializer.Deserialize<ManualDocument>(File.ReadAllBytes(Path.Combine(Root,"Docs","Verification","Manual-Evidence.json")),JsonOptions)??throw new InvalidDataException("Manual evidence ledger is empty.");
        return [.. OptionalEvidenceIds.Select(Id=>{
            ManualEntry Entry=Document.Entries.Single(Entry=>Entry.Id==Id);
            return new PlatformReleaseOptionalEvidence(Entry.Id,Entry.Owner,Entry.Status,Entry.Evidence,Entry.ObservedAtUtc,Entry.ExpiresAtUtc);
        })];
    }

    private static void ValidateOptionalEvidence(string Root,IReadOnlyList<PlatformReleaseOptionalEvidence>?Evidence,List<string>Errors){
        if(Evidence is null||Evidence.Count!=OptionalEvidenceIds.Length){ Errors.Add("Platform release optional hardware evidence is incomplete.");return; }
        try { if(!Evidence.SequenceEqual(ReadOptionalEvidence(Root))){ Errors.Add("Platform release optional hardware evidence no longer matches the manual ledger."); } }
        catch(Exception Ex)when(Ex is not OperationCanceledException){ Errors.Add($"Platform release optional hardware evidence cannot be validated: {Ex.Message}"); }
    }

    private static async Task WriteCanonicalAsync(string PathValue,PlatformReleaseReport Report,CancellationToken CancellationToken){
        string Temporary=PathValue+".tmp";
        try { await File.WriteAllBytesAsync(Temporary,CanonicalBytes(Report),CancellationToken).ConfigureAwait(false);File.Move(Temporary,PathValue,true); }
        finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
    }
    private static byte[]CanonicalBytes(PlatformReleaseReport Report){
        byte[]Json=JsonSerializer.SerializeToUtf8Bytes(Report,JsonOptions);
        byte[]Result=new byte[Json.Length+1];Json.CopyTo(Result,0);Result[^1]=(byte)'\n';return Result;
    }
    private static string ComputeStageArtifactHash(FeatureStageRunResult Result){
        string Evidence=string.Join("\n",[Result.StageId,Result.Status.ToString(),Result.Summary,Result.StateHash??string.Empty,Result.EventHash??string.Empty,Result.StandardOutput,Result.StandardError,.. Result.Assertions.Select(static Assertion=>$"{Assertion.Name}:{Assertion.Passed}:{Assertion.Details}"),.. Result.Errors]);
        return Sha256(Encoding.UTF8.GetBytes(Evidence));
    }
    private static string ResolveRelative(string Root,string RelativePath){
        if(string.IsNullOrWhiteSpace(RelativePath)||Path.IsPathRooted(RelativePath)||RelativePath.Contains('\\')){ throw new InvalidDataException("Evidence path must be a canonical forward-slash relative path."); }
        string[]Segments=RelativePath.Split('/');
        if(Segments.Any(static Segment=>Segment.Length==0||Segment is "." or "..")){ throw new InvalidDataException("Evidence path contains an invalid segment."); }
        string FullPath=Path.GetFullPath(Path.Combine(Path.GetFullPath(Root),RelativePath.Replace('/',Path.DirectorySeparatorChar)));
        if(!IsContained(Root,FullPath)){ throw new InvalidDataException("Evidence path escaped its root."); }
        return FullPath;
    }
    private static bool IsContained(string Root,string Candidate){
        string FullRoot=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        return Path.GetFullPath(Candidate).StartsWith(FullRoot,StringComparison.OrdinalIgnoreCase);
    }
    private static void EnsureNoReparsePoints(string Root,string PathValue){
        string FullRoot=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
        string Current=Path.GetFullPath(PathValue);
        if(!IsContained(FullRoot,Current)&&!string.Equals(FullRoot,Current,StringComparison.OrdinalIgnoreCase)){ throw new InvalidDataException("Evidence path escaped its validation root."); }
        while(Current.Length>=FullRoot.Length){
            if((File.Exists(Current)||Directory.Exists(Current))&&(File.GetAttributes(Current)&FileAttributes.ReparsePoint)!=0){ throw new InvalidDataException("Evidence paths cannot contain filesystem reparse points."); }
            if(string.Equals(Current,FullRoot,StringComparison.OrdinalIgnoreCase)){ break; }
            Current=Path.GetDirectoryName(Current)??throw new InvalidDataException("Evidence path has no validation parent.");
        }
    }
    private static string Relative(string Root,string PathValue)=>Path.GetRelativePath(Path.GetFullPath(Root),Path.GetFullPath(PathValue)).Replace('\\','/');
    private static string Sha256(byte[]Bytes)=>Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
    private static bool FixedHashEquals(string?Left,string?Right){
        if(Left is null||Right is null||Left.Length!=64||Right.Length!=64){ return false; }
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Left.ToLowerInvariant()),Encoding.ASCII.GetBytes(Right.ToLowerInvariant()));
    }
    private static void ValidateConfiguration(string Configuration){
        if(Configuration is not("Debug" or "Release")){ throw new ArgumentException("Configuration must be Debug or Release.",nameof(Configuration)); }
    }
    private sealed record InputDefinition(string Role,string Path);
    private sealed record ManualDocument(string SchemaVersion,IReadOnlyList<ManualEntry>Entries);
    private sealed record ManualEntry(string Id,string Requirement,string Owner,string Status,string Reason,string?Evidence,string?ObservedAtUtc,string?ExpiresAtUtc);
}
