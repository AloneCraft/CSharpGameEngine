namespace CSharpGameEngine.VSExtension.Tests;

public sealed class ExperimentalToolWindowStateTests {
    [Fact]
    public void Source_LeavesVisualStudioPlacementToTheShell(){
        string Source=File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,"TestAssets","Source","ExperimentalToolWindowState.cs"));

        Assert.Contains("Frame.ShowNoActivate()",Source);
        Assert.Contains("preserving Visual Studio's current Experimental layout",Source);
        Assert.Contains("Visual Studio will retain the previous window placement",Source);
        Assert.DoesNotContain("SetFramePos(",Source);
        Assert.DoesNotContain("Frame.Hide()",Source);
    }

    [Fact]
    public void Package_RestoresAfterSolutionOpen_AndCapturesOnNormalShutdown(){
        string Source=File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,"TestAssets","Source","CSharpGameEngineViewerPackage.cs"));

        Assert.Contains("await this.WaitForSolutionOpenAsync()",Source);
        Assert.Contains("ExperimentalToolWindowState.RestoreAsync",Source);
        Assert.Contains("ExperimentalToolWindowState?.Capture()",Source);
    }
}