using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.FeatureStages;

public sealed record ContentReleaseInput(string Role,string Path,string Sha256,long ByteLength);
public sealed record ContentReleasePackageFile(string Path,string Sha256,long ByteLength);
public sealed record ContentReleasePerformanceEvidence(
    string Id,string ReportPath,string FileSha256,string ReportSha256,
    DateTimeOffset CreatedUtc,string MachineFingerprintSha256);
public sealed record ContentReleaseReportPayload(
    string Schema,DateTimeOffset CreatedUtc,string Configuration,string Commit,bool IsDirty,
    int AssetMetadataSchemaVersion,int ShippingManifestSchemaVersion,
    string ArtifactEnvelopeSchema,string PackageRelativePath,string PackageManifestFingerprint,
    IReadOnlyList<ContentReleasePackageFile>PackageFiles,
    IReadOnlyList<ContentReleaseInput>Inputs,
    IReadOnlyList<ContentReleasePerformanceEvidence>Performance);
public sealed record ContentReleaseReport(
    string Schema,string ReportSha256,DateTimeOffset CreatedUtc,string Configuration,
    string Commit,bool IsDirty,int AssetMetadataSchemaVersion,
    int ShippingManifestSchemaVersion,string ArtifactEnvelopeSchema,
    string PackageRelativePath,string PackageManifestFingerprint,
    IReadOnlyList<ContentReleasePackageFile>PackageFiles,
    IReadOnlyList<ContentReleaseInput>Inputs,
    IReadOnlyList<ContentReleasePerformanceEvidence>Performance);

/// <summary>
/// Publishes and revalidates the content-scoped release evidence consumed later by the
/// product-wide package/SBOM/signing stages. This is evidence over existing content truths;
/// it is deliberately not a second artifact, package, or release-manifest format.
/// </summary>
public static class ContentReleaseEvidence {
    public const string ReportSchema="csharp-game-engine-content-release-v1";
    public const string ReportFileName="content-release.json";
    private const string PackageDirectoryName="package";
    private const string ArtifactEnvelopeSchema="CGEA/1";
    private static readonly TimeSpan MaximumPerformanceAge=TimeSpan.FromHours(2);
    private static readonly StableId FixtureNamespace=StableId.Parse(
        "438b6d83-0b1d-5ff4-aa88-759836b4f858");
    private static readonly InputDefinition[]InputDefinitions=[
        new("schema.asset-metadata","src/CSharpGameEngine.Content/AssetIdentity.cs"),
        new("schema.artifact-envelope","src/CSharpGameEngine.Content/ContentArtifact.cs"),
        new("schema.shipping-manifest","src/CSharpGameEngine.Content/ShippingContentManifest.cs"),
        new("package.publisher","src/CSharpGameEngine.Content/ShippingContentPackage.cs"),
        new("sbom.project","src/CSharpGameEngine.Contracts/CSharpGameEngine.Contracts.csproj"),
        new("sbom.project","src/CSharpGameEngine.Content/CSharpGameEngine.Content.csproj"),
        new("sbom.project","src/CSharpGameEngine.Import/CSharpGameEngine.Import.csproj"),
        new("sbom.project","tools/CSharpGameEngine.ContentCompiler/CSharpGameEngine.ContentCompiler.csproj"),
        new("sbom.project","tools/CSharpGameEngine.Import.AssetAuthoring.Cli/CSharpGameEngine.Import.AssetAuthoring.Cli.csproj"),
        new("sbom.toolchain","global.json"),
    ];
    private static readonly string[]RequiredPerformanceIds=[
        "content.cold-compile","content.one-change",
    ];

    public static IEnumerable<string>RequiredRepositoryInputPaths=>
        InputDefinitions.Select(static Definition=>Definition.Path);

    public static async Task<int>RunAsync(
        string RepositoryRoot,string Configuration,TextWriter Output,TextWriter Error,
        CancellationToken CancellationToken){
        string Root=Path.GetFullPath(RepositoryRoot);
        ValidateConfiguration(Configuration);
        DateTimeOffset CreatedUtc=DateTimeOffset.UtcNow;
        (string Commit,bool IsDirty)Provenance;
        try {
            Provenance=await GitChangedPathProvider.ReadProvenanceAsync(Root,CancellationToken)
                .ConfigureAwait(false);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Provenance=("unknown",true);
            await Error.WriteLineAsync(
                $"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false);
        }

        string EvidenceRoot=Path.Combine(
            Root,".artifacts","ContentRelease",
            $"{CreatedUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        string PackageRoot=Path.Combine(EvidenceRoot,PackageDirectoryName);
        string ReportPath=Path.Combine(EvidenceRoot,ReportFileName);
        Directory.CreateDirectory(EvidenceRoot);
        bool Completed=false;
        try {
            await PublishFixturePackageAsync(PackageRoot,CancellationToken).ConfigureAwait(false);
            ShippingContentPackage Package=await ShippingContentPackage.OpenAsync(
                PackageRoot,CancellationToken).ConfigureAwait(false);
            ContentReleasePackageFile[]PackageFiles=ReadPackageFiles(PackageRoot);
            ContentReleaseInput[]Inputs=ReadInputs(Root);
            ContentReleasePerformanceEvidence[]Performance=ReadPerformanceEvidence(
                Root,Configuration,Provenance,CreatedUtc);
            var Payload=new ContentReleaseReportPayload(
                ReportSchema,CreatedUtc,Configuration,Provenance.Commit,Provenance.IsDirty,
                AssetMetadata.CurrentSchemaVersion,
                ShippingContentManifestCodec.CurrentSchemaVersion,ArtifactEnvelopeSchema,
                PackageDirectoryName,Package.Manifest.Fingerprint.ToString(),PackageFiles,Inputs,
                Performance);
            string PayloadHash=Sha256(JsonSerializer.SerializeToUtf8Bytes(
                Payload,FeatureRunResultStore.CreateJsonOptions()));
            var Report=new ContentReleaseReport(
                Payload.Schema,PayloadHash,Payload.CreatedUtc,Payload.Configuration,Payload.Commit,
                Payload.IsDirty,Payload.AssetMetadataSchemaVersion,
                Payload.ShippingManifestSchemaVersion,Payload.ArtifactEnvelopeSchema,
                Payload.PackageRelativePath,Payload.PackageManifestFingerprint,
                Payload.PackageFiles,Payload.Inputs,Payload.Performance);
            await WriteCanonicalAsync(ReportPath,Report,CancellationToken).ConfigureAwait(false);
            IReadOnlyList<string>Errors=await ValidateAsync(
                Root,ReportPath,CancellationToken).ConfigureAwait(false);
            foreach(string ValidationError in Errors){
                await Error.WriteLineAsync(ValidationError).ConfigureAwait(false);
            }
            if(Errors.Count!=0){ return 1; }
            Completed=true;
            await Output.WriteLineAsync(
                $"contentRelease packageFiles={PackageFiles.Length} artifacts={Package.Manifest.Entries.Length} inputs={Inputs.Length} performanceReports={Performance.Length}")
                .ConfigureAwait(false);
            await Output.WriteLineAsync(ReportPath).ConfigureAwait(false);
            return 0;
        } catch(Exception Ex)when(Ex is not OperationCanceledException
            &&Ex is InvalidDataException or IOException or UnauthorizedAccessException
                or JsonException or CryptographicException){
            await Error.WriteLineAsync(
                $"Content release verification failed: {Ex.Message}").ConfigureAwait(false);
            return 1;
        } finally {
            if(!Completed&&Directory.Exists(EvidenceRoot)){ Directory.Delete(EvidenceRoot,true); }
        }
    }

    public static async Task<IReadOnlyList<string>>ValidateAsync(
        string RepositoryRoot,string ReportPath,CancellationToken CancellationToken=default){
        var Errors=new List<string>();
        string Root;
        string FullReportPath;
        byte[]ReportBytes;
        ContentReleaseReport?Report;
        try {
            Root=Path.GetFullPath(RepositoryRoot);
            FullReportPath=Path.GetFullPath(ReportPath);
            string EvidenceParent=Path.Combine(Root,".artifacts","ContentRelease");
            if(!IsContained(EvidenceParent,FullReportPath)){
                return ["Content release report must be inside .artifacts/ContentRelease."];
            }
            EnsureNoReparsePoints(Root,FullReportPath);
            ReportBytes=await File.ReadAllBytesAsync(FullReportPath,CancellationToken)
                .ConfigureAwait(false);
            Report=JsonSerializer.Deserialize<ContentReleaseReport>(
                ReportBytes,FeatureRunResultStore.CreateJsonOptions());
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            return [$"Content release report cannot be read: {Ex.Message}"];
        }
        if(Report is null){ return ["Content release report is empty."]; }
        try {
            ValidateConfiguration(Report.Configuration);
            byte[]Canonical=CanonicalBytes(Report);
            if(!ReportBytes.AsSpan().SequenceEqual(Canonical)){
                Errors.Add("Content release report is not canonically encoded.");
            }
            if(Report.Schema!=ReportSchema){
                Errors.Add($"Content release report schema must be {ReportSchema}.");
            }
            var Payload=new ContentReleaseReportPayload(
                Report.Schema,Report.CreatedUtc,Report.Configuration,Report.Commit,Report.IsDirty,
                Report.AssetMetadataSchemaVersion,Report.ShippingManifestSchemaVersion,
                Report.ArtifactEnvelopeSchema,Report.PackageRelativePath,
                Report.PackageManifestFingerprint,Report.PackageFiles,Report.Inputs,
                Report.Performance);
            string ExpectedReportHash=Sha256(JsonSerializer.SerializeToUtf8Bytes(
                Payload,FeatureRunResultStore.CreateJsonOptions()));
            if(!FixedHashEquals(ExpectedReportHash,Report.ReportSha256)){
                Errors.Add("Content release report SHA-256 mismatch.");
            }
            if(Report.AssetMetadataSchemaVersion!=AssetMetadata.CurrentSchemaVersion){
                Errors.Add("Asset metadata schema evidence does not match the runtime codec.");
            }
            if(Report.ShippingManifestSchemaVersion!=
                ShippingContentManifestCodec.CurrentSchemaVersion){
                Errors.Add("Shipping manifest schema evidence does not match the runtime codec.");
            }
            if(Report.ArtifactEnvelopeSchema!=ArtifactEnvelopeSchema){
                Errors.Add("Artifact envelope schema evidence is unsupported.");
            }
            ValidateInputs(Root,Report.Inputs,Errors);
            await ValidatePackageAsync(
                Root,FullReportPath,Report,CancellationToken,Errors).ConfigureAwait(false);
            ValidatePerformance(Root,Report,Errors);
            ValidateEvidenceClosure(FullReportPath,Report,Errors);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"Content release evidence validation failed: {Ex.Message}");
        }
        return Errors;
    }

    private static async Task PublishFixturePackageAsync(
        string PackageRoot,CancellationToken CancellationToken){
        string WorkRoot=Path.Combine(
            Path.GetTempPath(),"CgeContentRelease",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WorkRoot);
        try {
            var Store=new ContentAddressedStore(Path.Combine(WorkRoot,"cas"));
            var Published=new List<PublishedContentArtifact>();
            PublishedContentArtifact Texture=await PublishAsync(
                Store,"texture",ContentArtifactKind.Texture,[],CancellationToken).ConfigureAwait(false);
            Published.Add(Texture);
            PublishedContentArtifact Shader=await PublishAsync(
                Store,"shader",ContentArtifactKind.Shader,[],CancellationToken).ConfigureAwait(false);
            Published.Add(Shader);
            PublishedContentArtifact Material=await PublishAsync(
                Store,"material",ContentArtifactKind.Material,
                [Texture.Reference,Shader.Reference],CancellationToken).ConfigureAwait(false);
            Published.Add(Material);
            PublishedContentArtifact Mesh=await PublishAsync(
                Store,"mesh",ContentArtifactKind.Mesh,[],CancellationToken).ConfigureAwait(false);
            Published.Add(Mesh);
            PublishedContentArtifact Audio=await PublishAsync(
                Store,"audio",ContentArtifactKind.Audio,[],CancellationToken).ConfigureAwait(false);
            Published.Add(Audio);
            PublishedContentArtifact Cell=await PublishAsync(
                Store,"cell",ContentArtifactKind.WorldCell,
                [Mesh.Reference,Material.Reference,Audio.Reference],CancellationToken)
                .ConfigureAwait(false);
            Published.Add(Cell);
            PublishedContentArtifact Scene=await PublishAsync(
                Store,"scene",ContentArtifactKind.Scene,[Cell.Reference],CancellationToken)
                .ConfigureAwait(false);
            Published.Add(Scene);
            var Index=new ContentArtifactIndex(Published);
            ShippingContentManifest Manifest=await ShippingContentManifestBuilder.BuildAsync(
                Store,Index,[Scene.Reference],CancellationToken).ConfigureAwait(false);
            await ShippingContentPackage.PublishAsync(
                Store,Manifest,PackageRoot,CancellationToken).ConfigureAwait(false);
        } finally {
            if(Directory.Exists(WorkRoot)){ Directory.Delete(WorkRoot,true); }
        }
    }

    private static async ValueTask<PublishedContentArtifact>PublishAsync(
        ContentAddressedStore Store,string Name,ContentArtifactKind Kind,
        IEnumerable<AssetArtifactReference>Dependencies,CancellationToken CancellationToken){
        byte[]Payload=Encoding.UTF8.GetBytes("content-release-fixture:"+Name+":v1");
        var Artifact=new ContentArtifact(
            Kind,new ArtifactProvenance(
                "content-release/1","asset://release-fixture/"+Name,
                Hash256.Compute(Payload),"built-in-release-fixture",
                Hash256.Compute(Encoding.UTF8.GetBytes("canonical-options-v1"))),
            Dependencies,Payload);
        var Key=new AssetKey(
            new AssetId(StableId.FromName(FixtureNamespace,Name)),null);
        return await Store.PublishAsync(
            Key,ContentArtifactCodec.Write(Artifact),CancellationToken).ConfigureAwait(false);
    }

    private static ContentReleaseInput[]ReadInputs(string Root)=>
        [.. InputDefinitions.Select(Definition=>{
            string FullPath=ResolveRelative(Root,Definition.Path);
            if(!File.Exists(FullPath)){
                throw new FileNotFoundException(
                    $"Required content release input is missing: {Definition.Path}.",FullPath);
            }
            EnsureNoReparsePoints(Root,FullPath);
            byte[]Bytes=File.ReadAllBytes(FullPath);
            return new ContentReleaseInput(
                Definition.Role,Definition.Path,Sha256(Bytes),Bytes.LongLength);
        })];

    private static ContentReleasePackageFile[]ReadPackageFiles(string PackageRoot)=>
        [.. Directory.EnumerateFiles(PackageRoot,"*",SearchOption.AllDirectories)
            .Select(PathValue=>{
                EnsureNoReparsePoints(PackageRoot,PathValue);
                byte[]Bytes=File.ReadAllBytes(PathValue);
                return new ContentReleasePackageFile(
                    Relative(PackageRoot,PathValue),Sha256(Bytes),Bytes.LongLength);
            })
            .OrderBy(static Entry=>Entry.Path,StringComparer.Ordinal)];

    private static ContentReleasePerformanceEvidence[]ReadPerformanceEvidence(
        string Root,string Configuration,(string Commit,bool IsDirty)Provenance,
        DateTimeOffset CreatedUtc){
        string PerformanceRoot=Path.Combine(Root,".artifacts","Performance");
        if(!Directory.Exists(PerformanceRoot)){
            throw new InvalidDataException(
                "Content release requires F-L7-028 performance reports for this configuration.");
        }
        var Reports=new List<(string Path,PerformanceReport Report)>();
        foreach(string PathValue in Directory.EnumerateFiles(
            PerformanceRoot,"*.performance.json",SearchOption.TopDirectoryOnly)){
            try {
                EnsureNoReparsePoints(Root,PathValue);
                if(PerformanceReportStore.Validate(PathValue).Count!=0){ continue; }
                PerformanceReport?Report=JsonSerializer.Deserialize<PerformanceReport>(
                    File.ReadAllBytes(PathValue),FeatureRunResultStore.CreateJsonOptions());
                if(Report is null||!RequiredPerformanceIds.Contains(
                    Report.Plan.Id,StringComparer.Ordinal)
                    ||Report.Plan.Configuration!=Configuration||!Report.Analysis.Passed
                    ||Report.Commit!=Provenance.Commit||Report.IsDirty!=Provenance.IsDirty
                    ||Report.CreatedUtc>CreatedUtc
                    ||CreatedUtc-Report.CreatedUtc>MaximumPerformanceAge){ continue; }
                Reports.Add((PathValue,Report));
            } catch(Exception Ex)when(Ex is not OperationCanceledException
                &&Ex is not OutOfMemoryException&&Ex is not StackOverflowException){
                continue;
            }
        }
        var Result=new List<ContentReleasePerformanceEvidence>();
        foreach(string Id in RequiredPerformanceIds){
            var Match=Reports.Where(Item=>Item.Report.Plan.Id==Id)
                .OrderByDescending(static Item=>Item.Report.CreatedUtc)
                .ThenByDescending(static Item=>Item.Path,StringComparer.Ordinal).FirstOrDefault();
            if(Match.Report is null){
                throw new InvalidDataException(
                    $"Content release requires a recent passing {Id} report for {Configuration} with matching Git provenance.");
            }
            byte[]Bytes=File.ReadAllBytes(Match.Path);
            Result.Add(new ContentReleasePerformanceEvidence(
                Id,Relative(Root,Match.Path),Sha256(Bytes),Match.Report.ReportSha256,
                Match.Report.CreatedUtc,Match.Report.Machine.FingerprintSha256));
        }
        if(Result.Select(static Entry=>Entry.MachineFingerprintSha256)
            .Distinct(StringComparer.Ordinal).Count()!=1){
            throw new InvalidDataException(
                "Content release performance reports must come from one machine fingerprint.");
        }
        return [.. Result.OrderBy(static Entry=>Entry.Id,StringComparer.Ordinal)];
    }

    private static void ValidateInputs(
        string Root,IReadOnlyList<ContentReleaseInput>?Inputs,List<string>Errors){
        if(Inputs is null||Inputs.Count!=InputDefinitions.Length){
            Errors.Add("Content release input closure is incomplete.");
            return;
        }
        for(int Index=0;Index<InputDefinitions.Length;Index++){
            InputDefinition Definition=InputDefinitions[Index];
            ContentReleaseInput Entry=Inputs[Index];
            if(Entry.Role!=Definition.Role||Entry.Path!=Definition.Path){
                Errors.Add($"Content release input {Index} is not the required canonical input.");
                continue;
            }
            try {
                string PathValue=ResolveRelative(Root,Entry.Path);
                EnsureNoReparsePoints(Root,PathValue);
                byte[]Bytes=File.ReadAllBytes(PathValue);
                if(Entry.ByteLength!=Bytes.LongLength||!FixedHashEquals(Sha256(Bytes),Entry.Sha256)){
                    Errors.Add($"Content release input changed: {Entry.Path}.");
                }
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Errors.Add($"Content release input cannot be validated: {Entry.Path}: {Ex.Message}");
            }
        }
    }

    private static async Task ValidatePackageAsync(
        string Root,string ReportPath,ContentReleaseReport Report,
        CancellationToken CancellationToken,List<string>Errors){
        if(Report.PackageRelativePath!=PackageDirectoryName){
            Errors.Add("Content release package path is not canonical.");
            return;
        }
        string EvidenceRoot=Path.GetDirectoryName(ReportPath)!;
        string PackageRoot=ResolveRelative(EvidenceRoot,Report.PackageRelativePath);
        try {
            EnsureNoReparsePoints(Root,PackageRoot);
            ShippingContentPackage Package=await ShippingContentPackage.OpenAsync(
                PackageRoot,CancellationToken).ConfigureAwait(false);
            if(Package.Manifest.Fingerprint.ToString()!=Report.PackageManifestFingerprint){
                Errors.Add("Content release package manifest fingerprint mismatch.");
            }
            ContentReleasePackageFile[]Actual=ReadPackageFiles(PackageRoot);
            if(!SequenceEqual(Report.PackageFiles,Actual)){
                Errors.Add("Content release package file closure, length, or SHA-256 mismatch.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"Content release package is invalid: {Ex.Message}");
        }
    }

    private static void ValidatePerformance(
        string Root,ContentReleaseReport Report,List<string>Errors){
        if(Report.Performance is null||Report.Performance.Count!=RequiredPerformanceIds.Length){
            Errors.Add("Content release performance evidence is incomplete.");
            return;
        }
        string?MachineFingerprint=null;
        for(int Index=0;Index<RequiredPerformanceIds.Length;Index++){
            string ExpectedId=RequiredPerformanceIds.Order(StringComparer.Ordinal).ElementAt(Index);
            ContentReleasePerformanceEvidence Entry=Report.Performance[Index];
            if(Entry.Id!=ExpectedId){
                Errors.Add($"Content release performance evidence {Index} has the wrong ID.");
                continue;
            }
            try {
                string PathValue=ResolveRelative(Root,Entry.ReportPath);
                string PerformanceRoot=Path.Combine(Root,".artifacts","Performance");
                if(!IsContained(PerformanceRoot,PathValue)){
                    throw new InvalidDataException("Performance report escaped .artifacts/Performance.");
                }
                EnsureNoReparsePoints(Root,PathValue);
                byte[]Bytes=File.ReadAllBytes(PathValue);
                if(!FixedHashEquals(Sha256(Bytes),Entry.FileSha256)){
                    Errors.Add($"Content performance report file SHA-256 mismatch: {Entry.Id}.");
                    continue;
                }
                IReadOnlyList<string>IntegrityErrors=PerformanceReportStore.Validate(PathValue);
                foreach(string IntegrityError in IntegrityErrors){
                    Errors.Add($"Content performance report {Entry.Id}: {IntegrityError}");
                }
                PerformanceReport?Performance=JsonSerializer.Deserialize<PerformanceReport>(
                    Bytes,FeatureRunResultStore.CreateJsonOptions());
                if(Performance is null||Performance.Plan.Id!=Entry.Id
                    ||Performance.Plan.Configuration!=Report.Configuration
                    ||!Performance.Analysis.Passed
                    ||Performance.ReportSha256!=Entry.ReportSha256
                    ||Performance.CreatedUtc!=Entry.CreatedUtc
                    ||Performance.Machine.FingerprintSha256!=Entry.MachineFingerprintSha256
                    ||Performance.Commit!=Report.Commit||Performance.IsDirty!=Report.IsDirty
                    ||Performance.CreatedUtc>Report.CreatedUtc
                    ||Report.CreatedUtc-Performance.CreatedUtc>MaximumPerformanceAge){
                    Errors.Add($"Content performance report contract mismatch: {Entry.Id}.");
                }
                MachineFingerprint??=Entry.MachineFingerprintSha256;
                if(MachineFingerprint!=Entry.MachineFingerprintSha256){
                    Errors.Add("Content performance reports use different machine fingerprints.");
                }
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Errors.Add($"Content performance report cannot be validated: {Entry.Id}: {Ex.Message}");
            }
        }
    }

    private static void ValidateEvidenceClosure(
        string ReportPath,ContentReleaseReport Report,List<string>Errors){
        string EvidenceRoot=Path.GetDirectoryName(ReportPath)!;
        var Expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase){
            Path.GetFullPath(ReportPath),
        };
        if(Report.PackageFiles is not null){
            foreach(ContentReleasePackageFile Entry in Report.PackageFiles){
                Expected.Add(Path.GetFullPath(Path.Combine(
                    EvidenceRoot,PackageDirectoryName,
                    Entry.Path.Replace('/',Path.DirectorySeparatorChar))));
            }
        }
        try {
            string[]Actual=Directory.EnumerateFiles(
                EvidenceRoot,"*",SearchOption.AllDirectories).Select(Path.GetFullPath).ToArray();
            if(Actual.Length!=Expected.Count||Actual.Any(PathValue=>!Expected.Contains(PathValue))){
                Errors.Add("Content release evidence directory contains missing or extra files.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"Content release evidence closure cannot be enumerated: {Ex.Message}");
        }
    }

    private static bool SequenceEqual(
        IReadOnlyList<ContentReleasePackageFile>?Expected,
        IReadOnlyList<ContentReleasePackageFile>Actual){
        if(Expected is null||Expected.Count!=Actual.Count){ return false; }
        for(int Index=0;Index<Expected.Count;Index++){
            if(Expected[Index]!=Actual[Index]){ return false; }
        }
        return true;
    }

    private static async Task WriteCanonicalAsync(
        string PathValue,ContentReleaseReport Report,CancellationToken CancellationToken){
        byte[]Bytes=CanonicalBytes(Report);
        string Temporary=PathValue+".tmp";
        try {
            await File.WriteAllBytesAsync(Temporary,Bytes,CancellationToken).ConfigureAwait(false);
            File.Move(Temporary,PathValue,true);
        } finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
    }

    private static byte[]CanonicalBytes(ContentReleaseReport Report){
        byte[]Json=JsonSerializer.SerializeToUtf8Bytes(
            Report,FeatureRunResultStore.CreateJsonOptions());
        var Result=new byte[Json.Length+1];
        Json.CopyTo(Result,0);
        Result[^1]=(byte)'\n';
        return Result;
    }

    private static string ResolveRelative(string Root,string RelativePath){
        if(string.IsNullOrWhiteSpace(RelativePath)||Path.IsPathRooted(RelativePath)
            ||RelativePath.Contains('\\')){
            throw new InvalidDataException("Evidence path must be a canonical forward-slash relative path.");
        }
        string[]Segments=RelativePath.Split('/');
        if(Segments.Any(static Segment=>Segment.Length==0||Segment is "." or "..")){
            throw new InvalidDataException("Evidence path contains an invalid segment.");
        }
        string FullPath=Path.GetFullPath(Path.Combine(
            Path.GetFullPath(Root),RelativePath.Replace('/',Path.DirectorySeparatorChar)));
        if(!IsContained(Root,FullPath)){ throw new InvalidDataException("Evidence path escaped its root."); }
        return FullPath;
    }

    private static bool IsContained(string Root,string Candidate){
        string FullRoot=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string FullCandidate=Path.GetFullPath(Candidate);
        return FullCandidate.StartsWith(FullRoot,StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePoints(string Root,string PathValue){
        string FullRoot=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
        string Current=Path.GetFullPath(PathValue);
        if(!IsContained(FullRoot,Current)&&!string.Equals(
            FullRoot,Current,StringComparison.OrdinalIgnoreCase)){
            throw new InvalidDataException("Evidence path escaped its validation root.");
        }
        while(Current.Length>=FullRoot.Length){
            if(File.Exists(Current)||Directory.Exists(Current)){
                if((File.GetAttributes(Current)&FileAttributes.ReparsePoint)!=0){
                    throw new InvalidDataException("Evidence paths cannot contain filesystem reparse points.");
                }
            }
            if(string.Equals(Current,FullRoot,StringComparison.OrdinalIgnoreCase)){ break; }
            Current=Path.GetDirectoryName(Current)
                ??throw new InvalidDataException("Evidence path has no validation parent.");
        }
    }

    private static string Relative(string Root,string PathValue)=>Path.GetRelativePath(
        Path.GetFullPath(Root),Path.GetFullPath(PathValue)).Replace('\\','/');
    private static string Sha256(byte[]Bytes)=>Convert.ToHexString(
        SHA256.HashData(Bytes)).ToLowerInvariant();
    private static bool FixedHashEquals(string Left,string Right){
        if(Left.Length!=64||Right.Length!=64){ return false; }
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Left.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(Right.ToLowerInvariant()));
    }
    private static void ValidateConfiguration(string Configuration){
        if(Configuration is not("Debug" or "Release")){
            throw new ArgumentException("Configuration must be Debug or Release.",nameof(Configuration));
        }
    }
    private sealed record InputDefinition(string Role,string Path);
}
