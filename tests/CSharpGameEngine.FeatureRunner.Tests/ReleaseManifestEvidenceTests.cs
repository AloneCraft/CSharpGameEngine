using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class ReleaseManifestEvidenceTests:IDisposable {
    private readonly string Root=Path.Combine(
        Path.GetTempPath(),"CgeReleaseManifestTests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PublishPreparedCreatesOneSelfValidatingContentAddressedRelease(){
        PreparedReleaseManifest Prepared=await this.CreatePreparedAsync();

        string ManifestPath=await ReleaseManifestEvidence.PublishPreparedAsync(
            this.Root,Prepared,CancellationToken.None);
        ReleaseManifest Report=ReadManifest(ManifestPath);

        Assert.Empty(await ReleaseManifestEvidence.ValidateAsync(
            this.Root,ManifestPath));
        Assert.Equal(64,Report.ReleaseId.Length);
        Assert.Equal("FormalRelease",Report.Retention.Class);
        Assert.True(Report.Retention.Immutable);
        Assert.Null(Report.Retention.ExpiresUtc);
        Assert.Equal(Report.ReleaseId,Report.Retention.RollbackIdentity);
        Assert.Equal(
            ReleaseManifestEvidence.RequiredEvidenceRoles,Report.Evidence.Select(
                static Evidence=>Evidence.Role));
        Assert.StartsWith(
            Path.Combine(this.Root,".artifacts","Releases",Report.ReleaseId),
            ManifestPath,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepublishingTheSameIdentityIsIdempotentAndNeverRewrites(){
        PreparedReleaseManifest Prepared=await this.CreatePreparedAsync();
        string First=await ReleaseManifestEvidence.PublishPreparedAsync(
            this.Root,Prepared,CancellationToken.None);
        byte[]Before=await File.ReadAllBytesAsync(First);
        DateTime BeforeWrite=File.GetLastWriteTimeUtc(First);

        string Second=await ReleaseManifestEvidence.PublishPreparedAsync(
            this.Root,Prepared,CancellationToken.None);

        Assert.Equal(First,Second);
        Assert.Equal(Before,await File.ReadAllBytesAsync(Second));
        Assert.Equal(BeforeWrite,File.GetLastWriteTimeUtc(Second));
    }

    [Fact]
    public async Task ChangedOrExtraReleasedBytesFailAndCannotBeOverwritten(){
        PreparedReleaseManifest Prepared=await this.CreatePreparedAsync();
        string ManifestPath=await ReleaseManifestEvidence.PublishPreparedAsync(
            this.Root,Prepared,CancellationToken.None);
        ReleaseManifest Report=ReadManifest(ManifestPath);
        string EvidencePath=Resolve(
            Path.GetDirectoryName(ManifestPath)!,Report.Evidence[0].Path);
        await File.AppendAllTextAsync(EvidencePath,"changed",Encoding.UTF8);
        await File.WriteAllTextAsync(
            Path.Combine(Path.GetDirectoryName(ManifestPath)!,"extra.txt"),"extra");

        IReadOnlyList<string>Errors=await ReleaseManifestEvidence.ValidateAsync(
            this.Root,ManifestPath);

        Assert.Contains(Errors,Error=>Error.Contains(
            "SHA-256",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains(
            "missing or extra",StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<InvalidDataException>(()=>
            ReleaseManifestEvidence.PublishPreparedAsync(
                this.Root,Prepared,CancellationToken.None));
    }

    [Fact]
    public async Task ChangedSourceBeforePublicationIsRejectedWithoutPartialRelease(){
        PreparedReleaseManifest Prepared=await this.CreatePreparedAsync();
        await File.AppendAllTextAsync(
            Prepared.Evidence[0].SourcePath,"changed",Encoding.UTF8);

        await Assert.ThrowsAsync<InvalidDataException>(()=>
            ReleaseManifestEvidence.PublishPreparedAsync(
                this.Root,Prepared,CancellationToken.None));

        string Releases=Path.Combine(this.Root,".artifacts","Releases");
        Assert.False(Directory.Exists(Releases)
            &&Directory.EnumerateFileSystemEntries(Releases).Any());
    }

    [Fact]
    public void ReleaseStagesUseTheCurrentRunnerWithoutRebuildingTheirRunningHost(){
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();
        FeatureStageDefinition Candidate=Catalog.GetStage("release.candidate");
        FeatureStageDefinition Manifest=Catalog.GetStage("release.manifest");

        FeatureStageCommand CandidateRunner=Candidate.Commands[0];
        Assert.Equal("dotnet",CandidateRunner.FileName);
        Assert.StartsWith(
            "tools/CSharpGameEngine.FeatureRunner/bin/{configuration}/",
            CandidateRunner.Arguments[0],StringComparison.Ordinal);
        Assert.DoesNotContain("--project",CandidateRunner.Arguments);
        Assert.Equal("{configuration}",
            CandidateRunner.Arguments[CandidateRunner.Arguments.ToList()
                .IndexOf("--configuration")+1]);
        Assert.All(Manifest.Commands.Take(2),Command=>{
            Assert.Equal("dotnet",Command.FileName);
            Assert.StartsWith(
                "tools/CSharpGameEngine.FeatureRunner/bin/{configuration}/",
                Command.Arguments[0],StringComparison.Ordinal);
            Assert.DoesNotContain("--project",Command.Arguments);
        });
    }

    public void Dispose(){
        if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); }
    }

    private async Task<PreparedReleaseManifest>CreatePreparedAsync(){
        PreparedReleasePayload Payload=this.CreatePreparedPayload();
        ReleasePackageBuildResult Package=await ReleasePackageEvidence.BuildPreparedPackageAsync(
            Payload,Path.Combine(this.Root,"package-source"),CancellationToken.None);
        var Evidence=new List<PreparedReleaseEvidence>();
        foreach(string Role in ReleaseManifestEvidence.RequiredEvidenceRoles){
            string PathValue=Path.Combine(
                this.Root,"source-evidence",Role+".json");
            Write(PathValue,$$"""{"schema":"fixture","role":"{{Role}}"}""");
            byte[]Bytes=await File.ReadAllBytesAsync(PathValue);
            Evidence.Add(new PreparedReleaseEvidence(
                Role,PathValue,Hash(Bytes),Bytes.LongLength));
        }
        return new PreparedReleaseManifest(
            "CSharpGameEngine",Package.ProductVersion,"Release","win-x64",
            "fixture-commit",true,Package.ArchivePath,Package.SigningInputsPath,
            Package.ArchiveSha256,Package.ManifestSha256,Package.SbomSha256,
            Evidence);
    }

    private PreparedReleasePayload CreatePreparedPayload(){
        string Input=Path.Combine(this.Root,"package-input");
        string Cli=Path.Combine(Input,"cli");
        Write(Path.Combine(Cli,"CSharpGameEngine.Extension.exe"),"fixture-exe");
        Write(Path.Combine(Cli,"CSharpGameEngine.Extension.dll"),"fixture-dll");
        Write(Path.Combine(Cli,"CSharpGameEngine.Extension.deps.json"),
            """{"runtimeTarget":{"name":".NETCoreApp,Version=v10.0/win-x64"},"targets":{},"libraries":{}}""");
        Write(Path.Combine(Cli,"CSharpGameEngine.Extension.runtimeconfig.json"),
            """{"runtimeOptions":{"tfm":"net10.0"}}""");
        Write(Path.Combine(Cli,"assimp.dll"),"fixture-native");
        Write(Path.Combine(Cli,"Samples","Box.glb"),"fixture-model");
        Write(Path.Combine(Cli,"Shaders","Stage.cgeshaderartifact"),"fixture-shader");
        string Vsix=Path.Combine(Input,"CSharpGameEngine.VSExtension.vsix");
        CreateZip(Vsix,new Dictionary<string,string>(StringComparer.Ordinal){
            ["extension.vsixmanifest"]="""<PackageManifest><Metadata><Identity Id="CSharpGameEngine.VSExtension.aec17804-e2e4-46e7-b373-de11af816203" Version="0.1.7" /></Metadata></PackageManifest>""",
            ["CSharpGameEngine.VSExtension.dll"]="fixture-vsix",
            ["PreviewHost/CSharpGameEngine.EditorPreview.Host.exe"]="fixture-preview",
            ["ItemTemplates/CSharp/GameEngine/1033/SceneFile/Scene.cs"]="fixture-en",
            ["ItemTemplates/CSharp/GameEngine/1041/SceneFile/Scene.cs"]="fixture-ja",
        });
        string Nuget=Path.Combine(Input,"CSharpGameEngine.CodeAnalysis.0.1.7.nupkg");
        CreateZip(Nuget,new Dictionary<string,string>(StringComparer.Ordinal){
            ["CSharpGameEngine.CodeAnalysis.nuspec"]="<package><metadata><id>CSharpGameEngine.CodeAnalysis</id><version>0.1.7</version></metadata></package>",
            ["analyzers/dotnet/cs/CSharpGameEngine.CodeAnalysis.dll"]="fixture-analyzer",
        });
        return new PreparedReleasePayload(
            Cli,Vsix,Nuget,"0.1.7",
            [new ReleaseNugetDependency(
                "SharpGLTF.Core","1.0.6",new string('a',64),"NOASSERTION")]);
    }

    private static ReleaseManifest ReadManifest(string PathValue)=>
        JsonSerializer.Deserialize<ReleaseManifest>(
            File.ReadAllBytes(PathValue),new JsonSerializerOptions {
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
            })
        ??throw new InvalidDataException("Release manifest is empty.");

    private static void CreateZip(
        string PathValue,IReadOnlyDictionary<string,string>Files){
        Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
        using ZipArchive Archive=ZipFile.Open(PathValue,ZipArchiveMode.Create);
        foreach((string Name,string Contents)in Files.OrderBy(
            static Pair=>Pair.Key,StringComparer.Ordinal)){
            ZipArchiveEntry Entry=Archive.CreateEntry(Name,CompressionLevel.NoCompression);
            Entry.LastWriteTime=new DateTimeOffset(1980,1,1,0,0,0,TimeSpan.Zero);
            using StreamWriter Writer=new(Entry.Open(),new UTF8Encoding(false));
            Writer.Write(Contents);
        }
    }

    private static void Write(string PathValue,string Contents){
        Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
        File.WriteAllText(PathValue,Contents,new UTF8Encoding(false));
    }

    private static string Hash(byte[]Bytes)=>Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(Bytes)).ToLowerInvariant();

    private static string Resolve(string Root,string Relative)=>Path.Combine(
        Root,Relative.Replace('/',Path.DirectorySeparatorChar));
}
