using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetWorkspaceTests {
    [Theory]
    [InlineData("{\"meshes\":[{}]}",true)]
    [InlineData("{\"animations\":[{}]}",false)]
    [InlineData("{\"meshes\":[{}],\"skins\":[{}]}",false)]
    public void StaticMeshSelectionFiltersByModelContents(string Json,bool Expected){
        var Source=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".gltf");
        try {
            File.WriteAllText(Source,Json);
            Assert.Equal(Expected,AssetPlacement.HasStaticMesh(Source));
        }finally{
            File.Delete(Source);
        }
    }
    [Fact]
    public void ModelAssignmentAcceptsOnlyAvailableModelsFromTheSceneProject(){
        var Model=new AssetWorkspaceItem{Kind=AssetWorkspaceKind.Model,State=AssetWorkspaceState.Source,Source=new(){ProjectPath=@"C:\Game\Game.csproj",RelativePath="Assets/Stage/Wall.glb"}};
        Assert.True(AssetPlacement.CanAssignModel(Model,@"c:\game\game.csproj"));
        Assert.False(AssetPlacement.CanAssignModel(Model,@"C:\Other\Other.csproj"));
        Model.Kind=AssetWorkspaceKind.Audio;Assert.False(AssetPlacement.CanAssignModel(Model,@"C:\Game\Game.csproj"));
        Model.Kind=AssetWorkspaceKind.Model;Model.State=AssetWorkspaceState.MissingSource;Assert.False(AssetPlacement.CanAssignModel(Model,@"C:\Game\Game.csproj"));
        Model.State=AssetWorkspaceState.InvalidMetadata;Assert.False(AssetPlacement.CanAssignModel(Model,@"C:\Game\Game.csproj"));
        Model.State=AssetWorkspaceState.Ready;Model.Source.RelativePath="Outside/Wall.glb";Assert.False(AssetPlacement.CanAssignModel(Model,@"C:\Game\Game.csproj"));
    }
    [Fact]
    public void InlineRenameIsTransientAndKeepsTheFolderAndExtension(){
        var Source=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"RenameAssets","Ship.glb"));
        var Row=new AssetWorkspaceRow(new(){Source=new(){SourcePath=Source}});var Changes=new List<string?>();
        Row.PropertyChanged+=(_,Args)=>Changes.Add(Args.PropertyName);
        Assert.True(Row.BeginRename());Assert.True(Row.IsRenaming);Assert.Equal("Ship.glb",Row.RenameText);
        Row.RenameText="New Ship.glb";Assert.Equal(Path.Combine(Path.GetDirectoryName(Source)!,"New Ship.glb"),Row.RenameDestination());
        Assert.Equal(Source,Row.Item.Source.SourcePath);Assert.Equal("Ship.glb",Row.Name);
        Row.EndRename();Assert.False(Row.IsRenaming);Assert.True(Row.BeginRename());Assert.Equal("Ship.glb",Row.RenameText);
        Assert.Equal(3,Changes.Count(Name=>Name==nameof(Row.IsRenaming)));
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../Other.glb")]
    [InlineData("Other\\Ship.glb")]
    [InlineData("C:\\Other.glb")]
    [InlineData("Ship.glb.")]
    [InlineData("Ship.glb ")]
    public void InlineRenameRejectsEmptyNamesAndPathsWithoutChangingTheSource(string Name){
        var Row=new AssetWorkspaceRow(new(){Source=new(){SourcePath=Path.GetFullPath("Assets/Ship.glb")}});
        Assert.True(Row.BeginRename());Row.RenameText=Name;
        Assert.Throws<ArgumentException>(()=>Row.RenameDestination());Assert.True(Row.IsRenaming);Assert.Equal("Ship.glb",Row.Name);
    }
    [Theory]
    [InlineData(true,AssetWorkspaceState.ReadOnly)]
    [InlineData(false,AssetWorkspaceState.ReadOnly)]
    [InlineData(false,AssetWorkspaceState.MissingSource)]
    public void InlineRenameCannotEditUnavailableOrProviderSources(bool ReadOnly,AssetWorkspaceState State){
        var Row=new AssetWorkspaceRow(new(){State=State,Source=new(){ReadOnly=ReadOnly,SourcePath="Ship.glb"}});
        Assert.False(Row.BeginRename());Assert.False(Row.IsRenaming);
    }
    [Fact]
    public void InlineRenameCannotRenameAChildByChangingItsSourceFile(){
        var Key=new AssetKey(AssetId.Parse(Guid.NewGuid().ToString("D")),SubAssetId.FromCanonicalBytes([1,2,3]));
        var Row=new AssetWorkspaceRow(new(){Source=new(){SourcePath="Ship.glb"}},Key);
        Assert.False(Row.BeginRename());Assert.False(Row.IsRenaming);
    }
    [Fact]
    public void ImageOutputRefactoringRejectsOtherLoadedProjectOwnersAndReadOnlySources(){
        var Item=new AssetWorkspaceItem{Source=new(){ProjectPath="A",SourcePath="C:/Images/Wall.png"}};
        ImageReferenceMigration.ValidateOwnership(Item,[Item]);
        var Other=new AssetWorkspaceItem{Source=new(){ProjectPath="B",SourcePath="C:/Images/Wall.png"}};
        Assert.Throws<InvalidOperationException>(()=>ImageReferenceMigration.ValidateOwnership(Item,[Item,Other]));
        Item.Source.ReadOnly=true;Assert.Throws<InvalidOperationException>(()=>ImageReferenceMigration.ValidateOwnership(Item,[Item]));
    }
    [Fact]
    public void SemanticImageDefaultPropertyAndOutputFieldsAreReplacedWithoutEditingLabels(){
        const string Declarations="internal static class Assets { internal static class Wall { public static readonly string Color=\"color\"; public static readonly string Other=\"other\"; public static string Default=>Color; } }";
        const string Source="internal class User { public string A=>Assets.Wall.Default; public string B=>Assets.Wall.Color; public string Label=>\"Assets.Wall.Color\"; }";
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(Declarations,Source);var Tree=Compilation.SyntaxTrees.Single(T=>T.GetText().ToString()==Source);
        var Renames=new Dictionary<string,string>{{"Assets.Wall.Default","Assets.Wall.Other"},{"Assets.Wall.Color","Assets.Wall.Other"}};
        var After=AssetReferencePlanner.RewriteCSharp(Tree.GetText(),Tree.GetRoot(),Compilation.GetSemanticModel(Tree),Renames,out var Found);
        Assert.True(Found);Assert.Equal(2,After.Split(["global::Assets.Wall.Other"],StringSplitOptions.None).Length-1);Assert.Contains("\"Assets.Wall.Color\"",After);
    }
    [Fact]
    public void UnregisteredSourcesHaveNoPersistentKeyAndProviderAssetsCannotBeEdited(){
        var Item=new AssetWorkspaceItem{State=AssetWorkspaceState.Source};Assert.Null(Item.GetKey());
        var Identity=AssetId.Parse(Guid.NewGuid().ToString("D"));Item.AssetId=Identity.ToString();
        Assert.Equal(new AssetKey(Identity,null),Item.GetKey());
        Item.Source.ReadOnly=true;
        Assert.Equal(AssetWorkspaceCapabilities.None,Item.GetCapabilities()&(AssetWorkspaceCapabilities.Edit|AssetWorkspaceCapabilities.Organize));
        Assert.True((Item.GetCapabilities()&AssetWorkspaceCapabilities.Duplicate)!=0);
    }
    [Theory]
    [InlineData("Scenes/Main.json",AssetWorkspaceKind.Scene)]
    [InlineData("Assets/Ship.glb",AssetWorkspaceKind.Model)]
    [InlineData("Assets/A.cgeasset",AssetWorkspaceKind.Data)]
    [InlineData("Assets/A.cgeshadergraph",AssetWorkspaceKind.ShaderGraph)]
    public void SharedClassificationRecognizesSources(string Path,AssetWorkspaceKind Expected)=>Assert.Equal(Expected,AssetWorkspaceClassification.Classify(Path));
    [Theory]
    [InlineData("Scenes/scene.schema.json")]
    [InlineData("Gameplay/Player.cs")]
    [InlineData("Assets/Ship.glb.meta")]
    public void NonAssetsAreNotAddedToTheAssetFilter(string Path)=>Assert.Null(AssetWorkspaceClassification.Classify(Path));
    [Fact]
    public void QueryHasFolderBoundaryAndProjectIdentityAndSupportsCancellation(){
        var Root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"cge-query"));
        AssetWorkspaceItem Item(string Project,string Folder,string Name)=>new(){Source=new(){ProjectPath=Project,SourcePath=Path.Combine(Root,Folder,Name),RelativePath=Folder+"/"+Name},Kind=AssetWorkspaceKind.Texture};
        var Items=new[]{Item("A","Assets","Image.png"),Item("B","Assets","Image.png"),Item("A","AssetsOther","Image.png")};
        var Query=new AssetWorkspaceQuery{ProjectPath="A",Folder=Path.Combine(Root,"Assets"),Search="Image",Kind="Texture"};
        Assert.Same(Items[0],Assert.Single(Query.Apply(Items,CancellationToken.None)));
        using var Cancellation=new CancellationTokenSource();Cancellation.Cancel();Assert.Throws<OperationCanceledException>(()=>Query.Apply(Items,Cancellation.Token));
    }
    [Fact]
    public void LinkedProviderAssetsUseTheProjectsLogicalFolderScope(){
        var Root=Path.Combine(Path.GetTempPath(),"CgeLogicalScope");
        var Item=new AssetWorkspaceItem{Source=new(){ProjectPath="Game",ProjectRoot=Root,RelativePath="Fonts/Roboto.cgefont",SourcePath=Path.Combine(Path.GetTempPath(),"SdkFonts","Roboto.cgefont"),ReadOnly=true}};
        Assert.Same(Item,Assert.Single(new AssetWorkspaceQuery{ProjectPath="Game",Folder=Path.Combine(Root,"Fonts")}.Apply([Item],CancellationToken.None)));
        Assert.Empty(new AssetWorkspaceQuery{ProjectPath="Game",Folder=Path.Combine(Root,"Font")}.Apply([Item],CancellationToken.None));
    }
    [Fact]
    public void CatalogNamingIsSharedWithGeneratorAndReservesFolderNames(){
        var Map=AssetCatalogLayout.Map(["Assets/Box.glb","Assets/Box.fbx","Assets/Box/Part.glb","Assets/3d box.glb","Plugins/Weather/Assets/Rain.wav"]);
        Assert.Equal("Assets.Box_fbx",Map["Assets/Box.fbx"]);Assert.Equal("Assets.Box_glb",Map["Assets/Box.glb"]);
        Assert.Equal("Assets.Box.Part",Map["Assets/Box/Part.glb"]);Assert.Equal("Assets._3d_box",Map["Assets/3d box.glb"]);
        Assert.Equal("Assets.Plugins.Weather.Rain",Map["Plugins/Weather/Assets/Rain.wav"]);
    }
    [Fact]
    public void JsonReferencesAreParsedAndDuplicateDoesNotRedirectExistingScenes(){
        const string Json="""{"root":{"components":[{"members":{"AssetPath":"Assets/Ship.glb"}}]}}""";
        var Root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"cge-ref"));
        var Renamed=AssetReferencePlanner.RewriteJson(Json,"Assets/Ship.glb","Assets/New.glb",new Dictionary<string,string>(),AssetWorkspaceKind.Scene,Path.Combine(Root,"Scenes","Main.json"),Path.Combine(Root,"Assets","Ship.glb"),Path.Combine(Root,"Assets","New.glb"),AssetFileOperation.Rename,out var Found);
        Assert.True(Found);Assert.Contains("Assets/New.glb",Renamed);
        var Duplicated=AssetReferencePlanner.RewriteJson(Json,"Assets/Ship.glb","Assets/Copy.glb",new Dictionary<string,string>(),AssetWorkspaceKind.Scene,Path.Combine(Root,"Scenes","Main.json"),Path.Combine(Root,"Assets","Ship.glb"),Path.Combine(Root,"Assets","Copy.glb"),AssetFileOperation.Duplicate,out Found);
        Assert.False(Found);Assert.Equal(Json,Duplicated);
    }
    [Fact]
    public void UnknownStringCandidateFailsClosedInsteadOfReplacingArbitraryText(){
        const string Json="""{"members":{"Description":"Assets/Ship.glb"}}""";
        Assert.Throws<InvalidOperationException>(()=>AssetReferencePlanner.RewriteJson(Json,"Assets/Ship.glb","Assets/New.glb",new Dictionary<string,string>(),AssetWorkspaceKind.Data,"Data.cgeasset","Ship.glb","New.glb",AssetFileOperation.Move,out _));
    }
    [Fact]
    public void MovingAGraphRebasesDependenciesWithoutChangingTheirIdentity(){
        var Graph=new CSharpGameEngine.ShaderGraph.ShaderGraphDocument();Graph.Dependencies.Add(new(){Id=Graph.Id,Path="Parts/Shared.cgesubgraph"});
        var Root=Path.Combine(Path.GetTempPath(),"CgeGraphMove");
        var Before=CSharpGameEngine.ShaderGraph.ShaderGraphCanonicalization.ToCanonicalJson(Graph);
        var After=AssetReferencePlanner.RelocateGraph(Before,Path.Combine(Root,"Old.cgeshadergraph"),Path.Combine(Root,"Variants","New.cgeshadergraph"));
        var Moved=CSharpGameEngine.ShaderGraph.ShaderGraphCanonicalization.FromJson(After)!;
        var Dependency=Assert.Single(Moved.Dependencies);Assert.Equal("../Parts/Shared.cgesubgraph",Dependency.Path);Assert.Equal(Graph.Id,Dependency.Id);Assert.Equal(Graph.Id,Moved.Id);
    }
    [Fact]
    public void SemanticReferenceRewriteHandlesAliasesStaticImportsAndRemovedContainers(){
        const string Declarations="internal static class Assets { internal static class Models { public static string Ship=\"mesh\"; } }";
        const string Source="""
            using Models=Assets.Models;
            using static Assets.Models;
            internal class User { public object A=>Models.Ship; public object B=>Ship; public System.Type T=>typeof(Models); public string Label=>"Assets.Models.Ship"; }
            """;
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(Declarations,Source);
        var Tree=Compilation.SyntaxTrees.Single(Tree=>Tree.GetText().ToString()==Source);
        var After=AssetReferencePlanner.RewriteCSharp(Tree.GetText(),Tree.GetRoot(),Compilation.GetSemanticModel(Tree),
            new Dictionary<string,string>{{"Assets.Models.Ship","Assets.Props.NewShip"},{"Assets.Models","Assets.Props"}},out var Found);
        Assert.True(Found);Assert.Contains("using Models=global::Assets.Props",After);Assert.Contains("using static global::Assets.Props",After);
        Assert.Contains("=>global::Assets.Props.NewShip",After);Assert.Contains("typeof(global::Assets.Props)",After);Assert.Contains("\"Assets.Models.Ship\"",After);
        var Updated=SceneAnalysisTestHelper.CreateCompilation("internal static class Assets { internal static class Props { public static string NewShip=\"mesh\"; } }",After);
        Assert.DoesNotContain(Updated.GetDiagnostics(),static Diagnostic=>Diagnostic.Severity==Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }
    [Fact]
    public void DataEditingUsesExistingTypeValidationAndPreservesOtherText(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation("""
            namespace CSharpGameEngine.Authoring { [System.AttributeUsage(System.AttributeTargets.Struct)] public sealed class DataAssetAttribute:System.Attribute {} }
            namespace Game { [CSharpGameEngine.Authoring.DataAsset] public struct Setup { public float Mass; public bool Enabled; public float[] Values; public Nested Child; } public struct Nested { public int Count; } }
            """);
        const string Json="""{"schemaVersion":3,"type":"Game.Setup","members":{"Mass":2,"Enabled":true,"Values":[1,2],"Child":{"Count":4}}}   """;
        var Rows=AssetDataEditing.Rows(Json,Compilation);var Mass=Assert.Single(Rows,Row=>Row.Name=="Mass");
        var After=AssetDataEditing.Edit(Json,Mass,"3",Compilation);Assert.Equal(Json.Replace("\"Mass\":2","\"Mass\":3"),After);
        Assert.Contains(Rows,Row=>Row.Name=="Values[1]");Assert.Contains(Rows,Row=>Row.Name=="Child.Count");
        Assert.Throws<InvalidOperationException>(()=>AssetDataEditing.Edit(Json,Mass,"\"not a number\"",Compilation));
        Assert.Throws<InvalidOperationException>(()=>AssetDataEditing.Edit(After,Mass,"4",Compilation));
    }
}
