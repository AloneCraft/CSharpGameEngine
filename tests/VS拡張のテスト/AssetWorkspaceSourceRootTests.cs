using System;
using System.IO;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.VSExtension;
using Xunit;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetWorkspaceSourceRootTests:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"cge-source-root-"+Guid.NewGuid().ToString("N"));
    public void Dispose(){ if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); } }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProjectAssetMetadataUsesGameRootWhileBridgeWorkspaceIsPreserved(bool SourceMode){
        var Context=this.CreateContext(SourceMode);
        var SourcePath=this.Write("Engine/Game/Assets/Box.glb","source");
        var Source=AssetWorkspaceProjectItems.CreateSource(Context,SourcePath,"Assets/Box.glb");
        Assert.Equal(Context.ProjectFilePath,Source.ProjectPath);
        Assert.Equal(Context.ProjectDirectory,Source.ProjectRoot);
        Assert.Equal(Context.ProjectDirectory,Source.MetadataRoot);
        Assert.Equal("Assets/Box.glb",Source.RelativePath);
        Assert.Equal("Project",Source.Provider);
        Assert.False(Source.ReadOnly);
        Assert.Equal(SourceMode?Path.Combine(this.Root,"Engine"):Context.ProjectDirectory,Context.WorkspaceRoot);
    }
    [Theory]
    [InlineData(true,"Engine")]
    [InlineData(false,"Package")]
    public void ExternalProviderRemainsReadOnlyWithItsExistingMetadataRoot(bool SourceMode,string Provider){
        var Context=this.CreateContext(SourceMode);
        var SourcePath=this.Write("Engine/Shared/Box.glb","source");
        var Source=AssetWorkspaceProjectItems.CreateSource(Context,SourcePath,"Assets/LinkedBox.glb");
        Assert.Equal(Provider,Source.Provider);
        Assert.True(Source.ReadOnly);
        Assert.Equal(Context.WorkspaceRoot,Source.MetadataRoot);
        Assert.Equal(Context.ProjectDirectory,Source.ProjectRoot);
        var Request=AssetAuthoringHostClient.CreateRequest(Context,SourcePath,AssetAuthoringCommand.Inspect);
        Assert.Equal(Context.WorkspaceRoot,Request.RepositoryRoot);
        var Image=new AssetImageRequest{SourcePath=SourcePath};AssetAuthoringHostClient.BindRequest(Image,Context);
        var Model=new AssetModelRequest{SourcePath=SourcePath};AssetAuthoringHostClient.BindRequest(Model,Context);
        Assert.Equal(Context.WorkspaceRoot,Image.Root);
        Assert.Equal(Context.WorkspaceRoot,Model.Root);
    }
    [Fact]
    public void ProjectPluginMetadataUsesGameRoot(){
        var Context=this.CreateContext(true);
        var SourcePath=this.Write("Engine/Game/Plugins/Gameplay/Assets/Box.glb","source");
        var Source=AssetWorkspaceProjectItems.CreateSource(Context,SourcePath,"Plugins/Gameplay/Assets/Box.glb");
        Assert.Equal("ProjectPlugin",Source.Provider);
        Assert.Equal(Context.ProjectDirectory,Source.MetadataRoot);
        Assert.False(Source.ReadOnly);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AuthoringWireRequestsUseGameRootWithoutChangingBridgeWorkspace(bool SourceMode){
        var Context=this.CreateContext(SourceMode);
        var SourcePath=this.Write("Engine/Game/Assets/Box.glb","source");
        var WorkspaceRoot=Context.WorkspaceRoot;
        foreach(var Command in new[]{AssetAuthoringCommand.Inspect,AssetAuthoringCommand.SaveMetadata,AssetAuthoringCommand.MigrateLegacyMetadata}){
            var Request=AssetAuthoringHostClient.CreateRequest(Context,SourcePath,Command);
            Assert.Equal(Context.ProjectDirectory,Request.RepositoryRoot);
            Assert.Equal(SourcePath,Request.SourcePath);
            Assert.Equal(Command,Request.Command);
            Assert.NotEqual(Guid.Empty,Request.RequestId);
        }
        var Image=new AssetImageRequest{SourcePath=SourcePath};AssetAuthoringHostClient.BindRequest(Image,Context);
        var Model=new AssetModelRequest{SourcePath=SourcePath};AssetAuthoringHostClient.BindRequest(Model,Context);
        Assert.Equal(Context.ProjectDirectory,Image.Root);
        Assert.Equal(Context.ProjectDirectory,Model.Root);
        var Mutation=AssetReferencePlanner.CreateRequest(Context);
        Assert.Equal(Context.ProjectDirectory,Mutation.Root);
        Assert.Equal(Context.ProjectDirectory,Mutation.MetadataRoot);
        Assert.NotEqual(Guid.Empty,Mutation.OperationId);
        Assert.Equal(WorkspaceRoot,Context.WorkspaceRoot);
    }
    private CgeProjectContext CreateContext(bool SourceMode){
        this.Write("Engine/build/CSharpGameEngine.Game.props","<Project />");
        var Project=this.Write("Engine/Game/Game.csproj",SourceMode?"<Project><Import Project=\"../build/CSharpGameEngine.Game.props\" /></Project>":"<Project><ItemGroup><PackageReference Include=\"CSharpGameEngine.Sdk\" Version=\"0.1.40\" /></ItemGroup></Project>");
        return CgeProjectContext.TryCreate(Project)!;
    }
    private string Write(string Relative,string Text){
        var PathValue=Path.Combine(this.Root,Relative.Replace('/',Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
        File.WriteAllText(PathValue,Text);
        return PathValue;
    }
}
