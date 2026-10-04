using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class ReleasePackageEvidenceTests:IDisposable {
    private readonly string Root=Path.Combine(
        Path.GetTempPath(),"CgeReleasePackageTests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BuildPreparedPackage_PublishesExactArchiveSpdxAndSigningInputs(){
        PreparedReleasePayload Prepared=this.CreatePreparedPayload();
        string Output=Path.Combine(this.Root,"output");

        ReleasePackageBuildResult Result=await ReleasePackageEvidence.BuildPreparedPackageAsync(
            Prepared,Output,CancellationToken.None);
        IReadOnlyList<string>Errors=await ReleasePackageEvidence.ValidateBuiltPackageAsync(
            Result.ArchivePath,Result.SigningInputsPath,CancellationToken.None);

        Assert.Empty(Errors);
        Assert.Equal("0.1.7",Result.ProductVersion);
        Assert.Equal(64,Result.ArchiveSha256.Length);
        Assert.Equal(64,Result.ManifestSha256.Length);
        Assert.Equal(64,Result.SbomSha256.Length);
        using ZipArchive Archive=ZipFile.OpenRead(Result.ArchivePath);
        string[]Entries=[.. Archive.Entries.Select(static Entry=>Entry.FullName)
            .Order(StringComparer.Ordinal)];
        Assert.Contains("cli/CSharpGameEngine.Extension.exe",Entries);
        Assert.Contains("cli/assimp.dll",Entries);
        Assert.Contains("nuget/CSharpGameEngine.CodeAnalysis.0.1.7.nupkg",Entries);
        Assert.Contains("vsix/CSharpGameEngine.VSExtension.vsix",Entries);
        Assert.Contains("manifest/package-manifest.json",Entries);
        Assert.Contains("manifest/sbom.spdx.json",Entries);
        ZipArchiveEntry SbomEntry=Archive.GetEntry("manifest/sbom.spdx.json")!;
        using JsonDocument Sbom=JsonDocument.Parse(SbomEntry.Open());
        Assert.All(
            Sbom.RootElement.GetProperty("packages").EnumerateArray(),
            static Package=>Assert.Equal(
                JsonValueKind.False,Package.GetProperty("filesAnalyzed").ValueKind));
    }

    [Fact]
    public async Task ValidateBuiltPackage_RejectsChangedArchive(){
        ReleasePackageBuildResult Result=await ReleasePackageEvidence.BuildPreparedPackageAsync(
            this.CreatePreparedPayload(),Path.Combine(this.Root,"output"),
            CancellationToken.None);
        await File.AppendAllTextAsync(Result.ArchivePath,"changed",Encoding.UTF8);

        IReadOnlyList<string>Errors=await ReleasePackageEvidence.ValidateBuiltPackageAsync(
            Result.ArchivePath,Result.SigningInputsPath,CancellationToken.None);

        Assert.Contains(Errors,Error=>Error.Contains(
            "archive SHA-256",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreparedPackage_RejectsMissingNativePayload(){
        PreparedReleasePayload Prepared=this.CreatePreparedPayload();
        File.Delete(Path.Combine(Prepared.CliDirectory,"assimp.dll"));

        InvalidDataException Error=await Assert.ThrowsAsync<InvalidDataException>(
            ()=>ReleasePackageEvidence.BuildPreparedPackageAsync(
                Prepared,Path.Combine(this.Root,"output"),CancellationToken.None));

        Assert.Contains("assimp.dll",Error.Message,StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(this.Root,"output"))
            &&Directory.EnumerateFileSystemEntries(Path.Combine(this.Root,"output")).Any());
    }

    [Fact]
    public async Task BuildPreparedPackage_RejectsVsixWithoutLocalizedTemplates(){
        PreparedReleasePayload Prepared=this.CreatePreparedPayload(includeJapaneseTemplate:false);

        InvalidDataException Error=await Assert.ThrowsAsync<InvalidDataException>(
            ()=>ReleasePackageEvidence.BuildPreparedPackageAsync(
                Prepared,Path.Combine(this.Root,"output"),CancellationToken.None));

        Assert.Contains("1041",Error.Message,StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallRunUninstall_UsesExtractedPayloadAndLeavesNoResidue(){
        string ArchivePath=this.CreateSmokeArchive();
        string InstallParent=Path.Combine(this.Root,"install");

        ReleasePackageInstallResult Result=
            await ReleasePackageEvidence.InstallRunUninstallAsync(
                ArchivePath,InstallParent,"cli/smoke.cmd",[],
                ["PACKAGE-SMOKE-PASSED"],CancellationToken.None);

        Assert.True(Result.Installed);
        Assert.True(Result.Ran);
        Assert.True(Result.Uninstalled);
        Assert.Equal(0,Result.ExitCode);
        Assert.Equal(0,Result.ResidueCount);
        Assert.Contains("PACKAGE-SMOKE-PASSED",Result.StandardOutput,StringComparison.Ordinal);
        Assert.Equal(
            ReleasePackageEvidence.ComputeArchiveClosureHash(ArchivePath),
            Result.InstalledClosureSha256);
        Assert.False(Directory.Exists(InstallParent)
            &&Directory.EnumerateFileSystemEntries(InstallParent).Any());
    }

    [Fact]
    public async Task InstallRunUninstall_FailsAndStillUninstallsWhenOracleIsMissing(){
        string ArchivePath=this.CreateSmokeArchive();
        string InstallParent=Path.Combine(this.Root,"install");

        InvalidDataException Error=await Assert.ThrowsAsync<InvalidDataException>(
            ()=>ReleasePackageEvidence.InstallRunUninstallAsync(
                ArchivePath,InstallParent,"cli/smoke.cmd",[],
                ["MISSING-ORACLE"],CancellationToken.None));

        Assert.Contains("oracle",Error.Message,StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(InstallParent)
            &&Directory.EnumerateFileSystemEntries(InstallParent).Any());
    }

    [Fact]
    public async Task InstallRunUninstall_RejectsUnownedDirectoryEntryAndLeavesNoResidue(){
        string ArchivePath=this.CreateSmokeArchive();
        using(ZipArchive Archive=ZipFile.Open(ArchivePath,ZipArchiveMode.Update)){
            ZipArchiveEntry DirectoryEntry=Archive.CreateEntry("unexpected/");
            DirectoryEntry.LastWriteTime=
                new DateTimeOffset(1980,1,1,0,0,0,TimeSpan.Zero);
        }
        string InstallParent=Path.Combine(this.Root,"install");

        InvalidDataException Error=await Assert.ThrowsAsync<InvalidDataException>(
            ()=>ReleasePackageEvidence.InstallRunUninstallAsync(
                ArchivePath,InstallParent,"cli/smoke.cmd",[],
                ["PACKAGE-SMOKE-PASSED"],CancellationToken.None));

        Assert.Contains("directory entry",Error.Message,StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(InstallParent)
            &&Directory.EnumerateFileSystemEntries(InstallParent).Any());
    }

    [Fact]
    public void ShaderToolBuild_DoesNotInheritProductPublishProperties(){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Targets=File.ReadAllText(Path.Combine(
            RepositoryRoot,"build","CSharpGameEngine.ShaderGraph.targets"));

        Assert.Contains(
            "RuntimeIdentifier;SelfContained;PublishDir",
            Targets,StringComparison.Ordinal);
        Assert.Contains(
            "CollectCgeShaderArtifactsForPublish",
            Targets,StringComparison.Ordinal);
        Assert.Contains(
            "<CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>",
            Targets,StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasePackageStage_IsolatesTheFocusedTestBuildFromItsRunningHost(){
        FeatureStageDefinition Stage=FeatureStageCatalog.CreateDefault()
            .GetStage("release.packages");
        FeatureStageCommand Command=Assert.Single(
            Stage.Commands,
            static Command=>Command.Arguments.Count>0
                &&Command.Arguments[0]=="test");

        int ArtifactsPathIndex=Command.Arguments.ToList().IndexOf("--artifacts-path");
        Assert.True(ArtifactsPathIndex>=0);
        Assert.Equal(
            ".artifacts/FeatureStageBuild/release-packages/{configuration}",
            Command.Arguments[ArtifactsPathIndex+1]);
        Assert.Contains("--disable-build-servers",Command.Arguments);
    }

    public void Dispose(){
        if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); }
    }

    private PreparedReleasePayload CreatePreparedPayload(bool includeJapaneseTemplate=true){
        string Input=Path.Combine(this.Root,"input-"+Guid.NewGuid().ToString("N"));
        string Cli=Path.Combine(Input,"cli");
        Directory.CreateDirectory(Path.Combine(Cli,"Samples"));
        Directory.CreateDirectory(Path.Combine(Cli,"Shaders"));
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
            ["ItemTemplates/CSharp/GameEngine/1033/SceneFile/Scene.json"]="fixture-en",
            [includeJapaneseTemplate
                ?"ItemTemplates/CSharp/GameEngine/1041/SceneFile/Scene.json"
                :"ItemTemplates/CSharp/GameEngine/1041/SceneFile/missing.txt"]="fixture-ja",
        });
        string Nuget=Path.Combine(Input,"CSharpGameEngine.CodeAnalysis.0.1.7.nupkg");
        CreateZip(Nuget,new Dictionary<string,string>(StringComparer.Ordinal){
            ["CSharpGameEngine.CodeAnalysis.nuspec"]="<package><metadata><id>CSharpGameEngine.CodeAnalysis</id><version>0.1.7</version></metadata></package>",
            ["analyzers/dotnet/cs/CSharpGameEngine.CodeAnalysis.dll"]="fixture-analyzer",
        });
        return new PreparedReleasePayload(
            Cli,Vsix,Nuget,"0.1.7",
            [new ReleaseNugetDependency(
                "SharpGLTF.Core","1.0.6",
                new string('a',64),"NOASSERTION")]);
    }

    private string CreateSmokeArchive(){
        string PathValue=Path.Combine(this.Root,"smoke.zip");
        CreateZip(PathValue,new Dictionary<string,string>(StringComparer.Ordinal){
            ["cli/smoke.cmd"]="@echo off\r\necho PACKAGE-SMOKE-PASSED\r\n",
        });
        return PathValue;
    }

    private static void CreateZip(string PathValue,IReadOnlyDictionary<string,string>Files){
        Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
        using ZipArchive Archive=ZipFile.Open(PathValue,ZipArchiveMode.Create);
        foreach((string Name,string Contents)in Files.OrderBy(static Pair=>Pair.Key,StringComparer.Ordinal)){
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
}
