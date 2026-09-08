namespace CSharpGameEngine.VSExtension.Tests;

public sealed class BakeIntegrationArchitectureTests {
    [Fact]
    public void PhysicsBakeUsesOneProtocolBackedPresentationState(){
        string Control=Read("SceneDesignerToolWindowControl.xaml.cs");
        string Xaml=Read("SceneDesignerToolWindowControl.xaml");
        string Service=Read("PhysicsSceneBakeService.cs");

        Assert.Contains("AuthoringOperationSnapshot?PhysicsBakeOperation",Control,StringComparison.Ordinal);
        Assert.DoesNotContain("PhysicsBakeRunning",Control,StringComparison.Ordinal);
        Assert.Contains("Operation.ApplyProgress(Progress",Control,StringComparison.Ordinal);
        Assert.Contains("Operation.RequestCancellation(",Control,StringComparison.Ordinal);
        Assert.Contains("PhysicsBakeCancelButton",Xaml,StringComparison.Ordinal);
        Assert.Contains("PhysicsBakeProgressBar",Xaml,StringComparison.Ordinal);
        Assert.Contains("PhysicsBakeDiagnosticText",Xaml,StringComparison.Ordinal);
        Assert.Contains("PhysicsBakeArtifactList",Xaml,StringComparison.Ordinal);
        Assert.Contains("Result.OutputPaths",Control,StringComparison.Ordinal);
        Assert.Contains("OnPhysicsBakeOpenArtifact",Control,StringComparison.Ordinal);
        Assert.Contains(
            "State:AuthoringOperationUiState.Running",Control,StringComparison.Ordinal);
        Assert.Contains("AuthoringHostProgress",Service,StringComparison.Ordinal);
        Assert.Contains("AuthoringHostDiagnostic",Service,StringComparison.Ordinal);
        Assert.Contains("Outputs.Count",Service,StringComparison.Ordinal);
    }

    [Fact]
    public void AssetImportPublishesInlineProgressAndDiagnostics(){
        string Control=Read("AssetPreviewControl.xaml.cs");
        string Xaml=Read("AssetPreviewControl.xaml");
        string Host=Read("AssetPreviewHwndHost.cs");

        Assert.Contains("OperationChanged",Control,StringComparison.Ordinal);
        Assert.Contains("AssetOperationProgressBar",Xaml,StringComparison.Ordinal);
        Assert.Contains("AssetOperationDiagnosticText",Xaml,StringComparison.Ordinal);
        Assert.Contains("AuthoringHostOperation.Import",Host,StringComparison.Ordinal);
        Assert.Contains("ApplyProgress(",Host,StringComparison.Ordinal);
        Assert.Contains("ASSET_IMPORT_FAILED",Host,StringComparison.Ordinal);
        Assert.Contains("ASSET_PREVIEW_HOST_FAILED",Host,StringComparison.Ordinal);
        Assert.Contains("HostFailure.Result.Diagnostics",Host,StringComparison.Ordinal);
    }

    [Fact]
    public void AssetAuthoringKeepsCanonicalMetadataOutsideTheVsix(){
        string Control=Read("AssetPreviewControl.xaml.cs");
        string Xaml=Read("AssetPreviewControl.xaml");
        string Client=Read("AssetAuthoringHostClient.cs");

        Assert.Contains("AssetAuthoringHostClient.Inspect",Control,StringComparison.Ordinal);
        Assert.Contains("AssetAuthoringHostClient.SaveMetadata",Control,StringComparison.Ordinal);
        Assert.Contains("CreateAssetMetadataButton",Xaml,StringComparison.Ordinal);
        Assert.Contains("AssetAuthoringProtocol.Validate(Request,Result",Client,StringComparison.Ordinal);
        Assert.Contains("CSharpGameEngine.Import.AssetAuthoring.Cli",Client,StringComparison.Ordinal);
        Assert.DoesNotContain("AssetMetadata.TryParse",Client,StringComparison.Ordinal);
        Assert.DoesNotContain("File.WriteAllBytes",Client,StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationRejectsFalseSuccessAndUncorrelatedProgress(){
        string Source=Read("AuthoringOperationPresentation.cs");

        Assert.Contains("UI_FALSE_SUCCESS",Source,StringComparison.Ordinal);
        Assert.Contains("UI_REQUEST_MISMATCH",Source,StringComparison.Ordinal);
        Assert.Contains("Progress.Sequence<=this.Sequence",Source,StringComparison.Ordinal);
        Assert.Contains("Progress.Fraction<this.Fraction",Source,StringComparison.Ordinal);
        Assert.Contains("ValidateTerminalProgress(Result.Progress)",Source,StringComparison.Ordinal);
        string Bridge=Read("ExternalBridgeProcess.cs");
        Assert.Contains("!IsSuccessfulResult(ResidentResult)",Bridge,StringComparison.Ordinal);
        Assert.Contains(
            "Diagnostic.Severity==AuthoringHostDiagnosticSeverity.Error",
            Bridge,StringComparison.Ordinal);
    }

    private static string Read(string FileName)=>File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory,"TestAssets","Source",FileName));
}
