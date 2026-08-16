using System.Xml.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class VsixManifestTests {
    [Fact]
    public void SourceManifest_RegistersExtensionAssemblyAsMefComponent(){
        string ManifestPath=Path.Combine(AppContext.BaseDirectory,"TestAssets","source.extension.vsixmanifest");
        XNamespace VsixNamespace="http://schemas.microsoft.com/developer/vsx-schema/2011";

        XElement? MefAsset=XDocument.Load(ManifestPath)
            .Root?
            .Element(VsixNamespace+"Assets")?
            .Elements(VsixNamespace+"Asset")
            .SingleOrDefault(Asset=>(string?)Asset.Attribute("Type")=="Microsoft.VisualStudio.MefComponent");

        Assert.NotNull(MefAsset);
        Assert.Equal("|%CurrentProject%|",(string?)MefAsset.Attribute("Path"));
    }
}
