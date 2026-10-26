using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace CSharpGameEngine.FeatureStages;

internal sealed record PreparedReleaseEvidence(
    string Role,string SourcePath,string Sha256,long ByteLength);
internal sealed record PreparedReleaseManifest(
    string Product,string ProductVersion,string Configuration,string Target,
    string Commit,bool IsDirty,string ArchivePath,string SigningInputsPath,
    string ArchiveSha256,string PackageManifestSha256,string SbomSha256,
    IReadOnlyList<PreparedReleaseEvidence>Evidence);
public sealed record ReleaseRetention(
    string Class,bool Immutable,DateTimeOffset?ExpiresUtc,string RollbackIdentity);
public sealed record ReleasePackageReference(
    string ArchivePath,string ArchiveSha256,long ArchiveByteLength,
    string SigningInputsPath,string SigningInputsSha256,
    string PackageManifestSha256,string SbomSha256);
public sealed record ReleaseEvidenceReference(
    string Role,string Path,string SourcePath,string Sha256,long ByteLength);
public sealed record ReleaseIdentity(
    string Schema,string Product,string ProductVersion,string Configuration,string Target,
    string Commit,bool IsDirty,string ArchiveSha256,string SigningInputsSha256,
    string PackageManifestSha256,string SbomSha256,
    IReadOnlyList<ReleaseIdentityEvidence>Evidence);
public sealed record ReleaseIdentityEvidence(string Role,string Sha256,long ByteLength);
public sealed record ReleaseManifestPayload(
    string Schema,string ReleaseId,DateTimeOffset CreatedUtc,string Product,
    string ProductVersion,string Configuration,string Target,string Commit,bool IsDirty,
    ReleaseRetention Retention,ReleasePackageReference Package,
    IReadOnlyList<ReleaseEvidenceReference>Evidence);
public sealed record ReleaseManifest(
    string Schema,string ManifestSha256,string ReleaseId,DateTimeOffset CreatedUtc,
    string Product,string ProductVersion,string Configuration,string Target,string Commit,
    bool IsDirty,ReleaseRetention Retention,ReleasePackageReference Package,
    IReadOnlyList<ReleaseEvidenceReference>Evidence);

/// <summary>
/// 検証済みの RC とパッケージの閉包を、コンテンツアドレス方式の正式リリースディレクトリへ公開する。公開はアトミックかつ冪等であり、既存の無効な ID を修復したり上書きしたりしない。
/// </summary>
public static class ReleaseManifestEvidence {
    public const string ManifestSchema="csharp-game-engine-formal-release-v1";
    public const string ManifestFileName="release-manifest.json";
    private const string ProductName="CSharpGameEngine";
    private const string Configuration="Release";
    private const string Target="win-x64";
    private const string RetentionClass="FormalRelease";
    public const string CandidateStageProfile="release-manifest-prerequisite";
    private static readonly TimeSpan MaximumEvidenceAge=TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions=
        FeatureRunResultStore.CreateJsonOptions();
    public static IReadOnlyList<string>RequiredEvidenceRoles{get;}=[
        "content-release-report",
        "ecs-release-report",
        "platform-release-report",
        "release-candidate-report",
        "release-candidate-stage-result",
        "release-package-report",
        "rendering-release-report",
        "vsix-release-report",
    ];

    public static async Task<int>RunAsync(
        string RepositoryRoot,FeatureStageCatalog Catalog,TextWriter Output,
        TextWriter Error,CancellationToken CancellationToken){
        string Root=Path.GetFullPath(RepositoryRoot);
        try {
            (string Commit,bool IsDirty)Provenance;
            try {
                Provenance=await GitChangedPathProvider.ReadProvenanceAsync(
                    Root,CancellationToken).ConfigureAwait(false);
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Provenance=("unknown",true);
                await Error.WriteLineAsync(
                    $"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false);
            }
            DateTimeOffset NowUtc=DateTimeOffset.UtcNow;
            (string CandidateStagePath,FeatureStageRunResult CandidateStage)=
                FindCandidateStage(Root,Catalog,Provenance,NowUtc);
            (string CandidatePath,ReleaseCandidateReport Candidate)=await FindReportAsync(
                Root,Path.Combine(Root,".artifacts","ReleaseCandidates"),
                ReleaseCandidateEvidence.ReportFileName,
                static Bytes=>JsonSerializer.Deserialize<ReleaseCandidateReport>(
                    Bytes,JsonOptions),
                Report=>Report.Configuration==Configuration
                    &&Report.Adapter=="Warp"
                    &&Report.Commit==Provenance.Commit
                    &&Report.IsDirty==Provenance.IsDirty
                    &&Report.CreatedUtc>=CandidateStage.StartedUtc
                    &&Report.CreatedUtc<=CandidateStage.FinishedUtc,
                static Report=>Report.CreatedUtc,
                (PathValue,Token)=>ReleaseCandidateEvidence.ValidateAsync(
                    Root,PathValue,Catalog,Token),
                "release candidate",CancellationToken).ConfigureAwait(false);
            Dictionary<string,FeatureStageRunResult>BoundStages=
                ReadBoundCandidateStages(Root,Candidate);
            (string PackagePath,ReleasePackageReport Package)=await FindReportAsync(
                Root,Path.Combine(Root,".artifacts","ReleasePackages"),
                ReleasePackageEvidence.ReportFileName,
                static Bytes=>JsonSerializer.Deserialize<ReleasePackageReport>(
                    Bytes,JsonOptions),
                Report=>MatchesTrackReport(
                    Report.Configuration,Report.Commit,Report.IsDirty,Report.CreatedUtc,
                    Provenance,BoundStages["release.packages"]),
                static Report=>Report.CreatedUtc,
                (PathValue,Token)=>ReleasePackageEvidence.ValidateAsync(
                    Root,PathValue,Token),
                "release package",CancellationToken).ConfigureAwait(false);
            (string RenderingPath,RenderingReleaseReport Rendering)=await FindReportAsync(
                Root,Path.Combine(Root,".artifacts","RenderingRelease"),
                RenderingReleaseEvidence.ReportFileName,
                static Bytes=>JsonSerializer.Deserialize<RenderingReleaseReport>(
                    Bytes,JsonOptions),
                Report=>MatchesTrackReport(
                    Report.Configuration,Report.Commit,Report.IsDirty,Report.CreatedUtc,
                    Provenance,BoundStages["rendering.release"]),
                static Report=>Report.CreatedUtc,
                (PathValue,Token)=>RenderingReleaseEvidence.ValidateAsync(
                    Root,PathValue,Token),
                "rendering release",CancellationToken).ConfigureAwait(false);
            (string ContentPath,ContentReleaseReport Content)=await FindReportAsync(
                Root,Path.Combine(Root,".artifacts","ContentRelease"),
                ContentReleaseEvidence.ReportFileName,
                static Bytes=>JsonSerializer.Deserialize<ContentReleaseReport>(
                    Bytes,JsonOptions),
                Report=>MatchesTrackReport(
                    Report.Configuration,Report.Commit,Report.IsDirty,Report.CreatedUtc,
                    Provenance,BoundStages["content.release"]),
                static Report=>Report.CreatedUtc,
                (PathValue,Token)=>ContentReleaseEvidence.ValidateAsync(
                    Root,PathValue,Token),
                "content release",CancellationToken).ConfigureAwait(false);
            (string EcsPath,EcsReleaseReport Ecs)=await FindReportAsync(
                Root,Path.Combine(Root,".artifacts","EcsRelease"),
                EcsReleaseEvidence.ReportFileName,
                static Bytes=>JsonSerializer.Deserialize<EcsReleaseReport>(
                    Bytes,JsonOptions),
                Report=>MatchesTrackReport(
                    Report.Configuration,Report.Commit,Report.IsDirty,Report.CreatedUtc,
                    Provenance,BoundStages["ecs.release"]),
                static Report=>Report.CreatedUtc,
                (PathValue,Token)=>EcsReleaseEvidence.ValidateAsync(
                    Root,PathValue,Token),
                "ECS release",CancellationToken).ConfigureAwait(false);
            (string PlatformPath,PlatformReleaseReport Platform)=await FindReportAsync(
                Root,Path.Combine(Root,".artifacts","PlatformRelease"),
                PlatformReleaseEvidence.ReportFileName,
                static Bytes=>JsonSerializer.Deserialize<PlatformReleaseReport>(
                    Bytes,JsonOptions),
                Report=>MatchesTrackReport(
                    Report.Configuration,Report.Commit,Report.IsDirty,Report.CreatedUtc,
                    Provenance,BoundStages["platform.release"]),
                static Report=>Report.CreatedUtc,
                (PathValue,Token)=>PlatformReleaseEvidence.ValidateAsync(
                    Root,PathValue,Token),
                "platform release",CancellationToken).ConfigureAwait(false);
            (string VsixPath,VsixReleaseReport Vsix)=await FindReportAsync(
                Root,Path.Combine(Root,".artifacts","VsixRelease"),
                VsixReleaseEvidence.ReportFileName,
                static Bytes=>JsonSerializer.Deserialize<VsixReleaseReport>(
                    Bytes,JsonOptions),
                Report=>MatchesTrackReport(
                    Report.Configuration,Report.Commit,Report.IsDirty,Report.CreatedUtc,
                    Provenance,BoundStages["vsix.release"]),
                static Report=>Report.CreatedUtc,
                (PathValue,Token)=>VsixReleaseEvidence.ValidateAsync(
                    Root,PathValue,Token),
                "VSIX release",CancellationToken).ConfigureAwait(false);
            if(Package.ProductVersion!=Vsix.Package.Version){
                throw new InvalidDataException(
                    "Product package and VSIX release versions differ.");
            }
            string PackageDirectory=Path.GetDirectoryName(PackagePath)!;
            string ArchivePath=ResolveRelative(PackageDirectory,Package.ArchivePath);
            string SigningPath=ResolveRelative(
                PackageDirectory,Package.SigningInputsPath);
            ReleasePackageSigningInputs Signing=JsonSerializer.Deserialize<
                ReleasePackageSigningInputs>(
                    await File.ReadAllBytesAsync(SigningPath,CancellationToken)
                        .ConfigureAwait(false),JsonOptions)
                ??throw new InvalidDataException("Release signing inputs are empty.");
            string PackagedVsixSha=await ReadArchiveEntryShaAsync(
                ArchivePath,"vsix/CSharpGameEngine.VSExtension.vsix",
                CancellationToken).ConfigureAwait(false);
            if(!FixedHashEquals(PackagedVsixSha,Vsix.Package.PackageSha256)){
                throw new InvalidDataException(
                    "Product archive and VSIX release evidence bind different VSIX bytes.");
            }
            var Sources=new Dictionary<string,string>(StringComparer.Ordinal){
                ["content-release-report"]=ContentPath,
                ["ecs-release-report"]=EcsPath,
                ["platform-release-report"]=PlatformPath,
                ["release-candidate-report"]=CandidatePath,
                ["release-candidate-stage-result"]=CandidateStagePath,
                ["release-package-report"]=PackagePath,
                ["rendering-release-report"]=RenderingPath,
                ["vsix-release-report"]=VsixPath,
            };
            PreparedReleaseEvidence[]Evidence=[.. RequiredEvidenceRoles.Select(Role=>{
                string PathValue=Sources[Role];
                byte[]Bytes=File.ReadAllBytes(PathValue);
                return new PreparedReleaseEvidence(
                    Role,PathValue,Sha256(Bytes),Bytes.LongLength);
            })];
            var Prepared=new PreparedReleaseManifest(
                ProductName,Package.ProductVersion,Configuration,Target,
                Provenance.Commit,Provenance.IsDirty,ArchivePath,SigningPath,
                Package.ArchiveSha256,Signing.ManifestSha256,Signing.SbomSha256,
                Evidence);
            string ManifestPath=await PublishPreparedAsync(
                Root,Prepared,CancellationToken).ConfigureAwait(false);
            ReleaseManifest Manifest=JsonSerializer.Deserialize<ReleaseManifest>(
                await File.ReadAllBytesAsync(ManifestPath,CancellationToken)
                    .ConfigureAwait(false),JsonOptions)
                ??throw new InvalidDataException("Formal release manifest is empty.");
            await Output.WriteLineAsync(
                $"releaseManifest id={Manifest.ReleaseId} version={Manifest.ProductVersion} "
                +$"archive={Manifest.Package.ArchiveSha256} retention={RetentionClass}")
                .ConfigureAwait(false);
            await Output.WriteLineAsync(ManifestPath).ConfigureAwait(false);
            return 0;
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            await Error.WriteLineAsync(
                $"Formal release manifest verification failed: {Ex.Message}")
                .ConfigureAwait(false);
            return 1;
        }
    }

    internal static async Task<string>PublishPreparedAsync(
        string RepositoryRoot,PreparedReleaseManifest Prepared,
        CancellationToken CancellationToken){
        string Root=Path.GetFullPath(RepositoryRoot);
        ValidatePreparedIdentity(Prepared);
        PreparedReleaseEvidence[]Evidence=[.. Prepared.Evidence.OrderBy(
            static Item=>Item.Role,StringComparer.Ordinal)];
        ValidatePreparedSources(Root,Prepared,Evidence);
        IReadOnlyList<string>PackageErrors=await ReleasePackageEvidence
            .ValidateBuiltPackageAsync(
                Prepared.ArchivePath,Prepared.SigningInputsPath,CancellationToken)
            .ConfigureAwait(false);
        if(PackageErrors.Count!=0){
            throw new InvalidDataException(string.Join(Environment.NewLine,PackageErrors));
        }
        byte[]ArchiveBytes=await File.ReadAllBytesAsync(
            Prepared.ArchivePath,CancellationToken).ConfigureAwait(false);
        byte[]SigningBytes=await File.ReadAllBytesAsync(
            Prepared.SigningInputsPath,CancellationToken).ConfigureAwait(false);
        ReleasePackageSigningInputs Signing=JsonSerializer.Deserialize<
            ReleasePackageSigningInputs>(SigningBytes,JsonOptions)
            ??throw new InvalidDataException("Release signing inputs are empty.");
        if(!FixedHashEquals(Signing.ArchiveSha256,Prepared.ArchiveSha256)
            ||!FixedHashEquals(Signing.ManifestSha256,Prepared.PackageManifestSha256)
            ||!FixedHashEquals(Signing.SbomSha256,Prepared.SbomSha256)
            ||Signing.ProductVersion!=Prepared.ProductVersion
            ||Signing.Configuration!=Configuration||Signing.Target!=Target){
            throw new InvalidDataException(
                "Prepared release identity does not match its signing inputs.");
        }
        string SigningSha=Sha256(SigningBytes);
        var Identity=new ReleaseIdentity(
            ManifestSchema,Prepared.Product,Prepared.ProductVersion,
            Prepared.Configuration,Prepared.Target,Prepared.Commit,Prepared.IsDirty,
            Prepared.ArchiveSha256,SigningSha,Prepared.PackageManifestSha256,
            Prepared.SbomSha256,[.. Evidence.Select(static Item=>
                new ReleaseIdentityEvidence(Item.Role,Item.Sha256,Item.ByteLength))]);
        string ReleaseId=Sha256(JsonSerializer.SerializeToUtf8Bytes(Identity,JsonOptions));
        string ReleasesRoot=Path.Combine(Root,".artifacts","Releases");
        string Destination=Path.Combine(ReleasesRoot,ReleaseId);
        string ManifestPath=Path.Combine(Destination,ManifestFileName);
        if(Directory.Exists(Destination)){
            IReadOnlyList<string>ExistingErrors=await ValidateAsync(
                Root,ManifestPath,CancellationToken).ConfigureAwait(false);
            if(ExistingErrors.Count!=0){
                throw new InvalidDataException(
                    "Existing formal release is invalid and cannot be overwritten: "
                    +string.Join(Environment.NewLine,ExistingErrors));
            }
            return ManifestPath;
        }
        string Temporary=Path.Combine(
            ReleasesRoot,$".tmp-{ReleaseId}-{Guid.NewGuid():N}");
        bool DestinationCreated=false;
        try {
            Directory.CreateDirectory(Path.Combine(Temporary,"package"));
            Directory.CreateDirectory(Path.Combine(Temporary,"evidence"));
            string ArchiveRelative="package/"+Signing.ArchiveFileName;
            string SigningRelative="package/signing-inputs.json";
            await File.WriteAllBytesAsync(
                ResolveRelative(Temporary,ArchiveRelative),ArchiveBytes,CancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllBytesAsync(
                ResolveRelative(Temporary,SigningRelative),SigningBytes,CancellationToken)
                .ConfigureAwait(false);
            var ReleasedEvidence=new List<ReleaseEvidenceReference>(Evidence.Length);
            foreach(PreparedReleaseEvidence Source in Evidence){
                string RelativePath="evidence/"+Source.Role+".json";
                byte[]Bytes=await File.ReadAllBytesAsync(
                    Source.SourcePath,CancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(
                    ResolveRelative(Temporary,RelativePath),Bytes,CancellationToken)
                    .ConfigureAwait(false);
                ReleasedEvidence.Add(new ReleaseEvidenceReference(
                    Source.Role,RelativePath,Relative(Root,Source.SourcePath),
                    Source.Sha256,Source.ByteLength));
            }
            DateTimeOffset CreatedUtc=DateTimeOffset.UtcNow;
            var Retention=new ReleaseRetention(
                RetentionClass,true,null,ReleaseId);
            var Package=new ReleasePackageReference(
                ArchiveRelative,Prepared.ArchiveSha256,ArchiveBytes.LongLength,
                SigningRelative,SigningSha,Prepared.PackageManifestSha256,
                Prepared.SbomSha256);
            var Payload=new ReleaseManifestPayload(
                ManifestSchema,ReleaseId,CreatedUtc,Prepared.Product,
                Prepared.ProductVersion,Prepared.Configuration,Prepared.Target,
                Prepared.Commit,Prepared.IsDirty,Retention,Package,ReleasedEvidence);
            string ManifestSha=Sha256(
                JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions));
            var Manifest=new ReleaseManifest(
                Payload.Schema,ManifestSha,Payload.ReleaseId,Payload.CreatedUtc,
                Payload.Product,Payload.ProductVersion,Payload.Configuration,
                Payload.Target,Payload.Commit,Payload.IsDirty,Payload.Retention,
                Payload.Package,Payload.Evidence);
            await WriteCanonicalAsync(
                Path.Combine(Temporary,ManifestFileName),Manifest,CancellationToken)
                .ConfigureAwait(false);
            Directory.CreateDirectory(ReleasesRoot);
            Directory.Move(Temporary,Destination);
            DestinationCreated=true;
            IReadOnlyList<string>Errors=await ValidateAsync(
                Root,ManifestPath,CancellationToken).ConfigureAwait(false);
            if(Errors.Count!=0){
                throw new InvalidDataException(string.Join(Environment.NewLine,Errors));
            }
            return ManifestPath;
        } catch {
            if(Directory.Exists(Temporary)){ Directory.Delete(Temporary,true); }
            if(DestinationCreated&&Directory.Exists(Destination)){
                Directory.Delete(Destination,true);
            }
            throw;
        }
    }

    public static async Task<IReadOnlyList<string>>ValidateAsync(
        string RepositoryRoot,string ManifestPath,
        CancellationToken CancellationToken=default){
        var Errors=new List<string>();
        string Root=Path.GetFullPath(RepositoryRoot);
        string FullManifestPath=Path.GetFullPath(ManifestPath);
        ReleaseManifest?Manifest;
        byte[]ManifestBytes;
        try {
            string ReleasesRoot=Path.Combine(Root,".artifacts","Releases");
            if(!IsContained(ReleasesRoot,FullManifestPath)){
                return ["Formal release manifest must be inside .artifacts/Releases."];
            }
            EnsureNoReparsePoints(Root,FullManifestPath);
            ManifestBytes=await File.ReadAllBytesAsync(
                FullManifestPath,CancellationToken).ConfigureAwait(false);
            Manifest=JsonSerializer.Deserialize<ReleaseManifest>(ManifestBytes,JsonOptions);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            return [$"Formal release manifest cannot be read: {Ex.Message}"];
        }
        if(Manifest is null){ return ["Formal release manifest is empty."]; }
        try {
            string ReleaseRoot=Path.GetDirectoryName(FullManifestPath)!;
            if(Path.GetFileName(FullManifestPath)!=ManifestFileName
                ||Path.GetFileName(ReleaseRoot)!=Manifest.ReleaseId){
                Errors.Add("Formal release path does not match its release ID.");
            }
            if(Manifest.Schema!=ManifestSchema||Manifest.Product!=ProductName
                ||Manifest.Configuration!=Configuration||Manifest.Target!=Target){
                Errors.Add("Formal release identity is unsupported.");
            }
            if(Manifest.Retention.Class!=RetentionClass
                ||!Manifest.Retention.Immutable
                ||Manifest.Retention.ExpiresUtc is not null
                ||Manifest.Retention.RollbackIdentity!=Manifest.ReleaseId){
                Errors.Add("Formal release retention must be immutable and non-expiring.");
            }
            if(!ManifestBytes.AsSpan().SequenceEqual(CanonicalBytes(Manifest))){
                Errors.Add("Formal release manifest is not canonically encoded.");
            }
            var Payload=new ReleaseManifestPayload(
                Manifest.Schema,Manifest.ReleaseId,Manifest.CreatedUtc,Manifest.Product,
                Manifest.ProductVersion,Manifest.Configuration,Manifest.Target,
                Manifest.Commit,Manifest.IsDirty,Manifest.Retention,Manifest.Package,
                Manifest.Evidence);
            if(!FixedHashEquals(
                Sha256(JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions)),
                Manifest.ManifestSha256)){
                Errors.Add("Formal release manifest SHA-256 mismatch.");
            }
            ValidateEvidence(ReleaseRoot,Manifest.Evidence,Errors);
            string ArchivePath=ResolveRelative(
                ReleaseRoot,Manifest.Package.ArchivePath);
            string SigningPath=ResolveRelative(
                ReleaseRoot,Manifest.Package.SigningInputsPath);
            byte[]ArchiveBytes=File.ReadAllBytes(ArchivePath);
            byte[]SigningBytes=File.ReadAllBytes(SigningPath);
            if(ArchiveBytes.LongLength!=Manifest.Package.ArchiveByteLength
                ||!FixedHashEquals(Sha256(ArchiveBytes),Manifest.Package.ArchiveSha256)
                ||!FixedHashEquals(
                    Sha256(SigningBytes),Manifest.Package.SigningInputsSha256)){
                Errors.Add("Formal release package or signing-input SHA-256 mismatch.");
            }
            ReleasePackageSigningInputs?Signing=JsonSerializer.Deserialize<
                ReleasePackageSigningInputs>(SigningBytes,JsonOptions);
            if(Signing is null
                ||!FixedHashEquals(Signing.ArchiveSha256,Manifest.Package.ArchiveSha256)
                ||!FixedHashEquals(
                    Signing.ManifestSha256,Manifest.Package.PackageManifestSha256)
                ||!FixedHashEquals(Signing.SbomSha256,Manifest.Package.SbomSha256)){
                Errors.Add("Formal release signing identity changed.");
            }
            foreach(string PackageError in await ReleasePackageEvidence
                .ValidateBuiltPackageAsync(ArchivePath,SigningPath,CancellationToken)
                .ConfigureAwait(false)){
                Errors.Add(PackageError);
            }
            var Identity=new ReleaseIdentity(
                Manifest.Schema,Manifest.Product,Manifest.ProductVersion,
                Manifest.Configuration,Manifest.Target,Manifest.Commit,Manifest.IsDirty,
                Manifest.Package.ArchiveSha256,Manifest.Package.SigningInputsSha256,
                Manifest.Package.PackageManifestSha256,Manifest.Package.SbomSha256,
                [.. Manifest.Evidence.Select(static Item=>
                    new ReleaseIdentityEvidence(
                        Item.Role,Item.Sha256,Item.ByteLength))]);
            if(!FixedHashEquals(
                Sha256(JsonSerializer.SerializeToUtf8Bytes(Identity,JsonOptions)),
                Manifest.ReleaseId)){
                Errors.Add("Formal release ID does not match its immutable identity.");
            }
            string[]ExpectedFiles=[
                FullManifestPath,ArchivePath,SigningPath,
                .. Manifest.Evidence.Select(Item=>ResolveRelative(ReleaseRoot,Item.Path)),
            ];
            string[]ActualFiles=[.. Directory.EnumerateFiles(
                ReleaseRoot,"*",SearchOption.AllDirectories)
                .Select(Path.GetFullPath).Order(StringComparer.Ordinal)];
            if(!ActualFiles.SequenceEqual(
                ExpectedFiles.Select(Path.GetFullPath).Order(StringComparer.Ordinal),
                StringComparer.Ordinal)){
                Errors.Add("Formal release has missing or extra files.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"Formal release cannot be validated: {Ex.Message}");
        }
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static (string Path,FeatureStageRunResult Result)FindCandidateStage(
        string Root,FeatureStageCatalog Catalog,(string Commit,bool IsDirty)Provenance,
        DateTimeOffset NowUtc){
        string StageRoot=Path.Combine(Root,".artifacts","FeatureStages");
        FeatureStageDefinition Stage=Catalog.GetStage("release.candidate");
        var Candidates=new List<(string Path,FeatureStageRunResult Result)>();
        if(Directory.Exists(StageRoot)){
            foreach(string PathValue in Directory.EnumerateFiles(
                StageRoot,"*.run-result.json",SearchOption.TopDirectoryOnly)){
                try {
                    FeatureRunResultDocument?Document=JsonSerializer.Deserialize<
                        FeatureRunResultDocument>(File.ReadAllBytes(PathValue),JsonOptions);
                    if(Document?.SchemaVersion!=
                        FeatureRunResultDocument.CurrentSchemaVersion){ continue; }
                    Candidates.AddRange(Document.Results.Where(Result=>
                        Result.StageId==Stage.Id
                        &&Result.FeatureIds.SequenceEqual(
                            Stage.FeatureIds,StringComparer.Ordinal)
                        &&Result.Pack==Stage.Pack.ToString()
                        &&Result.Configuration==Configuration
                        &&Result.Profile==CandidateStageProfile
                        &&Result.Adapter=="Warp"
                        &&Result.Seed==Stage.Seed&&Result.Ticks==Stage.Ticks
                        &&Result.GitCommit==Provenance.Commit
                        &&Result.IsDirty==Provenance.IsDirty
                        &&Result.Status==FeatureStageRunStatus.Passed
                        &&Result.Errors.Count==0
                        &&Result.Assertions.Count>=Stage.Commands.Count
                        &&Result.Assertions.All(static Assertion=>Assertion.Passed)
                        &&Result.FinishedUtc>=Result.StartedUtc
                        &&Result.FinishedUtc<=NowUtc
                        &&NowUtc-Result.FinishedUtc<=MaximumEvidenceAge
                        &&FixedHashEquals(
                            ReleaseCandidateEvidence.ComputeStageArtifactHash(Result),
                            Result.ArtifactSha256))
                        .Select(Result=>(PathValue,Result)));
                } catch(Exception Ex)when(
                    Ex is JsonException or IOException or UnauthorizedAccessException
                    or InvalidDataException){ }
            }
        }
        return Candidates.OrderByDescending(
                static Candidate=>Candidate.Result.FinishedUtc).FirstOrDefault()
            is{Path.Length:>0}Match
            ?Match
            :throw new InvalidDataException(
                $"No current passing {CandidateStageProfile} release.candidate result exists.");
    }

    private static Dictionary<string,FeatureStageRunResult>ReadBoundCandidateStages(
        string Root,ReleaseCandidateReport Candidate){
        var Results=new Dictionary<string,FeatureStageRunResult>(StringComparer.Ordinal);
        foreach(ReleaseCandidateStageEvidence Evidence in Candidate.Stages){
            string PathValue=ResolveRelative(Root,Evidence.ResultPath);
            FeatureRunResultDocument?Document=JsonSerializer.Deserialize<
                FeatureRunResultDocument>(File.ReadAllBytes(PathValue),JsonOptions);
            FeatureStageRunResult?Result=Document?.Results.SingleOrDefault(Item=>
                Item.StageId==Evidence.StageId
                &&FixedHashEquals(Item.ArtifactSha256,Evidence.ArtifactSha256));
            if(Result is null){
                throw new InvalidDataException(
                    $"RC Stage result is unavailable: {Evidence.StageId}.");
            }
            Results.Add(Evidence.StageId,Result);
        }
        foreach(string Required in new[]{
            "content.release","ecs.release","platform.release","release.packages",
            "rendering.release","vsix.release",
        }){
            if(!Results.ContainsKey(Required)){
                throw new InvalidDataException(
                    $"RC report is missing formal release input Stage {Required}.");
            }
        }
        return Results;
    }

    private static bool MatchesTrackReport(
        string ReportConfiguration,string Commit,bool IsDirty,
        DateTimeOffset CreatedUtc,(string Commit,bool IsDirty)Provenance,
        FeatureStageRunResult Stage)=>
        ReportConfiguration==Configuration
        &&Commit==Provenance.Commit&&IsDirty==Provenance.IsDirty
        &&CreatedUtc>=Stage.StartedUtc&&CreatedUtc<=Stage.FinishedUtc;

    private static async Task<(string Path,T Report)>FindReportAsync<T>(
        string Root,string SearchRoot,string FileName,Func<byte[],T?>Deserialize,
        Func<T,bool>Predicate,Func<T,DateTimeOffset>CreatedUtc,
        Func<string,CancellationToken,Task<IReadOnlyList<string>>>Validate,
        string Label,CancellationToken CancellationToken)where T:class{
        var Candidates=new List<(string Path,T Report)>();
        if(Directory.Exists(SearchRoot)){
            foreach(string PathValue in Directory.EnumerateFiles(
                SearchRoot,FileName,SearchOption.AllDirectories)){
                CancellationToken.ThrowIfCancellationRequested();
                try {
                    string FullPath=Path.GetFullPath(PathValue);
                    if(!IsContained(SearchRoot,FullPath)){ continue; }
                    EnsureNoReparsePoints(Root,FullPath);
                    T?Report=Deserialize(await File.ReadAllBytesAsync(
                        FullPath,CancellationToken).ConfigureAwait(false));
                    if(Report is null||!Predicate(Report)){ continue; }
                    IReadOnlyList<string>Errors=await Validate(
                        FullPath,CancellationToken).ConfigureAwait(false);
                    if(Errors.Count==0){ Candidates.Add((FullPath,Report)); }
                } catch(Exception Ex)when(
                    Ex is JsonException or IOException or UnauthorizedAccessException
                    or InvalidDataException){ }
            }
        }
        return Candidates.OrderByDescending(Candidate=>CreatedUtc(Candidate.Report))
            .FirstOrDefault()is{Path.Length:>0}Match
            ?Match
            :throw new InvalidDataException(
                $"No valid {Label} report matches the RC Stage window and provenance.");
    }

    private static async Task<string>ReadArchiveEntryShaAsync(
        string ArchivePath,string EntryPath,CancellationToken CancellationToken){
        using ZipArchive Archive=ZipFile.OpenRead(ArchivePath);
        ZipArchiveEntry Entry=Archive.GetEntry(EntryPath)
            ??throw new InvalidDataException(
                $"Release archive is missing {EntryPath}.");
        await using Stream Stream=Entry.Open();
        using SHA256 Hash=SHA256.Create();
        byte[]Bytes=await Hash.ComputeHashAsync(Stream,CancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexString(Bytes).ToLowerInvariant();
    }

    private static void ValidatePreparedIdentity(PreparedReleaseManifest Prepared){
        if(Prepared.Product!=ProductName||Prepared.Configuration!=Configuration
            ||Prepared.Target!=Target||string.IsNullOrWhiteSpace(Prepared.ProductVersion)
            ||string.IsNullOrWhiteSpace(Prepared.Commit)){
            throw new InvalidDataException("Prepared formal release identity is invalid.");
        }
        string[]Roles=[.. Prepared.Evidence.Select(static Item=>Item.Role)
            .Order(StringComparer.Ordinal)];
        if(!Roles.SequenceEqual(RequiredEvidenceRoles,StringComparer.Ordinal)
            ||Roles.Distinct(StringComparer.Ordinal).Count()!=Roles.Length){
            throw new InvalidDataException(
                "Prepared formal release evidence roles are incomplete.");
        }
    }

    private static void ValidatePreparedSources(
        string Root,PreparedReleaseManifest Prepared,
        IReadOnlyList<PreparedReleaseEvidence>Evidence){
        foreach(PreparedReleaseEvidence Source in Evidence){
            string PathValue=Path.GetFullPath(Source.SourcePath);
            if(!IsContained(Root,PathValue)){
                throw new InvalidDataException(
                    $"Prepared release evidence escaped the repository: {Source.Role}.");
            }
            EnsureNoReparsePoints(Root,PathValue);
            byte[]Bytes=File.ReadAllBytes(PathValue);
            if(Bytes.LongLength!=Source.ByteLength
                ||!FixedHashEquals(Sha256(Bytes),Source.Sha256)){
                throw new InvalidDataException(
                    $"Prepared release evidence changed: {Source.Role}.");
            }
        }
        foreach(string PathValue in new[]{
            Prepared.ArchivePath,Prepared.SigningInputsPath,
        }){
            string FullPath=Path.GetFullPath(PathValue);
            if(!IsContained(Root,FullPath)){
                throw new InvalidDataException(
                    "Prepared release package escaped the repository.");
            }
            EnsureNoReparsePoints(Root,FullPath);
        }
        if(!FixedHashEquals(
            Sha256(File.ReadAllBytes(Prepared.ArchivePath)),Prepared.ArchiveSha256)){
            throw new InvalidDataException("Prepared release archive changed.");
        }
    }

    private static void ValidateEvidence(
        string ReleaseRoot,IReadOnlyList<ReleaseEvidenceReference>Evidence,
        List<string>Errors){
        string[]Roles=[.. Evidence.Select(static Item=>Item.Role)];
        if(!Roles.SequenceEqual(RequiredEvidenceRoles,StringComparer.Ordinal)
            ||Roles.Distinct(StringComparer.Ordinal).Count()!=Roles.Length){
            Errors.Add("Formal release evidence roles are incomplete or unordered.");
        }
        foreach(ReleaseEvidenceReference Item in Evidence){
            try {
                if(Item.Path!="evidence/"+Item.Role+".json"){
                    Errors.Add($"Formal release evidence path changed: {Item.Role}.");
                }
                string PathValue=ResolveRelative(ReleaseRoot,Item.Path);
                EnsureNoReparsePoints(ReleaseRoot,PathValue);
                byte[]Bytes=File.ReadAllBytes(PathValue);
                if(Bytes.LongLength!=Item.ByteLength
                    ||!FixedHashEquals(Sha256(Bytes),Item.Sha256)){
                    Errors.Add($"Formal release evidence SHA-256 mismatch: {Item.Role}.");
                }
                if(string.IsNullOrWhiteSpace(Item.SourcePath)
                    ||Path.IsPathRooted(Item.SourcePath)
                    ||Item.SourcePath.Split('/').Contains("..",StringComparer.Ordinal)){
                    Errors.Add($"Formal release evidence source path is invalid: {Item.Role}.");
                }
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Errors.Add($"Formal release evidence cannot be read: {Item.Role}: {Ex.Message}");
            }
        }
    }

    private static byte[]CanonicalBytes<T>(T Value){
        byte[]Json=JsonSerializer.SerializeToUtf8Bytes(Value,JsonOptions);
        byte[]Canonical=new byte[Json.Length+1];
        Json.CopyTo(Canonical,0);
        Canonical[^1]=(byte)'\n';
        return Canonical;
    }

    private static async Task WriteCanonicalAsync<T>(
        string PathValue,T Value,CancellationToken CancellationToken){
        await File.WriteAllBytesAsync(
            PathValue,CanonicalBytes(Value),CancellationToken).ConfigureAwait(false);
    }

    private static string ResolveRelative(string Root,string RelativePath){
        string FullPath=Path.GetFullPath(Path.Combine(
            Root,RelativePath.Replace('/',Path.DirectorySeparatorChar)));
        if(!IsContained(Root,FullPath)){
            throw new InvalidDataException($"Path escaped its root: {RelativePath}.");
        }
        return FullPath;
    }

    private static string Relative(string Root,string PathValue)=>
        Path.GetRelativePath(Root,PathValue).Replace(Path.DirectorySeparatorChar,'/');

    private static bool IsContained(string Parent,string Candidate){
        string Root=Path.GetFullPath(Parent).TrimEnd(
            Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)
            +Path.DirectorySeparatorChar;
        return Path.GetFullPath(Candidate).StartsWith(
            Root,StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePoints(string Root,string PathValue){
        string FullRoot=Path.GetFullPath(Root);
        string Current=Path.GetFullPath(PathValue);
        while(IsContained(FullRoot,Current)){
            if(File.Exists(Current)||Directory.Exists(Current)){
                if((File.GetAttributes(Current)&FileAttributes.ReparsePoint)!=0){
                    throw new InvalidDataException(
                        $"Path contains a reparse point: {Current}.");
                }
            }
            if(Current.Equals(FullRoot,StringComparison.OrdinalIgnoreCase)){ break; }
            string?Parent=Path.GetDirectoryName(Current);
            if(Parent is null){ break; }
            Current=Parent;
        }
    }

    private static bool FixedHashEquals(string Left,string Right){
        if(Left.Length!=64||Right.Length!=64){ return false; }
        try {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(Left),Convert.FromHexString(Right));
        } catch(FormatException){ return false; }
    }

    private static string Sha256(ReadOnlySpan<byte>Bytes)=>
        Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
}
