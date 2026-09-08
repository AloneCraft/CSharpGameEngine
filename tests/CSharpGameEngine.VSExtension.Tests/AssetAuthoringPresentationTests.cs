using System.Text.Json;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension.Tests;

public class AssetAuthoringPresentationTests {
    [Fact]
    public void MissingMetadata_EnablesOnlyCanonicalCreation(){
        AssetAuthoringResult Result=ValidResult(AssetAuthoringState.MissingMetadata);

        AssetAuthoringPresentation Presentation=AssetAuthoringPresentation.FromResult(Result);

        Assert.True(Presentation.CanCreateMetadata);
        Assert.Equal(Result.AssetId,Presentation.AssetId);
        Assert.Equal(Result.AssetUri,Presentation.AssetUri);
    }

    [Fact]
    public void RejectedState_DisablesCreationAndShowsDiagnostic(){
        AssetAuthoringResult Result=new(){
            RequestId=Guid.NewGuid(),State=AssetAuthoringState.Rejected,Success=false,
            Diagnostics=[new AuthoringHostDiagnostic {Code="ASSET_METADATA_INVALID",Message="invalid"}],
        };

        AssetAuthoringPresentation Presentation=AssetAuthoringPresentation.FromResult(Result);

        Assert.False(Presentation.CanCreateMetadata);
        Assert.Contains("ASSET_METADATA_INVALID",Presentation.Diagnostics,StringComparison.Ordinal);
    }

    [Fact]
    public void ClientRejectsUncorrelatedResult(){
        AssetAuthoringRequest Request=new(){
            RequestId=Guid.NewGuid(),Command=AssetAuthoringCommand.Inspect,
            RepositoryRoot=@"G:\Repo",SourcePath=@"G:\Repo\mesh.glb",
        };
        AssetAuthoringResult Result=ValidResult(AssetAuthoringState.MissingMetadata);

        InvalidDataException Exception=Assert.Throws<InvalidDataException>(()=>
            AssetAuthoringHostClient.DeserializeValidatedResult(
                Request,JsonSerializer.Serialize(Result)));

        Assert.Contains("RequestId",Exception.Message,StringComparison.Ordinal);
    }

    private static AssetAuthoringResult ValidResult(AssetAuthoringState State)=>new(){
        RequestId=Guid.NewGuid(),State=State,Success=true,SourceUri="assets/mesh.glb",
        MetadataPath=@"G:\Repo\assets\mesh.glb.meta",
        AssetId="11111111111111111111111111111111",
        AssetUri="asset://11111111111111111111111111111111",Importer="SharpGLTF",
        MetadataSha256=State==AssetAuthoringState.Ready?new string('a',64):string.Empty,
    };
}
