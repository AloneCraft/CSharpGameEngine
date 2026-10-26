using System.IO;
using System.Linq;
using CSharpGameEngine.SceneAnalysis.Json;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// ADR-0070：「Add New Item > Scene」は、追加先のフォルダーに合ったスキーマ参照を持ち、シーン全体を構成するトークンを含む Scene JSON ドキュメントを作成する。
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
    /// M0095：プロジェクトの Scenes フォルダー内にある JSON だけがシーンである (ADR-0070)。そのため別の場所に追加した項目はビルドされず、Scene Editor でも開かれない。ウィザードは黙って失敗せず、そのことを通知する。
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
        // エンジンの型だけを使う。ゲーム側の型（ゲームの PlayerStart など）が含まれると、それのないゲームのビルドが失敗する。
        var Types=new[]{Root}.Concat(Root.Get("children")!.Items).SelectMany(static Node=>Node.Get("components")!.Items).Select(static Component=>Component.Get("type")!.Text!);
        Assert.All(Types,static Type=>Assert.StartsWith("CSharpGameEngine.",Type));
    }
    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(System.AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
