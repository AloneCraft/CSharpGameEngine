using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetReferenceGraphTests {
    private const string Project="C:/Game/Game.csproj";
    private const string Root="C:/Game";
    private static AssetWorkspaceItem Item(string Relative,AssetWorkspaceKind Kind,string Project=Project)=>new(){Kind=Kind,Source=new(){ProjectPath=Project,ProjectRoot=Root,SourcePath=Root+"/"+Relative,RelativePath=Relative}};
    private static Task<IReadOnlyList<string>> NoneAsync(AssetWorkspaceItem Target,CancellationToken Token)=>Task.FromResult<IReadOnlyList<string>>([]);
    [Fact]
    public async Task ReferencersBecomeLeftColumnsAndDependenciesRightColumnsAsync(){
        var Cube=Item("Assets/Cube.glb",AssetWorkspaceKind.Model);
        var Scene=Item("Scenes/Main.json",AssetWorkspaceKind.Scene);
        var Wall=Item("Assets/Wall.png",AssetWorkspaceKind.Texture);
        var Items=new[]{Cube,Scene,Wall};
        Task<IReadOnlyList<string>> ReferencersAsync(AssetWorkspaceItem Target,CancellationToken Token)=>Task.FromResult<IReadOnlyList<string>>(Target==Cube?[Scene.Source.SourcePath,Root+"/Scenes/Assets.cs"]:[]);
        string?Read(string Path)=>Path==Scene.Source.SourcePath?"{\"asset\":\"Assets/Cube.glb\",\"texture\":\"Assets/Wall.png\"}":null;
        var Graph=await AssetReferenceGraphBuilder.BuildAsync(Scene,Items,1,1,ReferencersAsync,Read,CancellationToken.None);
        Assert.Equal(AssetReferenceNodeRole.Focus,Graph.Focus.Role);
        Assert.Equal(0,Graph.Focus.Depth);
        Assert.Equal(new[]{Cube.Source.SourcePath,Wall.Source.SourcePath}.OrderBy(static Value=>Value),Graph.Nodes.Where(static Node=>Node.Role==AssetReferenceNodeRole.Dependency).Select(static Node=>Node.Path).OrderBy(static Value=>Value));
        Assert.All(Graph.Nodes.Where(static Node=>Node.Role==AssetReferenceNodeRole.Dependency),static Node=>Assert.Equal(1,Node.Depth));
        Assert.Equal(2,Graph.Edges.Count);
        Assert.All(Graph.Edges,static Edge=>Assert.EndsWith("Main.json",Edge.FromPath));
    }
    [Fact]
    public async Task ReferencerFilesThatAreNotAssetsStillBecomeNodesButCannotBeFollowedAsync(){
        var Cube=Item("Assets/Cube.glb",AssetWorkspaceKind.Model);
        var Code=Root+"/Scenes/Assets.cs";
        Task<IReadOnlyList<string>> ReferencersAsync(AssetWorkspaceItem Target,CancellationToken Token)=>Task.FromResult<IReadOnlyList<string>>(Target==Cube?[Code]:[]);
        var Graph=await AssetReferenceGraphBuilder.BuildAsync(Cube,[Cube],2,0,ReferencersAsync,static _=>null,CancellationToken.None);
        var Node=Assert.Single(Graph.Nodes,static Node=>Node.Role==AssetReferenceNodeRole.Referencer);
        Assert.False(Node.IsAsset);
        Assert.Equal(-1,Node.Depth);
        Assert.Null(Node.Kind);
        Assert.Equal("Scenes/Assets.cs",Node.RelativePath);
        var Edge=Assert.Single(Graph.Edges);
        Assert.Equal(Code,Edge.FromPath);
        Assert.Equal(Cube.Source.SourcePath,Edge.ToPath);
    }
    [Fact]
    public async Task DepthIsFollowedThroughAssetsAndCyclesTerminateAsync(){
        var Cube=Item("Assets/Cube.glb",AssetWorkspaceKind.Model);
        var Data=Item("Assets/Cube.cgeasset",AssetWorkspaceKind.Data);
        var Scene=Item("Scenes/Main.json",AssetWorkspaceKind.Scene);
        Task<IReadOnlyList<string>> ReferencersAsync(AssetWorkspaceItem Target,CancellationToken Token)=>Task.FromResult<IReadOnlyList<string>>(
            Target==Cube?[Data.Source.SourcePath]:Target==Data?[Scene.Source.SourcePath,Cube.Source.SourcePath]:Target==Scene?[Data.Source.SourcePath]:[]);
        var Items=new[]{Cube,Data,Scene};
        var One=await AssetReferenceGraphBuilder.BuildAsync(Cube,Items,1,0,ReferencersAsync,static _=>null,CancellationToken.None);
        Assert.Equal(2,One.Nodes.Count);
        var Three=await AssetReferenceGraphBuilder.BuildAsync(Cube,Items,3,0,ReferencersAsync,static _=>null,CancellationToken.None);
        Assert.Equal(3,Three.Nodes.Count);
        Assert.Equal(-2,Three.Nodes.Single(Node=>Node.Path==Scene.Source.SourcePath).Depth);
    }
    [Fact]
    public async Task DepthZeroShowsOnlyTheFocusAndDepthIsCappedAsync(){
        var Cube=Item("Assets/Cube.glb",AssetWorkspaceKind.Model);
        var Graph=await AssetReferenceGraphBuilder.BuildAsync(Cube,[Cube],0,0,NoneAsync,static _=>null,CancellationToken.None);
        Assert.Single(Graph.Nodes);
        Assert.Empty(Graph.Edges);
        var Capped=await AssetReferenceGraphBuilder.BuildAsync(Cube,[Cube],99,99,NoneAsync,static _=>null,CancellationToken.None);
        Assert.Single(Capped.Nodes);
    }
    [Fact]
    public void DependenciesOnlyComeFromTextAssetsOfTheSameProject(){
        var Cube=Item("Assets/Cube.glb",AssetWorkspaceKind.Model);
        var Other=Item("Assets/Cube.glb",AssetWorkspaceKind.Model,"C:/Other/Other.csproj");
        var Scene=Item("Scenes/Main.json",AssetWorkspaceKind.Scene);
        string?Read(string Path)=>"\"Assets/Cube.glb\"";
        Assert.Empty(AssetReferenceGraphBuilder.FindDependencies(Cube,[Cube,Scene],Read));
        Assert.Same(Cube,Assert.Single(AssetReferenceGraphBuilder.FindDependencies(Scene,[Cube,Other,Scene],Read)));
        Assert.Same(Cube,Assert.Single(AssetReferenceGraphBuilder.FindDependencies(Scene,[Cube],static _=>"\"Assets\\\\Cube.glb\"")));
        Assert.Empty(AssetReferenceGraphBuilder.FindDependencies(Scene,[Cube],static _=>null));
    }
    [Fact]
    public async Task LayoutPutsReferencersLeftOfTheFocusAndCentersEachColumnAsync(){
        var Cube=Item("Assets/Cube.glb",AssetWorkspaceKind.Model);
        var A=Root+"/Scenes/A.json";
        var B=Root+"/Scenes/B.json";
        Task<IReadOnlyList<string>> ReferencersAsync(AssetWorkspaceItem Target,CancellationToken Token)=>Task.FromResult<IReadOnlyList<string>>(Target==Cube?[A,B]:[]);
        var Graph=await AssetReferenceGraphBuilder.BuildAsync(Cube,[Cube],1,0,ReferencersAsync,static _=>null,CancellationToken.None);
        var Layout=AssetReferenceGraphLayout.Compute(Graph);
        Assert.Equal(0,Layout[A].X);
        Assert.Equal(AssetReferenceGraphLayout.NodeWidth+AssetReferenceGraphLayout.ColumnSpacing,Layout[Cube.Source.SourcePath].X);
        Assert.True(Layout[A].Y<Layout[B].Y);
        var Middle=(Layout[A].Y+Layout[B].Y+AssetReferenceGraphLayout.NodeHeight)/2;
        Assert.Equal(Middle,Layout[Cube.Source.SourcePath].Y+AssetReferenceGraphLayout.NodeHeight/2,3);
    }
}
