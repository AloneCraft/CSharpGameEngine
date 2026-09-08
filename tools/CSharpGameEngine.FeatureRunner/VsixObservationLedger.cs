using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CSharpGameEngine.FeatureStages;

public sealed record VsixObservationAssertion(string Id,string Detail);

/// <summary>
/// Registers an already completed automated observation or records a human Visual Studio
/// observation. The checked-in ledgers are updated only after the evidence is bound to the
/// current Release package and the resulting repository contract validates.
/// </summary>
public static class VsixObservationLedger {
    private const string AutomatedEvidenceSchema="cge-vsix-experimental-evidence-v1";
    private const string ExperimentalLedgerSchema="cge-vsix-experimental-baseline-v1";
    private const string ManualLedgerSchema="csharp-game-engine-manual-evidence-v1";
    private const string EvidenceRootSuffix="CgeAutomation";
    private const string SummaryEvidenceSchema=
        "csharp-game-engine-vsix-observation-summary-v1";
    private static readonly TimeSpan MaximumValidity=TimeSpan.FromDays(14);
    private static readonly JsonSerializerOptions JsonOptions=
        FeatureRunResultStore.CreateJsonOptions();
    private static readonly IReadOnlyDictionary<string,string[]>ManualAssertionIds=
        new Dictionary<string,string[]>(StringComparer.Ordinal){
            ["scene-designer"]=[
                "document-open","snapshot-rendered","selection-synchronized",
            ],
            ["dpi-scaling"]=[
                "content-visible","resize-stable","focus-retained",
            ],
            ["escape-routing"]=[
                "edit-cancelled","selection-cleared","windows-remained-open",
                "non-scene-unaffected",
            ],
            ["physics-sidebar"]=[
                "controls-visible","overlay-visible","escape-remained-open",
            ],
            ["startup-layout"]=[
                "placement-preserved","no-focus-theft","normal-restart",
            ],
            ["preview-render-audio"]=[
                "warp-rendered","audio-lifecycle-observed","host-ownership-confirmed",
            ],
        };

    public static IReadOnlyList<string>GetRequiredManualAssertionIds(string CheckId){
        ArgumentException.ThrowIfNullOrWhiteSpace(CheckId);
        if(!ManualAssertionIds.TryGetValue(CheckId,out string[]?Ids)){
            throw new ArgumentException($"Unknown manual VSIX observation check: {CheckId}");
        }
        return Ids;
    }

    public static async Task<string>RegisterAutomatedAsync(
        string RepositoryRoot,string CheckId,string EvidencePath,
        DateTimeOffset ExpiresUtc,DateTimeOffset NowUtc,
        CancellationToken CancellationToken=default){
        string Root=Path.GetFullPath(RepositoryRoot);
        CheckContract Contract=ReadContract(Root,CheckId);
        if(Contract.Kind!="Automated"){
            throw new ArgumentException($"VSIX observation {CheckId} is not automated.");
        }
        string FullEvidencePath=ResolveExistingContainedPath(Root,EvidencePath);
        AutomatedEvidence Evidence=ReadJson<AutomatedEvidence>(FullEvidencePath);
        if(Evidence.SchemaVersion!=AutomatedEvidenceSchema||Evidence.Status!="Passed"
            ||Evidence.Configuration!="Release"||Evidence.Locale!=Contract.Locale
            ||Evidence.RootSuffix!=EvidenceRootSuffix){
            throw new InvalidDataException(
                $"VSIX automated evidence {CheckId} does not match its Release/locale/root-suffix contract.");
        }
        ValidateLifetime(Evidence.ObservedUtc,ExpiresUtc,NowUtc);
        VsixReleasePackage Package=VsixReleaseEvidence.ReadCurrentPackage(Root,"Release");
        ValidateIdentity(
            CheckId,Package,Evidence.PackageSha256,Evidence.PackageDllSha256,
            Evidence.BuildDllSha256,Evidence.InstalledDllSha256,Evidence.Assertions);
        return await UpdateLedgersAsync(
            Root,CheckId,FullEvidencePath,Evidence.ObservedUtc,ExpiresUtc,
            NowUtc,CancellationToken).ConfigureAwait(false);
    }

    public static async Task<string>RecordManualAsync(
        string RepositoryRoot,string CheckId,string InstalledDllPath,
        IReadOnlyList<VsixObservationAssertion>Assertions,
        DateTimeOffset ObservedUtc,DateTimeOffset ExpiresUtc,
        CancellationToken CancellationToken=default){
        string Root=Path.GetFullPath(RepositoryRoot);
        CheckContract Contract=ReadContract(Root,CheckId);
        if(Contract.Kind!="Manual"){
            throw new ArgumentException($"VSIX observation {CheckId} is not manual.");
        }
        ValidateLifetime(ObservedUtc,ExpiresUtc,DateTimeOffset.UtcNow);
        ValidateManualAssertions(CheckId,Assertions);
        string FullInstalledDllPath=Path.GetFullPath(InstalledDllPath);
        if(!File.Exists(FullInstalledDllPath)){
            throw new FileNotFoundException(
                "The observed Visual Studio installation DLL does not exist.",
                FullInstalledDllPath);
        }
        VsixReleasePackage Package=VsixReleaseEvidence.ReadCurrentPackage(Root,"Release");
        string InstalledDllSha256=Hash(await File.ReadAllBytesAsync(
            FullInstalledDllPath,CancellationToken).ConfigureAwait(false));
        if(!FixedHashEquals(InstalledDllSha256,Package.BuildDllSha256)){
            throw new InvalidDataException(
                $"Installed DLL for {CheckId} does not match the current Release package.");
        }
        string RunId=$"{ObservedUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        string EvidenceDirectory=Path.Combine(
            Root,".artifacts","VsixObservations",RunId);
        string EvidencePath=Path.Combine(EvidenceDirectory,CheckId+".json");
        var Evidence=new ManualEvidence(
            VsixReleaseEvidence.ManualEvidenceSchema,CheckId,"Passed",
            ObservedUtc,ExpiresUtc,Package.PackageSha256,Package.PackageDllSha256,
            Package.BuildDllSha256,InstalledDllSha256,
            [.. Assertions.Select(static Assertion=>
                new EvidenceAssertion(Assertion.Id,true,Assertion.Detail))]);
        Directory.CreateDirectory(EvidenceDirectory);
        try {
            await WriteCanonicalAtomicAsync(
                EvidencePath,Evidence,CancellationToken).ConfigureAwait(false);
            return await UpdateLedgersAsync(
                Root,CheckId,EvidencePath,ObservedUtc,ExpiresUtc,
                DateTimeOffset.UtcNow,CancellationToken).ConfigureAwait(false);
        } catch {
            if(File.Exists(EvidencePath)){ File.Delete(EvidencePath); }
            if(Directory.Exists(EvidenceDirectory)
                &&!Directory.EnumerateFileSystemEntries(EvidenceDirectory).Any()){
                Directory.Delete(EvidenceDirectory);
            }
            throw;
        }
    }

    private static async Task<string>UpdateLedgersAsync(
        string Root,string CheckId,string EvidencePath,
        DateTimeOffset ObservedUtc,DateTimeOffset ExpiresUtc,
        DateTimeOffset NowUtc,CancellationToken CancellationToken){
        string ExperimentalPath=Path.Combine(
            Root,"Docs","Verification","VSIX-Experimental-Baseline.json");
        string ManualPath=Path.Combine(
            Root,"Docs","Verification","Manual-Evidence.json");
        byte[]OriginalExperimental=await File.ReadAllBytesAsync(
            ExperimentalPath,CancellationToken).ConfigureAwait(false);
        byte[]OriginalManual=await File.ReadAllBytesAsync(
            ManualPath,CancellationToken).ConfigureAwait(false);
        string SummaryEvidencePath=Path.Combine(
            Root,".artifacts","VsixObservations","vsix-experimental-summary.json");
        byte[]?OriginalSummary=File.Exists(SummaryEvidencePath)
            ?await File.ReadAllBytesAsync(
                SummaryEvidencePath,CancellationToken).ConfigureAwait(false)
            :null;
        JsonObject Experimental=ParseObject(OriginalExperimental,ExperimentalPath);
        JsonObject Manual=ParseObject(OriginalManual,ManualPath);
        if(Experimental["schemaVersion"]?.GetValue<string>()!=ExperimentalLedgerSchema
            ||Manual["schemaVersion"]?.GetValue<string>()!=ManualLedgerSchema){
            throw new InvalidDataException("VSIX observation ledgers use an unexpected schema.");
        }
        JsonArray Checks=Experimental["checks"]?.AsArray()
            ??throw new InvalidDataException("VSIX Experimental ledger has no checks.");
        JsonObject[]Matches=[.. Checks.OfType<JsonObject>().Where(Item=>
            Item["id"]?.GetValue<string>()==CheckId)];
        if(Matches.Length!=1){
            throw new InvalidDataException(
                $"VSIX Experimental ledger must contain exactly one {CheckId} row.");
        }
        JsonObject Check=Matches[0];
        Check["status"]="Passed";
        Check["evidencePath"]=Path.GetRelativePath(
            Path.GetDirectoryName(ExperimentalPath)!,EvidencePath).Replace('\\','/');
        Check["observedUtc"]=ObservedUtc;
        Check["expiresUtc"]=ExpiresUtc;
        ObservationSummary?ObservationSummary=UpdateManualSummary(
            Manual,Checks,Root,SummaryEvidencePath,NowUtc);
        try {
            if(ObservationSummary is not null){
                await WriteCanonicalAtomicAsync(
                    SummaryEvidencePath,ObservationSummary,CancellationToken)
                    .ConfigureAwait(false);
            }
            await WriteCanonicalAtomicAsync(
                ExperimentalPath,Experimental,CancellationToken).ConfigureAwait(false);
            await WriteCanonicalAtomicAsync(
                ManualPath,Manual,CancellationToken).ConfigureAwait(false);
            IReadOnlyList<string>Errors=[
                .. VsixCompatibilityMatrixValidator.Validate(
                    Root,FeatureStageCatalog.CreateDefault(),"Release",false,NowUtc),
                .. ManualEvidenceLedgerValidator.Validate(Root,false,NowUtc),
            ];
            if(Errors.Count!=0){
                throw new InvalidDataException(
                    "Updated VSIX observation ledgers failed validation: "
                    +string.Join(" | ",Errors));
            }
        } catch {
            await WriteBytesAtomicAsync(
                ExperimentalPath,OriginalExperimental,CancellationToken).ConfigureAwait(false);
            await WriteBytesAtomicAsync(
                ManualPath,OriginalManual,CancellationToken).ConfigureAwait(false);
            if(OriginalSummary is null){
                if(File.Exists(SummaryEvidencePath)){ File.Delete(SummaryEvidencePath); }
            } else {
                await WriteBytesAtomicAsync(
                    SummaryEvidencePath,OriginalSummary,CancellationToken)
                    .ConfigureAwait(false);
            }
            throw;
        }
        return Path.GetRelativePath(Root,EvidencePath).Replace('\\','/');
    }

    private static ObservationSummary?UpdateManualSummary(
        JsonObject Manual,JsonArray Checks,string Root,
        string SummaryEvidencePath,DateTimeOffset CreatedUtc){
        JsonArray Entries=Manual["entries"]?.AsArray()
            ??throw new InvalidDataException("Manual evidence ledger has no entries.");
        JsonObject[]Summaries=[.. Entries.OfType<JsonObject>().Where(Item=>
            Item["id"]?.GetValue<string>()=="vsix-experimental")];
        if(Summaries.Length!=1){
            throw new InvalidDataException(
                "Manual evidence ledger must contain exactly one vsix-experimental entry.");
        }
        JsonObject Summary=Summaries[0];
        JsonObject[]DefinedChecks=[.. Checks.OfType<JsonObject>()];
        bool AllPassed=DefinedChecks.Length!=0&&DefinedChecks.All(Item=>
            Item["status"]?.GetValue<string>()=="Passed");
        bool AnyFailed=DefinedChecks.Any(Item=>
            Item["status"]?.GetValue<string>()=="Failed");
        if(AllPassed){
            DateTimeOffset ExpiresUtc=DefinedChecks.Select(Item=>
                Item["expiresUtc"]?.GetValue<DateTimeOffset>()
                ??throw new InvalidDataException(
                    "Passed VSIX observation has no expiry.")).Min();
            Summary["status"]="Passed";
            Summary["evidencePath"]=Path.GetRelativePath(
                Root,SummaryEvidencePath).Replace('\\','/');
            Summary["expiresUtc"]=ExpiresUtc;
            Summary.Remove("reason");
            ObservationSummaryEntry[]EvidenceEntries=[.. DefinedChecks
                .OrderBy(Item=>Item["id"]!.GetValue<string>(),StringComparer.Ordinal)
                .Select(Item=>{
                    string Id=Item["id"]!.GetValue<string>();
                    string LedgerEvidencePath=Item["evidencePath"]!.GetValue<string>();
                    string FullEvidencePath=Path.GetFullPath(Path.Combine(
                        Root,"Docs","Verification",
                        LedgerEvidencePath.Replace('/',Path.DirectorySeparatorChar)));
                    string RootPrefix=Root.TrimEnd(Path.DirectorySeparatorChar)
                        +Path.DirectorySeparatorChar;
                    if(!FullEvidencePath.StartsWith(
                        RootPrefix,StringComparison.OrdinalIgnoreCase)
                        ||!File.Exists(FullEvidencePath)){
                        throw new InvalidDataException(
                            $"VSIX observation {Id} evidence is missing or escapes the repository.");
                    }
                    return new ObservationSummaryEntry(
                        Id,Path.GetRelativePath(Root,FullEvidencePath).Replace('\\','/'),
                        Hash(File.ReadAllBytes(FullEvidencePath)));
                })];
            return new ObservationSummary(
                SummaryEvidenceSchema,CreatedUtc,ExpiresUtc,EvidenceEntries);
        } else {
            Summary["status"]=AnyFailed?"Failed":"Pending";
            Summary.Remove("evidencePath");
            Summary.Remove("expiresUtc");
            Summary["reason"]=AnyFailed
                ?"One or more detailed VSIX observations failed."
                :"Detailed VSIX release observations are not yet complete.";
            return null;
        }
    }

    private static CheckContract ReadContract(string Root,string CheckId){
        string MatrixPath=Path.Combine(
            Root,"Docs","Verification","VSIX-Compatibility-Matrix.json");
        MatrixDocument Matrix=ReadJson<MatrixDocument>(MatrixPath);
        CheckContract[]Matches=[.. (Matrix.Automation?.Checks??[]).Where(
            Item=>Item.Id==CheckId)];
        if(Matches.Length!=1){
            throw new ArgumentException(
                $"VSIX compatibility matrix must contain exactly one {CheckId} check.");
        }
        return Matches[0];
    }

    private static void ValidateManualAssertions(
        string CheckId,IReadOnlyList<VsixObservationAssertion>Assertions){
        ArgumentNullException.ThrowIfNull(Assertions);
        string[]Required=[.. GetRequiredManualAssertionIds(CheckId)];
        if(Assertions.Count!=Required.Length
            ||Assertions.Any(static Assertion=>string.IsNullOrWhiteSpace(Assertion.Id)
                ||string.IsNullOrWhiteSpace(Assertion.Detail))
            ||Assertions.GroupBy(static Assertion=>Assertion.Id,StringComparer.Ordinal)
                .Any(static Group=>Group.Count()!=1)
            ||!Assertions.Select(static Assertion=>Assertion.Id)
                .ToHashSet(StringComparer.Ordinal).SetEquals(Required)){
            throw new ArgumentException(
                $"Manual VSIX observation {CheckId} requires exactly: "
                +string.Join(", ",Required));
        }
    }

    private static void ValidateLifetime(
        DateTimeOffset ObservedUtc,DateTimeOffset ExpiresUtc,DateTimeOffset NowUtc){
        if(ObservedUtc>DateTimeOffset.UtcNow.AddMinutes(1)
            ||ExpiresUtc<=ObservedUtc||ExpiresUtc<=NowUtc
            ||ExpiresUtc-ObservedUtc>MaximumValidity){
            throw new ArgumentException(
                "VSIX observation lifetime must be current, positive, and no longer than 14 days.");
        }
    }

    private static void ValidateIdentity(
        string CheckId,VsixReleasePackage Package,
        string PackageSha256,string PackageDllSha256,string BuildDllSha256,
        string InstalledDllSha256,IReadOnlyList<EvidenceAssertion>?Assertions){
        if(Assertions is null||Assertions.Count==0
            ||Assertions.Any(static Assertion=>!Assertion.Passed
                ||string.IsNullOrWhiteSpace(Assertion.Id)
                ||string.IsNullOrWhiteSpace(Assertion.Detail))
            ||!FixedHashEquals(PackageSha256,Package.PackageSha256)
            ||!FixedHashEquals(PackageDllSha256,Package.PackageDllSha256)
            ||!FixedHashEquals(BuildDllSha256,Package.BuildDllSha256)
            ||!FixedHashEquals(InstalledDllSha256,Package.BuildDllSha256)){
            throw new InvalidDataException(
                $"VSIX evidence {CheckId} does not prove the current package and installed DLL.");
        }
    }

    private static string ResolveExistingContainedPath(string Root,string PathValue){
        string FullPath=Path.GetFullPath(Path.IsPathRooted(PathValue)
            ?PathValue:Path.Combine(Root,PathValue));
        string RootPrefix=Root.TrimEnd(Path.DirectorySeparatorChar)
            +Path.DirectorySeparatorChar;
        if(!FullPath.StartsWith(RootPrefix,StringComparison.OrdinalIgnoreCase)
            ||!File.Exists(FullPath)){
            throw new FileNotFoundException(
                "VSIX observation evidence must be an existing repository artifact.",FullPath);
        }
        return FullPath;
    }

    private static JsonObject ParseObject(byte[]Bytes,string PathValue)=>
        JsonNode.Parse(Bytes)?.AsObject()
        ??throw new InvalidDataException($"JSON document is empty: {PathValue}");
    private static T ReadJson<T>(string PathValue)=>
        JsonSerializer.Deserialize<T>(File.ReadAllBytes(PathValue),JsonOptions)
        ??throw new InvalidDataException($"JSON document is empty: {PathValue}");
    private static string Hash(byte[]Bytes)=>
        Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
    private static bool FixedHashEquals(string Left,string Right)=>
        Left.Length==Right.Length&&CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(Left.ToLowerInvariant()),
            System.Text.Encoding.ASCII.GetBytes(Right.ToLowerInvariant()));

    private static Task WriteCanonicalAtomicAsync<T>(
        string PathValue,T Value,CancellationToken CancellationToken)=>
        WriteBytesAtomicAsync(
            PathValue,JsonSerializer.SerializeToUtf8Bytes(Value,JsonOptions),
            CancellationToken);
    private static async Task WriteBytesAtomicAsync(
        string PathValue,byte[]Bytes,CancellationToken CancellationToken){
        Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
        string TemporaryPath=PathValue+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            await File.WriteAllBytesAsync(
                TemporaryPath,Bytes,CancellationToken).ConfigureAwait(false);
            File.Move(TemporaryPath,PathValue,true);
        } finally {
            if(File.Exists(TemporaryPath)){ File.Delete(TemporaryPath); }
        }
    }

    private sealed record MatrixDocument(MatrixAutomation?Automation);
    private sealed record MatrixAutomation(IReadOnlyList<CheckContract>?Checks);
    private sealed record CheckContract(
        string Id,string Owner,string Kind,string?Configuration,
        string?Locale,bool RequiredForRelease);
    private sealed record AutomatedEvidence(
        string SchemaVersion,string Status,DateTimeOffset ObservedUtc,
        string Configuration,string Locale,string RootSuffix,
        string PackageSha256,string PackageDllSha256,string BuildDllSha256,
        string InstalledDllSha256,IReadOnlyList<EvidenceAssertion>?Assertions);
    private sealed record ManualEvidence(
        string SchemaVersion,string CheckId,string Status,
        DateTimeOffset ObservedUtc,DateTimeOffset ExpiresUtc,
        string PackageSha256,string PackageDllSha256,string BuildDllSha256,
        string InstalledDllSha256,IReadOnlyList<EvidenceAssertion>Assertions);
    private sealed record EvidenceAssertion(string Id,bool Passed,string Detail);
    private sealed record ObservationSummary(
        string SchemaVersion,DateTimeOffset CreatedUtc,DateTimeOffset ExpiresUtc,
        IReadOnlyList<ObservationSummaryEntry>Entries);
    private sealed record ObservationSummaryEntry(
        string CheckId,string EvidencePath,string EvidenceSha256);
}
