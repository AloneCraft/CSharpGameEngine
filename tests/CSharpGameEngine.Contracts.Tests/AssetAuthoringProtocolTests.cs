using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Contracts.Tests;

public sealed class AssetAuthoringProtocolTests {
    [Fact]
    public void InspectAndSaveContractsRejectMutableOrUncorrelatedShapes(){
        AssetAuthoringRequest Inspect=CreateInspect();
        Assert.Null(AssetAuthoringProtocol.Validate(Inspect));
        Inspect.AssetId="unexpected";
        Assert.NotNull(AssetAuthoringProtocol.Validate(Inspect));

        AssetAuthoringRequest Save=CreateInspect();
        Save.Command=AssetAuthoringCommand.SaveMetadata;
        Save.AssetId="17b3e569-5a8a-53fd-b317-0d29f9764c39";
        Save.Importer="SharpGLTF";
        Assert.Null(AssetAuthoringProtocol.Validate(Save));
        Save.Options=[new AssetAuthoringOption {Name="x",Value="1"},new AssetAuthoringOption {Name="x",Value="2"}];
        Assert.Contains("duplicated",AssetAuthoringProtocol.Validate(Save),StringComparison.Ordinal);
    }

    [Fact]
    public void ResultRequiresCorrelationCanonicalUriAndStateConsistency(){
        AssetAuthoringRequest Request=CreateInspect();
        AssetAuthoringResult Result=new(){
            RequestId=Request.RequestId,State=AssetAuthoringState.MissingMetadata,Success=true,
            SourceUri="Assets/Ship.glb",MetadataPath="Ship.glb.meta",Importer="SharpGLTF",
            AssetId="17b3e569-5a8a-53fd-b317-0d29f9764c39",
            AssetUri="asset://17b3e569-5a8a-53fd-b317-0d29f9764c39",
        };
        Assert.Null(AssetAuthoringProtocol.Validate(Request,Result));
        Result.RequestId=Guid.NewGuid();
        Assert.Contains("RequestId",AssetAuthoringProtocol.Validate(Request,Result),StringComparison.Ordinal);
        Result.RequestId=Request.RequestId;
        Result.AssetUri="http://invalid";
        Assert.Contains("non-canonical",AssetAuthoringProtocol.Validate(Request,Result),StringComparison.Ordinal);
    }

    [Fact]
    public void ResultRejectsUnboundedOrFalseFailurePayloads(){
        AssetAuthoringRequest Request=CreateInspect();
        AssetAuthoringResult Rejected=new(){
            RequestId=Request.RequestId,State=AssetAuthoringState.Rejected,Success=false,
            Diagnostics=[new AuthoringHostDiagnostic {
                Severity=AuthoringHostDiagnosticSeverity.Info,Code="ASSET_REJECTED",Message="rejected",
            }],
        };
        Assert.Contains("error diagnostic",AssetAuthoringProtocol.Validate(Request,Rejected),StringComparison.Ordinal);
        Rejected.Diagnostics[0].Severity=AuthoringHostDiagnosticSeverity.Error;
        Assert.Null(AssetAuthoringProtocol.Validate(Request,Rejected));
        Rejected.Diagnostics[0].Message=new string('x',AssetAuthoringProtocol.MaximumTextLength+1);
        Assert.Contains("invalid diagnostic",AssetAuthoringProtocol.Validate(Request,Rejected),StringComparison.Ordinal);
    }

    private static AssetAuthoringRequest CreateInspect()=>new(){
        RequestId=Guid.NewGuid(),Command=AssetAuthoringCommand.Inspect,
        RepositoryRoot="G:/repo",SourcePath="G:/repo/Assets/Ship.glb",
    };
}
