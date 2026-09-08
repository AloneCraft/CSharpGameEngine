using System.Text.Json;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Import.AssetAuthoring.Cli;

namespace CSharpGameEngine.Bridge.Hosting.Tests;

public class AssetAuthoringBridgeWorkerTests {
    [Fact]
    public void InspectAndSave_RoundTripsThroughTypedFiles(){
        using TemporaryDirectory Directory=new();
        string Source=Path.Combine(Directory.Path,"mesh.glb");
        File.WriteAllBytes(Source,[0x67,0x6c,0x54,0x46]);

        AssetAuthoringResult Missing=Run(
            Directory.Path,Source,AssetAuthoringCommand.Inspect,string.Empty,string.Empty);
        Assert.Equal(AssetAuthoringState.MissingMetadata,Missing.State);

        AssetAuthoringResult Ready=Run(
            Directory.Path,Source,AssetAuthoringCommand.SaveMetadata,Missing.AssetId,string.Empty);
        Assert.Equal(AssetAuthoringState.Ready,Ready.State);
        Assert.Equal(Missing.AssetId,Ready.AssetId);
        Assert.True(File.Exists(Source+".meta"));
    }

    [Fact]
    public void MalformedRequest_FailsWithoutPublishingResult(){
        using TemporaryDirectory Directory=new();
        string RequestPath=Path.Combine(Directory.Path,"request.json");
        string ResultPath=Path.Combine(Directory.Path,"result.json");
        File.WriteAllText(RequestPath,"{");

        (int ExitCode,string Output)=AssetAuthoringBridgeWorker.Run(RequestPath,ResultPath);

        Assert.NotEqual(0,ExitCode);
        Assert.NotEmpty(Output);
        Assert.False(File.Exists(ResultPath));
    }

    private static AssetAuthoringResult Run(
        string Root,string Source,AssetAuthoringCommand Command,string AssetId,string ExpectedHash){
        string RequestPath=Path.Combine(Root,Guid.NewGuid().ToString("N")+".request.json");
        string ResultPath=Path.Combine(Root,Guid.NewGuid().ToString("N")+".result.json");
        AssetAuthoringRequest Request=new(){
            RequestId=Guid.NewGuid(),Command=Command,RepositoryRoot=Root,SourcePath=Source,
            AssetId=AssetId,Importer=Command==AssetAuthoringCommand.SaveMetadata?"SharpGLTF":string.Empty,
            ExpectedMetadataSha256=ExpectedHash,
        };
        File.WriteAllText(RequestPath,JsonSerializer.Serialize(Request));

        (int ExitCode,string Output)=AssetAuthoringBridgeWorker.Run(RequestPath,ResultPath);

        Assert.Equal(0,ExitCode);
        Assert.Empty(Output);
        AssetAuthoringResult?Result=JsonSerializer.Deserialize<AssetAuthoringResult>(
            File.ReadAllText(ResultPath));
        Assert.NotNull(Result);
        Assert.Null(AssetAuthoringProtocol.Validate(Request,Result));
        return Result;
    }

    private sealed class TemporaryDirectory:IDisposable {
        public TemporaryDirectory(){
            this.Path=System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),"CgeAssetAuthoringBridgeTests",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.Path);
        }
        public string Path{get;}
        public void Dispose()=>Directory.Delete(this.Path,true);
    }
}
