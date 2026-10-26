using System.Security.Cryptography;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Content.Tests;
public sealed class AssetWorkspaceTests:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"cge-asset-workspace-"+Guid.NewGuid().ToString("N"));
    private string Source(string Relative,string Text="source"){
        var PathValue=Path.Combine(this.Root,Relative);Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);File.WriteAllText(PathValue,Text);return PathValue;
    }
    private AssetWorkspaceSource Entry(string PathValue)=>new(){ProjectPath=Path.Combine(this.Root,"Game.csproj"),ProjectRoot=this.Root,SourcePath=PathValue,RelativePath=Path.GetRelativePath(this.Root,PathValue).Replace('\\','/')};
    [Fact]
    public void InventoryDoesNotCreateMetadataOrInventIdentity(){
        var Model=this.Source("Assets/Ship.glb");var Scene=this.Source("Scenes/Main.json","{}");
        var Result=AssetWorkspaceService.Inspect(new(){RequestId=Guid.NewGuid(),Sources=[this.Entry(Model),this.Entry(Scene),this.Entry(Model)]});
        Assert.True(Result.Success);Assert.Equal(2,Result.Items.Length);
        Assert.Equal(AssetWorkspaceState.MissingMetadata,Result.Items[0].State);Assert.Empty(Result.Items[0].AssetId);
        Assert.False(File.Exists(Model+".meta"));Assert.Equal(AssetWorkspaceKind.Scene,Result.Items[1].Kind);
    }
    [Fact]
    public void InventoryRejectsOldMetadataWithoutSilentlyMigrating(){
        var Model=this.Source("Assets/Ship.glb");var Legacy="guid: "+Guid.NewGuid().ToString("N")+"\n";File.WriteAllText(Model+".meta",Legacy);
        var Result=AssetWorkspaceService.Inspect(new(){RequestId=Guid.NewGuid(),Sources=[this.Entry(Model)]});
        Assert.Equal(AssetWorkspaceState.InvalidMetadata,Assert.Single(Result.Items).State);Assert.Equal(Legacy,File.ReadAllText(Model+".meta"));
    }
    [Fact]
    public void LegacyConversionPreservesGuidAndRequiresSnapshotHash(){
        var Model=this.Source("Assets/Ship.glb");var Id=Guid.NewGuid();File.WriteAllText(Model+".meta","guid: "+Id.ToString("N")+"\n");
        var Inspect=AssetAuthoringService.Execute(new(){RequestId=Guid.NewGuid(),RepositoryRoot=this.Root,SourcePath=Model});
        Assert.Equal(AssetAuthoringState.Rejected,Inspect.State);
        var Request=new AssetAuthoringRequest{RequestId=Guid.NewGuid(),RepositoryRoot=this.Root,SourcePath=Model,Command=AssetAuthoringCommand.MigrateLegacyMetadata,ExpectedMetadataSha256=Inspect.MetadataSha256};
        var Result=AssetAuthoringService.Execute(Request);
        Assert.True(Result.Success);Assert.Equal(Id.ToString("D"),Result.AssetId);
        Assert.True(AssetMetadata.TryParse(File.ReadAllBytes(Model+".meta"),out var Metadata,out _));Assert.Equal(Result.AssetId,Metadata!.AssetId.ToString());
        Assert.False(AssetAuthoringService.Execute(Request).Success);
    }
    [Theory]
    [InlineData("guid: not-a-guid")]
    [InlineData("guid: 00000000000000000000000000000000")]
    [InlineData("guid: 123456781234123412341234567890ab\nother: value")]
    public void InvalidLegacyConversionLeavesBytesUntouched(string Legacy){
        var Model=this.Source("Assets/Ship.glb");File.WriteAllText(Model+".meta",Legacy);
        var Hash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Model+".meta")));
        var Result=AssetAuthoringService.Execute(new(){RequestId=Guid.NewGuid(),RepositoryRoot=this.Root,SourcePath=Model,Command=AssetAuthoringCommand.MigrateLegacyMetadata,ExpectedMetadataSha256=Hash});
        Assert.False(Result.Success);Assert.Equal(Legacy,File.ReadAllText(Model+".meta"));
    }
    [Fact]
    public void TenThousandEvaluatedSourcesSupportCancellationAndExcludeBuildOutputs(){
        Directory.CreateDirectory(this.Root);
        var Items=Enumerable.Range(0,10_000).Select(Index=>this.Entry(Path.Combine(this.Root,"Assets",Index+".png"))).ToArray();
        using var Cancellation=new CancellationTokenSource();Cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>AssetWorkspaceService.Inspect(new(){RequestId=Guid.NewGuid(),Sources=Items},Cancellation.Token));
        var Result=AssetWorkspaceService.Inspect(new(){RequestId=Guid.NewGuid(),Sources=Items.Concat([this.Entry(Path.Combine(this.Root,"obj","cache.png"))]).ToArray()});
        Assert.True(Result.Success);Assert.Equal(10_000,Result.Items.Length);
    }
    public void Dispose(){ if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); } }
}
