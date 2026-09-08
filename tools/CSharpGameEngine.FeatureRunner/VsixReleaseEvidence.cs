using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace CSharpGameEngine.FeatureStages;

public sealed record VsixReleaseInput(string Role,string Path,string Sha256,long ByteLength);
public sealed record VsixReleasePackage(
    string PackagePath,string PackageSha256,long PackageByteLength,
    string BuildDllPath,string BuildDllSha256,string PackageDllSha256,
    string ExtensionId,string Version,int EntryCount,string ContentClosureSha256);
public sealed record VsixReleaseStageEvidence(
    string ReportPath,string FileSha256,string ResultArtifactSha256,
    DateTimeOffset FinishedUtc,string Profile,string Adapter);
public sealed record VsixReleaseObservation(
    string Id,string Kind,string EvidencePath,string EvidenceSha256,
    DateTimeOffset ObservedUtc,DateTimeOffset ExpiresUtc,string PackageSha256,
    string PackageDllSha256,string BuildDllSha256,string InstalledDllSha256);
public sealed record VsixReleaseLifecycle(
    string EvidencePath,string EvidenceSha256,DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,string RootSuffix,int InstallExitCode,
    int UninstallExitCode,int RemainingInstallations,string PackageSha256,
    string PackageDllSha256,string BuildDllSha256,string InstalledDllSha256);
public sealed record VsixReleasePayload(
    string Schema,DateTimeOffset CreatedUtc,string Configuration,string Commit,
    bool IsDirty,PerformanceMachineFingerprint Machine,VsixReleasePackage Package,
    VsixReleaseStageEvidence Compatibility,IReadOnlyList<VsixReleaseObservation>Observations,
    VsixReleaseLifecycle Lifecycle,IReadOnlyList<VsixReleaseInput>Inputs);
public sealed record VsixReleaseReport(
    string Schema,string ReportSha256,DateTimeOffset CreatedUtc,string Configuration,
    string Commit,bool IsDirty,PerformanceMachineFingerprint Machine,
    VsixReleasePackage Package,VsixReleaseStageEvidence Compatibility,
    IReadOnlyList<VsixReleaseObservation>Observations,VsixReleaseLifecycle Lifecycle,
    IReadOnlyList<VsixReleaseInput>Inputs);

/// <summary>
/// Binds the exact VSIX package to strict compatibility, current-package UI observations,
/// and an isolated install/uninstall lifecycle. It owns no Visual Studio process or hive.
/// </summary>
public static class VsixReleaseEvidence {
    public const string ReportSchema="csharp-game-engine-vsix-release-v1";
    public const string ReportFileName="vsix-release.json";
    public const string CompatibilityProfile="vsix-release-prerequisite";
    public const string LifecycleSchema="csharp-game-engine-vsix-lifecycle-v1";
    public const string ManualEvidenceSchema="csharp-game-engine-vsix-manual-evidence-v1";
    private const string ExperimentalEvidenceSchema="cge-vsix-experimental-evidence-v1";
    private const string ExtensionId="CSharpGameEngine.VSExtension.aec17804-e2e4-46e7-b373-de11af816203";
    private const string ReleaseRootSuffix="CgeRelease";
    private static readonly TimeSpan MaximumEvidenceAge=TimeSpan.FromHours(4);
    private static readonly InputDefinition[]InputDefinitions=[
        new("vsix.matrix","Docs/Verification/VSIX-Compatibility-Matrix.json"),
        new("vsix.experimental-ledger","Docs/Verification/VSIX-Experimental-Baseline.json"),
        new("vsix.release-input",VsixReleaseInputStore.ActiveRelativePath),
        new("manual.ledger","Docs/Verification/Manual-Evidence.json"),
        new("ci.matrix","Docs/Verification/CI-Matrix.json"),
        new("editor.specification","Docs/Specifications/Editor-VSIX.md"),
        new("release.specification","Docs/Specifications/Testing-CI-Release.md"),
        new("toolchain","global.json"),
        new("vsix.project","tools/CSharpGameEngine.VSExtension/CSharpGameEngine.VSExtension.csproj"),
        new("vsix.manifest","tools/CSharpGameEngine.VSExtension/source.extension.vsixmanifest"),
        new("vsix.deployment","tools/CSharpGameEngine.VSExtension/Reinstall-CgeFixedVsix.ps1"),
        new("automation.project","tools/CSharpGameEngine.Vsix.Automation/CSharpGameEngine.Vsix.Automation.csproj"),
        new("automation.contract","tools/CSharpGameEngine.Vsix.Automation/AutomationContracts.cs"),
        new("automation.scenario","tools/CSharpGameEngine.Vsix.Automation/VsixExperimentalScenario.cs"),
        new("compatibility.validator","tools/CSharpGameEngine.FeatureRunner/VsixCompatibilityMatrixValidator.cs"),
        new("release.input-store","tools/CSharpGameEngine.FeatureRunner/VsixReleaseInputStore.cs"),
        new("release.evidence","tools/CSharpGameEngine.FeatureRunner/VsixReleaseEvidence.cs"),
        new("stage.catalog","tools/CSharpGameEngine.FeatureRunner/FeatureStageCatalog.cs"),
    ];
    private static readonly JsonSerializerOptions JsonOptions=FeatureRunResultStore.CreateJsonOptions();

    public static IEnumerable<string>RequiredRepositoryInputPaths=>
        InputDefinitions.Select(static Definition=>Definition.Path);

    public static async Task<int>RunAsync(
        string RepositoryRoot,string Configuration,TextWriter Output,TextWriter Error,
        CancellationToken CancellationToken){
        string Root=Path.GetFullPath(RepositoryRoot);
        ValidateConfiguration(Configuration);
        DateTimeOffset CreatedUtc=DateTimeOffset.UtcNow;
        IReadOnlyList<string>StrictErrors=VsixCompatibilityMatrixValidator.Validate(
            Root,FeatureStageCatalog.CreateDefault(),Configuration,true,CreatedUtc);
        if(StrictErrors.Count!=0){
            foreach(string StrictError in StrictErrors){
                await Error.WriteLineAsync(StrictError).ConfigureAwait(false);
            }
            return 1;
        }
        (string Commit,bool IsDirty)Provenance;
        try {
            Provenance=await GitChangedPathProvider.ReadProvenanceAsync(
                Root,CancellationToken).ConfigureAwait(false);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Provenance=("unknown",true);
            await Error.WriteLineAsync(
                $"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false);
        }
        string EvidenceRoot=Path.Combine(
            Root,".artifacts","VsixRelease",$"{CreatedUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        string ReportPath=Path.Combine(EvidenceRoot,ReportFileName);
        bool Completed=false;
        try {
            VsixReleasePackage Package=ReadPackage(Root,Configuration);
            VsixReleaseStageEvidence Compatibility=FindCompatibilityEvidence(
                Root,Configuration,Provenance,CreatedUtc);
            VsixReleaseObservation[]Observations=ReadObservations(
                Root,Package,CreatedUtc);
            VsixReleaseLifecycle Lifecycle=FindLifecycleEvidence(
                Root,Configuration,Package,CreatedUtc);
            VsixReleaseInput[]Inputs=ReadInputs(Root);
            PerformanceMachineFingerprint Machine=
                PerformanceMeasurementFramework.CaptureMachineFingerprint();
            var Payload=new VsixReleasePayload(
                ReportSchema,CreatedUtc,Configuration,Provenance.Commit,Provenance.IsDirty,
                Machine,Package,Compatibility,Observations,Lifecycle,Inputs);
            string Hash=Sha256(JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions));
            var Report=new VsixReleaseReport(
                Payload.Schema,Hash,Payload.CreatedUtc,Payload.Configuration,Payload.Commit,
                Payload.IsDirty,Payload.Machine,Payload.Package,Payload.Compatibility,
                Payload.Observations,Payload.Lifecycle,Payload.Inputs);
            Directory.CreateDirectory(EvidenceRoot);
            await WriteCanonicalAsync(ReportPath,Report,CancellationToken).ConfigureAwait(false);
            IReadOnlyList<string>Errors=await ValidateAsync(
                Root,ReportPath,CancellationToken).ConfigureAwait(false);
            foreach(string ValidationError in Errors){
                await Error.WriteLineAsync(ValidationError).ConfigureAwait(false);
            }
            if(Errors.Count!=0){ return 1; }
            Completed=true;
            await Output.WriteLineAsync(
                $"vsixRelease package={Package.PackageSha256} observations={Observations.Length} "
                +$"lifecycle={Lifecycle.EvidenceSha256} machine={Machine.FingerprintSha256}")
                .ConfigureAwait(false);
            await Output.WriteLineAsync(ReportPath).ConfigureAwait(false);
            return 0;
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            await Error.WriteLineAsync(
                $"VSIX release verification failed: {Ex.Message}").ConfigureAwait(false);
            return 1;
        } finally {
            if(!Completed&&Directory.Exists(EvidenceRoot)){
                Directory.Delete(EvidenceRoot,true);
            }
        }
    }

    public static async Task<IReadOnlyList<string>>ValidateAsync(
        string RepositoryRoot,string ReportPath,CancellationToken CancellationToken=default){
        var Errors=new List<string>();
        string Root;
        string FullReportPath;
        byte[]Bytes;
        VsixReleaseReport?Report;
        try {
            Root=Path.GetFullPath(RepositoryRoot);
            FullReportPath=Path.GetFullPath(ReportPath);
            if(!IsContained(Path.Combine(Root,".artifacts","VsixRelease"),FullReportPath)){
                return ["VSIX release report must be inside .artifacts/VsixRelease."];
            }
            EnsureNoReparsePoints(Root,FullReportPath);
            Bytes=await File.ReadAllBytesAsync(FullReportPath,CancellationToken)
                .ConfigureAwait(false);
            Report=JsonSerializer.Deserialize<VsixReleaseReport>(Bytes,JsonOptions);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            return [$"VSIX release report cannot be read: {Ex.Message}"];
        }
        if(Report is null){ return ["VSIX release report is empty."]; }
        try {
            ValidateConfiguration(Report.Configuration);
            if(!Bytes.AsSpan().SequenceEqual(CanonicalBytes(Report))){
                Errors.Add("VSIX release report is not canonically encoded.");
            }
            if(Report.Schema!=ReportSchema){
                Errors.Add($"VSIX release report schema must be {ReportSchema}.");
            }
            var Payload=new VsixReleasePayload(
                Report.Schema,Report.CreatedUtc,Report.Configuration,Report.Commit,
                Report.IsDirty,Report.Machine,Report.Package,Report.Compatibility,
                Report.Observations,Report.Lifecycle,Report.Inputs);
            if(!FixedHashEquals(
                Sha256(JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions)),
                Report.ReportSha256)){
                Errors.Add("VSIX release report SHA-256 mismatch.");
            }
            if(!FixedHashEquals(
                PerformanceMeasurementFramework.HashFingerprint(Report.Machine),
                Report.Machine.FingerprintSha256)){
                Errors.Add("VSIX release machine fingerprint mismatch.");
            }
            if(Report.Machine.OsArchitecture!="X64"
                ||Report.Machine.ProcessArchitecture!="X64"){
                Errors.Add("VSIX release evidence must be captured by an x64 OS and process.");
            }
            foreach(string ContractError in VsixCompatibilityMatrixValidator.Validate(
                Root,FeatureStageCatalog.CreateDefault(),Report.Configuration,true,
                Report.CreatedUtc)){
                Errors.Add($"VSIX compatibility: {ContractError}");
            }
            ValidateInputs(Root,Report.Inputs,Errors);
            ValidatePackage(Root,Report,Errors);
            ValidateCompatibilityEvidence(Root,Report,Errors);
            ValidateObservations(Root,Report,Errors);
            ValidateLifecycle(Root,Report,Errors);
            string[]Files=Directory.EnumerateFiles(
                Path.GetDirectoryName(FullReportPath)!,"*",SearchOption.AllDirectories).ToArray();
            if(Files.Length!=1
                ||!string.Equals(Path.GetFullPath(Files[0]),FullReportPath,
                    StringComparison.OrdinalIgnoreCase)){
                Errors.Add("VSIX release evidence directory contains missing or extra files.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"VSIX release evidence validation failed: {Ex.Message}");
        }
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public static VsixReleasePackage ReadCurrentPackage(
        string RepositoryRoot,string Configuration){
        string Root=Path.GetFullPath(RepositoryRoot);
        ValidateConfiguration(Configuration);
        return ReadPackage(Root,Configuration);
    }

    private static VsixReleasePackage ReadPackage(string Root,string Configuration){
        if(Configuration=="Release"){ return VsixReleaseInputStore.ReadActive(Root); }
        string PackageRelative=$"tools/CSharpGameEngine.VSExtension/bin/{Configuration}/net472/CSharpGameEngine.VSExtension.vsix";
        string BuildDllRelative=$"tools/CSharpGameEngine.VSExtension/bin/{Configuration}/net472/CSharpGameEngine.VSExtension.dll";
        return InspectPackage(Root,PackageRelative,BuildDllRelative);
    }

    public static VsixReleasePackage InspectPackage(
        string RepositoryRoot,string PackageRelative,string BuildDllRelative){
        string Root=Path.GetFullPath(RepositoryRoot);
        string PackagePath=ResolveRelative(Root,PackageRelative);
        string BuildDllPath=ResolveRelative(Root,BuildDllRelative);
        EnsureNoReparsePoints(Root,PackagePath);
        EnsureNoReparsePoints(Root,BuildDllPath);
        byte[]PackageBytes=File.ReadAllBytes(PackagePath);
        byte[]BuildDllBytes=File.ReadAllBytes(BuildDllPath);
        using var Stream=new MemoryStream(PackageBytes,false);
        using var Archive=new ZipArchive(Stream,ZipArchiveMode.Read,false);
        ZipArchiveEntry[]Entries=[.. Archive.Entries
            .Where(static Entry=>!string.IsNullOrEmpty(Entry.Name))
            .OrderBy(static Entry=>Entry.FullName,StringComparer.Ordinal)];
        ZipArchiveEntry[]DllEntries=[.. Entries.Where(static Entry=>
            Entry.FullName.EndsWith("CSharpGameEngine.VSExtension.dll",StringComparison.OrdinalIgnoreCase))];
        ZipArchiveEntry[]ManifestEntries=[.. Entries.Where(static Entry=>
            Entry.FullName.EndsWith("extension.vsixmanifest",StringComparison.OrdinalIgnoreCase))];
        if(DllEntries.Length!=1||ManifestEntries.Length!=1){
            throw new InvalidDataException(
                "VSIX package must contain exactly one extension DLL and one extension manifest.");
        }
        byte[]PackageDllBytes=ReadEntry(DllEntries[0]);
        string BuildDllHash=Sha256(BuildDllBytes);
        string PackageDllHash=Sha256(PackageDllBytes);
        if(!FixedHashEquals(BuildDllHash,PackageDllHash)){
            throw new InvalidDataException("VSIX package DLL does not match the build output.");
        }
        (string SourceId,string SourceVersion)=ReadManifestIdentity(
            ResolveRelative(Root,"tools/CSharpGameEngine.VSExtension/source.extension.vsixmanifest"));
        byte[]EmbeddedManifestBytes=ReadEntry(ManifestEntries[0]);
        using var EmbeddedManifestStream=new MemoryStream(EmbeddedManifestBytes,false);
        (string EmbeddedId,string EmbeddedVersion)=ReadManifestIdentity(EmbeddedManifestStream);
        if(SourceId!=ExtensionId||EmbeddedId!=SourceId||EmbeddedVersion!=SourceVersion){
            throw new InvalidDataException("VSIX package identity does not match the source manifest.");
        }
        using var Closure=new MemoryStream();
        foreach(ZipArchiveEntry Entry in Entries){
            byte[]EntryBytes=ReadEntry(Entry);
            byte[]Line=System.Text.Encoding.UTF8.GetBytes(
                $"{Entry.FullName.Replace('\\','/')}\n{EntryBytes.LongLength}\n{Sha256(EntryBytes)}\n");
            Closure.Write(Line);
        }
        return new VsixReleasePackage(
            PackageRelative,Sha256(PackageBytes),PackageBytes.LongLength,
            BuildDllRelative,BuildDllHash,PackageDllHash,SourceId,SourceVersion,
            Entries.Length,Sha256(Closure.ToArray()));
    }

    private static VsixReleaseStageEvidence FindCompatibilityEvidence(
        string Root,string Configuration,(string Commit,bool IsDirty)Provenance,
        DateTimeOffset CreatedUtc){
        string StageRoot=Path.Combine(Root,".artifacts","FeatureStages");
        if(!Directory.Exists(StageRoot)){
            throw new InvalidDataException(
                "VSIX release requires a recent passing vsix.compatibility Stage result.");
        }
        var Candidates=new List<(string Path,FeatureStageRunResult Result)>();
        foreach(string PathValue in Directory.EnumerateFiles(
            StageRoot,"*.run-result.json",SearchOption.TopDirectoryOnly)){
            try {
                EnsureNoReparsePoints(Root,PathValue);
                FeatureRunResultDocument?Document=JsonSerializer.Deserialize<FeatureRunResultDocument>(
                    File.ReadAllBytes(PathValue),JsonOptions);
                if(Document?.SchemaVersion!=FeatureRunResultDocument.CurrentSchemaVersion){ continue; }
                Candidates.AddRange(Document.Results.Where(Result=>
                    Result.StageId=="vsix.compatibility"
                    &&Result.Configuration==Configuration
                    &&Result.Profile==CompatibilityProfile
                    &&Result.Status==FeatureStageRunStatus.Passed
                    &&Result.Adapter=="Warp"
                    &&Result.GitCommit==Provenance.Commit
                    &&Result.IsDirty==Provenance.IsDirty
                    &&Result.FinishedUtc<=CreatedUtc
                    &&CreatedUtc-Result.FinishedUtc<=MaximumEvidenceAge
                    &&ValidateStageResult(Result).Count==0).Select(Result=>(PathValue,Result)));
            } catch(Exception Ex)when(Ex is not OperationCanceledException
                &&Ex is not OutOfMemoryException&&Ex is not StackOverflowException){ continue; }
        }
        (string Path,FeatureStageRunResult Result)Match=Candidates
            .OrderByDescending(static Candidate=>Candidate.Result.FinishedUtc)
            .ThenByDescending(static Candidate=>Candidate.Path,StringComparer.Ordinal)
            .FirstOrDefault();
        if(Match.Result is null){
            throw new InvalidDataException(
                $"VSIX release requires a recent passing vsix.compatibility result for {Configuration} "
                +$"with {CompatibilityProfile} profile, matching Git provenance, and Warp adapter.");
        }
        return new VsixReleaseStageEvidence(
            Relative(Root,Match.Path),Sha256(File.ReadAllBytes(Match.Path)),
            Match.Result.ArtifactSha256,Match.Result.FinishedUtc,
            Match.Result.Profile,Match.Result.Adapter);
    }

    private static VsixReleaseObservation[]ReadObservations(
        string Root,VsixReleasePackage Package,DateTimeOffset NowUtc){
        string MatrixPath=ResolveRelative(
            Root,"Docs/Verification/VSIX-Compatibility-Matrix.json");
        string LedgerPath=ResolveRelative(
            Root,"Docs/Verification/VSIX-Experimental-Baseline.json");
        VsixMatrixDocument Matrix=ReadJson<VsixMatrixDocument>(MatrixPath);
        VsixLedgerDocument Ledger=ReadJson<VsixLedgerDocument>(LedgerPath);
        Dictionary<string,VsixMatrixCheck>Contracts=(Matrix.Automation?.Checks??[])
            .ToDictionary(static Check=>Check.Id,StringComparer.Ordinal);
        VsixLedgerCheck[]Checks=Ledger.Checks?.ToArray()??[];
        if(Contracts.Count==0||Checks.Length!=Contracts.Count){
            throw new InvalidDataException("VSIX release observation closure is incomplete.");
        }
        var Result=new List<VsixReleaseObservation>(Checks.Length);
        foreach(VsixLedgerCheck Check in Checks.OrderBy(static Check=>Check.Id,StringComparer.Ordinal)){
            if(!Contracts.TryGetValue(Check.Id,out VsixMatrixCheck?Contract)
                ||Check.Status!="Passed"||Check.ObservedUtc is null||Check.ExpiresUtc is null
                ||Check.ExpiresUtc<=NowUtc||string.IsNullOrWhiteSpace(Check.EvidencePath)){
                throw new InvalidDataException(
                    $"VSIX release observation {Check.Id} is not a current passing contract row.");
            }
            string EvidencePath=ResolveLedgerEvidence(Root,Path.GetDirectoryName(LedgerPath)!,Check.EvidencePath);
            EnsureNoReparsePoints(Root,EvidencePath);
            byte[]EvidenceBytes=File.ReadAllBytes(EvidencePath);
            EvidenceIdentity Identity=Contract.Kind=="Automated"
                ?ReadAutomatedEvidence(EvidenceBytes,Check.Id,Package)
                :ReadManualEvidence(EvidenceBytes,Check.Id,Package,Check.ObservedUtc.Value,Check.ExpiresUtc.Value);
            Result.Add(new VsixReleaseObservation(
                Check.Id,Contract.Kind,Relative(Root,EvidencePath),Sha256(EvidenceBytes),
                Check.ObservedUtc.Value,Check.ExpiresUtc.Value,Identity.PackageSha256,
                Identity.PackageDllSha256,Identity.BuildDllSha256,Identity.InstalledDllSha256));
        }
        return [.. Result];
    }

    private static EvidenceIdentity ReadAutomatedEvidence(
        byte[]Bytes,string CheckId,VsixReleasePackage Package){
        VsixAutomatedEvidence Evidence=JsonSerializer.Deserialize<VsixAutomatedEvidence>(Bytes,JsonOptions)
            ??throw new InvalidDataException($"VSIX automated evidence {CheckId} is empty.");
        ValidateEvidenceIdentity(
            Evidence.SchemaVersion,ExperimentalEvidenceSchema,Evidence.Status,CheckId,
            Evidence.PackageSha256,Evidence.PackageDllSha256,Evidence.BuildDllSha256,
            Evidence.InstalledDllSha256,Evidence.Assertions,Package);
        return new EvidenceIdentity(
            Evidence.PackageSha256,Evidence.PackageDllSha256,
            Evidence.BuildDllSha256,Evidence.InstalledDllSha256);
    }

    private static EvidenceIdentity ReadManualEvidence(
        byte[]Bytes,string CheckId,VsixReleasePackage Package,
        DateTimeOffset ObservedUtc,DateTimeOffset ExpiresUtc){
        VsixManualEvidence Evidence=JsonSerializer.Deserialize<VsixManualEvidence>(Bytes,JsonOptions)
            ??throw new InvalidDataException($"VSIX manual evidence {CheckId} is empty.");
        if(Evidence.CheckId!=CheckId||Evidence.ObservedUtc!=ObservedUtc
            ||Evidence.ExpiresUtc!=ExpiresUtc){
            throw new InvalidDataException(
                $"VSIX manual evidence {CheckId} does not match its ledger identity or lifetime.");
        }
        ValidateEvidenceIdentity(
            Evidence.SchemaVersion,ManualEvidenceSchema,Evidence.Status,CheckId,
            Evidence.PackageSha256,Evidence.PackageDllSha256,Evidence.BuildDllSha256,
            Evidence.InstalledDllSha256,Evidence.Assertions,Package);
        return new EvidenceIdentity(
            Evidence.PackageSha256,Evidence.PackageDllSha256,
            Evidence.BuildDllSha256,Evidence.InstalledDllSha256);
    }

    private static void ValidateEvidenceIdentity(
        string Schema,string ExpectedSchema,string Status,string CheckId,
        string PackageSha256,string PackageDllSha256,string BuildDllSha256,
        string InstalledDllSha256,IReadOnlyList<VsixEvidenceAssertion>?Assertions,
        VsixReleasePackage Package){
        if(Schema!=ExpectedSchema||Status!="Passed"
            ||Assertions is null||Assertions.Count==0
            ||Assertions.Any(static Assertion=>Assertion is null||!Assertion.Passed)){
            throw new InvalidDataException(
                $"VSIX evidence {CheckId} must use {ExpectedSchema} and contain only passing assertions.");
        }
        if(!FixedHashEquals(PackageSha256,Package.PackageSha256)
            ||!FixedHashEquals(PackageDllSha256,Package.PackageDllSha256)
            ||!FixedHashEquals(BuildDllSha256,Package.BuildDllSha256)
            ||!FixedHashEquals(InstalledDllSha256,Package.BuildDllSha256)){
            throw new InvalidDataException(
                $"VSIX evidence {CheckId} does not prove the current package and installed DLL.");
        }
    }

    private static VsixReleaseLifecycle FindLifecycleEvidence(
        string Root,string Configuration,VsixReleasePackage Package,DateTimeOffset CreatedUtc){
        string LifecycleRoot=Path.Combine(Root,".artifacts","VsixReleaseLifecycle");
        if(!Directory.Exists(LifecycleRoot)){
            throw new InvalidDataException(
                "VSIX release requires current install and uninstall lifecycle evidence.");
        }
        var Candidates=new List<(string Path,VsixLifecycleDocument Document)>();
        foreach(string PathValue in Directory.EnumerateFiles(
            LifecycleRoot,"vsix-lifecycle.json",SearchOption.AllDirectories)){
            try {
                EnsureNoReparsePoints(Root,PathValue);
                VsixLifecycleDocument Document=ReadJson<VsixLifecycleDocument>(PathValue);
                if(Document.SchemaVersion==LifecycleSchema
                    &&Document.Configuration==Configuration
                    &&Document.RootSuffix==ReleaseRootSuffix
                    &&Document.ExtensionId==ExtensionId
                    &&Document.StartedUtc<=Document.FinishedUtc
                    &&Document.FinishedUtc<=CreatedUtc
                    &&CreatedUtc-Document.FinishedUtc<=MaximumEvidenceAge
                    &&Document.InstallExitCode==0&&Document.UninstallExitCode==0
                    &&Document.RemainingInstallations==0
                    &&Document.Assertions is not null&&Document.Assertions.Count>0
                    &&Document.Assertions.All(static Assertion=>Assertion is not null&&Assertion.Passed)
                    &&FixedHashEquals(Document.PackageSha256,Package.PackageSha256)
                    &&FixedHashEquals(Document.PackageDllSha256,Package.PackageDllSha256)
                    &&FixedHashEquals(Document.BuildDllSha256,Package.BuildDllSha256)
                    &&FixedHashEquals(Document.InstalledDllSha256,Package.BuildDllSha256)){
                    Candidates.Add((PathValue,Document));
                }
            } catch(Exception Ex)when(Ex is not OperationCanceledException
                &&Ex is not OutOfMemoryException&&Ex is not StackOverflowException){ continue; }
        }
        (string Path,VsixLifecycleDocument Document)Match=Candidates
            .OrderByDescending(static Candidate=>Candidate.Document.FinishedUtc)
            .ThenByDescending(static Candidate=>Candidate.Path,StringComparer.Ordinal)
            .FirstOrDefault();
        if(Match.Document is null){
            throw new InvalidDataException(
                "VSIX release requires current install and uninstall lifecycle evidence for the exact package.");
        }
        return new VsixReleaseLifecycle(
            Relative(Root,Match.Path),Sha256(File.ReadAllBytes(Match.Path)),
            Match.Document.StartedUtc,Match.Document.FinishedUtc,
            Match.Document.RootSuffix,Match.Document.InstallExitCode,
            Match.Document.UninstallExitCode,Match.Document.RemainingInstallations,
            Match.Document.PackageSha256,Match.Document.PackageDllSha256,
            Match.Document.BuildDllSha256,Match.Document.InstalledDllSha256);
    }

    private static void ValidatePackage(string Root,VsixReleaseReport Report,List<string>Errors){
        try {
            VsixReleasePackage Current=ReadPackage(Root,Report.Configuration);
            if(Current!=Report.Package){ Errors.Add("VSIX release package changed after evidence capture."); }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"VSIX release package cannot be validated: {Ex.Message}");
        }
    }

    private static void ValidateCompatibilityEvidence(
        string Root,VsixReleaseReport Report,List<string>Errors){
        try {
            string PathValue=ResolveRelative(Root,Report.Compatibility.ReportPath);
            if(!IsContained(Path.Combine(Root,".artifacts","FeatureStages"),PathValue)){
                throw new InvalidDataException(
                    "Compatibility result escaped .artifacts/FeatureStages.");
            }
            EnsureNoReparsePoints(Root,PathValue);
            byte[]Bytes=File.ReadAllBytes(PathValue);
            if(!FixedHashEquals(Sha256(Bytes),Report.Compatibility.FileSha256)){
                Errors.Add("VSIX compatibility result file SHA-256 mismatch.");
                return;
            }
            FeatureRunResultDocument?Document=
                JsonSerializer.Deserialize<FeatureRunResultDocument>(Bytes,JsonOptions);
            FeatureStageRunResult?Result=Document?.Results.FirstOrDefault(Result=>
                Result.StageId=="vsix.compatibility"
                &&Result.ArtifactSha256==Report.Compatibility.ResultArtifactSha256);
            if(Result is null){ Errors.Add("Bound VSIX compatibility result is missing.");return; }
            Errors.AddRange(ValidateStageResult(Result));
            if(Result.Configuration!=Report.Configuration||Result.GitCommit!=Report.Commit
                ||Result.IsDirty!=Report.IsDirty
                ||Result.FinishedUtc!=Report.Compatibility.FinishedUtc
                ||Result.Profile!=CompatibilityProfile
                ||Result.Profile!=Report.Compatibility.Profile
                ||Result.Adapter!=Report.Compatibility.Adapter
                ||Result.FinishedUtc>Report.CreatedUtc
                ||Report.CreatedUtc-Result.FinishedUtc>MaximumEvidenceAge){
                Errors.Add("VSIX compatibility result provenance does not match the release report.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"VSIX compatibility result cannot be validated: {Ex.Message}");
        }
    }

    private static IReadOnlyList<string>ValidateStageResult(FeatureStageRunResult Result){
        var Errors=new List<string>();
        FeatureStageDefinition Stage=FeatureStageCatalog.CreateDefault()
            .GetStage("vsix.compatibility");
        if(Result.Status!=FeatureStageRunStatus.Passed
            ||Result.Pack!=FeatureStagePack.Vsix.ToString()
            ||Result.Adapter!="Warp"
            ||Result.FeatureIds.Count!=1||Result.FeatureIds[0]!="F-L8-028"
            ||Result.Errors.Count!=0||Result.Assertions.Count!=Stage.Commands.Count
            ||Result.Assertions.Any(static Assertion=>!Assertion.Passed)){
            Errors.Add(
                "Bound vsix.compatibility result is not a complete passing VSIX/Warp verdict.");
        }
        if(!FixedHashEquals(ComputeStageArtifactHash(Result),Result.ArtifactSha256)){
            Errors.Add("Bound vsix.compatibility result artifact SHA-256 mismatch.");
        }
        if(Result.FinishedUtc<Result.StartedUtc||Result.DurationMilliseconds<0){
            Errors.Add("Bound vsix.compatibility result has invalid timing.");
        }
        return Errors;
    }

    private static void ValidateObservations(
        string Root,VsixReleaseReport Report,List<string>Errors){
        try {
            VsixReleaseObservation[]Current=ReadObservations(
                Root,Report.Package,Report.CreatedUtc);
            if(!Current.SequenceEqual(Report.Observations)){
                Errors.Add("VSIX release observations changed after evidence capture.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"VSIX release observations cannot be validated: {Ex.Message}");
        }
    }

    private static void ValidateLifecycle(
        string Root,VsixReleaseReport Report,List<string>Errors){
        try {
            string PathValue=ResolveRelative(Root,Report.Lifecycle.EvidencePath);
            if(!IsContained(Path.Combine(Root,".artifacts","VsixReleaseLifecycle"),PathValue)){
                throw new InvalidDataException(
                    "Lifecycle evidence escaped .artifacts/VsixReleaseLifecycle.");
            }
            EnsureNoReparsePoints(Root,PathValue);
            byte[]Bytes=File.ReadAllBytes(PathValue);
            if(!FixedHashEquals(Sha256(Bytes),Report.Lifecycle.EvidenceSha256)){
                Errors.Add("VSIX lifecycle evidence file SHA-256 mismatch.");return;
            }
            VsixLifecycleDocument Document=JsonSerializer.Deserialize<VsixLifecycleDocument>(
                Bytes,JsonOptions)??throw new InvalidDataException("Lifecycle evidence is empty.");
            var Current=new VsixReleaseLifecycle(
                Report.Lifecycle.EvidencePath,Sha256(Bytes),Document.StartedUtc,
                Document.FinishedUtc,Document.RootSuffix,Document.InstallExitCode,
                Document.UninstallExitCode,Document.RemainingInstallations,
                Document.PackageSha256,Document.PackageDllSha256,
                Document.BuildDllSha256,Document.InstalledDllSha256);
            if(Current!=Report.Lifecycle||Document.SchemaVersion!=LifecycleSchema
                ||Document.Configuration!=Report.Configuration
                ||Document.ExtensionId!=ExtensionId
                ||Document.Assertions is null||Document.Assertions.Count==0
                ||Document.Assertions.Any(static Assertion=>Assertion is null||!Assertion.Passed)
                ||Report.Lifecycle.FinishedUtc>Report.CreatedUtc
                ||Report.CreatedUtc-Report.Lifecycle.FinishedUtc>MaximumEvidenceAge
                ||Report.Lifecycle.RootSuffix!=ReleaseRootSuffix
                ||Report.Lifecycle.InstallExitCode!=0||Report.Lifecycle.UninstallExitCode!=0
                ||Report.Lifecycle.RemainingInstallations!=0
                ||!FixedHashEquals(Report.Lifecycle.PackageSha256,Report.Package.PackageSha256)
                ||!FixedHashEquals(Report.Lifecycle.PackageDllSha256,Report.Package.PackageDllSha256)
                ||!FixedHashEquals(Report.Lifecycle.BuildDllSha256,Report.Package.BuildDllSha256)
                ||!FixedHashEquals(Report.Lifecycle.InstalledDllSha256,Report.Package.BuildDllSha256)){
                Errors.Add("VSIX lifecycle evidence does not prove exact-package install and uninstall.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"VSIX lifecycle evidence cannot be validated: {Ex.Message}");
        }
    }

    private static VsixReleaseInput[]ReadInputs(string Root)=>
        [.. InputDefinitions.Select(Definition=>{
            string PathValue=ResolveRelative(Root,Definition.Path);
            EnsureNoReparsePoints(Root,PathValue);
            byte[]Bytes=File.ReadAllBytes(PathValue);
            return new VsixReleaseInput(
                Definition.Role,Definition.Path,Sha256(Bytes),Bytes.LongLength);
        })];

    private static void ValidateInputs(
        string Root,IReadOnlyList<VsixReleaseInput>?Inputs,List<string>Errors){
        if(Inputs is null||Inputs.Count!=InputDefinitions.Length){
            Errors.Add("VSIX release input closure is incomplete.");return;
        }
        for(int Index=0;Index<InputDefinitions.Length;Index++){
            InputDefinition Definition=InputDefinitions[Index];
            VsixReleaseInput Entry=Inputs[Index];
            if(Entry.Role!=Definition.Role||Entry.Path!=Definition.Path){
                Errors.Add($"VSIX release input {Index} is not canonical.");continue;
            }
            try {
                string PathValue=ResolveRelative(Root,Entry.Path);
                EnsureNoReparsePoints(Root,PathValue);
                byte[]Bytes=File.ReadAllBytes(PathValue);
                if(Entry.ByteLength!=Bytes.LongLength
                    ||!FixedHashEquals(Sha256(Bytes),Entry.Sha256)){
                    Errors.Add($"VSIX release input changed: {Entry.Path}.");
                }
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Errors.Add(
                    $"VSIX release input cannot be validated: {Entry.Path}: {Ex.Message}");
            }
        }
    }

    private static async Task WriteCanonicalAsync(
        string PathValue,VsixReleaseReport Report,CancellationToken CancellationToken){
        string Temporary=PathValue+".tmp";
        try {
            await File.WriteAllBytesAsync(
                Temporary,CanonicalBytes(Report),CancellationToken).ConfigureAwait(false);
            File.Move(Temporary,PathValue,true);
        } finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
    }

    private static byte[]CanonicalBytes(VsixReleaseReport Report){
        byte[]Json=JsonSerializer.SerializeToUtf8Bytes(Report,JsonOptions);
        byte[]Result=new byte[Json.Length+1];
        Json.CopyTo(Result,0);Result[^1]=(byte)'\n';return Result;
    }

    private static string ComputeStageArtifactHash(FeatureStageRunResult Result){
        string Evidence=string.Join("\n",[
            Result.StageId,Result.Status.ToString(),Result.Summary,
            Result.StateHash??string.Empty,Result.EventHash??string.Empty,
            Result.StandardOutput,Result.StandardError,
            .. Result.Assertions.Select(static Assertion=>
                $"{Assertion.Name}:{Assertion.Passed}:{Assertion.Details}"),
            .. Result.Errors,
        ]);
        return Sha256(System.Text.Encoding.UTF8.GetBytes(Evidence));
    }

    private static byte[]ReadEntry(ZipArchiveEntry Entry){
        using Stream Stream=Entry.Open();
        using var Buffer=new MemoryStream();
        Stream.CopyTo(Buffer);return Buffer.ToArray();
    }

    private static (string Id,string Version)ReadManifestIdentity(string PathValue){
        using FileStream Stream=File.OpenRead(PathValue);
        return ReadManifestIdentity(Stream);
    }

    private static (string Id,string Version)ReadManifestIdentity(Stream Stream){
        XDocument Document=XDocument.Load(Stream,LoadOptions.None);
        XElement Identity=Document.Descendants().Single(Element=>
            Element.Name.LocalName=="Identity");
        string Id=(string?)Identity.Attribute("Id")??string.Empty;
        string Version=(string?)Identity.Attribute("Version")??string.Empty;
        if(string.IsNullOrWhiteSpace(Id)||string.IsNullOrWhiteSpace(Version)){
            throw new InvalidDataException("VSIX manifest identity is incomplete.");
        }
        return (Id,Version);
    }

    private static T ReadJson<T>(string PathValue)=>
        JsonSerializer.Deserialize<T>(File.ReadAllBytes(PathValue),JsonOptions)
        ??throw new InvalidDataException($"JSON document is empty: {PathValue}");

    private static string ResolveLedgerEvidence(
        string Root,string LedgerDirectory,string PathValue){
        string Full=Path.IsPathRooted(PathValue)
            ?Path.GetFullPath(PathValue)
            :Path.GetFullPath(Path.Combine(LedgerDirectory,PathValue));
        if(!IsContained(Root,Full)){
            throw new InvalidDataException("VSIX observation evidence escaped the repository.");
        }
        return Full;
    }

    private static string ResolveRelative(string Root,string RelativePath){
        if(Path.IsPathRooted(RelativePath)){
            throw new InvalidDataException($"Path must be repository-relative: {RelativePath}");
        }
        string Full=Path.GetFullPath(
            Path.Combine(Root,RelativePath.Replace('/',Path.DirectorySeparatorChar)));
        if(!IsContained(Root,Full)){
            throw new InvalidDataException($"Path escaped repository root: {RelativePath}");
        }
        if(!File.Exists(Full)){ throw new FileNotFoundException("Required file is missing.",Full); }
        return Full;
    }

    private static bool IsContained(string Root,string PathValue){
        string FullRoot=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar)
            +Path.DirectorySeparatorChar;
        string FullPath=Path.GetFullPath(PathValue);
        return FullPath.StartsWith(FullRoot,StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePoints(string Root,string PathValue){
        string FullRoot=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
        string Current=Path.GetFullPath(PathValue);
        if(!IsContained(FullRoot,Current)&&!string.Equals(
            FullRoot,Current,StringComparison.OrdinalIgnoreCase)){
            throw new InvalidDataException("Evidence path escaped the repository.");
        }
        while(Current.Length>=FullRoot.Length){
            if(File.Exists(Current)||Directory.Exists(Current)){
                FileAttributes Attributes=File.GetAttributes(Current);
                if((Attributes&FileAttributes.ReparsePoint)!=0){
                    throw new InvalidDataException($"Reparse points are not allowed: {Current}");
                }
            }
            if(string.Equals(Current,FullRoot,StringComparison.OrdinalIgnoreCase)){ break; }
            Current=Path.GetDirectoryName(Current)
                ??throw new InvalidDataException("Evidence path has no parent.");
        }
    }

    private static string Relative(string Root,string PathValue)=>
        Path.GetRelativePath(Root,Path.GetFullPath(PathValue)).Replace('\\','/');
    private static string Sha256(byte[]Bytes)=>
        Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
    private static bool FixedHashEquals(string Left,string Right){
        if(Left.Length!=64||Right.Length!=64){ return false; }
        try {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(Left),Convert.FromHexString(Right));
        } catch(FormatException){ return false; }
    }
    private static void ValidateConfiguration(string Configuration){
        if(Configuration!="Release"){
            throw new ArgumentException("VSIX release configuration must be Release.");
        }
    }

    private sealed record InputDefinition(string Role,string Path);
    private sealed record EvidenceIdentity(
        string PackageSha256,string PackageDllSha256,string BuildDllSha256,
        string InstalledDllSha256);
    private sealed record VsixMatrixDocument(VsixMatrixAutomation?Automation);
    private sealed record VsixMatrixAutomation(IReadOnlyList<VsixMatrixCheck>?Checks);
    private sealed record VsixMatrixCheck(string Id,string Kind);
    private sealed record VsixLedgerDocument(IReadOnlyList<VsixLedgerCheck>?Checks);
    private sealed record VsixLedgerCheck(
        string Id,string Status,string?EvidencePath,
        DateTimeOffset?ObservedUtc,DateTimeOffset?ExpiresUtc);
    private sealed record VsixAutomatedEvidence(
        string SchemaVersion,string Status,string PackageSha256,
        string PackageDllSha256,string BuildDllSha256,string InstalledDllSha256,
        IReadOnlyList<VsixEvidenceAssertion>?Assertions);
    private sealed record VsixManualEvidence(
        string SchemaVersion,string CheckId,string Status,DateTimeOffset ObservedUtc,
        DateTimeOffset ExpiresUtc,string PackageSha256,string PackageDllSha256,
        string BuildDllSha256,string InstalledDllSha256,
        IReadOnlyList<VsixEvidenceAssertion>?Assertions);
    private sealed record VsixEvidenceAssertion(string Id,bool Passed,string Detail);
    private sealed record VsixLifecycleDocument(
        string SchemaVersion,string Configuration,string ExtensionId,string RootSuffix,
        DateTimeOffset StartedUtc,DateTimeOffset FinishedUtc,string PackageSha256,
        string PackageDllSha256,string BuildDllSha256,string InstalledDllSha256,
        int InstallExitCode,int UninstallExitCode,int RemainingInstallations,
        IReadOnlyList<VsixEvidenceAssertion?>?Assertions);
}
