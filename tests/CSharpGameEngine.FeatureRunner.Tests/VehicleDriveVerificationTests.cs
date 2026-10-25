using System.Text.Json;
using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class VehicleDriveVerificationTests {
    /// <summary>A report shaped like the template run: the camera follows a car that drives forward and bends right while it is steered.</summary>
    private static JsonElement Report(float TravelMeters=60f,float TurnDegrees=12f,float Height=2.7f,int D3D12Errors=0,int Frames=VehicleDriveVerification.Frames,bool Stationary=false,int HudStates=20,bool WithHud=true){
        var Positions=new List<float[]?>();
        var R=44.5f;
        var F=6f;
        var Heading=MathF.PI;
        for(var Frame=0;Frame<Frames;Frame++){
            if(Frame<3){
                Positions.Add(null);
                continue;
            }
            if(!Stationary&&Frame>=VehicleDriveVerification.ThrottleStartFrame&&Frame<VehicleDriveVerification.ThrottleEndFrame){
                // 進む向きは、ハンドルを切った区間で、指定の角度だけ右へ回る。
                if(Frame>=VehicleDriveVerification.SteerStartFrame&&Frame<VehicleDriveVerification.TurnedFrom){
                    Heading-=TurnDegrees*MathF.PI/180f/(VehicleDriveVerification.TurnedFrom-VehicleDriveVerification.SteerStartFrame);
                }
                var Step=TravelMeters/(VehicleDriveVerification.ThrottleEndFrame-VehicleDriveVerification.ThrottleStartFrame);
                R+=MathF.Sin(Heading)*Step;
                F+=MathF.Cos(Heading)*Step;
            }
            Positions.Add([R,Height,F]);
        }
        // 走り出す前は一定の画面、走り出したあとは HudStates 通りの文字の画面(0 は何も描かない)。
        var Fingerprints=Enumerable.Range(0,Frames).Select(Frame=>Frame<VehicleDriveVerification.ThrottleStartFrame?"00000000000000aa":HudStates==0?"0000000000000000":((Frame%HudStates)+1).ToString("x16")).ToArray();
        var Json=WithHud
            ?JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=Frames,d3d12Errors=D3D12Errors,d3d12DebugLayerEnabled=true,cameraPositions=Positions,screenLayerFingerprints=Fingerprints })
            :JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=Frames,d3d12Errors=D3D12Errors,d3d12DebugLayerEnabled=true,cameraPositions=Positions });
        return JsonDocument.Parse(Json).RootElement.Clone();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluate_RejectsDisabledOrMissingDebugLayer(bool Missing){
        var Json=Report().GetRawText().Replace("\"d3d12DebugLayerEnabled\":true,",Missing?string.Empty:"\"d3d12DebugLayerEnabled\":false,");
        var ReportWithoutDebug=JsonDocument.Parse(Json).RootElement;
        var Failures=VehicleDriveVerification.Evaluate(ReportWithoutDebug,out _);
        Assert.Contains(Failures,Failure=>Failure.Contains("not enabled",StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_AcceptsADriveThatMovesAndBends(){
        var Failures=VehicleDriveVerification.Evaluate(Report(),out var Summary);
        Assert.Empty(Failures);
        Assert.Contains("travel=",Summary,StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(2f,12f,2.7f,0,"Holding W")]
    [InlineData(60f,1f,2.7f,0,"Steering right")]
    [InlineData(60f,12f,0.2f,0,"camera height")]
    [InlineData(60f,12f,9f,0,"camera height")]
    [InlineData(60f,12f,2.7f,3,"debug layer")]
    public void Evaluate_ReportsTheFailedExpectation(float Travel,float Turn,float Height,int Errors,string Expected){
        var Failures=VehicleDriveVerification.Evaluate(Report(Travel,Turn,Height,Errors),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains(Expected,StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void Evaluate_ReportsACarThatNeverMoves(){
        var Failures=VehicleDriveVerification.Evaluate(Report(Stationary:true),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains("Holding W",StringComparison.Ordinal));
        Assert.Contains(Failures,Failure=>Failure.Contains("heading is unknown",StringComparison.Ordinal));
    }
    [Theory]
    [InlineData(1,"HUD took")]
    [InlineData(0,"HUD took")]
    public void Evaluate_ReportsAHudThatDoesNotChange(int States,string Expected){
        var Failures=VehicleDriveVerification.Evaluate(Report(HudStates:States),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains(Expected,StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_ReportsAMissingHudRecord(){
        var Failures=VehicleDriveVerification.Evaluate(Report(WithHud:false),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains("screenLayerFingerprints",StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_RejectsAShortReport(){
        Assert.Contains(VehicleDriveVerification.Evaluate(Report(Frames:30),out _),Failure=>Failure.Contains("Expected",StringComparison.Ordinal));
    }
    [Fact]
    public void TheDriveIsScheduledAsThrottleAndASteer(){
        Assert.Equal("W@10-50;D@24-29",VehicleDriveVerification.InputSchedule);
    }
    [Fact]
    public void TheAnalogDrivesUseHalfATriggerWithTheKeySteeringAndHalfAStickWithTheKeyThrottle(){
        Assert.Equal("GamepadRightTrigger=0.5@10-50;D@24-29",VehicleDriveVerification.AnalogThrottleSchedule);
        Assert.Equal("W@10-50;GamepadLeftX=0.5@24-29",VehicleDriveVerification.AnalogSteeringSchedule);
    }
    [Fact]
    public void Evaluate_DoesNotHoldAnAnalogDriveToTheKeyDrivesMinimums(){
        // Half a trigger goes less far and half a stick turns less than the 20 m and 5 degrees that the key drive must reach.
        Assert.Empty(VehicleDriveVerification.Evaluate(Report(TravelMeters:8f,TurnDegrees:1f),out _,AnalogDrive:true));
        Assert.NotEmpty(VehicleDriveVerification.Evaluate(Report(TravelMeters:8f,TurnDegrees:1f),out _));
    }
    [Fact]
    public void Measure_ReadsTheTravelAndTheTurnOfAReport(){
        var Measured=VehicleDriveVerification.Measure(Report(TravelMeters:60f,TurnDegrees:12f));
        Assert.InRange(Measured.Travel,40f,61f);
        Assert.InRange(Measured.Turn,5f,15f);
        Assert.Equal(default,VehicleDriveVerification.Measure(Report(Frames:30)));
    }
    [Fact]
    public void EvaluateAnalog_AcceptsHalfValuesThatTravelLessAndTurnLess(){
        Assert.Empty(VehicleDriveVerification.EvaluateAnalog(new VehicleDriveVerification.Measurement(48f,38f),new VehicleDriveVerification.Measurement(27f,59f),new VehicleDriveVerification.Measurement(52f,31f),out var Summary));
        Assert.Contains("halfTriggerTravel=27.000",Summary,StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(48f,48f,10f,"at most")]
    [InlineData(48f,60f,10f,"at most")]
    [InlineData(48f,1f,10f,"expected at least")]
    [InlineData(48f,float.NaN,10f,"expected at least")]
    [InlineData(48f,27f,38f,"expected less")]
    [InlineData(48f,27f,45f,"expected less")]
    [InlineData(48f,27f,0f,"expected at least")]
    public void EvaluateAnalog_RejectsValuesThatActLikeOnOffSwitches(float Keys,float HalfTrigger,float HalfStickTurn,string Expected){
        var Failures=VehicleDriveVerification.EvaluateAnalog(new VehicleDriveVerification.Measurement(Keys,38f),new VehicleDriveVerification.Measurement(HalfTrigger,59f),new VehicleDriveVerification.Measurement(52f,HalfStickTurn),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains(Expected,StringComparison.Ordinal));
    }
}
