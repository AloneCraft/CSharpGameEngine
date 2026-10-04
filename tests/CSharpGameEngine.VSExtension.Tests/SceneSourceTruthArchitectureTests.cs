namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneSourceTruthArchitectureTests {
    [Fact]
    public void SessionHasNoEditableDocumentOrMirroredUndoHistory(){
        string Source=ReadSessionSource();

        Assert.DoesNotContain("SceneEditDocument",Source,StringComparison.Ordinal);
        Assert.DoesNotContain("UnwrittenHistory",Source,StringComparison.Ordinal);
        Assert.DoesNotContain("TextWatermark",Source,StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyForSave",Source,StringComparison.Ordinal);
    }

    [Fact]
    public void SessionCommitsLatestRoslynPlanToTheLiveBuffer(){
        string Source=ReadSessionSource();

        Assert.Contains("CommitAsync(",Source,StringComparison.Ordinal);
        Assert.Contains("ResolveSceneEditContextAsync(",Source,StringComparison.Ordinal);
        Assert.Contains("SceneSourceCommitPlanner.Plan(",Source,StringComparison.Ordinal);
        Assert.Contains("ApplyTextAsync(",Source,StringComparison.Ordinal);
        Assert.Contains("ScheduleAnalysisNow()",Source,StringComparison.Ordinal);
    }

    [Fact]
    public void OwnWriteLandingsAreSceneScopedAndGenerationChecked(){
        string Source=ReadSessionSource();

        Assert.Contains(
            "Dictionary<SceneKey,PendingSourceLanding>PendingSourceLandings",Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AwaitingOwnWriteKey",Source,StringComparison.Ordinal);
        Assert.Contains("Pending.Matches(Model)",Source,StringComparison.Ordinal);
        Assert.Contains("Pending.WriteCompleted",Source,StringComparison.Ordinal);
        Assert.Contains("SceneModelStructuralSignature.Compute",Source,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CSharpGameEngineViewerPackage.cs")]
    [InlineData("CSharpGameEngineViewerPackage.ComponentDrop.cs")]
    [InlineData("InspectorToolWindowControl.xaml.cs")]
    [InlineData("SceneDesignerToolWindowControl.xaml.cs")]
    public void ProductEditCallersAwaitSourceCommit(string FileName){
        string Source=ReadSource(FileName);

        Assert.Contains("CommitAsync(",Source,StringComparison.Ordinal);
        Assert.DoesNotContain("Session.Apply(",Source,StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceHasOneAtomicWriterAndNoPrivateUndoLedger(){
        string Source=ReadSource("SceneWorkspaceService.cs");
        const string ApplyCall="TextBufferWriter.TryApply(";

        Assert.Contains(ApplyCall,Source,StringComparison.Ordinal);
        Assert.Equal(
            Source.IndexOf(ApplyCall,StringComparison.Ordinal),
            Source.LastIndexOf(ApplyCall,StringComparison.Ordinal));
        Assert.DoesNotContain("ApplyTextStepsAsync",Source,StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetBufferVersion",Source,StringComparison.Ordinal);
        Assert.DoesNotContain("SceneTextUndoLedger",Source,StringComparison.Ordinal);
    }

    [Fact]
    public void PredictedSnapshotIsReanalyzedFromVerifiedSource(){
        // ADR-0070: the written scene JSON is read back, and that canonical model is what lands.
        string Source=ReadSource("SceneJsonCommitPlanner.cs");

        Assert.Contains("TryAnalyze(NewText",Source,StringComparison.Ordinal);
        Assert.Contains("CanonicalProjection",Source,StringComparison.Ordinal);
        Assert.Contains("SceneSourceCommitPlan.Success(Request.DocumentVersion!=Latest.DocumentVersion,Canonical!,CanonicalProjection",Source,StringComparison.Ordinal);
        Assert.DoesNotContain("Success(Request.DocumentVersion!=Latest.DocumentVersion,Predicted,",Source,StringComparison.Ordinal);
    }

    [Fact]
    public void SourceWriterCreatesExactlyOneNativeUndoTransaction(){
        string Source=ReadSource("SceneTextBufferWriter.cs");
        const string CreateTransaction="History.CreateTransaction(";

        Assert.Contains(CreateTransaction,Source,StringComparison.Ordinal);
        Assert.Equal(
            Source.IndexOf(CreateTransaction,StringComparison.Ordinal),
            Source.LastIndexOf(CreateTransaction,StringComparison.Ordinal));
        Assert.Contains("Transaction.Complete()",Source,StringComparison.Ordinal);
        Assert.DoesNotContain("SceneTextUndoLedger",Source,StringComparison.Ordinal);
    }

    private static string ReadSessionSource()=>ReadSource("SceneEditorSessionService.cs");

    private static string ReadSource(string FileName)=>File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory,"TestAssets","Source",
            FileName));
}
