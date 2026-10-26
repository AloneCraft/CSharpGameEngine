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
        string Content=Read("StaticMeshEditorContent.cs");
        string Shell=Read("AssetEditorShell.cs");
        string Host=Read("AssetViewportHost.cs");

        Assert.Contains("OperationChanged",Content,StringComparison.Ordinal);
        Assert.Contains("Operation.Fraction",Content,StringComparison.Ordinal);
        Assert.Contains("Operation.Diagnostics",Content,StringComparison.Ordinal);
        Assert.Contains("OperationProgressBar",Shell,StringComparison.Ordinal);
        Assert.Contains("AuthoringHostOperation.Import",Host,StringComparison.Ordinal);
        Assert.Contains("ApplyProgress(",Host,StringComparison.Ordinal);
        Assert.Contains("ASSET_IMPORT_FAILED",Host,StringComparison.Ordinal);
        Assert.Contains("ASSET_PREVIEW_HOST_FAILED",Host,StringComparison.Ordinal);
        Assert.Contains("HostFailure.Result.Diagnostics",Host,StringComparison.Ordinal);
    }

    [Fact]
    public void AssetAuthoringKeepsCanonicalMetadataOutsideTheVsix(){
        string Inspector=Read("AssetInspectorControl.cs");
        string Mesh=Read("StaticMeshEditorContent.cs");
        string Client=Read("AssetAuthoringHostClient.cs");

        // アセットエディターのメタデータ操作は Details（Inspector と同じ経路）に一本化され、エディター自身は解析も保存もしない。
        Assert.Contains("AssetAuthoringHostClient.Inspect",Inspector,StringComparison.Ordinal);
        Assert.Contains("AssetAuthoringHostClient.SaveMetadata",Inspector,StringComparison.Ordinal);
        Assert.DoesNotContain("AssetAuthoringHostClient",Mesh,StringComparison.Ordinal);
        Assert.DoesNotContain("AssetMetadata.TryParse",Mesh,StringComparison.Ordinal);
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
