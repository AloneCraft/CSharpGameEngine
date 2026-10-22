using System.Text.Json;
using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class VehicleDriveVerificationTests {
    /// <summary>A report shaped like the template run: the camera follows a car that drives forward and bends right while it is steered.</summary>
    private static JsonElement Report(float TravelMeters=60f,float TurnDegrees=12f,float Height=2.7f,int D3D12Errors=0,int Frames=VehicleDriveVerification.Frames,bool Stationary=false){
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
        var Json=JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=Frames,d3d12Errors=D3D12Errors,cameraPositions=Positions });
        return JsonDocument.Parse(Json).RootElement.Clone();
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
    [Fact]
    public void Evaluate_RejectsAShortReport(){
        Assert.Contains(VehicleDriveVerification.Evaluate(Report(Frames:30),out _),Failure=>Failure.Contains("Expected",StringComparison.Ordinal));
    }
    [Fact]
    public void TheDriveIsScheduledAsThrottleAndASteer(){
        Assert.Equal("W@10-50;D@24-29",VehicleDriveVerification.InputSchedule);
    }
}
