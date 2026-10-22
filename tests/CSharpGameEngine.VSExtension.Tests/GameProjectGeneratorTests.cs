using System;
using System.IO;
using System.Linq;
using CSharpGameEngine.ProjectTemplates;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>ADR-0068: the template wizard and the package Stage create the same PackageMode game project.</summary>
public sealed class GameProjectGeneratorTests:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"cge-generator-"+Guid.NewGuid().ToString("N"));

    public GameProjectGeneratorTests(){
        Directory.CreateDirectory(this.Root);
    }

    public void Dispose(){
        try { Directory.Delete(this.Root,true); } catch(IOException){ }
    }

    private static string RepositoryFile(params string[]Parts){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){
            Directory=Directory.Parent;
        }
        Assert.NotNull(Directory);
        return Path.Combine(new[]{Directory!.FullName}.Concat(Parts).ToArray());
    }

    /// <summary>
    /// CopySources renames the template namespace as plain text, so in the real template source it may
    /// only appear as the namespace itself: an identifier such as CgeThirdPersonCharacter would be renamed too.
    /// </summary>
    [Theory]
    [InlineData("ThirdPerson")]
    [InlineData("FirstPerson")]
    [InlineData("TopDown")]
    [InlineData("Vehicle")]
    public void TemplateSource_UsesItsNamespaceOnlyAsTheNamespace(string TemplateName){
        var Template=RepositoryFile("Templates","CSharpGameEngine.Templates."+TemplateName);
        var TemplateNamespace=GameProjectGenerator.NamespaceOf(TemplateName);
        var Whole=new System.Text.RegularExpressions.Regex(@"(?<![A-Za-z0-9_])"+TemplateNamespace+@"(?![A-Za-z0-9_])");
        var Files=Directory.EnumerateFiles(Template,"*",SearchOption.AllDirectories)
            .Where(static File=>File.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)||File.EndsWith(".json",StringComparison.OrdinalIgnoreCase))
            .Where(static File=>!File.Contains(Path.DirectorySeparatorChar+"bin"+Path.DirectorySeparatorChar)&&!File.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar))
            .ToArray();
        Assert.NotEmpty(Files);
        foreach(var File in Files){
            var Text=System.IO.File.ReadAllText(File);
            var All=Text.Split([TemplateNamespace],StringSplitOptions.None).Length-1;
            Assert.True(All==Whole.Matches(Text).Count,$"{File} uses '{TemplateNamespace}' inside another identifier.");
        }
    }

    /// <summary>Each template the wizard offers has its project template, its two string resources and a source folder to copy from.</summary>
    [Fact]
    public void EveryOfferedTemplate_HasAVsTemplateWithItsOwnResourceStrings(){
        var Templates=RepositoryFile("tools","CSharpGameEngine.VSExtension","ProjectTemplates","CSharp","GameEngine");
        var Resources=System.Xml.Linq.XDocument.Load(RepositoryFile("tools","CSharpGameEngine.VSExtension","VSPackage.resx"));
        var JapaneseResources=System.Xml.Linq.XDocument.Load(RepositoryFile("tools","CSharpGameEngine.VSExtension","VSPackage.ja-JP.resx"));
        var Seen=new System.Collections.Generic.HashSet<string>();
        foreach(var Name in GameProjectGenerator.TemplateNames){
            Assert.True(Directory.Exists(RepositoryFile("Templates","CSharpGameEngine.Templates."+Name)),Name);
            var Template=System.Xml.Linq.XDocument.Load(Path.Combine(Templates,Name,Name+".vstemplate"));
            System.Xml.Linq.XNamespace Schema="http://schemas.microsoft.com/developer/vstemplate/2005";
            var Data=Template.Root!.Element(Schema+"TemplateData")!;
            Assert.Equal("CSharpGameEngine."+Name,Data.Element(Schema+"TemplateID")?.Value);
            Assert.True(File.Exists(Path.Combine(Templates,Name,Name+".csproj.template")),Name);
            Assert.True(File.Exists(Path.Combine(Templates,Name,"ReadMe.md")),Name);
            foreach(var Element in new[]{"Name","Description"}){
                var Id=Data.Element(Schema+Element)!.Attribute("ID")!.Value;
                Assert.True(Seen.Add(Id),$"Resource ID {Id} of {Name} is used twice.");
                Assert.Contains(Resources.Root!.Elements("data"),Item=>Item.Attribute("name")?.Value==Id);
                Assert.Contains(JapaneseResources.Root!.Elements("data"),Item=>Item.Attribute("name")?.Value==Id);
            }
        }
    }

    /// <summary>The wizard opens <see cref="GameProjectGenerator.StartupScenePath"/> after creation: it exists and is a scene by the build's rule (ADR-0070).</summary>
    [Theory]
    [InlineData("ThirdPerson")]
    [InlineData("FirstPerson")]
    [InlineData("TopDown")]
    [InlineData("Vehicle")]
    public void GeneratedProject_StartupSceneIsASceneOfTheProject(string TemplateName){
        var Project=Path.Combine(this.Root,"My Game");
        var Created=GameProjectGenerator.CopySources(RepositoryFile("Templates","CSharpGameEngine.Templates."+TemplateName),Project,"My_Game",TemplateName);
        var Startup=Path.GetFullPath(Path.Combine(Project,GameProjectGenerator.StartupSceneOf(TemplateName)));
        Assert.Contains(Startup,Created.Select(Path.GetFullPath));
        Assert.True(CSharpGameEngine.SceneAnalysis.Json.SceneFiles.IsScene(Startup,Project));
        Assert.Contains("\"namespace\": \"My_Game.Scenes\"",File.ReadAllText(Startup));
    }

    [Theory]
    [InlineData("MyGame","MyGame")]
    [InlineData("My Game-2","My_Game_2")]
    [InlineData("3D Game","_3D_Game")]
    [InlineData("","Game")]
    public void SafeIdentifier_MatchesVisualStudiosSafeProjectName(string ProjectName,string Expected){
        Assert.Equal(Expected,GameProjectGenerator.SafeIdentifier(ProjectName));
    }

    /// <summary>A project named like a C# keyword gets a namespace the generated sources can declare (<c>namespace event.Gameplay;</c> does not parse).</summary>
    [Theory]
    [InlineData("event","_event")]
    [InlineData("fixed","_fixed")]
    [InlineData("record","record")]
    public void SafeIdentifier_PrefixesAReservedKeyword(string ProjectName,string Expected){
        Assert.Equal(Expected,GameProjectGenerator.SafeIdentifier(ProjectName));
    }

    [Fact]
    public void Generate_WithAKeywordProjectName_WritesSourcesThatParse(){
        var Project=Path.Combine(this.Root,"event");
        GameProjectGenerator.Generate(RepositoryFile("Templates","CSharpGameEngine.Templates.ThirdPerson"),Project,"event","1.2.3",this.Root);
        foreach(var File in Directory.EnumerateFiles(Project,"*.cs",SearchOption.AllDirectories)){
            var Tree=Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(System.IO.File.ReadAllText(File),
                new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview));
            Assert.True(!Tree.GetDiagnostics().Any(),File+": "+string.Join("; ",Tree.GetDiagnostics()));
        }
    }

    [Fact]
    public void CopySources_RenamesTheNamespaceAndSkipsBuildOutputAndTheProjectFile(){
        var Template=Path.Combine(this.Root,"template");
        Directory.CreateDirectory(Path.Combine(Template,"Scenes"));
        Directory.CreateDirectory(Path.Combine(Template,"bin","Debug"));
        Directory.CreateDirectory(Path.Combine(Template,"Assets"));
        File.WriteAllText(Path.Combine(Template,"Scenes","Level.cs"),"using CgeThirdPerson.Input;\nnamespace CgeThirdPerson.Scenes;");
        File.WriteAllText(Path.Combine(Template,"bin","Debug","stale.dll"),"x");
        File.WriteAllText(Path.Combine(Template,"Template.csproj"),"<Project />");
        File.WriteAllBytes(Path.Combine(Template,"Assets","Mesh.glb"),[1,2,3]);
        var Project=Path.Combine(this.Root,"MyGame");

        var Created=GameProjectGenerator.CopySources(Template,Project,"MyGame");

        Assert.Equal(2,Created.Count);
        Assert.Equal("using MyGame.Input;\nnamespace MyGame.Scenes;",File.ReadAllText(Path.Combine(Project,"Scenes","Level.cs")));
        Assert.Equal([1,2,3],File.ReadAllBytes(Path.Combine(Project,"Assets","Mesh.glb")));
        Assert.False(Directory.Exists(Path.Combine(Project,"bin")));
        Assert.False(File.Exists(Path.Combine(Project,"Template.csproj")));
    }

    [Theory]
    [InlineData("ThirdPerson")]
    [InlineData("FirstPerson")]
    [InlineData("TopDown")]
    [InlineData("Vehicle")]
    public void TemplateProjectFile_IsTheGeneratorsProjectFileWithTemplateTokens(string TemplateName){
        var Template=RepositoryFile("tools","CSharpGameEngine.VSExtension","ProjectTemplates","CSharp","GameEngine",TemplateName,TemplateName+".csproj.template");
        var Plugins=GameProjectGenerator.PluginsOf(RepositoryFile("Templates","CSharpGameEngine.Templates."+TemplateName));
        Assert.Equal(GameProjectGenerator.ProjectFile("$safeprojectname$",GameProjectGenerator.EngineVersionToken,Plugins),File.ReadAllText(Template));
    }

    [Theory]
    [InlineData("ThirdPerson","")]
    [InlineData("TopDown","Navigation")]
    [InlineData("Vehicle","Vehicles")]
    public void TheGeneratedProject_EnablesTheTemplatesEnginePlugins(string TemplateName,string Plugins){
        // ADR-0079: the template's own project file names its engine plugins; the package-mode project carries them over.
        var Project=Path.Combine(this.Root,TemplateName);
        var ProjectFile=GameProjectGenerator.Generate(RepositoryFile("Templates","CSharpGameEngine.Templates."+TemplateName),Project,"Game","1.2.3",@"C:\feed",TemplateName);
        var Text=File.ReadAllText(ProjectFile);

        Assert.Equal(Plugins.Length>0,Text.Contains("<CgePlugins>"+Plugins+"</CgePlugins>",StringComparison.Ordinal));
        Assert.Equal(Plugins.Length>0,Text.Contains("CgePlugins",StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_WritesAPackageModeProjectAndItsFeed(){
        var Template=RepositoryFile("Templates","CSharpGameEngine.Templates.ThirdPerson");
        var Project=Path.Combine(this.Root,"My Game");
        var ProjectFile=GameProjectGenerator.Generate(Template,Project,"My Game","1.2.3",@"C:\feeds\cge & co");

        Assert.Equal(Path.Combine(Project,"My Game.csproj"),ProjectFile);
        var Text=File.ReadAllText(ProjectFile);
        Assert.Contains("<PackageReference Include=\"CSharpGameEngine.Sdk\" Version=\"1.2.3\" />",Text);
        Assert.Contains("<RootNamespace>My_Game</RootNamespace>",Text);
        Assert.DoesNotContain("Game.props",Text);
        Assert.Contains("value=\"C:\\feeds\\cge &amp; co\"",File.ReadAllText(Path.Combine(Project,"nuget.config")));
        var Level=File.ReadAllText(Path.Combine(Project,GameProjectGenerator.StartupScenePath));
        Assert.Contains("\"namespace\": \"My_Game.Scenes\"",Level);
        Assert.DoesNotContain(GameProjectGenerator.TemplateNamespace,string.Concat(Directory.EnumerateFiles(Project,"*.cs",SearchOption.AllDirectories).Select(File.ReadAllText)));
        Assert.DoesNotContain(GameProjectGenerator.TemplateNamespace,string.Concat(Directory.EnumerateFiles(Project,"*.json",SearchOption.AllDirectories).Select(File.ReadAllText)));
        Assert.False(Directory.Exists(Path.Combine(Project,".cge")));
        Assert.True(File.Exists(Path.Combine(Project,"Assets","Characters","Mannequins","SKM_Quinn_Simple.glb")));
    }

    [Fact]
    public void Wizard_CreatesTheProjectFromTheBundledSourcesAndPackage(){
        var Extension=Path.Combine(this.Root,"extension");
        Directory.CreateDirectory(Path.Combine(Extension,GameProjectTemplateWizard.PackagesFolder));
        File.WriteAllText(Path.Combine(Extension,GameProjectTemplateWizard.PackagesFolder,"CSharpGameEngine.Sdk.9.8.7.nupkg"),"package");
        var Sources=Path.Combine(Extension,GameProjectTemplateWizard.TemplateSourceFolder,"ThirdPerson","Scenes");
        Directory.CreateDirectory(Sources);
        File.WriteAllText(Path.Combine(Sources,"Lvl_ThirdPerson.json"),"{ \"namespace\": \"CgeThirdPerson.Scenes\" }");
        var Project=Path.Combine(this.Root,"Created");
        var FeedRoot=Path.Combine(this.Root,"feeds");

        var Version=GameProjectTemplateWizard.CreateProject(Extension,"ThirdPerson",Project,"Created",FeedRoot);

        Assert.Equal("9.8.7",Version);
        Assert.True(File.Exists(Path.Combine(FeedRoot,"9.8.7","CSharpGameEngine.Sdk.9.8.7.nupkg")));
        Assert.Equal("{ \"namespace\": \"Created.Scenes\" }",File.ReadAllText(Path.Combine(Project,GameProjectGenerator.StartupScenePath)));
        Assert.Contains(Path.Combine(FeedRoot,"9.8.7"),File.ReadAllText(Path.Combine(Project,"nuget.config")));
        File.WriteAllText(Path.Combine(Extension,GameProjectTemplateWizard.PackagesFolder,"CSharpGameEngine.Sdk.9.9.0.nupkg"),"second");
        Assert.Throws<InvalidOperationException>(()=>GameProjectTemplateWizard.PrepareFeed(Extension,FeedRoot));
    }

    [Fact]
    public void VersionOfPackage_ReadsTheSdkPackageFileName(){
        Assert.Equal("0.1.0",GameProjectGenerator.VersionOfPackage(@"C:\x\CSharpGameEngine.Sdk.0.1.0.nupkg"));
        Assert.Equal("1.0.0-preview.2",GameProjectGenerator.VersionOfPackage("CSharpGameEngine.Sdk.1.0.0-preview.2.nupkg"));
        Assert.Throws<ArgumentException>(()=>GameProjectGenerator.VersionOfPackage("Other.1.0.0.nupkg"));
    }

    [Fact]
    public void PublishToFeed_CopiesOnce(){
        var Package=Path.Combine(this.Root,"CSharpGameEngine.Sdk.0.1.0.nupkg");
        File.WriteAllText(Package,"package");
        var Feed=Path.Combine(this.Root,"feed");
        GameProjectGenerator.PublishToFeed(Package,Feed);
        var Published=Path.Combine(Feed,"CSharpGameEngine.Sdk.0.1.0.nupkg");
        var WriteTime=File.GetLastWriteTimeUtc(Published);
        GameProjectGenerator.PublishToFeed(Package,Feed);
        Assert.Equal("package",File.ReadAllText(Published));
        Assert.Equal(WriteTime,File.GetLastWriteTimeUtc(Published));
    }

    [Fact]
    public void PublishToFeed_ReplacesARebuiltPackageOfTheSameLength(){
        // The package keeps its version while it is rebuilt (ADR-0065); equal length does not make it the same package.
        var Package=Path.Combine(this.Root,"CSharpGameEngine.Sdk.0.1.0.nupkg");
        File.WriteAllText(Package,"package");
        var Feed=Path.Combine(this.Root,"feed");
        GameProjectGenerator.PublishToFeed(Package,Feed);
        File.WriteAllText(Package,"pAckage");

        GameProjectGenerator.PublishToFeed(Package,Feed);

        Assert.Equal("pAckage",File.ReadAllText(Path.Combine(Feed,"CSharpGameEngine.Sdk.0.1.0.nupkg")));
    }
}
