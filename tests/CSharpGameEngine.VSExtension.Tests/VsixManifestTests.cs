using System.Xml.Linq;

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
}
