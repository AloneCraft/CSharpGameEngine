using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// ADR-0070: a <c>*.scene.json</c> additional document of a Roslyn project is found by its class name
/// and read into the designer's model from its live text, with the JSON path as the model's file.
/// </summary>
public sealed class SceneJsonWorkspaceSourceTests {
    private const string JsonPath=@"C:\Game\Scenes\MainScene.scene.json";
    private const string Json="""
        {
          "schemaVersion": 1,
          "namespace": "Game",
          "className": "MainScene",
          "root": {
            "id": "0f3c6c7a-0000-4000-8000-000000000001",
            "components": [],
            "children": [
              { "id": "0f3c6c7a-0000-4000-8000-000000000002", "name": "Cube", "components": [], "children": [] }
            ]
          }
        }
        """;
    [Theory]
    [InlineData(@"C:\Game\Player.cs",false)]
    [InlineData(JsonPath,true)]
    [InlineData(@"C:\Game\appsettings.json",false)]
    [InlineData(null,false)]
    public void SceneDocuments_AreSceneJsonOnly(string?Path,bool Expected){
        Assert.Equal(Expected,SceneJsonCommitPlanner.IsJsonScene(Path));
    }
    [Theory]
    [InlineData("""{ "namespace": "Game", "className": "MainScene" }""","Game.MainScene")]
    [InlineData("""{ "className": "MainScene" }""","MainScene")]
    [InlineData("""{ "namespace": "Game" }""",null)]
    [InlineData("""{ "className": """,null)]
    public void ClassName_IsReadFromTheDocumentHeader(string Text,string?Expected){
        Assert.Equal(Expected,SceneJsonWorkspaceSource.TryReadClassFullyQualifiedName(Text));
    }
    [Fact]
    public async System.Threading.Tasks.Task JsonScene_IsFoundAndReadFromItsLiveText(){
        using var Workspace=new AdhocWorkspace();
        var Project=AddProject(Workspace,Json);
        Assert.Null(await SceneJsonWorkspaceSource.FindAsync(Project,"Game.Other",CancellationToken.None));
        var Document=await SceneJsonWorkspaceSource.FindAsync(Project,"Game.MainScene",CancellationToken.None);
        Assert.NotNull(Document);
        // SceneWorkspaceService.FindDocument resolves an open JSON editor to this document by path.
        Assert.Contains(Document!.Id,Project.Solution.GetDocumentIdsWithFilePath(JsonPath));
        var Model=await AnalyzeAsync(Project,Document!);
        Assert.Equal(JsonPath,Model.FilePath);
        Assert.Equal("Cube",Assert.Single(Model.Root!.Children).Name);
        var Edited=Project.Solution.WithAdditionalDocumentText(Document!.Id,SourceText.From(Json.Replace("\"Cube\"","\"Crate\""))).GetProject(Project.Id)!;
        var EditedModel=await AnalyzeAsync(Edited,Edited.GetAdditionalDocument(Document.Id)!);
        Assert.Equal("Crate",Assert.Single(EditedModel.Root!.Children).Name);
    }
    [Fact]
    public async System.Threading.Tasks.Task InvalidJsonScene_HasNoDesignerTree(){
        using var Workspace=new AdhocWorkspace();
        var Project=AddProject(Workspace,Json.Replace("\"components\": [],","\"components\": [ { \"type\": \"Game.Missing\" } ],"));
        var Document=Assert.Single(SceneJsonWorkspaceSource.JsonScenes(Project));
        var Compilation=(await Project.GetCompilationAsync())!;
        Assert.Null(await SceneJsonWorkspaceSource.AddDesignerTreeAsync(Project,Compilation,Document,CancellationToken.None));
    }
    private static async System.Threading.Tasks.Task<SceneModel>AnalyzeAsync(Project Project,TextDocument Document){
        var Compilation=(await Project.GetCompilationAsync())!;
        var Added=await SceneJsonWorkspaceSource.AddDesignerTreeAsync(Project,Compilation,Document,CancellationToken.None);
        Assert.NotNull(Added);
        var Type=Added!.Value.Compilation.GetTypeByMetadataName("Game.MainScene")!;
        return SceneDeclarationAnalyzer.Analyze(Type,Added.Value.Compilation,CancellationToken.None);
    }
    private static Project AddProject(AdhocWorkspace Workspace,string SceneJson){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation();
        var ProjectId=Microsoft.CodeAnalysis.ProjectId.CreateNewId();
        var Documents=Compilation.SyntaxTrees.Select((Tree,Index)=>DocumentInfo.Create(
            DocumentId.CreateNewId(ProjectId),$"Source{Index}.cs",
            loader:TextLoader.From(TextAndVersion.Create(Tree.GetText(),VersionStamp.Create()))));
        var Additional=new[]{
            DocumentInfo.Create(DocumentId.CreateNewId(ProjectId),"MainScene.scene.json",filePath:JsonPath,
                loader:TextLoader.From(TextAndVersion.Create(SourceText.From(SceneJson),VersionStamp.Create()))),
            DocumentInfo.Create(DocumentId.CreateNewId(ProjectId),"appsettings.json",filePath:@"C:\Game\appsettings.json",
                loader:TextLoader.From(TextAndVersion.Create(SourceText.From("{}"),VersionStamp.Create()))),
        };
        return Workspace.AddProject(ProjectInfo.Create(
            ProjectId,VersionStamp.Create(),"Game","Game",LanguageNames.CSharp,
            compilationOptions:Compilation.Options,parseOptions:Compilation.SyntaxTrees.First().Options,
            documents:Documents,metadataReferences:Compilation.References,additionalDocuments:Additional));
    }
}
