using System.Security.Cryptography;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Content.Tests;

public sealed class AssetAuthoringServiceTests:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"cge-asset-authoring-tests",Guid.NewGuid().ToString("N"));

    [Fact]
    public void InspectCreateAndReloadPreserveOneCanonicalIdentityWithoutTouchingSource(){
        string Source=CreateSource("Assets/Ship.glb",[1,2,3,4]);
        string SourceHash=Hash(File.ReadAllBytes(Source));
        AssetAuthoringRequest Inspect=CreateRequest(AssetAuthoringCommand.Inspect,Source);

        AssetAuthoringResult Missing=AssetAuthoringService.Execute(Inspect);
        AssetAuthoringRequest Save=CreateSave(Source,Missing);
        AssetAuthoringResult Ready=AssetAuthoringService.Execute(Save);
        AssetAuthoringResult Reloaded=AssetAuthoringService.Execute(CreateRequest(AssetAuthoringCommand.Inspect,Source));

        Assert.Equal(AssetAuthoringState.MissingMetadata,Missing.State);
        Assert.Equal(AssetAuthoringState.Ready,Ready.State);
        Assert.Equal(Ready.AssetId,Reloaded.AssetId);
        Assert.Equal("Assets/Ship.glb",Ready.SourceUri);
        Assert.Equal("SharpGLTF",Ready.Importer);
        Assert.Equal("asset://"+Ready.AssetId,Ready.AssetUri);
        Assert.Equal(SourceHash,Hash(File.ReadAllBytes(Source)));
        Assert.Equal(File.ReadAllBytes(Source+".meta"),AssetMetadata.Create(AssetId.Parse(Ready.AssetId),"Assets/Ship.glb","SharpGLTF").ToCanonicalUtf8());
    }

    [Fact]
    public void ConflictInvalidImporterAndIdentityMutationLeaveSourceAndMetadataUnchanged(){
        string Source=CreateSource("Assets/Ship.fbx",[9,8,7]);
        AssetAuthoringResult Missing=AssetAuthoringService.Execute(CreateRequest(AssetAuthoringCommand.Inspect,Source));
        AssetAuthoringResult Ready=AssetAuthoringService.Execute(CreateSave(Source,Missing));
        byte[]SourceBefore=File.ReadAllBytes(Source);
        byte[]MetadataBefore=File.ReadAllBytes(Source+".meta");

        AssetAuthoringRequest Conflict=CreateSave(Source,Ready);
        Conflict.ExpectedMetadataSha256=new string('0',64);
        Assert.Equal("ASSET_METADATA_CONFLICT",Assert.Single(AssetAuthoringService.Execute(Conflict).Diagnostics).Code);
        AssetAuthoringRequest Importer=CreateSave(Source,Ready);
        Importer.Importer="SharpGLTF";
        Assert.Equal("ASSET_IMPORTER_INVALID",Assert.Single(AssetAuthoringService.Execute(Importer).Diagnostics).Code);
        AssetAuthoringRequest Identity=CreateSave(Source,Ready);
        Identity.AssetId=new AssetId(StableId.New()).ToString();
        Assert.Equal("ASSET_ID_IMMUTABLE",Assert.Single(AssetAuthoringService.Execute(Identity).Diagnostics).Code);

        Assert.Equal(SourceBefore,File.ReadAllBytes(Source));
        Assert.Equal(MetadataBefore,File.ReadAllBytes(Source+".meta"));
    }

    [Fact]
    public void MissingOutsideUnsupportedAndNonCanonicalInputsFailClosed(){
        string Source=CreateSource("Assets/Ship.glb",[1]);
        File.WriteAllText(Source+".meta","""{ "schemaVersion": 1 }""");
        Assert.Equal(AssetAuthoringState.Rejected,AssetAuthoringService.Execute(CreateRequest(AssetAuthoringCommand.Inspect,Source)).State);
        string Unsupported=CreateSource("Assets/Ship.obj",[1]);
        Assert.Equal("ASSET_AUTHORING_FAILED",Assert.Single(AssetAuthoringService.Execute(CreateRequest(AssetAuthoringCommand.Inspect,Unsupported)).Diagnostics).Code);
        string Outside=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".glb");
        try {
            File.WriteAllBytes(Outside,[1]);
            Assert.Equal(AssetAuthoringState.Rejected,AssetAuthoringService.Execute(CreateRequest(AssetAuthoringCommand.Inspect,Outside)).State);
        } finally { File.Delete(Outside); }
    }

    private AssetAuthoringRequest CreateRequest(AssetAuthoringCommand Command,string Source)=>new(){
        RequestId=Guid.NewGuid(),Command=Command,RepositoryRoot=this.Root,SourcePath=Source,
    };
    private AssetAuthoringRequest CreateSave(string Source,AssetAuthoringResult Snapshot)=>new(){
        RequestId=Guid.NewGuid(),Command=AssetAuthoringCommand.SaveMetadata,RepositoryRoot=this.Root,SourcePath=Source,
        ExpectedMetadataSha256=Snapshot.MetadataSha256,AssetId=Snapshot.AssetId,Importer=Snapshot.Importer,Options=Snapshot.Options,
    };
    private string CreateSource(string Relative,byte[]Bytes){
        string PathValue=Path.Combine(this.Root,Relative.Replace('/',Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
        File.WriteAllBytes(PathValue,Bytes);
        return PathValue;
    }
    private static string Hash(byte[]Bytes)=>Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
    public void Dispose(){ if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); } }
}
