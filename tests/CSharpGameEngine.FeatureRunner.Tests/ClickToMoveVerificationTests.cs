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
    /// <summary>
    /// A report of the navigated run (M0106): the camera starts at (-6.36, 13.78, -6.36), the character walks along <paramref name="Route"/> (R, F offsets from the start, one leg per
    /// 8 frames) and the camera height falls by <paramref name="Descent"/> over the first 12 frames of the walk (the ramp), then stays.
    /// </summary>
    private static JsonElement NavigatedReport((float R,float F)[]?Route=null,float Descent=2.1f,float DropFrame=0f,int D3D12Errors=0){
        Route??=[(0f,0f),(0.9f,4.5f),(1.3f,7.7f),(2.2f,9.0f),(6.0f,8.3f),(7.06f,8.06f)];
        var Positions=new List<float[]?>();
        for(var Frame=0;Frame<ClickToMoveVerification.Frames;Frame++){
            if(Frame<3){
                Positions.Add(null);
                continue;
            }
            var Walked=Math.Clamp((Frame-ClickToMoveVerification.ClickStartFrame+1)/8f,0f,Route.Length-1f);
            var Leg=Math.Min((int)Walked,Route.Length-2);
            var Along=Walked-Leg;
            var R=Route[Leg].R+(Route[Leg+1].R-Route[Leg].R)*Along;
            var F=Route[Leg].F+(Route[Leg+1].F-Route[Leg].F)*Along;
            var Fell=Frame<ClickToMoveVerification.ClickStartFrame?0f:Math.Clamp((Frame-ClickToMoveVerification.ClickStartFrame)/12f,0f,1f)*Descent;
            // A fall: the whole descent in one frame.
            if(DropFrame>0f){ Fell=Frame>=DropFrame?Descent:0f; }
            Positions.Add([-6.36f+R,13.78f-Fell,-6.36f+F]);
        }
        var Json=JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=ClickToMoveVerification.Frames,d3d12Errors=D3D12Errors,audioRequests=12,cameraPositions=Positions });
        return JsonDocument.Parse(Json).RootElement.Clone();
    }
    [Fact]
    public void EvaluateNavigated_AcceptsARouteDownTheRampAroundTheWallEnd(){
        var Failures=ClickToMoveVerification.EvaluateNavigated(NavigatedReport(),out var Summary);
        Assert.Empty(Failures);
        Assert.Contains("wallEnd=0.00",Summary,StringComparison.Ordinal);
        Assert.Contains("goalError=0.00",Summary,StringComparison.Ordinal);
    }
    [Fact]
    public void EvaluateNavigated_RejectsAStraightWalkThatNeverGoesAroundTheWall(){
        var Failures=ClickToMoveVerification.EvaluateNavigated(NavigatedReport(Route:[(0f,0f),(7.06f,8.06f)]),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains("wall",StringComparison.OrdinalIgnoreCase)&&Failure.Contains("did not go around",StringComparison.Ordinal));
        Assert.Contains(Failures,Failure=>Failure.Contains("times longer",StringComparison.Ordinal));
    }
    [Fact]
    public void EvaluateNavigated_RejectsAFallOffThePlatform(){
        var Failures=ClickToMoveVerification.EvaluateNavigated(NavigatedReport(DropFrame:26f),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains("fell off the platform",StringComparison.Ordinal));
    }
    [Fact]
    public void EvaluateNavigated_RejectsAWalkThatStopsFarFromTheRoutesEnd(){
        var Failures=ClickToMoveVerification.EvaluateNavigated(NavigatedReport(Route:[(0f,0f),(0.9f,4.5f),(1.3f,7.7f),(2.2f,9.0f),(3.5f,8.8f)]),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains("the route ends at",StringComparison.Ordinal));
    }
    [Fact]
    public void EvaluateNavigated_ReportsTheDebugLayerToo(){
        Assert.Contains(ClickToMoveVerification.EvaluateNavigated(NavigatedReport(D3D12Errors:3),out _),Failure=>Failure.Contains("debug layer",StringComparison.Ordinal));
    }
    [Fact]
    public void TheStraightEvaluation_DoesNotJudgeTheRoute(){
        // The straight run (navigation off) is the pre-M0106 walk: the same report is fine for it whatever its route.
        Assert.Empty(ClickToMoveVerification.Evaluate(NavigatedReport(Route:[(0f,0f),(7.06f,8.06f)],Descent:0f),out _));
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
