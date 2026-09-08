namespace CSharpGameEngine.VSExtension.Tests;

public sealed class RuntimeDebugArchitectureTests {
    [Fact]
    public void ViewerRequestsOneSnapshotFromTheExistingPreviewSession(){
        string Client=Read("RenderPreviewHostClient.cs");
        string Host=Read("SceneHwndHost.cs");
        string Control=Read("SceneDesignerToolWindowControl.xaml.cs");
        string Xaml=Read("SceneDesignerToolWindowControl.xaml");

        Assert.Contains("Command=RenderPreviewHostCommand.Snapshot",Client,StringComparison.Ordinal);
        Assert.Contains("RenderPreviewHostProtocol.ValidateDebugSnapshot",Client,StringComparison.Ordinal);
        Assert.Contains("Client.GetDebugSnapshot()",Host,StringComparison.Ordinal);
        Assert.Contains("Host.CaptureRuntimeDebugSnapshot()",Control,StringComparison.Ordinal);
        Assert.Contains("OnRuntimeDebugRefresh",Xaml,StringComparison.Ordinal);
        Assert.DoesNotContain("RenderServer.Create",Host,StringComparison.Ordinal);
    }

    [Fact]
    public void ViewerHasNoPollingTimingFabricationOrRuntimeOwnership(){
        string Presentation=Read("RuntimeDebugSnapshotPresentation.cs");
        string SessionTable=Read("RenderPreviewHostSessionTable.cs");

        Assert.DoesNotContain("Timer",Presentation,StringComparison.Ordinal);
        Assert.DoesNotContain("Stopwatch",Presentation,StringComparison.Ordinal);
        Assert.DoesNotContain("Elapsed",Presentation,StringComparison.Ordinal);
        Assert.DoesNotContain("RenderServer",Presentation,StringComparison.Ordinal);
        Assert.DoesNotContain("RenderPreviewHostClient",Presentation,StringComparison.Ordinal);
        Assert.Contains("PREVIEW_SNAPSHOT_FAILED",SessionTable,StringComparison.Ordinal);
        Assert.Contains("Current.LastSequence=Request.Sequence",SessionTable,StringComparison.Ordinal);
    }

    private static string Read(string FileName)=>File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory,"TestAssets","Source",FileName));
}
