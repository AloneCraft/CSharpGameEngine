namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneAnalysisGenerationGateTests {
    [Fact]
    public void ActivateDocument_PreviousDocumentRequestIsNoLongerCurrent(){
        SceneAnalysisGenerationGate<string>Gate=new();

        SceneAnalysisRequest<string>RequestA=Gate.ActivateDocument(nameof(RequestA));
        SceneAnalysisRequest<string>RequestB=Gate.ActivateDocument(nameof(RequestB));

        Assert.False(Gate.IsCurrent(RequestA));
        Assert.True(Gate.IsCurrent(RequestB));
    }

    [Fact]
    public void Refresh_SupersedesAnInFlightRequestForTheSameDocument(){
        SceneAnalysisGenerationGate<string>Gate=new();

        SceneAnalysisRequest<string>Previous=Gate.ActivateDocument(nameof(Previous));
        SceneAnalysisRequest<string>Current=Gate.Refresh();

        Assert.False(Gate.IsCurrent(Previous));
        Assert.True(Gate.IsCurrent(Current));
        Assert.Equal("Previous",Current.Document);
    }
}
