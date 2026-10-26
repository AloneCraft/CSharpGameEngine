using System.IO.Compression;
using System.Xml.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class VsixPackagingLocalizationTests {
    private static readonly string ProjectDirectory=Path.Combine(VsixBuildIdentity.RepoRoot,"tools","CSharpGameEngine.VSExtension");
    [Fact]
    public void Manifest_UsesEnglishNeutralIdentityAndCurrentBuildVersion(){
        XNamespace Namespace="http://schemas.microsoft.com/developer/vsx-schema/2011";
        var Manifest=XDocument.Load(Path.Combine(ProjectDirectory,"source.extension.vsixmanifest"));
        var Metadata=Assert.IsType<XElement>(Manifest.Root?.Element(Namespace+"Metadata"));
        var Identity=Assert.IsType<XElement>(Metadata.Element(Namespace+"Identity"));
        Assert.Equal(VsixBuildIdentity.Version,(string?)Identity.Attribute("Version"));
        Assert.Equal("en-US",(string?)Identity.Attribute("Language"));
        Assert.Equal("C# Game Engine",Metadata.Element(Namespace+"DisplayName")?.Value);
        Assert.DoesNotContain("placeholder",Metadata.Element(Namespace+"Description")?.Value,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void LanguagePack_ProvidesJapaneseInstallerMetadata(){
        var LanguagePack=XDocument.Load(Path.Combine(ProjectDirectory,"ja-JP","Extension.vsixlangpack"));
        XNamespace Namespace="http://schemas.microsoft.com/developer/vsx-schema/2011";
        var Metadata=Assert.IsType<XElement>(LanguagePack.Root?.Element(Namespace+"Metadata"));
        Assert.Equal("C# Game Engine",Metadata.Element(Namespace+"DisplayName")?.Value);
        Assert.Contains("Visual Studio",Metadata.Element(Namespace+"Description")?.Value);
    }
    [Fact]
    public void VspackageResources_ExposeMatchingEnglishAndJapaneseTemplateIds(){
        var English=LoadResx(Path.Combine(ProjectDirectory,"VSPackage.resx"));
        var EnglishSatellite=LoadResx(Path.Combine(ProjectDirectory,"VSPackage.en-US.resx"));
        var Japanese=LoadResx(Path.Combine(ProjectDirectory,"VSPackage.ja-JP.resx"));
        Assert.Equal(English.OrderBy(Pair=>Pair.Key),EnglishSatellite.OrderBy(Pair=>Pair.Key));
        Assert.Equal(English.Keys.OrderBy(Key=>Key),Japanese.Keys.OrderBy(Key=>Key));
        Assert.Equal("Scene file",English["130"]);
        Assert.Equal("シーンファイル",Japanese["130"]);
        Assert.Equal("Creates a basic C# Game Engine stage.",English["131"]);
        Assert.Equal("C# Game Engine の基本ステージを作成します。",Japanese["131"]);
    }
    [Fact]
    public void NeutralAndJapaneseVsct_HaveIdenticalCommandStructure(){
        var English=XDocument.Load(Path.Combine(ProjectDirectory,"OpenViewerCommandPackage.vsct"));
        var EnglishSatellite=XDocument.Load(Path.Combine(ProjectDirectory,"OpenViewerCommandPackage.en-US.vsct"));
        var Japanese=XDocument.Load(Path.Combine(ProjectDirectory,"OpenViewerCommandPackage.ja-JP.vsct"));
        Assert.Equal(English.ToString(SaveOptions.DisableFormatting),EnglishSatellite.ToString(SaveOptions.DisableFormatting));
        RemoveLocalization(English);
        RemoveLocalization(Japanese);
        Assert.Equal(English.ToString(SaveOptions.DisableFormatting),Japanese.ToString(SaveOptions.DisableFormatting));
    }
    [Fact]
    public void BuiltVsix_ContainsJapaneseSatelliteLanguagePackAndBothTemplateCultures(){
        string VsixPath=Path.Combine(AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix");
        using var Archive=ZipFile.OpenRead(VsixPath);
        Assert.NotNull(Archive.GetEntry("ja-JP/CSharpGameEngine.VSExtension.resources.dll"));
        Assert.NotNull(Archive.GetEntry("ja-JP/Extension.vsixlangpack"));
        Assert.NotNull(Archive.GetEntry("ItemTemplates/templateManifest0.1033.vstman"));
        Assert.Contains(Archive.Entries,Entry=>Entry.FullName.StartsWith("ItemTemplates/templateManifest",StringComparison.Ordinal) && Entry.FullName.EndsWith(".1041.vstman",StringComparison.Ordinal));
        var EnglishSource=ReadEntry(Archive,"ItemTemplates/CSharp/GameEngine/1033/SceneFile/Scene.json");
        var JapaneseSource=ReadEntry(Archive,"ItemTemplates/CSharp/GameEngine/1041/SceneFile/Scene.json");
        Assert.Equal(EnglishSource,JapaneseSource);
        var EnglishTemplate=XDocument.Parse(ReadEntry(Archive,"ItemTemplates/CSharp/GameEngine/1033/SceneFile/SceneFile.vstemplate"));
        var JapaneseTemplate=XDocument.Parse(ReadEntry(Archive,"ItemTemplates/CSharp/GameEngine/1041/SceneFile/SceneFile.vstemplate"));
        XNamespace Namespace="http://schemas.microsoft.com/developer/vstemplate/2005";
        Assert.Equal("CSharpGameEngine.SceneFile",EnglishTemplate.Root?.Element(Namespace+"TemplateData")?.Element(Namespace+"TemplateID")?.Value);
        Assert.Equal("CSharpGameEngine.SceneFile",JapaneseTemplate.Root?.Element(Namespace+"TemplateData")?.Element(Namespace+"TemplateID")?.Value);
    }
    [Fact]
    public void CopiedBuiltVsix_MatchesPackageBesideBuildOutputAssemblyByteForByte(){
        string CopiedVsixPath=Path.Combine(AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix");
        string BuiltVsixPath=Path.ChangeExtension(VsixBuildIdentity.BuildOutputPath,".vsix");
        Assert.True(File.Exists(BuiltVsixPath),$"VSIX beside BuildOutputPath was not found: {BuiltVsixPath}");
        Assert.Equal(File.ReadAllBytes(BuiltVsixPath),File.ReadAllBytes(CopiedVsixPath));
    }

    private static Dictionary<string,string> LoadResx(string PathValue){
        return XDocument.Load(PathValue)
            .Root!
            .Elements("data")
            .ToDictionary(Data=>(string)Data.Attribute("name")!,Data=>Data.Element("value")?.Value??string.Empty,StringComparer.Ordinal);
    }
    private static void RemoveLocalization(XDocument Document){
        Document.DescendantNodes().OfType<XComment>().Remove();
        Document.Descendants().Where(Element=>Element.Name.LocalName=="Strings").Remove();
    }
    private static string ReadEntry(ZipArchive Archive,string Name){
        using var Reader=new StreamReader(Assert.IsType<ZipArchiveEntry>(Archive.GetEntry(Name)).Open());
        return Reader.ReadToEnd();
    }
}
