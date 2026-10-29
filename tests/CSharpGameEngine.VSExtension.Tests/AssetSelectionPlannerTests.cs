using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetSelectionPlannerTests {
    private const string Project=@"C:\Game\Game.csproj";
    private const string Root=@"C:\Game";
    private static readonly (string,string)[]Projects=[(Project,Root)];
    private static AssetWorkspaceItem Item(string RelativePath)=>new(){
        Kind=AssetWorkspaceKind.Model,State=AssetWorkspaceState.Source,
        Source=new AssetWorkspaceSource { ProjectPath=Project,ProjectRoot=Root,SourcePath=Path.Combine(Root,RelativePath.Replace('/','\\')),RelativePath=RelativePath },
    };
    private static bool Dir(string PathValue)=>PathValue is @"C:\Game\Assets" or @"C:\Game\Assets\Models" or Root;
    [Fact]
    public void ProjectFileSelectionShowsItsWholeProjectEvenWithoutAssets(){
        var Plan=AssetSelectionPlanner.Decide([],Projects,Project,Dir);
        Assert.Equal("Project",Plan.Kind.ToString());
        Assert.Equal(Project,Plan.ProjectPath);
        Assert.Equal(Root,Plan.Folder);
        Assert.Null(Plan.Item);
    }
    [Fact]
    public void ProjectFileSelectionIsCaseInsensitiveAndDoesNotSelectANeighbour(){
        var Plan=AssetSelectionPlanner.Decide([Item("Assets/Models/Hero.glb")],Projects,Project.ToUpperInvariant(),Dir);
        Assert.Equal("Project",Plan.Kind.ToString());
        Assert.Equal(Project,Plan.ProjectPath);
        Assert.Equal(AssetSelectionPlanner.Kind.Other,AssetSelectionPlanner.Decide([],Projects,Project+".backup",Dir).Kind);
    }
    [Fact]
    public void AnAssetIsSelectedAndItsFolderBecomesTheLocation(){
        var Hero=Item("Assets/Models/Hero.glb");
        var Plan=AssetSelectionPlanner.Decide([Hero],Projects,Hero.Source.SourcePath,Dir);
        Assert.Equal(AssetSelectionPlanner.Kind.Asset,Plan.Kind);
        Assert.Same(Hero,Plan.Item);
        Assert.Equal(Project,Plan.ProjectPath);
        Assert.Equal(@"C:\Game\Assets\Models",Plan.Folder);
    }
    [Fact]
    public void AFolderMovesTheLocationButSelectsNothing(){
        var Plan=AssetSelectionPlanner.Decide([Item("Assets/Models/Hero.glb")],Projects,@"C:\Game\Assets",Dir);
        Assert.Equal(AssetSelectionPlanner.Kind.Folder,Plan.Kind);
        Assert.Null(Plan.Item);
        Assert.Equal(@"C:\Game\Assets",Plan.Folder);
        Assert.Equal(Project,Plan.ProjectPath);
    }
    [Fact]
    public void AFolderWithoutOnDiskAccessIsStillRecognisedFromTheAssetsInsideIt(){
        var Plan=AssetSelectionPlanner.Decide([Item("Assets/Models/Hero.glb")],Projects,@"C:\Game\Assets\Models",static _=>false);
        Assert.Equal(AssetSelectionPlanner.Kind.Folder,Plan.Kind);
    }
    [Fact]
    public void AFileThatIsNotAnAssetChangesNothingAndSelectsNothing(){
        var Plan=AssetSelectionPlanner.Decide([Item("Assets/Models/Hero.glb")],Projects,@"C:\Game\Gameplay\Player.cs",Dir);
        Assert.Equal(AssetSelectionPlanner.Kind.Other,Plan.Kind);
        Assert.Null(Plan.Item);
    }
    [Fact]
    public void MoveToReportsAChangeOnlyWhenTheLocationDiffers(){
        var Query=new AssetWorkspaceQuery { ProjectPath=Project,Folder=@"C:\Game\Assets" };
        Assert.False(Query.MoveTo(Project,@"C:\Game\Assets"));
        Assert.True(Query.MoveTo(Project,@"C:\Game\Assets\Models"));
        Assert.Equal(@"C:\Game\Assets\Models",Query.Folder);
        Assert.True(Query.MoveTo(@"C:\Other\Other.csproj",@"C:\Game\Assets\Models"));
        Assert.False(Query.MoveTo(@"C:\Other\Other.csproj",@"C:\Game\Assets\Models"));
    }
    [Fact]
    public void TheViewKeyChangesWithEveryConditionThatChangesTheList(){
        var Query=new AssetWorkspaceQuery { ProjectPath=Project,Folder=Root };
        var Base=Query.ViewKey;
        Assert.Equal(Base,Query.ViewKey);
        Query.Search="x";Assert.NotEqual(Base,Query.ViewKey);Query.Search=string.Empty;
        Query.Kind="Model";Assert.NotEqual(Base,Query.ViewKey);Query.Kind=string.Empty;
        Query.Provider="Engine";Assert.NotEqual(Base,Query.ViewKey);Query.Provider=string.Empty;
        Query.State="Source";Assert.NotEqual(Base,Query.ViewKey);Query.State=string.Empty;
        Query.WholeProject=true;Assert.NotEqual(Base,Query.ViewKey);Query.WholeProject=false;
        Query.Details=true;Assert.NotEqual(Base,Query.ViewKey);Query.Details=false;
        Query.Folder=@"C:\Game\Assets";Assert.NotEqual(Base,Query.ViewKey);Query.Folder=Root;
        Assert.Equal(Base,Query.ViewKey);
    }
    [Fact]
    public void RootScopesIncludeLinkedAndExternalProjectAssetsAndKeepSearchFilters(){
        var Hero=Item("Assets/Models/Hero.glb");
        var Linked=Item("Assets/Models/Linked.glb");Linked.Source.SourcePath=@"D:\Sdk\Linked.glb";Linked.Source.RelativePath=Linked.Source.SourcePath;
        var Other=Item("Assets/Hero.glb");Other.Source.ProjectPath=@"D:\Other\Other.csproj";Other.Source.ProjectRoot=@"D:\Other";Other.Source.SourcePath=@"D:\Other\Assets\Hero.glb";
        var Items=new[]{Hero,Linked,Other};
        var Query=new AssetWorkspaceQuery{ProjectPath=Project,Folder=@"C:\Game\Assets\Missing"};
        Assert.Empty(Query.Apply(Items,default));
        Assert.True(Query.Follow(AssetSelectionPlanner.Decide(Items,Projects,Project,Dir)));
        Assert.Equal(new[]{Hero,Linked},Query.Apply(Items,default));
        Query.Search="Hero";Query.Kind="Model";Query.State="Source";
        Assert.True(Query.Follow(AssetSelectionPlanner.Decide(Items,Projects,string.Empty,Dir,true)));
        Assert.Equal(2,Query.Apply(Items,default).Length);
        Assert.False(Query.CanChooseDestination);
        Assert.Equal("Hero",Query.Search);Assert.Equal("Model",Query.Kind);Assert.Equal("Source",Query.State);
        Assert.True(Query.Follow(AssetSelectionPlanner.Decide(Items,Projects,@"C:\Game\Assets\Models",Dir)));
        Assert.Same(Hero,Assert.Single(Query.Apply(Items,default)));
        Assert.True(Query.CanChooseDestination);
    }
    [Fact]
    public void ScopeChangesInvalidateTheViewEvenAtTheSameProjectRootAndLockPreventsFollowing(){
        var Query=new AssetWorkspaceQuery{ProjectPath=Project,Folder=Root};
        var Before=Query.ViewKey;
        var Plan=AssetSelectionPlanner.Decide([],Projects,Project,Dir);
        Query.Locked=true;Assert.False(Query.Follow(Plan));Assert.Equal(Before,Query.ViewKey);
        Query.Locked=false;Assert.True(Query.Follow(Plan));Assert.NotEqual(Before,Query.ViewKey);
        Assert.False(Query.Follow(Plan));
        Query.Reveal(Item("Assets/Hero.glb"));Assert.Equal(AssetWorkspaceScope.Folder,Query.Scope);
    }
    [Fact]
    public void SolutionScopeSurvivesRefreshIncludingAnEmptyInventoryAndProjectScopeFallsBackWhenUnloaded(){
        var Query=new AssetWorkspaceQuery{Scope=AssetWorkspaceScope.Solution,ProjectPath=Project,Folder=Root};
        Query.ReconcileProjects(Projects);Assert.Equal(AssetWorkspaceScope.Solution,Query.Scope);Assert.Empty(Query.ProjectPath);Assert.Empty(Query.Folder);
        Query.ReconcileProjects([]);Assert.Equal(AssetWorkspaceScope.Solution,Query.Scope);Assert.Empty(Query.Apply([],default));
        Query.MoveTo(Project,Root,AssetWorkspaceScope.Project);Query.ReconcileProjects(Projects);Assert.Equal(AssetWorkspaceScope.Project,Query.Scope);
        Query.ReconcileProjects([(@"D:\Other\Other.csproj",@"D:\Other")]);Assert.Equal(@"D:\Other\Other.csproj",Query.ProjectPath);Assert.Equal(AssetWorkspaceScope.Folder,Query.Scope);
        Query.ReconcileProjects([]);Assert.False(Query.CanChooseDestination);
    }
    [Theory]
    [InlineData("Folder")]
    [InlineData("Project")]
    [InlineData("Solution")]
    public void FavoritesRoundTripScopeAndFilters(string Scope){
        var Query=new AssetWorkspaceQuery{Scope=(AssetWorkspaceScope)Enum.Parse(typeof(AssetWorkspaceScope),Scope),ProjectPath=Project,Folder=Root,Search="Hero",Kind="Model",Provider="Engine",State="Source",WholeProject=true};
        var Json=System.Text.Json.JsonSerializer.Serialize(AssetWorkspaceFavorite.Capture(Query));
        var Restored=new AssetWorkspaceQuery();System.Text.Json.JsonSerializer.Deserialize<AssetWorkspaceFavorite>(Json)!.Restore(Restored);
        Assert.Equal(Query.ViewKey,Restored.ViewKey);
    }
    [Theory]
    [InlineData("{\"Project\":\"Game\",\"Folder\":\"Assets\",\"WholeProject\":true}")]
    [InlineData("{\"Project\":\"Game\",\"Folder\":\"Assets\",\"Scope\":\"FutureScope\",\"WholeProject\":true}")]
    public void ExistingOrUnknownFavoriteScopesKeepLegacyFolderBehaviour(string Json){
        var Query=new AssetWorkspaceQuery{Scope=AssetWorkspaceScope.Solution};
        System.Text.Json.JsonSerializer.Deserialize<AssetWorkspaceFavorite>(Json)!.Restore(Query);
        Assert.Equal(AssetWorkspaceScope.Folder,Query.Scope);Assert.Equal("Game",Query.ProjectPath);Assert.Equal("Assets",Query.Folder);Assert.True(Query.WholeProject);
    }
}
[Collection(MockedVsCollection.Name)]
public sealed class AssetsEmptySpaceClickTests {
    public AssetsEmptySpaceClickTests(MockedVsFixture Fixture){ }
    [Fact]
    public void OnlyTheEmptyPartOfTheListClearsTheSelection(){
        Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Empty=new System.Windows.Controls.Grid();
            Assert.True(AssetsToolWindowControl.IsEmptySpace(Empty));
            Assert.False(AssetsToolWindowControl.IsEmptySpace(new System.Windows.Controls.Primitives.ScrollBar()));
            Assert.False(AssetsToolWindowControl.IsEmptySpace(new System.Windows.Controls.GridViewColumnHeader()));
            var Container=new System.Windows.Controls.ListBoxItem { Content=new System.Windows.Controls.Border() };
            Assert.False(AssetsToolWindowControl.IsEmptySpace(Container));
            Assert.True(AssetsToolWindowControl.IsEmptySpace(null));
        });
    }
}
