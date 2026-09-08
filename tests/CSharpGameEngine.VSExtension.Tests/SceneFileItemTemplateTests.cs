using System.IO.Compression;
using System.Xml.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneFileItemTemplateTests {
    private static readonly string TemplateDirectory=Path.Combine(
        AppContext.BaseDirectory,
        "TestAssets",
        "ItemTemplates",
        "CSharp",
        "GameEngine",
        "SceneFile");

    [Fact]
    public void Template_RegistersSceneFileInCSharpGameEngineCategory(){
        var Template=XDocument.Load(Path.Combine(TemplateDirectory,"SceneFile.vstemplate"));
        XNamespace TemplateNamespace="http://schemas.microsoft.com/developer/vstemplate/2005";
        var Data=Assert.IsType<XElement>(Template.Root?.Element(TemplateNamespace+"TemplateData"));

        var Name=Assert.IsType<XElement>(Data.Element(TemplateNamespace+"Name"));
        var Description=Assert.IsType<XElement>(Data.Element(TemplateNamespace+"Description"));
        Assert.Equal("{aec17804-e2e4-46e7-b373-de11af816203}",(string?)Name.Attribute("Package"));
        Assert.Equal("130",(string?)Name.Attribute("ID"));
        Assert.Equal("{aec17804-e2e4-46e7-b373-de11af816203}",(string?)Description.Attribute("Package"));
        Assert.Equal("131",(string?)Description.Attribute("ID"));
        Assert.Equal("CSharp",Data.Element(TemplateNamespace+"ProjectType")?.Value);
        Assert.Null(Data.Element(TemplateNamespace+"TemplateGroupID"));
        Assert.Equal("true",Data.Element(TemplateNamespace+"ShowByDefault")?.Value);
        Assert.Equal("1",Data.Element(TemplateNamespace+"NumberOfParentCategoriesToRollUp")?.Value);
        Assert.Equal("CSharpGameEngine.SceneFile",Data.Element(TemplateNamespace+"TemplateID")?.Value);
        Assert.Equal("Scene.cs",Data.Element(TemplateNamespace+"DefaultName")?.Value);
        Assert.Equal("__TemplateIcon.ico",Data.Element(TemplateNamespace+"Icon")?.Value);
    }

    [Fact]
    public void Template_CreatesBasicStageAndUsesVisualStudioReplacementTokens(){
        var Source=File.ReadAllText(Path.Combine(TemplateDirectory,"Scene.cs"));

        Assert.Contains("namespace $rootnamespace$;",Source);
        Assert.Contains("class $safeitemname$ : IScene",Source);
        Assert.Contains("new AmbientLight()",Source);
        Assert.Contains("new DirectionalLight()",Source);
        Assert.Contains("new HeightFog()",Source);
        Assert.Contains("new PrimitiveMesh { Type=PrimitiveType.Box }",Source);
        Assert.Contains("new PlayerStart()",Source);
        Assert.Contains("Position=new LogicalVector3(0f,-0.5f,0f)",Source);
        Assert.Contains("Scale=new LogicalVector3(10f,1f,10f)",Source);
        Assert.DoesNotContain("RotationW",Source);
    }

    [Fact]
    public void SourceManifest_RegistersGeneratedItemTemplates(){
        var ManifestPath=Path.Combine(AppContext.BaseDirectory,"TestAssets","source.extension.vsixmanifest");
        XNamespace VsixNamespace="http://schemas.microsoft.com/developer/vsx-schema/2011";

        var TemplateAsset=XDocument.Load(ManifestPath)
                                  .Root?
                                  .Element(VsixNamespace+"Assets")?
                                  .Elements(VsixNamespace+"Asset")
                                  .SingleOrDefault(Asset=>(string?)Asset.Attribute("Type")=="Microsoft.VisualStudio.ItemTemplate");

        Assert.NotNull(TemplateAsset);
        Assert.Equal("ItemTemplates",(string?)TemplateAsset.Attribute("Path"));
    }
    [Fact]
    public void Template_ProvidesCSharpBadgeIcon(){
        var IconPath=Path.Combine(TemplateDirectory,"__TemplateIcon.ico");
        var Header=File.ReadAllBytes(IconPath);

        Assert.True(Header.Length>6);
        Assert.Equal(new byte[] { 0,0,1,0,1,0 },Header.Take(6));
    }
    [Fact]
    public void BuiltVsix_ContainsRegisteredSceneTemplateManifestAndFiles(){
        var VsixPath=Path.Combine(AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix");

        using var Archive=ZipFile.OpenRead(VsixPath);
        var TemplateManifest=Archive.GetEntry("ItemTemplates/templateManifest0.1033.vstman");
        Assert.NotNull(TemplateManifest);
        Assert.NotNull(Archive.GetEntry("ItemTemplates/CSharp/GameEngine/1033/SceneFile/SceneFile.vstemplate"));
        Assert.NotNull(Archive.GetEntry("ItemTemplates/CSharp/GameEngine/1033/SceneFile/Scene.cs"));
        Assert.NotNull(Archive.GetEntry("ItemTemplates/CSharp/GameEngine/1033/SceneFile/__TemplateIcon.ico"));
        Assert.NotNull(Archive.GetEntry("ItemTemplates/CSharp/GameEngine/1041/SceneFile/SceneFile.vstemplate"));
        Assert.NotNull(Archive.GetEntry("ItemTemplates/CSharp/GameEngine/1041/SceneFile/Scene.cs"));
        Assert.NotNull(Archive.GetEntry("ItemTemplates/CSharp/GameEngine/1041/SceneFile/__TemplateIcon.ico"));
        Assert.DoesNotContain(Archive.Entries,Entry=>Entry.FullName.StartsWith("CSharp/GameEngine/",StringComparison.Ordinal));

        using var ManifestStream=TemplateManifest.Open();
        var Manifest=XDocument.Load(ManifestStream);
        XNamespace ManifestNamespace="http://schemas.microsoft.com/developer/vstemplatemanifest/2015";
        var Container=Assert.Single(Manifest.Root!.Elements(ManifestNamespace+"VSTemplateContainer"),Item=>Item.Element(ManifestNamespace+"RelativePathOnDisk")?.Value.EndsWith("SceneFile",StringComparison.Ordinal)==true);

        Assert.Equal("Item",(string?)Container.Attribute("TemplateType"));
        Assert.Equal("CSharp\\GameEngine\\1033\\SceneFile",Container.Element(ManifestNamespace+"RelativePathOnDisk")?.Value);
        Assert.Equal("SceneFile.vstemplate",Container.Element(ManifestNamespace+"TemplateFileName")?.Value);
    }
}