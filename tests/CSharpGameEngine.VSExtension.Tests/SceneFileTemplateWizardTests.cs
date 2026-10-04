using System.IO;
using System.Linq;
using CSharpGameEngine.SceneAnalysis.Json;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// ADR-0070: "Add New Item > Scene" creates a scene JSON document whose schema reference fits
/// the folder it was added to and whose tokens make a complete scene.
/// </summary>
public sealed class SceneFileTemplateWizardTests {
    private const string Project=@"C:\Games\My Game\My Game.csproj";
    [Theory]
    [InlineData(@"C:\Games\My Game\Level.json",".cge/scene.schema.json")]
    [InlineData(@"C:\Games\My Game\Scenes\Level.json","../.cge/scene.schema.json")]
    [InlineData(@"C:\Games\My Game\Scenes\Stages\Level.json","../../.cge/scene.schema.json")]
    public void SchemaReference_IsRelativeToTheItemFolder(string ItemPath,string Expected){
        Assert.Equal(Expected,SceneFileTemplateWizard.SchemaReference(ItemPath,Project));
    }
    /// <summary>
    /// M0095: only JSON under the project's Scenes folder is a scene (ADR-0070), so an item added elsewhere
    /// is neither built nor opened in the Scene Editor; the wizard says so instead of failing silently.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Games\My Game\Scenes\Level.json",false)]
    [InlineData(@"C:\Games\My Game\Scenes\Stages\Level.json",false)]
    [InlineData(@"C:\Games\My Game\Level.json",true)]
    [InlineData(@"C:\Games\My Game\Assets\Level.json",true)]
    public void IsOutsideScenes_WarnsOnlyForItemsTheBuildDoesNotTreatAsScenes(string ItemPath,bool Expected){
        Assert.Equal(Expected,SceneFileTemplateWizard.IsOutsideScenes(ItemPath,Project));
    }
    [Fact]
    public void Template_WithItsTokensReplaced_IsACompleteSceneDocument(){
        var Template=File.ReadAllText(Path.Combine(RepositoryRoot(),"tools","CSharpGameEngine.VSExtension","ItemTemplates","CSharp","GameEngine","SceneFile","Scene.json"));
        var Text=SceneFileTemplateWizard.Complete(Template,@"C:\Games\My Game\Scenes\Level.json",Project)
            .Replace("$rootnamespace$","My_Game.Scenes").Replace("$safeitemname$","Level")
            .Replace("$guid1$","7b1c2f4e-0d3a-4c7e-9f1b-2a6d8e5c3b10").Replace("$guid2$","9e4d1a6b-3c2f-4b8a-8d7e-1f0c5a2b6e93");
        Assert.DoesNotContain("$",Text.Replace("\"$schema\"",string.Empty));
        var Document=SceneJsonReader.Parse(Text);
        Assert.Equal("../.cge/scene.schema.json",Document.Get("$schema")!.Text);
        Assert.Equal("My_Game.Scenes",Document.Get("namespace")!.Text);
        Assert.Equal("Level",Document.Get("className")!.Text);
        var Root=Document.Get("root")!;
        var Ids=new[]{Root}.Concat(Root.Get("children")!.Items).Select(static Node=>Node.Get("id")!.Text).ToArray();
        Assert.All(Ids,static Id=>Assert.True(System.Guid.TryParse(Id,out _)));
        // Only engine types: a game type (such as a game's PlayerStart) would fail the build of games without it.
        var Types=new[]{Root}.Concat(Root.Get("children")!.Items).SelectMany(static Node=>Node.Get("components")!.Items).Select(static Component=>Component.Get("type")!.Text!);
        Assert.All(Types,static Type=>Assert.StartsWith("CSharpGameEngine.",Type));
    }
    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(System.AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
