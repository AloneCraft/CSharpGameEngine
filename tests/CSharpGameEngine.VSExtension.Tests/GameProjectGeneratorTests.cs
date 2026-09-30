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

    [Theory]
    [InlineData("MyGame","MyGame")]
    [InlineData("My Game-2","My_Game_2")]
    [InlineData("3D Game","_3D_Game")]
    [InlineData("","Game")]
    public void SafeIdentifier_MatchesVisualStudiosSafeProjectName(string ProjectName,string Expected){
        Assert.Equal(Expected,GameProjectGenerator.SafeIdentifier(ProjectName));
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

    [Fact]
    public void TemplateProjectFile_IsTheGeneratorsProjectFileWithTemplateTokens(){
        var Template=RepositoryFile("tools","CSharpGameEngine.VSExtension","ProjectTemplates","CSharp","GameEngine","ThirdPerson","ThirdPerson.csproj.template");
        Assert.Equal(GameProjectGenerator.ProjectFile("$safeprojectname$",GameProjectGenerator.EngineVersionToken),File.ReadAllText(Template));
    }

    [Fact]
    public void Generate_WritesAPackageModeProjectAndItsFeed(){
        var Template=RepositoryFile("templates","CSharpGameEngine.Templates.ThirdPerson");
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
}
