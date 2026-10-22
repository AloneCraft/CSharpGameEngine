using System.Text.Json;
using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class ClickToMoveVerificationTests {
    /// <summary>A report shaped like the template run: stand, click, walk to the point, stop there.</summary>
    private static JsonElement Report(float WalkMeters=6f,float StillDrift=0f,int D3D12Errors=0,int?AudioRequests=7,int Frames=ClickToMoveVerification.Frames,int[]?RenderObjects=null){
        var Positions=new List<float[]?>();
        for(var Frame=0;Frame<Frames;Frame++){
            if(Frame<3){
                Positions.Add(null);
                continue;
            }
            var Arrived=Frames-ClickToMoveVerification.SettledFrames-1;
            var Walked=Frame<=Arrived?Math.Clamp((Frame-ClickToMoveVerification.ClickStartFrame)/(float)(Arrived-ClickToMoveVerification.ClickStartFrame),0f,1f)*WalkMeters:WalkMeters+((Frame-Arrived)/(float)ClickToMoveVerification.SettledFrames*StillDrift);
            Positions.Add([0f,10f,-4f+Walked]);
        }
        var Json=AudioRequests is { } Requests
            ?JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=Frames,d3d12Errors=D3D12Errors,audioRequests=Requests,cameraPositions=Positions,renderObjectCounts=RenderObjects })
            :JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=Frames,d3d12Errors=D3D12Errors,cameraPositions=Positions,renderObjectCounts=RenderObjects });
        var Element=JsonDocument.Parse(Json).RootElement.Clone();
        // A run without the counts has no such property at all, not a null one.
        return RenderObjects is null?JsonDocument.Parse(Json.Replace(",\"renderObjectCounts\":null","")).RootElement.Clone():Element;
    }
    /// <summary>The renderer's object count per frame: <paramref name="Level"/> objects, the marker from <paramref name="From"/> up to (not including) <paramref name="To"/>.</summary>
    private static int[]Objects(int From,int To,int Level=40,int Marker=1)=>[.. Enumerable.Range(0,ClickToMoveVerification.Frames).Select(Frame=>Frame>=From&&Frame<To?Level+Marker:Level)];
    [Fact]
    public void Evaluate_AcceptsAWalkThatArrivesAndStops(){
        var Failures=ClickToMoveVerification.Evaluate(Report(),out var Summary);
        Assert.Empty(Failures);
        Assert.Contains("finalDrift=0.000",Summary,StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(0.3f,0f,0,7,"Clicking the ground")]
    [InlineData(30f,0f,0,7,"at most")]
    [InlineData(6f,3f,0,7,"did not stop")]
    [InlineData(6f,0f,2,7,"debug layer")]
    [InlineData(6f,0f,0,0,"footstep")]
    public void Evaluate_ReportsTheFailedExpectation(float Walk,float Drift,int Errors,int Audio,string Expected){
        var Failures=ClickToMoveVerification.Evaluate(Report(Walk,Drift,Errors,Audio),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains(Expected,StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void Evaluate_AcceptsAReportWithoutAudioFields(){
        Assert.Empty(ClickToMoveVerification.Evaluate(Report(AudioRequests:null),out _));
    }
    [Fact]
    public void Evaluate_RejectsAShortReport(){
        Assert.Contains(ClickToMoveVerification.Evaluate(Report(Frames:40),out _),Failure=>Failure.Contains("Expected",StringComparison.Ordinal));
    }
    [Fact]
    public void TheClickIsScheduledAsTheLeftMouseButton(){
        Assert.Equal("MouseLeft@20-25",ClickToMoveVerification.InputSchedule);
    }
    [Fact]
    public void Evaluate_AcceptsAMarkerThatAppearsAfterTheClickAndGoesAway(){
        var Failures=ClickToMoveVerification.Evaluate(Report(RenderObjects:Objects(22,28)),out var Summary);
        Assert.Empty(Failures);
        Assert.Contains("renderObjects=40->41->40 markerFrames=6",Summary,StringComparison.Ordinal);
    }
    [Fact]
    public void Evaluate_RejectsAClickThatShowsNoMarker(){
        Assert.Contains(ClickToMoveVerification.Evaluate(Report(RenderObjects:Objects(0,0)),out _),Failure=>Failure.Contains("no marker",StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_RejectsAMarkerThatStaysAfterItsLifetime(){
        Assert.Contains(ClickToMoveVerification.Evaluate(Report(RenderObjects:Objects(22,ClickToMoveVerification.Frames)),out _),Failure=>Failure.Contains("did not go away",StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_DoesNotCountAMarkerThatWasAlreadyThereBeforeTheClick(){
        // The scene had one more object from the start; only a rise after the click is the marker.
        Assert.Contains(ClickToMoveVerification.Evaluate(Report(RenderObjects:Objects(0,ClickToMoveVerification.Frames)),out _),Failure=>Failure.Contains("no marker",StringComparison.Ordinal));
    }
}
