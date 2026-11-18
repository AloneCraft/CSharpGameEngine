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
        Assert.EndsWith("VSExtension.dll",VsixBuildIdentity.BuildOutputPath,StringComparison.OrdinalIgnoreCase);
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
        string ProjectPath=Path.Combine(VsixBuildIdentity.RepoRoot,"tools","VSExtension","VSExtension.csproj");
        string Project=File.ReadAllText(ProjectPath);
        Assert.Contains("<DisableFastUpToDateCheck>true</DisableFastUpToDateCheck>",Project);
        Assert.Contains("BeforeTargets=\"CoreCompile\"",Project);
        Assert.Contains("AssemblyMetadata Remove=\"CSharpGameEngineBuildUtc\"",Project);
    }

    /// <summary>
    /// VS の中で拡張を作ると、入れ子の組み立ては参照先を作り直さない(BuildingInsideVisualStudio)。同梱する別プロセスの道具は
    /// どれも拡張の参照先ではないので、外さないと古い DLL が混ざる(2026-11-17、PreviewHost の Windows.dll だけ古く、
    /// 新しいシェーダーの入口が無いと言ってプレビューが開けなかった)。
    /// </summary>
    [Fact]
    public void VsixProject_NestedPublishesRebuildTheirReferencesInsideVisualStudio(){
        string Project=File.ReadAllText(Path.Combine(VsixBuildIdentity.RepoRoot,"tools","VSExtension","VSExtension.csproj"));
        var Nested=System.Text.RegularExpressions.Regex.Matches(Project,"<MSBuild\\b[^>]*Targets=\"(?:Publish|Restore;Pack)\"[^>]*>");
        Assert.Equal(3,Nested.Count);
        foreach(System.Text.RegularExpressions.Match Call in Nested){
            Assert.Contains("BuildingInsideVisualStudio=false",Call.Value);
        }
    }

    [Fact]
    public void VsixProject_UsesPreviewHostBoundaryWithoutNativeRendererPackages(){
        string ProjectRoot=VsixBuildIdentity.RepoRoot;
        string ProjectPath=Path.Combine(
            ProjectRoot,"tools","VSExtension",
            "VSExtension.csproj");
        string Project=File.ReadAllText(ProjectPath);

        Assert.Contains("Contracts.csproj",Project);
        Assert.Contains("PackageReference Include=\"PolySharp\"",Project);
        Assert.Contains("PackageReference Include=\"Microsoft.Bcl.HashCode\"",Project);
        Assert.DoesNotContain("PackageReference Include=\"Vortice.",Project);
        Assert.False(File.Exists(Path.Combine(
            ProjectRoot,"tools","VSExtension","SceneRenderer.cs")));
        Assert.False(File.Exists(Path.Combine(
            ProjectRoot,"tools","VSExtension","VorticeRenderer.cs")));
        Assert.False(File.Exists(Path.Combine(
            ProjectRoot,"tools","VSExtension","NativeEvent.cs")));
    }

    private static string GameProjectGeneratorStartup(string Template)=>CSharpGameEngine.ProjectTemplates.GameProjectGenerator.StartupSceneOf(Template);

    [Fact]
    public void VsixPackage_ContainsPreviewContractsWithoutNativeRendererAssemblies(){
        string VsixPath=Path.Combine(
            AppContext.BaseDirectory,"TestAssets","VSExtension.vsix");
        using ZipArchive Archive=ZipFile.OpenRead(VsixPath);
        string[]Entries=Archive.Entries.Select(static Entry=>Entry.FullName).ToArray();

        Assert.Contains("Contracts.dll",Entries);
        Assert.Contains("Microsoft.Bcl.HashCode.dll",Entries);
        Assert.Contains("PreviewHost/EditorPreview.Host.exe",Entries);
        Assert.Contains("PreviewHost/EditorPreview.Host.dll",Entries);
        Assert.Contains("PreviewHost/EditorPreview.Host.deps.json",Entries);
        Assert.Contains("PreviewHost/EditorPreview.Host.runtimeconfig.json",Entries);
        Assert.Contains("PreviewHost/Shaders/Stage.cgeshaderartifact",Entries);
        // ADR-0065/0068：ブリッジ、エンジン SDK パッケージ、ThirdPerson テンプレートがあるため、VSIX はリポジトリ外でも動作する。
        // Cge は取り込み・プレビューのブリッジとシェーダーのコンパイラーを下位コマンドとして持つ。Cook.Cli は別のプロセスのまま。
        foreach(var Bridge in new[]{"Cge","Cook.Cli"}){
            Assert.Contains($"Bridges/{Bridge}/{Bridge}.exe",Entries);
            Assert.Contains($"Bridges/{Bridge}/{Bridge}.runtimeconfig.json",Entries);
        }
        foreach(var File in new[]{"Cook.Cli.dll","Cook.Cli.deps.json","Cook.Cli.runtimeconfig.json"}){
            Assert.Contains("Bridges/Cge/Cook/"+File,Entries);
        }
        var SdkPath=Assert.Single(Entries,static Entry=>Entry.StartsWith("Packages/CSharpGameEngine.Sdk.",StringComparison.Ordinal)&&Entry.EndsWith(".nupkg",StringComparison.Ordinal));
        using(var Input=Archive.GetEntry(SdkPath)!.Open()){
            using var Bytes=new MemoryStream();Input.CopyTo(Bytes);Bytes.Position=0;
            using var Sdk=new ZipArchive(Bytes,ZipArchiveMode.Read);
            var SdkEntries=Sdk.Entries.Select(static Entry=>Entry.FullName.Replace('\\','/')).ToArray();
            Assert.DoesNotContain(SdkEntries,static Entry=>Entry.StartsWith("lib/",StringComparison.Ordinal)&&
                (Path.GetFileName(Entry).StartsWith("Cook",StringComparison.Ordinal)||Path.GetFileName(Entry).StartsWith("Import",StringComparison.Ordinal)||Path.GetFileName(Entry).StartsWith("Microsoft.CodeAnalysis",StringComparison.Ordinal)));
            Assert.Contains("tools/Cook/Cooking.dll",SdkEntries);Assert.Contains("tools/ModelImport/Import.dll",SdkEntries);
            // ゲームの組み立てが呼ぶ shader-graph・scene-schema・content は、SDK の tools/Cge の 1 つの道具にある(Sdk.props)。
            foreach(var File in new[]{"Cge.exe","Cge.dll","Cge.runtimeconfig.json","dxcompiler.dll","dxil.dll"}){
                Assert.Contains("tools/Cge/"+File,SdkEntries);
            }
            Assert.DoesNotContain(SdkEntries,static Entry=>Entry.StartsWith("tools/ShaderGraph/",StringComparison.Ordinal)||Entry.StartsWith("tools/SceneSchema/",StringComparison.Ordinal)||Entry.StartsWith("tools/ContentCompiler/",StringComparison.Ordinal)||Entry.StartsWith("tools/Cge/Cook/",StringComparison.Ordinal));
        }
        foreach(var Template in new[]{"ThirdPerson","FirstPerson","TopDown","Vehicle"}){
            Assert.Contains($"ProjectTemplates/CSharp/GameEngine/1033/{Template}/{Template}.vstemplate",Entries);
            Assert.Contains($"ProjectTemplates/CSharp/GameEngine/1041/{Template}/{Template}.csproj.template",Entries);
            Assert.Contains($"ProjectTemplateSources/{Template}/{GameProjectGeneratorStartup(Template)}",Entries);
        }
        Assert.Contains("ProjectTemplateSources/ThirdPerson/Assets/Characters/Mannequins/SKM_Quinn_Simple.glb",Entries);
        Assert.Contains("ProjectTemplateSources/TopDown/Assets/Characters/Mannequins/SKM_Manny_Simple.glb",Entries);
        Assert.Contains("ProjectTemplateSources/Vehicle/Assets/Vehicles/SportsCar/SM_SportsCar.glb",Entries);
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
        // vstemplates は IWizard を単純なアセンブリ名で指定する。Visual Studio がそれを解決できるのは、パッケージの
        // BindingPaths 経由だけである。これがないと、プロジェクト作成時に
        // Third Person テンプレートで「tried to load component assembly」が発生する。
        var VsixPath=Path.Combine(AppContext.BaseDirectory,"TestAssets","VSExtension.vsix");
        using var Archive=ZipFile.OpenRead(VsixPath);
        var Entry=Archive.GetEntry("VSExtension.pkgdef");
        Assert.NotNull(Entry);
        using var Reader=new StreamReader(Entry!.Open());
        var Pkgdef=Reader.ReadToEnd();
        Assert.Contains(@"[$RootKey$\BindingPaths\{"+CSharpGameEngineViewerPackage.PackageGuidString,Pkgdef,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"$PackageFolder$\"=\"\"",Pkgdef);
    }
}
