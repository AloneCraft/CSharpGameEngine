namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneSelectionAndShutdownArchitectureTests {
    [Fact]
    public void SceneSelector_UsesOneLatestWinsCompletionPath(){
        string Designer=ReadSource("SceneDesignerToolWindowControl.xaml.cs");
        int Handler=Designer.IndexOf("private async Task SelectSceneAsync",StringComparison.Ordinal);
        int End=Designer.IndexOf("private void DetachSession",Handler,StringComparison.Ordinal);
        string Body=Designer.Substring(Handler,End-Handler);
        Assert.Contains("SelectionRequestLifecycle.TryComplete",Body,StringComparison.Ordinal);
        Assert.Contains("ResolveSceneAsync(Key,CancellationToken)",Body,StringComparison.Ordinal);
        Assert.Contains("Session.Pin(Model);",Body,StringComparison.Ordinal);
        Assert.DoesNotContain("ShowSnapshot(Session.Pin(Model))",Body,StringComparison.Ordinal);
        Assert.Contains("catch(OperationCanceledException)",Body,StringComparison.Ordinal);
    }
    [Fact]
    public void PreviewHost_PresentsReplacementBeforePublishingIt(){
        string Session=ReadSource("RenderPreviewRenderSession.cs");
        int Method=Session.IndexOf("public void ReplaceScene",StringComparison.Ordinal);
        int Replacement=Session.IndexOf("RenderServer Replacement=CreateServer",Method,StringComparison.Ordinal);
        int Present=Session.IndexOf("Replacement.RenderFrame(CaptureForVerification:false)",Replacement,StringComparison.Ordinal);
        int Publish=Session.IndexOf("this.Server=Replacement",Method,StringComparison.Ordinal);
        Assert.True(Method>=0);
        Assert.True(Replacement>Method);
        Assert.True(Present>Replacement);
        Assert.True(Publish>Present);
    }
    [Fact]
    public void HierarchyClear_DoesNotRequireAModelOrPinnedProjection(){
        string Package=ReadSource("CSharpGameEngineViewerPackage.cs");
        int Handler=Package.IndexOf(
            "private void OnHierarchySelectionChanged",StringComparison.Ordinal);
        int Clear=Package.IndexOf(
            "Selection.Paths.Count==0",Handler,StringComparison.Ordinal);
        int CanonicalClear=Package.IndexOf(
            "Selection.TryClearCurrent",Clear,StringComparison.Ordinal);
        int ModelGate=Package.IndexOf(
            "SceneEditorHub.ActiveSceneModel is not",Handler,StringComparison.Ordinal);

        Assert.True(Handler>=0);
        Assert.True(Clear>Handler);
        Assert.True(CanonicalClear>Clear);
        Assert.True(ModelGate>CanonicalClear);
    }

    [Fact]
    public void HierarchyEscape_AlwaysPublishesTheEmptySelection(){
        string Hierarchy=ReadSource("HierarchyToolWindowControl.xaml.cs");
        int Handler=Hierarchy.IndexOf(
            "private void HandleEscape",StringComparison.Ordinal);
        int Publish=Hierarchy.IndexOf(
            "this.PublishCurrentSelection();",Handler,StringComparison.Ordinal);
        string EscapeBody=Hierarchy.Substring(Handler,Publish-Handler);

        Assert.Contains(
            "this.CurrentSelection=HierarchyNodeSelection.Empty",EscapeBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "this.CurrentSelection.Paths.Count==0",EscapeBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HierarchyPane_HandlesEscapeAtTheVisualStudioAcceleratorBoundary(){
        string Pane=ReadSource("HierarchyToolWindowPane.cs");

        Assert.Contains("PreProcessMessage(",Pane,StringComparison.Ordinal);
        Assert.Contains(
            "IsVisualStudioEscapeKeyDown",Pane,StringComparison.Ordinal);
        Assert.Contains("Hierarchy.HandleVisualStudioEscape()",Pane,StringComparison.Ordinal);
    }

    [Fact]
    public void SolutionShutdown_ReleasesRuntimeWithoutReenteringShellFrameClose(){
        string Lifecycle=ReadSource(
            "CSharpGameEngineViewerPackage.SolutionLifecycle.cs");
        string Documents=ReadSource("SceneJsonEditorPane.cs");
        int Query=Lifecycle.IndexOf(
            "OnQueryCloseSolution",StringComparison.Ordinal);
        int Prepare=Lifecycle.IndexOf(
            "this.PrepareForSolutionClose();",Query,StringComparison.Ordinal);

        Assert.True(Query>=0);
        Assert.True(Prepare>Query);
        Assert.Contains(
            "ReleaseRuntimeResourcesForSolutionClose",Lifecycle,
            StringComparison.Ordinal);
        Assert.Contains("Pane.ReleaseDesigner()",Documents,StringComparison.Ordinal);
        string PrepareBody=Lifecycle.Substring(
            Lifecycle.IndexOf("private void PrepareForSolutionClose",StringComparison.Ordinal),
            Lifecycle.IndexOf(
                "int IVsSolutionEvents.OnBeforeCloseSolution",StringComparison.Ordinal)
            -Lifecycle.IndexOf(
                "private void PrepareForSolutionClose",StringComparison.Ordinal));
        Assert.DoesNotContain("CloseAll()",PrepareBody,StringComparison.Ordinal);
        Assert.DoesNotContain("CloseFrame(",PrepareBody,StringComparison.Ordinal);
    }

    [Fact]
    public void SceneWindowTeardown_NeverWaitsForPreviewProcessesOnTheVsUiThread(){
        string Host=ReadSource("SceneHwndHost.cs");
        int Teardown=Host.IndexOf(
            "protected override void DestroyWindowCore",StringComparison.Ordinal);
        int TeardownEnd=Host.IndexOf(
            "internal void SetTarget",Teardown,StringComparison.Ordinal);
        string Body=Host.Substring(Teardown,TeardownEnd-Teardown);

        // M0096 (ADR-0047)：エディターのワールドはプレビュークライアントのプロセス内で実行され、バックグラウンドで破棄される。
        Assert.Contains(
            "DisposePreviewClient(ClearScene:true,DisposeInBackground:true)",
            Body,StringComparison.Ordinal);
        Assert.DoesNotContain("WaitForExit(",Body,StringComparison.Ordinal);
        Assert.DoesNotContain(".Kill()",Body,StringComparison.Ordinal);
    }

    private static string ReadSource(string FileName)=>File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory,"TestAssets","Source",FileName));
}
