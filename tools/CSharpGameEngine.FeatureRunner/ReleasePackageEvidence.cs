using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace CSharpGameEngine.FeatureStages;

internal sealed record ReleaseNugetDependency(
    string Name,string Version,string PackageSha256,string LicenseDeclared);
internal sealed record PreparedReleasePayload(
    string CliDirectory,string VsixPath,string NugetPath,string ProductVersion,
    IReadOnlyList<ReleaseNugetDependency>Dependencies,string Configuration="Release");
internal sealed record ReleasePackageBuildResult(
    string ArchivePath,string SigningInputsPath,string ProductVersion,
    string ArchiveSha256,string ManifestSha256,string SbomSha256);
internal sealed record ReleasePackageInstallResult(
    bool Installed,bool Ran,bool Uninstalled,int ExitCode,int ResidueCount,
    string StandardOutput,string StandardError,string InstalledClosureSha256);

public sealed record ReleasePackageFile(
    string Role,string Path,string Sha256,long ByteLength);
public sealed record ReleasePackageInput(
    string Role,string Path,string Sha256,long ByteLength);
public sealed record ReleasePackageSigningInputs(
    string Schema,string Product,string ProductVersion,string Configuration,
    string Target,string ArchiveFileName,string ArchiveSha256,long ArchiveByteLength,
    string ManifestSha256,string SbomSha256,IReadOnlyList<string>Algorithms);
public sealed record ReleasePackageInstallEvidence(
    string Schema,string ArchiveSha256,string ProductVersion,string Configuration,
    string Executable,string Arguments,string InstalledClosureSha256,
    bool Installed,bool Ran,bool Uninstalled,int ExitCode,int ResidueCount,
    string StandardOutput,string StandardError);
public sealed record ReleasePackageReportPayload(
    string Schema,DateTimeOffset CreatedUtc,string Configuration,string ProductVersion,
    string Commit,bool IsDirty,string Target,string ArchivePath,string ArchiveSha256,
    long ArchiveByteLength,string SigningInputsPath,string SigningInputsSha256,
    string InstallEvidencePath,string InstallEvidenceSha256,
    IReadOnlyList<ReleasePackageInput>Inputs,
    IReadOnlyList<ReleaseNugetDependencyDocument>Dependencies);
public sealed record ReleasePackageReport(
    string Schema,string ReportSha256,DateTimeOffset CreatedUtc,string Configuration,
    string ProductVersion,string Commit,bool IsDirty,string Target,string ArchivePath,
    string ArchiveSha256,long ArchiveByteLength,string SigningInputsPath,
    string SigningInputsSha256,string InstallEvidencePath,string InstallEvidenceSha256,
    IReadOnlyList<ReleasePackageInput>Inputs,
    IReadOnlyList<ReleaseNugetDependencyDocument>Dependencies);
public sealed record ReleaseNugetDependencyDocument(
    string Name,string Version,string PackageSha256,string LicenseDeclared);

internal sealed record ReleasePackageManifest(
    string Schema,string Product,string ProductVersion,string Configuration,string Target,
    IReadOnlyList<ReleasePackageFile>Files,string SbomPath,string SbomSha256);
internal sealed record SpdxCreationInfo(
    DateTimeOffset Created,IReadOnlyList<string>Creators);
internal sealed record SpdxChecksum(
    string Algorithm,string ChecksumValue);
internal sealed record SpdxExternalReference(
    string ReferenceCategory,string ReferenceType,string ReferenceLocator);
internal sealed record SpdxPackage(
    [property:JsonPropertyName("SPDXID")]string SpdxId,string Name,string VersionInfo,
    string DownloadLocation,bool FilesAnalyzed,string LicenseConcluded,
    string LicenseDeclared,string CopyrightText,IReadOnlyList<SpdxChecksum>Checksums,
    IReadOnlyList<SpdxExternalReference>ExternalRefs);
internal sealed record SpdxFile(
    [property:JsonPropertyName("SPDXID")]string SpdxId,string FileName,
    IReadOnlyList<SpdxChecksum>Checksums,string LicenseConcluded,
    string CopyrightText);
internal sealed record SpdxRelationship(
    [property:JsonPropertyName("spdxElementId")]string SpdxElementId,
    string RelationshipType,string RelatedSpdxElement);
internal sealed record SpdxDocument(
    string SpdxVersion,string DataLicense,
    [property:JsonPropertyName("SPDXID")]string SpdxId,string Name,
    string DocumentNamespace,SpdxCreationInfo CreationInfo,
    IReadOnlyList<SpdxPackage>Packages,IReadOnlyList<SpdxFile>Files,
    IReadOnlyList<SpdxRelationship>Relationships);

/// <summary>
/// Builds and proves one exact Windows x64 product archive. The archive owns only deployable
/// payloads; the later immutable release manifest aggregates this report with track evidence.
/// </summary>
public static class ReleasePackageEvidence {
    public const string ReportSchema="csharp-game-engine-release-package-v1";
    public const string ReportFileName="release-package.json";
    public const string SigningSchema="csharp-game-engine-signing-inputs-v1";
    public const string InstallSchema="csharp-game-engine-package-install-v1";
    internal const string ManifestSchema="csharp-game-engine-package-manifest-v1";
    private const string ProductName="CSharpGameEngine";
    private const string Target="win-x64";
    private const string ManifestEntryPath="manifest/package-manifest.json";
    private const string SbomEntryPath="manifest/sbom.spdx.json";
    private const string SigningFileName="signing-inputs.json";
    private const string InstallFileName="install-run-uninstall.json";
    private const long MaximumArchiveBytes=2L*1024*1024*1024;
    private const long MaximumExpandedBytes=4L*1024*1024*1024;
    private const int MaximumEntryCount=20_000;
    private static readonly DateTimeOffset ReproducibleTimestamp=
        new(1980,1,1,0,0,0,TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions=CreateJsonOptions();
    private static readonly InputDefinition[]InputDefinitions=[
        new("release.specification","Docs/Specifications/Testing-CI-Release.md"),
        new("release.roadmap","Docs/Roadmap.md"),
        new("release.catalog","tools/CSharpGameEngine.FeatureRunner/FeatureStageCatalog.cs"),
        new("release.package","tools/CSharpGameEngine.FeatureRunner/ReleasePackageEvidence.cs"),
        new("release.cli-project","tools/CSharpGameEngine.Extension/CSharpGameEngine.Extension.csproj"),
        new("release.analyzer-project","src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj"),
        new("release.vsix-project","tools/CSharpGameEngine.VSExtension/CSharpGameEngine.VSExtension.csproj"),
        new("release.vsix-manifest","tools/CSharpGameEngine.VSExtension/source.extension.vsixmanifest"),
        new("release.vsix-input",VsixReleaseInputStore.ActiveRelativePath),
        new("release.vsix-input-store","tools/CSharpGameEngine.FeatureRunner/VsixReleaseInputStore.cs"),
        new("release.toolchain","global.json"),
        new("release.build","Directory.Build.props"),
        new("release.build","Directory.Build.targets"),
        new("release.build","build/CSharpGameEngine.ShaderGraph.targets"),
    ];
    private static readonly string[]RequiredCliFiles=[
        "CSharpGameEngine.Extension.exe",
        "CSharpGameEngine.Extension.dll",
        "CSharpGameEngine.Extension.deps.json",
        "CSharpGameEngine.Extension.runtimeconfig.json",
        "assimp.dll",
        "Samples/Box.glb",
        "Shaders/Stage.cgeshaderartifact",
    ];
    private static readonly string[]RequiredVsixEntries=[
        "extension.vsixmanifest",
        "CSharpGameEngine.VSExtension.dll",
        "PreviewHost/CSharpGameEngine.EditorPreview.Host.exe",
        "ItemTemplates/CSharp/GameEngine/1033/SceneFile/Scene.cs",
        "ItemTemplates/CSharp/GameEngine/1041/SceneFile/Scene.cs",
    ];

    public static IEnumerable<string>RequiredRepositoryInputPaths=>
        InputDefinitions.Select(static Definition=>Definition.Path);

    public static async Task<int>RunAsync(
        string RepositoryRoot,string Configuration,TextWriter Output,TextWriter Error,
        CancellationToken CancellationToken){
        string Root=Path.GetFullPath(RepositoryRoot);
        ValidateConfiguration(Configuration);
        DateTimeOffset CreatedUtc=DateTimeOffset.UtcNow;
        string EvidenceRoot=Path.Combine(
            Root,".artifacts","ReleasePackages",
            $"{CreatedUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        string WorkRoot=Path.Combine(
            Path.GetTempPath(),"CgeReleasePackage",Guid.NewGuid().ToString("N"));
        bool Completed=false;
        try {
            ReleasePackageInput[]Inputs=ReadInputs(Root);
            string Version=ReadProductVersion(Path.Combine(
                Root,"tools","CSharpGameEngine.VSExtension","source.extension.vsixmanifest"));
            string CliRoot=Path.Combine(WorkRoot,"cli");
            string NugetRoot=Path.Combine(WorkRoot,"nuget");
            Directory.CreateDirectory(WorkRoot);
            await RunDotnetAsync(
                Root,[
                    "publish","tools/CSharpGameEngine.Extension/CSharpGameEngine.Extension.csproj",
                    "--configuration",Configuration,"--runtime","win-x64",
                    "--self-contained","false",
                    $"-p:Version={Version}","--output",CliRoot,
                ],CancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(NugetRoot);
            await RunDotnetAsync(
                Root,[
                    "pack","src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj",
                    "--configuration",Configuration,"--no-restore",
                    $"-p:PackageVersion={Version}","--output",NugetRoot,
                ],CancellationToken).ConfigureAwait(false);
            string NugetPath=Path.Combine(
                NugetRoot,$"CSharpGameEngine.CodeAnalysis.{Version}.nupkg");
            string VsixPath=Configuration=="Release"
                ?ResolveRelative(
                    Root,VsixReleaseInputStore.ReadActive(Root).PackagePath)
                :Path.Combine(
                    Root,"tools","CSharpGameEngine.VSExtension","bin",Configuration,"net472",
                    "CSharpGameEngine.VSExtension.vsix");
            if(!File.Exists(VsixPath)){
                throw new FileNotFoundException(
                    "The exact prebuilt VSIX is required; build it before package capture.",
                    VsixPath);
            }
            ReleaseNugetDependency[]Dependencies=ReadNugetDependencies(
                Path.Combine(Root,"tools","CSharpGameEngine.Extension","obj","project.assets.json"),
                Path.Combine(Root,"src","CSharpGameEngine.CodeAnalysis","obj","project.assets.json"));
            var Prepared=new PreparedReleasePayload(
                CliRoot,VsixPath,NugetPath,Version,Dependencies,Configuration);
            ReleasePackageBuildResult Build=await BuildPreparedPackageAsync(
                Prepared,EvidenceRoot,CancellationToken).ConfigureAwait(false);
            string InstallParent=Path.Combine(
                Path.GetTempPath(),"CgeReleaseInstall",Guid.NewGuid().ToString("N"));
            string[]SmokeArguments=[
                "--model","Samples/Box.glb","--frames","1","--hidden","--warp","--verify",
            ];
            ReleasePackageInstallResult Install=await InstallRunUninstallAsync(
                Build.ArchivePath,InstallParent,
                "cli/CSharpGameEngine.Extension.exe",SmokeArguments,
                ["Verification: PASSED","D3D12 validation errors: 0",
                 "SUCCESS: CSharpGameEngine viewer finished."],
                CancellationToken).ConfigureAwait(false);
            var InstallEvidence=new ReleasePackageInstallEvidence(
                InstallSchema,Build.ArchiveSha256,Version,Configuration,
                "cli/CSharpGameEngine.Extension.exe",JoinArguments(SmokeArguments),
                Install.InstalledClosureSha256,Install.Installed,Install.Ran,
                Install.Uninstalled,Install.ExitCode,Install.ResidueCount,
                Install.StandardOutput,Install.StandardError);
            string InstallPath=Path.Combine(EvidenceRoot,InstallFileName);
            await WriteCanonicalAsync(
                InstallPath,InstallEvidence,CancellationToken).ConfigureAwait(false);
            (string Commit,bool IsDirty)Provenance;
            try {
                Provenance=await GitChangedPathProvider.ReadProvenanceAsync(
                    Root,CancellationToken).ConfigureAwait(false);
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Provenance=("unknown",true);
                await Error.WriteLineAsync(
                    $"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false);
            }
            var DependencyDocuments=Dependencies.Select(static Dependency=>
                new ReleaseNugetDependencyDocument(
                    Dependency.Name,Dependency.Version,Dependency.PackageSha256,
                    Dependency.LicenseDeclared)).ToArray();
            var Payload=new ReleasePackageReportPayload(
                ReportSchema,CreatedUtc,Configuration,Version,Provenance.Commit,
                Provenance.IsDirty,Target,Relative(EvidenceRoot,Build.ArchivePath),
                Build.ArchiveSha256,new FileInfo(Build.ArchivePath).Length,
                Relative(EvidenceRoot,Build.SigningInputsPath),
                Sha256(await File.ReadAllBytesAsync(
                    Build.SigningInputsPath,CancellationToken).ConfigureAwait(false)),
                Relative(EvidenceRoot,InstallPath),
                Sha256(await File.ReadAllBytesAsync(
                    InstallPath,CancellationToken).ConfigureAwait(false)),
                Inputs,DependencyDocuments);
            string ReportHash=Sha256(JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions));
            var Report=new ReleasePackageReport(
                Payload.Schema,ReportHash,Payload.CreatedUtc,Payload.Configuration,
                Payload.ProductVersion,Payload.Commit,Payload.IsDirty,Payload.Target,
                Payload.ArchivePath,Payload.ArchiveSha256,Payload.ArchiveByteLength,
                Payload.SigningInputsPath,Payload.SigningInputsSha256,
                Payload.InstallEvidencePath,Payload.InstallEvidenceSha256,
                Payload.Inputs,Payload.Dependencies);
            string ReportPath=Path.Combine(EvidenceRoot,ReportFileName);
            await WriteCanonicalAsync(ReportPath,Report,CancellationToken).ConfigureAwait(false);
            IReadOnlyList<string>Errors=await ValidateAsync(
                Root,ReportPath,CancellationToken).ConfigureAwait(false);
            foreach(string ValidationError in Errors){
                await Error.WriteLineAsync(ValidationError).ConfigureAwait(false);
            }
            if(Errors.Count!=0){ return 1; }
            Completed=true;
            await Output.WriteLineAsync(
                $"releasePackage version={Version} target={Target} archive={Build.ArchiveSha256} dependencies={Dependencies.Length} residue=0")
                .ConfigureAwait(false);
            await Output.WriteLineAsync(ReportPath).ConfigureAwait(false);
            return 0;
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            await Error.WriteLineAsync(
                $"Release package verification failed: {Ex.Message}").ConfigureAwait(false);
            return 1;
        } finally {
            if(Directory.Exists(WorkRoot)){ Directory.Delete(WorkRoot,true); }
            if(!Completed&&Directory.Exists(EvidenceRoot)){ Directory.Delete(EvidenceRoot,true); }
        }
    }

    public static async Task<IReadOnlyList<string>>ValidateAsync(
        string RepositoryRoot,string ReportPath,CancellationToken CancellationToken=default){
        var Errors=new List<string>();
        string Root=Path.GetFullPath(RepositoryRoot);
        string FullReportPath=Path.GetFullPath(ReportPath);
        ReleasePackageReport?Report;
        byte[]ReportBytes;
        try {
            string EvidenceRoot=Path.Combine(Root,".artifacts","ReleasePackages");
            if(!IsContained(EvidenceRoot,FullReportPath)){
                return ["Release package report must be inside .artifacts/ReleasePackages."];
            }
            EnsureNoReparsePoints(Root,FullReportPath);
            ReportBytes=await File.ReadAllBytesAsync(
                FullReportPath,CancellationToken).ConfigureAwait(false);
            Report=JsonSerializer.Deserialize<ReleasePackageReport>(ReportBytes,JsonOptions);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            return [$"Release package report cannot be read: {Ex.Message}"];
        }
        if(Report is null){ return ["Release package report is empty."]; }
        try {
            ValidateConfiguration(Report.Configuration);
            if(Report.Schema!=ReportSchema){
                Errors.Add($"Release package report schema must be {ReportSchema}.");
            }
            if(!ReportBytes.AsSpan().SequenceEqual(CanonicalBytes(Report))){
                Errors.Add("Release package report is not canonically encoded.");
            }
            var Payload=new ReleasePackageReportPayload(
                Report.Schema,Report.CreatedUtc,Report.Configuration,Report.ProductVersion,
                Report.Commit,Report.IsDirty,Report.Target,Report.ArchivePath,
                Report.ArchiveSha256,Report.ArchiveByteLength,Report.SigningInputsPath,
                Report.SigningInputsSha256,Report.InstallEvidencePath,
                Report.InstallEvidenceSha256,Report.Inputs,Report.Dependencies);
            if(!FixedHashEquals(
                Sha256(JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions)),
                Report.ReportSha256)){
                Errors.Add("Release package report SHA-256 mismatch.");
            }
            if(Report.Target!=Target){
                Errors.Add("Release package target must be win-x64.");
            }
            string Version=ReadProductVersion(Path.Combine(
                Root,"tools","CSharpGameEngine.VSExtension","source.extension.vsixmanifest"));
            if(Report.ProductVersion!=Version){
                Errors.Add("Release package product version no longer matches the VSIX.");
            }
            ValidateInputs(Root,Report.Inputs,Errors);
            string EvidenceDirectory=Path.GetDirectoryName(FullReportPath)!;
            string ArchivePath=ResolveRelative(EvidenceDirectory,Report.ArchivePath);
            string SigningPath=ResolveRelative(EvidenceDirectory,Report.SigningInputsPath);
            string InstallPath=ResolveRelative(EvidenceDirectory,Report.InstallEvidencePath);
            EnsureNoReparsePoints(Root,ArchivePath);
            EnsureNoReparsePoints(Root,SigningPath);
            EnsureNoReparsePoints(Root,InstallPath);
            if(new FileInfo(ArchivePath).Length!=Report.ArchiveByteLength
                ||!FixedHashEquals(
                    Sha256(await File.ReadAllBytesAsync(
                        ArchivePath,CancellationToken).ConfigureAwait(false)),
                    Report.ArchiveSha256)){
                Errors.Add("Release package archive length or SHA-256 mismatch.");
            }
            if(!FixedHashEquals(
                Sha256(await File.ReadAllBytesAsync(
                    SigningPath,CancellationToken).ConfigureAwait(false)),
                Report.SigningInputsSha256)){
                Errors.Add("Release package signing inputs SHA-256 mismatch.");
            }
            if(!FixedHashEquals(
                Sha256(await File.ReadAllBytesAsync(
                    InstallPath,CancellationToken).ConfigureAwait(false)),
                Report.InstallEvidenceSha256)){
                Errors.Add("Release package install evidence SHA-256 mismatch.");
            }
            foreach(string PackageError in await ValidateBuiltPackageAsync(
                ArchivePath,SigningPath,CancellationToken).ConfigureAwait(false)){
                Errors.Add(PackageError);
            }
            ValidateInstallEvidence(InstallPath,Report,ArchivePath,Errors);
            ReleaseNugetDependency[]CurrentDependencies=ReadNugetDependencies(
                Path.Combine(Root,"tools","CSharpGameEngine.Extension","obj","project.assets.json"),
                Path.Combine(Root,"src","CSharpGameEngine.CodeAnalysis","obj","project.assets.json"));
            var CurrentDocuments=CurrentDependencies.Select(static Dependency=>
                new ReleaseNugetDependencyDocument(
                    Dependency.Name,Dependency.Version,Dependency.PackageSha256,
                    Dependency.LicenseDeclared)).ToArray();
            if(!Report.Dependencies.SequenceEqual(CurrentDocuments)){
                Errors.Add("Release package NuGet dependency closure changed.");
            }
            string[]ActualFiles=Directory.EnumerateFiles(
                EvidenceDirectory,"*",SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            string[]ExpectedFiles=[
                Path.GetFullPath(ArchivePath),Path.GetFullPath(SigningPath),
                Path.GetFullPath(InstallPath),Path.GetFullPath(FullReportPath),
            ];
            Array.Sort(ExpectedFiles,StringComparer.OrdinalIgnoreCase);
            if(!ActualFiles.SequenceEqual(ExpectedFiles,StringComparer.OrdinalIgnoreCase)
                ||Directory.EnumerateDirectories(
                    EvidenceDirectory,"*",SearchOption.TopDirectoryOnly).Any()){
                Errors.Add("Release package evidence directory contains missing or extra files.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"Release package validation failed: {Ex.Message}");
        }
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    internal static async Task<ReleasePackageBuildResult>BuildPreparedPackageAsync(
        PreparedReleasePayload Prepared,string OutputDirectory,
        CancellationToken CancellationToken){
        ArgumentNullException.ThrowIfNull(Prepared);
        ValidateConfiguration(Prepared.Configuration);
        ValidateVersion(Prepared.ProductVersion);
        string CliRoot=Path.GetFullPath(Prepared.CliDirectory);
        string VsixPath=Path.GetFullPath(Prepared.VsixPath);
        string NugetPath=Path.GetFullPath(Prepared.NugetPath);
        ValidatePreparedPayload(CliRoot,VsixPath,NugetPath,Prepared.ProductVersion);
        ReleaseNugetDependency[]Dependencies=CanonicalDependencies(Prepared.Dependencies);
        string Destination=Path.GetFullPath(OutputDirectory);
        if(Directory.Exists(Destination)||File.Exists(Destination)){
            throw new IOException($"Release package output already exists: {Destination}.");
        }
        string Parent=Path.GetDirectoryName(Destination)
            ??throw new ArgumentException("Package output requires a parent.",nameof(OutputDirectory));
        Directory.CreateDirectory(Parent);
        string Temporary=Destination+".tmp."+Guid.NewGuid().ToString("N");
        try {
            string PayloadRoot=Path.Combine(Temporary,"payload");
            Directory.CreateDirectory(PayloadRoot);
            CopyCliPayload(CliRoot,Path.Combine(PayloadRoot,"cli"));
            Directory.CreateDirectory(Path.Combine(PayloadRoot,"vsix"));
            File.Copy(VsixPath,Path.Combine(
                PayloadRoot,"vsix","CSharpGameEngine.VSExtension.vsix"));
            Directory.CreateDirectory(Path.Combine(PayloadRoot,"nuget"));
            string NugetName=$"CSharpGameEngine.CodeAnalysis.{Prepared.ProductVersion}.nupkg";
            File.Copy(NugetPath,Path.Combine(PayloadRoot,"nuget",NugetName));
            ReleasePackageFile[]PayloadFiles=ReadPayloadFiles(PayloadRoot);
            SpdxDocument Spdx=CreateSpdx(
                Prepared.ProductVersion,PayloadFiles,Dependencies);
            byte[]SbomBytes=CanonicalBytes(Spdx);
            string SbomSha=Sha256(SbomBytes);
            Directory.CreateDirectory(Path.Combine(PayloadRoot,"manifest"));
            await File.WriteAllBytesAsync(
                Path.Combine(PayloadRoot,SbomEntryPath.Replace(
                    '/',Path.DirectorySeparatorChar)),SbomBytes,CancellationToken)
                .ConfigureAwait(false);
            var Manifest=new ReleasePackageManifest(
                ManifestSchema,ProductName,Prepared.ProductVersion,
                Prepared.Configuration,Target,PayloadFiles,SbomEntryPath,SbomSha);
            byte[]ManifestBytes=CanonicalBytes(Manifest);
            string ManifestSha=Sha256(ManifestBytes);
            await File.WriteAllBytesAsync(
                Path.Combine(PayloadRoot,ManifestEntryPath.Replace(
                    '/',Path.DirectorySeparatorChar)),ManifestBytes,CancellationToken)
                .ConfigureAwait(false);
            string ArchiveName=
                $"{ProductName}.{Prepared.ProductVersion}.{Prepared.Configuration}.{Target}.zip";
            string TemporaryArchive=Path.Combine(Temporary,ArchiveName);
            CreateDeterministicArchive(PayloadRoot,TemporaryArchive);
            byte[]ArchiveBytes=await File.ReadAllBytesAsync(
                TemporaryArchive,CancellationToken).ConfigureAwait(false);
            string ArchiveSha=Sha256(ArchiveBytes);
            var Signing=new ReleasePackageSigningInputs(
                SigningSchema,ProductName,Prepared.ProductVersion,Prepared.Configuration,
                Target,ArchiveName,ArchiveSha,ArchiveBytes.LongLength,
                ManifestSha,SbomSha,["SHA256"]);
            byte[]SigningBytes=CanonicalBytes(Signing);
            string TemporarySigning=Path.Combine(Temporary,SigningFileName);
            await File.WriteAllBytesAsync(
                TemporarySigning,SigningBytes,CancellationToken).ConfigureAwait(false);
            Directory.Delete(PayloadRoot,true);
            Directory.Move(Temporary,Destination);
            var Result=new ReleasePackageBuildResult(
                Path.Combine(Destination,ArchiveName),
                Path.Combine(Destination,SigningFileName),Prepared.ProductVersion,
                ArchiveSha,ManifestSha,SbomSha);
            IReadOnlyList<string>Errors=await ValidateBuiltPackageAsync(
                Result.ArchivePath,Result.SigningInputsPath,CancellationToken)
                .ConfigureAwait(false);
            if(Errors.Count!=0){
                Directory.Delete(Destination,true);
                throw new InvalidDataException(string.Join(" ",Errors));
            }
            return Result;
        } finally {
            if(Directory.Exists(Temporary)){ Directory.Delete(Temporary,true); }
        }
    }

    internal static async Task<IReadOnlyList<string>>ValidateBuiltPackageAsync(
        string ArchivePath,string SigningInputsPath,
        CancellationToken CancellationToken=default){
        var Errors=new List<string>();
        try {
            string FullArchivePath=Path.GetFullPath(ArchivePath);
            string FullSigningPath=Path.GetFullPath(SigningInputsPath);
            byte[]SigningBytes=await File.ReadAllBytesAsync(
                FullSigningPath,CancellationToken).ConfigureAwait(false);
            ReleasePackageSigningInputs?Signing=
                JsonSerializer.Deserialize<ReleasePackageSigningInputs>(
                    SigningBytes,JsonOptions);
            if(Signing is null){ return ["Signing inputs are empty."]; }
            if(!SigningBytes.AsSpan().SequenceEqual(CanonicalBytes(Signing))){
                Errors.Add("Signing inputs are not canonically encoded.");
            }
            if(Signing.Schema!=SigningSchema||Signing.Product!=ProductName
                ||Signing.Target!=Target||Signing.Algorithms.Count!=1
                ||Signing.Algorithms[0]!="SHA256"){
                Errors.Add("Signing inputs contract is unsupported.");
            }
            byte[]ArchiveBytes=await File.ReadAllBytesAsync(
                FullArchivePath,CancellationToken).ConfigureAwait(false);
            if(ArchiveBytes.LongLength!=Signing.ArchiveByteLength
                ||!FixedHashEquals(Sha256(ArchiveBytes),Signing.ArchiveSha256)){
                Errors.Add("Release archive SHA-256 or length does not match signing inputs.");
            }
            if(Path.GetFileName(FullArchivePath)!=Signing.ArchiveFileName){
                Errors.Add("Release archive file name does not match signing inputs.");
            }
            using ZipArchive Archive=ZipFile.OpenRead(FullArchivePath);
            Dictionary<string,ZipArchiveEntry>Entries=ReadCanonicalEntries(
                Archive,RequireReproducibleArchive:true);
            if(!Entries.TryGetValue(ManifestEntryPath,out ZipArchiveEntry?ManifestEntry)
                ||!Entries.TryGetValue(SbomEntryPath,out ZipArchiveEntry?SbomEntry)){
                Errors.Add("Release archive is missing its manifest or SPDX SBOM.");
                return Errors;
            }
            byte[]ManifestBytes=await ReadEntryAsync(
                ManifestEntry,CancellationToken).ConfigureAwait(false);
            byte[]SbomBytes=await ReadEntryAsync(
                SbomEntry,CancellationToken).ConfigureAwait(false);
            if(!FixedHashEquals(Sha256(ManifestBytes),Signing.ManifestSha256)
                ||!FixedHashEquals(Sha256(SbomBytes),Signing.SbomSha256)){
                Errors.Add("Manifest or SBOM SHA-256 does not match signing inputs.");
            }
            ReleasePackageManifest?Manifest=
                JsonSerializer.Deserialize<ReleasePackageManifest>(
                    ManifestBytes,JsonOptions);
            SpdxDocument?Spdx=JsonSerializer.Deserialize<SpdxDocument>(
                SbomBytes,JsonOptions);
            if(Manifest is null||Spdx is null){
                Errors.Add("Release manifest or SPDX SBOM is empty.");
                return Errors;
            }
            if(!ManifestBytes.AsSpan().SequenceEqual(CanonicalBytes(Manifest))
                ||!SbomBytes.AsSpan().SequenceEqual(CanonicalBytes(Spdx))){
                Errors.Add("Release manifest or SPDX SBOM is not canonically encoded.");
            }
            if(Manifest.Schema!=ManifestSchema||Manifest.Product!=ProductName
                ||Manifest.ProductVersion!=Signing.ProductVersion
                ||Manifest.Configuration!=Signing.Configuration
                ||Manifest.Target!=Target||Manifest.SbomPath!=SbomEntryPath
                ||!FixedHashEquals(Manifest.SbomSha256,Signing.SbomSha256)){
                Errors.Add("Release manifest identity does not match signing inputs.");
            }
            ReleasePackageFile[]ActualFiles=await ReadArchivePayloadFilesAsync(
                Entries,CancellationToken).ConfigureAwait(false);
            if(!Manifest.Files.SequenceEqual(ActualFiles)){
                Errors.Add("Release archive payload closure does not match its manifest.");
            }
            ValidateRequiredPayload(Entries,Manifest.ProductVersion,Errors);
            ValidateSpdx(Spdx,Manifest,ActualFiles,Errors);
            ValidateNoDevelopmentPaths(Entries,Errors);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"Release package cannot be validated: {Ex.Message}");
        }
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    internal static async Task<ReleasePackageInstallResult>InstallRunUninstallAsync(
        string ArchivePath,string InstallRoot,string ExecutableRelativePath,
        IReadOnlyList<string>Arguments,IReadOnlyList<string>RequiredOutputTokens,
        CancellationToken CancellationToken){
        string FullArchivePath=Path.GetFullPath(ArchivePath);
        string FullInstallRoot=Path.GetFullPath(InstallRoot);
        if(Directory.Exists(FullInstallRoot)||File.Exists(FullInstallRoot)){
            throw new IOException($"Install root already exists: {FullInstallRoot}.");
        }
        bool Installed=false;
        bool Ran=false;
        int ExitCode=-1;
        string StandardOutput=string.Empty;
        string StandardError=string.Empty;
        string ClosureHash=string.Empty;
        try {
            Directory.CreateDirectory(FullInstallRoot);
            using(ZipArchive Archive=ZipFile.OpenRead(FullArchivePath)){
                ExtractSafely(Archive,FullInstallRoot);
            }
            Installed=true;
            ClosureHash=ComputeInstalledClosureHash(FullInstallRoot);
            string Executable=ResolveRelative(
                FullInstallRoot,ExecutableRelativePath);
            if(!File.Exists(Executable)){
                throw new InvalidDataException(
                    $"Installed smoke executable is missing: {ExecutableRelativePath}.");
            }
            var StartInfo=new ProcessStartInfo {
                WorkingDirectory=Path.GetDirectoryName(Executable)!,
                UseShellExecute=false,RedirectStandardOutput=true,
                RedirectStandardError=true,CreateNoWindow=true,
            };
            if(Executable.EndsWith(".cmd",StringComparison.OrdinalIgnoreCase)){
                StartInfo.FileName=Environment.GetEnvironmentVariable("ComSpec")??"cmd.exe";
                StartInfo.ArgumentList.Add("/d");
                StartInfo.ArgumentList.Add("/c");
                StartInfo.ArgumentList.Add(Executable);
            } else {
                StartInfo.FileName=Executable;
            }
            foreach(string Argument in Arguments){ StartInfo.ArgumentList.Add(Argument); }
            using var Process=new Process { StartInfo=StartInfo };
            if(!Process.Start()){ throw new InvalidOperationException("Package smoke process did not start."); }
            Task<string>OutputTask=Process.StandardOutput.ReadToEndAsync(CancellationToken);
            Task<string>ErrorTask=Process.StandardError.ReadToEndAsync(CancellationToken);
            await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
            StandardOutput=await OutputTask.ConfigureAwait(false);
            StandardError=await ErrorTask.ConfigureAwait(false);
            ExitCode=Process.ExitCode;
            Ran=true;
            if(ExitCode!=0){
                throw new InvalidDataException(
                    $"Package smoke process failed with exit code {ExitCode}: {StandardError}");
            }
            foreach(string Token in RequiredOutputTokens){
                if(string.IsNullOrEmpty(Token)
                    ||!StandardOutput.Contains(Token,StringComparison.Ordinal)){
                    throw new InvalidDataException(
                        $"Package smoke output oracle is missing: {Token}.");
                }
            }
        } finally {
            if(Directory.Exists(FullInstallRoot)){ Directory.Delete(FullInstallRoot,true); }
        }
        int ResidueCount=Directory.Exists(FullInstallRoot)
            ?Directory.EnumerateFileSystemEntries(
                FullInstallRoot,"*",SearchOption.AllDirectories).Count():0;
        if(Directory.Exists(FullInstallRoot)||ResidueCount!=0){
            throw new IOException("Package uninstall left residual files.");
        }
        return new ReleasePackageInstallResult(
            Installed,Ran,true,ExitCode,ResidueCount,StandardOutput,StandardError,ClosureHash);
    }

    private static void ValidatePreparedPayload(
        string CliRoot,string VsixPath,string NugetPath,string ProductVersion){
        if(!Directory.Exists(CliRoot)){ throw new DirectoryNotFoundException(CliRoot); }
        foreach(string Required in RequiredCliFiles){
            string PathValue=ResolveRelative(CliRoot,Required);
            if(!File.Exists(PathValue)){
                throw new InvalidDataException($"CLI payload is missing {Required}.");
            }
        }
        if(!File.Exists(VsixPath)){ throw new FileNotFoundException("VSIX is missing.",VsixPath); }
        if(!File.Exists(NugetPath)){ throw new FileNotFoundException("NuGet package is missing.",NugetPath); }
        using(ZipArchive Vsix=ZipFile.OpenRead(VsixPath)){
            Dictionary<string,ZipArchiveEntry>Entries=ReadCanonicalEntries(Vsix);
            foreach(string Required in RequiredVsixEntries){
                if(!Entries.ContainsKey(Required)){
                    throw new InvalidDataException($"VSIX payload is missing {Required}.");
                }
            }
            string Version=ReadVsixVersion(Entries["extension.vsixmanifest"]);
            if(Version!=ProductVersion){
                throw new InvalidDataException("VSIX version does not match the product version.");
            }
        }
        using(ZipArchive Nuget=ZipFile.OpenRead(NugetPath)){
            Dictionary<string,ZipArchiveEntry>Entries=ReadCanonicalEntries(Nuget);
            string NuspecName="CSharpGameEngine.CodeAnalysis.nuspec";
            string AnalyzerName="analyzers/dotnet/cs/CSharpGameEngine.CodeAnalysis.dll";
            if(!Entries.ContainsKey(NuspecName)||!Entries.ContainsKey(AnalyzerName)){
                throw new InvalidDataException("Analyzer NuGet payload is incomplete.");
            }
            string Version=ReadNuspecVersion(Entries[NuspecName]);
            if(Version!=ProductVersion){
                throw new InvalidDataException("Analyzer NuGet version does not match the product version.");
            }
        }
    }

    private static void CopyCliPayload(string SourceRoot,string DestinationRoot){
        foreach(string Source in Directory.EnumerateFiles(
            SourceRoot,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)){
            string RelativePath=Relative(SourceRoot,Source);
            if(RelativePath.EndsWith(".pdb",StringComparison.OrdinalIgnoreCase)){ continue; }
            if((File.GetAttributes(Source)&FileAttributes.ReparsePoint)!=0){
                throw new InvalidDataException("CLI payload cannot contain reparse points.");
            }
            string Destination=ResolveRelative(DestinationRoot,RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            File.Copy(Source,Destination);
        }
    }

    private static ReleasePackageFile[]ReadPayloadFiles(string PayloadRoot)=>
        [.. Directory.EnumerateFiles(PayloadRoot,"*",SearchOption.AllDirectories)
            .Where(PathValue=>!Relative(PayloadRoot,PathValue).StartsWith(
                "manifest/",StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(PathValue=>{
                byte[]Bytes=File.ReadAllBytes(PathValue);
                string RelativePath=Relative(PayloadRoot,PathValue);
                return new ReleasePackageFile(
                    RoleFor(RelativePath),RelativePath,Sha256(Bytes),Bytes.LongLength);
            })];

    private static async Task<ReleasePackageFile[]>ReadArchivePayloadFilesAsync(
        IReadOnlyDictionary<string,ZipArchiveEntry>Entries,
        CancellationToken CancellationToken){
        var Files=new List<ReleasePackageFile>();
        foreach((string Name,ZipArchiveEntry Entry)in Entries.OrderBy(
            static Pair=>Pair.Key,StringComparer.Ordinal)){
            if(Name.StartsWith("manifest/",StringComparison.Ordinal)){ continue; }
            byte[]Bytes=await ReadEntryAsync(Entry,CancellationToken).ConfigureAwait(false);
            Files.Add(new ReleasePackageFile(
                RoleFor(Name),Name,Sha256(Bytes),Bytes.LongLength));
        }
        return [.. Files];
    }

    private static string RoleFor(string PathValue){
        if(PathValue=="cli/assimp.dll"){ return "native"; }
        if(PathValue.StartsWith("cli/",StringComparison.Ordinal)){ return "cli"; }
        if(PathValue.StartsWith("vsix/",StringComparison.Ordinal)){ return "vsix"; }
        if(PathValue.StartsWith("nuget/",StringComparison.Ordinal)){ return "nuget"; }
        throw new InvalidDataException($"Unknown release payload role: {PathValue}.");
    }

    private static SpdxDocument CreateSpdx(
        string Version,IReadOnlyList<ReleasePackageFile>Files,
        IReadOnlyList<ReleaseNugetDependency>Dependencies){
        string ClosureHash=Sha256(Encoding.UTF8.GetBytes(string.Join(
            "\n",Files.Select(static File=>$"{File.Path}:{File.Sha256}")
                .Concat(Dependencies.Select(static Dependency=>
                    $"{Dependency.Name}/{Dependency.Version}:{Dependency.PackageSha256}")))));
        var SpdxFiles=Files.Select((File,Index)=>new SpdxFile(
            $"SPDXRef-File-{Index+1}",File.Path,
            [new SpdxChecksum("SHA256",File.Sha256)],
            "NOASSERTION","NOASSERTION")).ToArray();
        var Packages=new List<SpdxPackage>{
            new("SPDXRef-Package-CSharpGameEngine",ProductName,Version,
                "NOASSERTION",false,"NOASSERTION","NOASSERTION","NOASSERTION",
                [],[new SpdxExternalReference(
                    "PACKAGE-MANAGER","purl",$"pkg:generic/{ProductName}@{Version}")]),
        };
        Packages.AddRange(Dependencies.Select((Dependency,Index)=>new SpdxPackage(
            $"SPDXRef-Package-NuGet-{Index+1}",Dependency.Name,Dependency.Version,
            "NOASSERTION",false,"NOASSERTION",Dependency.LicenseDeclared,
            "NOASSERTION",[new SpdxChecksum("SHA256",Dependency.PackageSha256)],
            [new SpdxExternalReference(
                "PACKAGE-MANAGER","purl",
                $"pkg:nuget/{Dependency.Name}@{Dependency.Version}")])));
        var Relationships=new List<SpdxRelationship>{
            new("SPDXRef-DOCUMENT","DESCRIBES","SPDXRef-Package-CSharpGameEngine"),
        };
        Relationships.AddRange(SpdxFiles.Select(File=>new SpdxRelationship(
            "SPDXRef-Package-CSharpGameEngine","CONTAINS",File.SpdxId)));
        for(int Index=0;Index<Dependencies.Count;Index++){
            Relationships.Add(new SpdxRelationship(
                "SPDXRef-Package-CSharpGameEngine","DEPENDS_ON",
                $"SPDXRef-Package-NuGet-{Index+1}"));
        }
        return new SpdxDocument(
            "SPDX-2.3","CC0-1.0","SPDXRef-DOCUMENT",
            $"{ProductName}-{Version}",
            $"https://csharp-game-engine.invalid/spdx/{Version}/{ClosureHash}",
            new SpdxCreationInfo(
                ReproducibleTimestamp,["Tool: CSharpGameEngine.FeatureRunner"]),
            Packages,SpdxFiles,Relationships);
    }

    private static void ValidateSpdx(
        SpdxDocument Spdx,ReleasePackageManifest Manifest,
        IReadOnlyList<ReleasePackageFile>Files,List<string>Errors){
        if(Spdx.SpdxVersion!="SPDX-2.3"||Spdx.DataLicense!="CC0-1.0"
            ||Spdx.SpdxId!="SPDXRef-DOCUMENT"
            ||Spdx.Packages.Count==0||Spdx.Files.Count!=Files.Count){
            Errors.Add("SPDX 2.3 document structure is incomplete.");
            return;
        }
        for(int Index=0;Index<Files.Count;Index++){
            SpdxFile SpdxFile=Spdx.Files[Index];
            ReleasePackageFile File=Files[Index];
            if(SpdxFile.FileName!=File.Path||SpdxFile.Checksums.Count!=1
                ||SpdxFile.Checksums[0].Algorithm!="SHA256"
                ||!FixedHashEquals(
                    SpdxFile.Checksums[0].ChecksumValue,File.Sha256)){
                Errors.Add($"SPDX file closure mismatch: {File.Path}.");
            }
        }
        if(!Spdx.Packages.Any(Package=>
            Package.SpdxId=="SPDXRef-Package-CSharpGameEngine"
            &&Package.VersionInfo==Manifest.ProductVersion)){
            Errors.Add("SPDX product package identity is missing.");
        }
        foreach(SpdxPackage Package in Spdx.Packages.Skip(1)){
            if(Package.Checksums.Count!=1||Package.Checksums[0].Algorithm!="SHA256"
                ||Package.ExternalRefs.Count!=1
                ||Package.ExternalRefs[0].ReferenceType!="purl"
                ||!Package.ExternalRefs[0].ReferenceLocator.StartsWith(
                    "pkg:nuget/",StringComparison.Ordinal)){
                Errors.Add($"SPDX NuGet dependency is incomplete: {Package.Name}.");
            }
        }
    }

    private static void ValidateRequiredPayload(
        IReadOnlyDictionary<string,ZipArchiveEntry>Entries,string Version,
        List<string>Errors){
        foreach(string Required in RequiredCliFiles.Select(static PathValue=>"cli/"+PathValue)){
            if(!Entries.ContainsKey(Required)){
                Errors.Add($"Release archive is missing {Required}.");
            }
        }
        if(!Entries.ContainsKey("vsix/CSharpGameEngine.VSExtension.vsix")){
            Errors.Add("Release archive is missing its VSIX.");
        }
        if(!Entries.ContainsKey(
            $"nuget/CSharpGameEngine.CodeAnalysis.{Version}.nupkg")){
            Errors.Add("Release archive is missing its analyzer NuGet package.");
        }
    }

    private static void ValidateNoDevelopmentPaths(
        IReadOnlyDictionary<string,ZipArchiveEntry>Entries,List<string>Errors){
        string[]TextExtensions=[
            ".json",".xml",".config",".nuspec",".vsixmanifest",".vstemplate",".cs",
        ];
        foreach((string Name,ZipArchiveEntry Entry)in Entries){
            if(!TextExtensions.Any(Extension=>Name.EndsWith(
                Extension,StringComparison.OrdinalIgnoreCase))){ continue; }
            if(Entry.Length>16*1024*1024){ continue; }
            using var Reader=new StreamReader(
                Entry.Open(),Encoding.UTF8,true,1024,leaveOpen:false);
            string Text=Reader.ReadToEnd();
            if(Text.Contains("\\obj\\",StringComparison.OrdinalIgnoreCase)
                ||Text.Contains("/obj/",StringComparison.OrdinalIgnoreCase)
                ||Text.Contains("\\bin\\",StringComparison.OrdinalIgnoreCase)
                ||Text.Contains("/bin/",StringComparison.OrdinalIgnoreCase)
                ||ContainsAbsoluteWindowsPath(Text)){
                Errors.Add($"Release payload contains a development path: {Name}.");
            }
        }
    }

    private static bool ContainsAbsoluteWindowsPath(string Text){
        for(int Index=0;Index+2<Text.Length;Index++){
            if(char.IsAsciiLetter(Text[Index])&&Text[Index+1]==':'
                &&(Text[Index+2]=='\\'
                    ||(Text[Index+2]=='/'
                        &&(Index+3>=Text.Length||Text[Index+3]!='/')))){
                return true;
            }
        }
        return false;
    }

    private static void CreateDeterministicArchive(string SourceRoot,string ArchivePath){
        using ZipArchive Archive=ZipFile.Open(ArchivePath,ZipArchiveMode.Create);
        foreach(string Source in Directory.EnumerateFiles(
            SourceRoot,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)){
            string Name=Relative(SourceRoot,Source);
            ZipArchiveEntry Entry=Archive.CreateEntry(Name,CompressionLevel.Optimal);
            Entry.LastWriteTime=ReproducibleTimestamp;
            using Stream Input=File.OpenRead(Source);
            using Stream Output=Entry.Open();
            Input.CopyTo(Output);
        }
    }

    private static Dictionary<string,ZipArchiveEntry>ReadCanonicalEntries(
        ZipArchive Archive,bool RequireReproducibleArchive=false){
        if(Archive.Entries.Count==0||Archive.Entries.Count>MaximumEntryCount){
            throw new InvalidDataException("Archive entry count exceeds supported bounds.");
        }
        var Entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.Ordinal);
        if(RequireReproducibleArchive){
            string[]Order=[.. Archive.Entries.Select(static Entry=>Entry.FullName)];
            if(!Order.SequenceEqual(Order.Order(StringComparer.Ordinal),StringComparer.Ordinal)){
                throw new InvalidDataException("Archive entries are not in canonical ordinal order.");
            }
        }
        long ExpandedBytes=0;
        foreach(ZipArchiveEntry Entry in Archive.Entries){
            string Name=Entry.FullName;
            if(Name.EndsWith("/",StringComparison.Ordinal)){
                throw new InvalidDataException(
                    $"Archive contains an unowned directory entry: {Name}.");
            }
            if(RequireReproducibleArchive
                &&Entry.LastWriteTime.DateTime!=ReproducibleTimestamp.DateTime){
                throw new InvalidDataException(
                    $"Archive entry timestamp is not reproducible: {Name}.");
            }
            ValidateArchivePath(Name);
            if(!Entries.TryAdd(Name,Entry)){
                throw new InvalidDataException($"Archive contains duplicate entry {Name}.");
            }
            ExpandedBytes=checked(ExpandedBytes+Entry.Length);
            if(ExpandedBytes>MaximumExpandedBytes){
                throw new InvalidDataException("Archive expanded length exceeds supported bounds.");
            }
        }
        return Entries;
    }

    private static void ValidateArchivePath(string Name){
        if(string.IsNullOrWhiteSpace(Name)||Name.Contains('\\')
            ||Name.StartsWith("/",StringComparison.Ordinal)
            ||Name.Contains(':')){
            throw new InvalidDataException($"Archive entry path is not canonical: {Name}.");
        }
        string[]Segments=Name.Split('/');
        if(Segments.Any(static Segment=>Segment.Length==0||Segment is "." or "..")){
            throw new InvalidDataException($"Archive entry path is unsafe: {Name}.");
        }
    }

    private static void ExtractSafely(ZipArchive Archive,string DestinationRoot){
        Dictionary<string,ZipArchiveEntry>Entries=ReadCanonicalEntries(
            Archive,RequireReproducibleArchive:true);
        foreach((string Name,ZipArchiveEntry Entry)in Entries.OrderBy(
            static Pair=>Pair.Key,StringComparer.Ordinal)){
            string Destination=ResolveRelative(DestinationRoot,Name);
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            using Stream Input=Entry.Open();
            using FileStream Output=new(
                Destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            Input.CopyTo(Output);
        }
    }

    private static string ComputeInstalledClosureHash(string InstallRoot){
        using var Hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(string PathValue in Directory.EnumerateFiles(
            InstallRoot,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)){
            string Header=Relative(InstallRoot,PathValue)+"\n";
            Hash.AppendData(Encoding.UTF8.GetBytes(Header));
            using FileStream Stream=File.OpenRead(PathValue);
            byte[]Buffer=new byte[64*1024];
            int Read;
            while((Read=Stream.Read(Buffer,0,Buffer.Length))!=0){
                Hash.AppendData(Buffer,0,Read);
            }
        }
        return Convert.ToHexString(Hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static string ComputeArchiveClosureHash(string ArchivePath){
        using ZipArchive Archive=ZipFile.OpenRead(ArchivePath);
        Dictionary<string,ZipArchiveEntry>Entries=ReadCanonicalEntries(
            Archive,RequireReproducibleArchive:true);
        using var Hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach((string Name,ZipArchiveEntry Entry)in Entries.OrderBy(
            static Pair=>Pair.Key,StringComparer.Ordinal)){
            Hash.AppendData(Encoding.UTF8.GetBytes(Name+"\n"));
            using Stream Stream=Entry.Open();
            byte[]Buffer=new byte[64*1024];
            int Read;
            while((Read=Stream.Read(Buffer,0,Buffer.Length))!=0){
                Hash.AppendData(Buffer,0,Read);
            }
        }
        return Convert.ToHexString(Hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static ReleaseNugetDependency[]ReadNugetDependencies(
        params string[]AssetsPaths){
        var Dependencies=new Dictionary<string,ReleaseNugetDependency>(
            StringComparer.OrdinalIgnoreCase);
        foreach(string AssetsPath in AssetsPaths){
            using JsonDocument Document=JsonDocument.Parse(File.ReadAllBytes(AssetsPath));
            JsonElement Root=Document.RootElement;
            string PackageRoot=Root.GetProperty("packageFolders")
                .EnumerateObject().First().Name;
            foreach(JsonProperty Library in Root.GetProperty("libraries").EnumerateObject()){
                JsonElement Value=Library.Value;
                if(Value.GetProperty("type").GetString()!="package"){ continue; }
                string[]Identity=Library.Name.Split('/');
                if(Identity.Length!=2){
                    throw new InvalidDataException($"NuGet library identity is invalid: {Library.Name}.");
                }
                string RelativePackagePath=Value.GetProperty("path").GetString()
                    ??throw new InvalidDataException("NuGet package path is missing.");
                string PackageDirectory=Path.Combine(
                    PackageRoot,RelativePackagePath.Replace('/',Path.DirectorySeparatorChar));
                string[]Packages=Directory.GetFiles(
                    PackageDirectory,"*.nupkg",SearchOption.TopDirectoryOnly);
                if(Packages.Length!=1){
                    throw new InvalidDataException(
                        $"Restored NuGet package bytes are unavailable: {Library.Name}.");
                }
                string Hash=Sha256(File.ReadAllBytes(Packages[0]));
                var Dependency=new ReleaseNugetDependency(
                    Identity[0],Identity[1],Hash,"NOASSERTION");
                string Key=Identity[0]+"/"+Identity[1];
                if(Dependencies.TryGetValue(Key,out ReleaseNugetDependency?Existing)
                    &&Existing!=Dependency){
                    throw new InvalidDataException($"NuGet dependency conflicts: {Key}.");
                }
                Dependencies[Key]=Dependency;
            }
        }
        return CanonicalDependencies(Dependencies.Values);
    }

    private static ReleaseNugetDependency[]CanonicalDependencies(
        IEnumerable<ReleaseNugetDependency>Dependencies){
        ReleaseNugetDependency[]Result=[.. Dependencies.OrderBy(
            static Dependency=>Dependency.Name,StringComparer.Ordinal)
            .ThenBy(static Dependency=>Dependency.Version,StringComparer.Ordinal)];
        if(Result.Length==0){ throw new InvalidDataException("NuGet dependency closure is empty."); }
        for(int Index=0;Index<Result.Length;Index++){
            ReleaseNugetDependency Dependency=Result[Index];
            if(string.IsNullOrWhiteSpace(Dependency.Name)
                ||string.IsNullOrWhiteSpace(Dependency.Version)
                ||Dependency.PackageSha256.Length!=64
                ||Dependency.LicenseDeclared.Length==0){
                throw new InvalidDataException("NuGet dependency evidence is incomplete.");
            }
            if(Index>0&&Result[Index-1].Name==Dependency.Name
                &&Result[Index-1].Version==Dependency.Version){
                throw new InvalidDataException("NuGet dependency closure contains duplicates.");
            }
        }
        return Result;
    }

    private static ReleasePackageInput[]ReadInputs(string Root)=>
        [.. InputDefinitions.Select(Definition=>{
            string PathValue=ResolveRelative(Root,Definition.Path);
            EnsureNoReparsePoints(Root,PathValue);
            byte[]Bytes=File.ReadAllBytes(PathValue);
            return new ReleasePackageInput(
                Definition.Role,Definition.Path,Sha256(Bytes),Bytes.LongLength);
        })];

    private static void ValidateInputs(
        string Root,IReadOnlyList<ReleasePackageInput>?Inputs,List<string>Errors){
        if(Inputs is null||Inputs.Count!=InputDefinitions.Length){
            Errors.Add("Release package input closure is incomplete.");
            return;
        }
        for(int Index=0;Index<InputDefinitions.Length;Index++){
            InputDefinition Definition=InputDefinitions[Index];
            ReleasePackageInput Entry=Inputs[Index];
            if(Entry.Role!=Definition.Role||Entry.Path!=Definition.Path){
                Errors.Add($"Release package input {Index} is not canonical.");
                continue;
            }
            try {
                string PathValue=ResolveRelative(Root,Entry.Path);
                EnsureNoReparsePoints(Root,PathValue);
                byte[]Bytes=File.ReadAllBytes(PathValue);
                if(Entry.ByteLength!=Bytes.LongLength
                    ||!FixedHashEquals(Entry.Sha256,Sha256(Bytes))){
                    Errors.Add($"Release package input changed: {Entry.Path}.");
                }
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Errors.Add($"Release package input cannot be validated: {Entry.Path}: {Ex.Message}");
            }
        }
    }

    private static void ValidateInstallEvidence(
        string InstallPath,ReleasePackageReport Report,string ArchivePath,
        List<string>Errors){
        byte[]Bytes=File.ReadAllBytes(InstallPath);
        ReleasePackageInstallEvidence?Install=
            JsonSerializer.Deserialize<ReleasePackageInstallEvidence>(Bytes,JsonOptions);
        if(Install is null){
            Errors.Add("Release package install evidence is empty.");
            return;
        }
        if(!Bytes.AsSpan().SequenceEqual(CanonicalBytes(Install))){
            Errors.Add("Release package install evidence is not canonically encoded.");
        }
        string ExpectedArguments=JoinArguments([
            "--model","Samples/Box.glb","--frames","1","--hidden","--warp","--verify",
        ]);
        if(Install.Schema!=InstallSchema
            ||!FixedHashEquals(Install.ArchiveSha256,Report.ArchiveSha256)
            ||Install.ProductVersion!=Report.ProductVersion
            ||Install.Configuration!=Report.Configuration
            ||Install.Executable!="cli/CSharpGameEngine.Extension.exe"
            ||Install.Arguments!=ExpectedArguments||!Install.Installed||!Install.Ran
            ||!Install.Uninstalled||Install.ExitCode!=0||Install.ResidueCount!=0
            ||!FixedHashEquals(
                Install.InstalledClosureSha256,
                ComputeArchiveClosureHash(ArchivePath))
            ||!Install.StandardOutput.Contains(
                "Verification: PASSED",StringComparison.Ordinal)
            ||!Install.StandardOutput.Contains(
                "D3D12 validation errors: 0",StringComparison.Ordinal)
            ||!Install.StandardOutput.Contains(
                "SUCCESS: CSharpGameEngine viewer finished.",StringComparison.Ordinal)){
            Errors.Add("Release package install/run/uninstall evidence is incomplete.");
        }
        if(!File.Exists(ArchivePath)){
            Errors.Add("Installed release archive no longer exists.");
        }
    }

    private static async Task RunDotnetAsync(
        string WorkingDirectory,IReadOnlyList<string>Arguments,
        CancellationToken CancellationToken){
        var StartInfo=new ProcessStartInfo {
            FileName="dotnet",WorkingDirectory=WorkingDirectory,UseShellExecute=false,
            RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,
        };
        foreach(string Argument in Arguments){ StartInfo.ArgumentList.Add(Argument); }
        using var Process=new Process { StartInfo=StartInfo };
        if(!Process.Start()){ throw new InvalidOperationException("dotnet process did not start."); }
        Task<string>OutputTask=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        Task<string>ErrorTask=Process.StandardError.ReadToEndAsync(CancellationToken);
        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
        string Output=await OutputTask.ConfigureAwait(false);
        string Error=await ErrorTask.ConfigureAwait(false);
        if(Process.ExitCode!=0){
            throw new InvalidDataException(
                $"dotnet {Arguments[0]} failed with exit code {Process.ExitCode}: {Output}\n{Error}");
        }
    }

    private static string ReadProductVersion(string ManifestPath){
        using FileStream Stream=File.OpenRead(ManifestPath);
        XDocument Document=XDocument.Load(Stream,LoadOptions.None);
        XElement Identity=Document.Descendants().First(Element=>
            Element.Name.LocalName=="Identity");
        string Version=Identity.Attribute("Version")?.Value
            ??throw new InvalidDataException("VSIX source manifest version is missing.");
        ValidateVersion(Version);
        return Version;
    }

    private static string ReadVsixVersion(ZipArchiveEntry Entry){
        using Stream Stream=Entry.Open();
        XDocument Document=XDocument.Load(Stream,LoadOptions.None);
        string Version=Document.Descendants().First(Element=>
            Element.Name.LocalName=="Identity").Attribute("Version")?.Value
            ??throw new InvalidDataException("VSIX identity version is missing.");
        ValidateVersion(Version);
        return Version;
    }

    private static string ReadNuspecVersion(ZipArchiveEntry Entry){
        using Stream Stream=Entry.Open();
        XDocument Document=XDocument.Load(Stream,LoadOptions.None);
        string Version=Document.Descendants().First(Element=>
            Element.Name.LocalName=="version").Value;
        ValidateVersion(Version);
        return Version;
    }

    private static void ValidateVersion(string Version){
        if(!System.Version.TryParse(Version,out System.Version?Parsed)
            ||Parsed.Major<0||Version.Contains('+')||Version.Contains('-')){
            throw new InvalidDataException($"Product version is not canonical: {Version}.");
        }
    }

    private static async Task<byte[]>ReadEntryAsync(
        ZipArchiveEntry Entry,CancellationToken CancellationToken){
        if(Entry.Length<0||Entry.Length>MaximumArchiveBytes){
            throw new InvalidDataException("Archive entry length exceeds supported bounds.");
        }
        using Stream Stream=Entry.Open();
        using var Memory=new MemoryStream((int)Math.Min(Entry.Length,int.MaxValue));
        await Stream.CopyToAsync(Memory,CancellationToken).ConfigureAwait(false);
        return Memory.ToArray();
    }

    private static async Task WriteCanonicalAsync<T>(
        string PathValue,T Value,CancellationToken CancellationToken){
        string Temporary=PathValue+".tmp";
        try {
            await File.WriteAllBytesAsync(
                Temporary,CanonicalBytes(Value),CancellationToken).ConfigureAwait(false);
            File.Move(Temporary,PathValue,true);
        } finally {
            if(File.Exists(Temporary)){ File.Delete(Temporary); }
        }
    }

    private static byte[]CanonicalBytes<T>(T Value){
        byte[]Json=JsonSerializer.SerializeToUtf8Bytes(Value,JsonOptions);
        byte[]Result=new byte[Json.Length+1];
        Json.CopyTo(Result,0);
        Result[^1]=(byte)'\n';
        return Result;
    }

    private static JsonSerializerOptions CreateJsonOptions(){
        var Options=new JsonSerializerOptions {
            PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
        };
        Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return Options;
    }

    private static string JoinArguments(IEnumerable<string>Arguments)=>
        string.Join(" ",Arguments.Select(static Argument=>
            Argument.Any(char.IsWhiteSpace)
                ?"\""+Argument.Replace("\"","\\\"",StringComparison.Ordinal)+"\""
                :Argument));

    private static string ResolveRelative(string Root,string RelativePath){
        if(string.IsNullOrWhiteSpace(RelativePath)||Path.IsPathRooted(RelativePath)
            ||RelativePath.Contains('\\')){
            throw new InvalidDataException(
                "Release evidence path must be a canonical forward-slash relative path.");
        }
        string[]Segments=RelativePath.Split('/');
        if(Segments.Any(static Segment=>Segment.Length==0||Segment is "." or "..")){
            throw new InvalidDataException("Release evidence path contains an invalid segment.");
        }
        string FullPath=Path.GetFullPath(Path.Combine(
            Path.GetFullPath(Root),RelativePath.Replace(
                '/',Path.DirectorySeparatorChar)));
        if(!IsContained(Root,FullPath)){
            throw new InvalidDataException("Release evidence path escaped its root.");
        }
        return FullPath;
    }

    private static bool IsContained(string Root,string Candidate){
        string FullRoot=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar)
            +Path.DirectorySeparatorChar;
        return Path.GetFullPath(Candidate).StartsWith(
            FullRoot,StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePoints(string Root,string PathValue){
        string FullRoot=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
        string Current=Path.GetFullPath(PathValue);
        if(!IsContained(FullRoot,Current)
            &&!string.Equals(FullRoot,Current,StringComparison.OrdinalIgnoreCase)){
            throw new InvalidDataException("Release evidence path escaped its validation root.");
        }
        while(Current.Length>=FullRoot.Length){
            if((File.Exists(Current)||Directory.Exists(Current))
                &&(File.GetAttributes(Current)&FileAttributes.ReparsePoint)!=0){
                throw new InvalidDataException(
                    "Release evidence paths cannot contain filesystem reparse points.");
            }
            if(string.Equals(Current,FullRoot,StringComparison.OrdinalIgnoreCase)){ break; }
            Current=Path.GetDirectoryName(Current)
                ??throw new InvalidDataException("Release evidence path has no parent.");
        }
    }

    private static string Relative(string Root,string PathValue)=>
        Path.GetRelativePath(Path.GetFullPath(Root),Path.GetFullPath(PathValue))
            .Replace('\\','/');
    private static string Sha256(byte[]Bytes)=>
        Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
    private static bool FixedHashEquals(string?Left,string?Right){
        if(Left is null||Right is null||Left.Length!=64||Right.Length!=64){ return false; }
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Left.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(Right.ToLowerInvariant()));
    }
    private static void ValidateConfiguration(string Configuration){
        if(Configuration is not("Debug" or "Release")){
            throw new ArgumentException(
                "Configuration must be Debug or Release.",nameof(Configuration));
        }
    }
    private sealed record InputDefinition(string Role,string Path);
}
