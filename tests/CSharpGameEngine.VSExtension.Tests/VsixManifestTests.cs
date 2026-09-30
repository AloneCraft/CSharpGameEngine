using System.Xml.Linq;
using System.IO.Compression;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class VsixManifestTests {
    [Fact]
    public void SourceManifest_RegistersExtensionAssemblyAsMefComponent(){
        var ManifestPath=Path.Combine(AppContext.BaseDirectory,"TestAssets","source.extension.vsixmanifest");
        XNamespace VsixNamespace="http://schemas.microsoft.com/developer/vsx-schema/2011";

        var MefAsset=XDocument.Load(ManifestPath)
                              .Root?
                              .Element(VsixNamespace+"Assets")?
                              .Elements(VsixNamespace+"Asset")
                              .SingleOrDefault(Asset=>(string?)Asset.Attribute("Type")=="Microsoft.VisualStudio.MefComponent");

        Assert.NotNull(MefAsset);
        Assert.Equal("|%CurrentProject%|",(string?)MefAsset.Attribute("Path"));
    }

    [Fact]
    public void SourceManifest_VersionMatchesLoadedAssemblyIdentity(){
        var ManifestPath=Path.Combine(AppContext.BaseDirectory,"TestAssets","source.extension.vsixmanifest");
        XNamespace VsixNamespace="http://schemas.microsoft.com/developer/vsx-schema/2011";

        string? ManifestVersion=(string?)XDocument.Load(ManifestPath)
            .Root?
            .Element(VsixNamespace+"Metadata")?
            .Element(VsixNamespace+"Identity")?
            .Attribute("Version");

        Assert.Equal(VsixBuildIdentity.Version,ManifestVersion);
    }

    [Fact]
    public void LoadedAssembly_BuildIdentityPreservesEachRequiredField(){
        Assert.NotEqual("unknown",VsixBuildIdentity.Version);
        Assert.NotEqual("unknown",VsixBuildIdentity.RepoRoot);
        Assert.NotEqual("unknown",VsixBuildIdentity.BuildUtc);
        Assert.NotEqual("unknown",VsixBuildIdentity.GitRevision);
        Assert.NotEqual("unknown",VsixBuildIdentity.BuildOutputPath);
        Assert.True(Path.IsPathRooted(VsixBuildIdentity.RepoRoot));
        Assert.True(File.Exists(Path.Combine(VsixBuildIdentity.RepoRoot,RepoLocator.RepoMarkerFileName)));
        Assert.Equal(VsixBuildIdentity.RepoRoot,RepoLocator.ResolveRepoRoot());
        Assert.True(DateTimeOffset.TryParse(VsixBuildIdentity.BuildUtc,out _));
        Assert.EndsWith("CSharpGameEngine.VSExtension.dll",VsixBuildIdentity.BuildOutputPath,StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(VsixBuildIdentity.GitRevision,VsixBuildIdentity.RepoRoot);
        Assert.NotEqual(VsixBuildIdentity.GitRevision,VsixBuildIdentity.Version);
        Assert.NotEqual(VsixBuildIdentity.GitRevision,VsixBuildIdentity.BuildUtc);
        Assert.NotEqual(VsixBuildIdentity.GitRevision,VsixBuildIdentity.BuildOutputPath);
    }
    [Theory]
    [InlineData("2026-08-23T14:20:31.1234567Z", "2026-08-23 14:20Z")]
    [InlineData("2026-08-23T23:59:59+09:00", "2026-08-23 14:59Z")]
    public void BuildUtc_FormatsCompactUniversalTimestamp(string Value,string Expected){
        Assert.Equal(Expected,VsixBuildIdentity.FormatBuildUtc(Value));
    }

    [Fact]
    public void BuildUtc_InvalidMetadataUsesUnknown(){
        Assert.Equal("unknown",VsixBuildIdentity.FormatBuildUtc("not-a-timestamp"));
    }
    [Fact]
    public void DisplayText_ContainsEmbeddedBuildTimestamp(){
        Assert.Contains($"build {VsixBuildIdentity.FormatBuildUtc(VsixBuildIdentity.BuildUtc)}",VsixBuildIdentity.DisplayText);
    }
    [Fact]
    public void VsixProject_ForcesIdentityRefreshDuringIncrementalBuild(){
        string ProjectPath=Path.Combine(VsixBuildIdentity.RepoRoot,"tools","CSharpGameEngine.VSExtension","CSharpGameEngine.VSExtension.csproj");
        string Project=File.ReadAllText(ProjectPath);
        Assert.Contains("<DisableFastUpToDateCheck>true</DisableFastUpToDateCheck>",Project);
        Assert.Contains("BeforeTargets=\"CoreCompile\"",Project);
        Assert.Contains("AssemblyMetadata Remove=\"CSharpGameEngineBuildUtc\"",Project);
    }

    [Fact]
    public void VsixProject_UsesPreviewHostBoundaryWithoutNativeRendererPackages(){
        string ProjectRoot=VsixBuildIdentity.RepoRoot;
        string ProjectPath=Path.Combine(
            ProjectRoot,"tools","CSharpGameEngine.VSExtension",
            "CSharpGameEngine.VSExtension.csproj");
        string Project=File.ReadAllText(ProjectPath);

        Assert.Contains("CSharpGameEngine.Contracts.csproj",Project);
        Assert.Contains("PackageReference Include=\"PolySharp\"",Project);
        Assert.Contains("PackageReference Include=\"Microsoft.Bcl.HashCode\"",Project);
        Assert.DoesNotContain("PackageReference Include=\"Vortice.",Project);
        Assert.False(File.Exists(Path.Combine(
            ProjectRoot,"tools","CSharpGameEngine.VSExtension","SceneRenderer.cs")));
        Assert.False(File.Exists(Path.Combine(
            ProjectRoot,"tools","CSharpGameEngine.VSExtension","VorticeRenderer.cs")));
        Assert.False(File.Exists(Path.Combine(
            ProjectRoot,"tools","CSharpGameEngine.VSExtension","NativeEvent.cs")));
    }

    [Fact]
    public void VsixPackage_ContainsPreviewContractsWithoutNativeRendererAssemblies(){
        string VsixPath=Path.Combine(
            AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix");
        using ZipArchive Archive=ZipFile.OpenRead(VsixPath);
        string[]Entries=Archive.Entries.Select(static Entry=>Entry.FullName).ToArray();

        Assert.Contains("CSharpGameEngine.Contracts.dll",Entries);
        Assert.Contains("Microsoft.Bcl.HashCode.dll",Entries);
        Assert.Contains("PreviewHost/CSharpGameEngine.EditorPreview.Host.exe",Entries);
        Assert.Contains("PreviewHost/CSharpGameEngine.EditorPreview.Host.dll",Entries);
        Assert.Contains("PreviewHost/CSharpGameEngine.EditorPreview.Host.deps.json",Entries);
        Assert.Contains("PreviewHost/CSharpGameEngine.EditorPreview.Host.runtimeconfig.json",Entries);
        Assert.Contains("PreviewHost/Shaders/Stage.cgeshaderartifact",Entries);
        // ADR-0065/0068: bridges, the engine SDK package and the ThirdPerson template make the VSIX work outside the repository.
        foreach(var Bridge in new[]{"CSharpGameEngine.Import.Gltf.Cli","CSharpGameEngine.Import.Fbx.Cli","CSharpGameEngine.Import.AssetAuthoring.Cli","CSharpGameEngine.Primitive.Cli","CSharpGameEngine.ShaderGraph.Cli"}){
            Assert.Contains($"Bridges/{Bridge}/{Bridge}.exe",Entries);
            Assert.Contains($"Bridges/{Bridge}/{Bridge}.runtimeconfig.json",Entries);
        }
        Assert.Single(Entries,static Entry=>Entry.StartsWith("Packages/CSharpGameEngine.Sdk.",StringComparison.Ordinal)&&Entry.EndsWith(".nupkg",StringComparison.Ordinal));
        Assert.Contains("ProjectTemplates/CSharp/GameEngine/1033/ThirdPerson/ThirdPerson.vstemplate",Entries);
        Assert.Contains("ProjectTemplates/CSharp/GameEngine/1041/ThirdPerson/ThirdPerson.csproj.template",Entries);
        Assert.Contains("ProjectTemplateSources/ThirdPerson/Scenes/Lvl_ThirdPerson.scene.json",Entries);
        Assert.Contains("ProjectTemplateSources/ThirdPerson/Assets/Characters/Mannequins/SKM_Quinn_Simple.glb",Entries);
        Assert.DoesNotContain(Entries,static Entry=>Entry.StartsWith("ProjectTemplateSources/",StringComparison.Ordinal)&&(Entry.Contains("/bin/")||Entry.Contains("/obj/")||Entry.EndsWith(".csproj",StringComparison.Ordinal)));
        Assert.DoesNotContain(Entries,static Entry=>Entry.StartsWith(
            "Vortice.",StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Entries,static Entry=>Entry.StartsWith(
            "SharpGen.",StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Entries,static Entry=>Entry.StartsWith(
            "PolySharp.",StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Entries,static Entry=>Entry.StartsWith(
            "PreviewHost/",StringComparison.OrdinalIgnoreCase)
            &&Entry.EndsWith(".pdb",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuiltVsix_PutsTheExtensionFolderOnTheTemplateWizardProbingPath(){
        // The vstemplates name their IWizard by the simple assembly name, which Visual Studio
        // resolves only through the package's BindingPaths; without it creating a project from
        // the Third Person template fails with "tried to load component assembly".
        var VsixPath=Path.Combine(AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix");
        using var Archive=ZipFile.OpenRead(VsixPath);
        var Entry=Archive.GetEntry("CSharpGameEngine.VSExtension.pkgdef");
        Assert.NotNull(Entry);
        using var Reader=new StreamReader(Entry!.Open());
        var Pkgdef=Reader.ReadToEnd();
        Assert.Contains(@"[$RootKey$\BindingPaths\{"+CSharpGameEngineViewerPackage.PackageGuidString,Pkgdef,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"$PackageFolder$\"=\"\"",Pkgdef);
    }
}
