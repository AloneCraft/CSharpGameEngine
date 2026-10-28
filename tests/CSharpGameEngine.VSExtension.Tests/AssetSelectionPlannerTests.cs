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
