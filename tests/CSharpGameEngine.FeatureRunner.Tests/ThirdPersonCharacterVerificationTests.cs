using System.Text.Json;
using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class ThirdPersonCharacterVerificationTests {
    /// <summary>A report shaped like the template run: stand, walk forward, jump, land.</summary>
    private static JsonElement Report(float WalkMeters=5f,float JumpMeters=1.2f,float FloorHeight=0.985f,int D3D12Errors=0,int Frames=ThirdPersonCharacterVerification.Frames,int?AudioRequests=null){
        var Positions=new List<float[]?>();
        for(var Frame=0;Frame<Frames;Frame++){
            if(Frame<3){
                Positions.Add(null);
                continue;
            }
            var Walked=Math.Clamp((Frame-ThirdPersonCharacterVerification.WalkStartFrame)/(float)(ThirdPersonCharacterVerification.WalkEndFrame-ThirdPersonCharacterVerification.WalkStartFrame),0f,1f)*WalkMeters;
            var SinceJump=Frame-ThirdPersonCharacterVerification.JumpFrame;
            var Height=SinceJump is >=0 and <30?JumpMeters*MathF.Sin(SinceJump/30f*MathF.PI):0f;
            Positions.Add([0f,FloorHeight+Height,-4f+Walked]);
        }
        var Json=AudioRequests is { } Requests
            ?JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=Frames,d3d12Errors=D3D12Errors,d3d12DebugLayerEnabled=true,audioRequests=Requests,cameraPositions=Positions })
            :JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=Frames,d3d12Errors=D3D12Errors,d3d12DebugLayerEnabled=true,cameraPositions=Positions });
        return JsonDocument.Parse(Json).RootElement.Clone();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluate_RejectsDisabledOrMissingDebugLayer(bool Missing){
        var Json=Report().GetRawText().Replace("\"d3d12DebugLayerEnabled\":true,",Missing?string.Empty:"\"d3d12DebugLayerEnabled\":false,");
        var ReportWithoutDebug=JsonDocument.Parse(Json).RootElement;
        var Failures=ThirdPersonCharacterVerification.Evaluate(ReportWithoutDebug,out _);
        Assert.Contains(Failures,Failure=>Failure.Contains("not enabled",StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_AcceptsAWalkAndALandedJump(){
        var Failures=ThirdPersonCharacterVerification.Evaluate(Report(),out var Summary);
        Assert.Empty(Failures);
        Assert.Contains("walk=5.000",Summary,StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(0.2f,1.2f,0.985f,0,"Holding W")]
    [InlineData(5f,0.1f,0.985f,0,"The jump")]
    [InlineData(5f,1.2f,-3f,0,"dropped")]
    [InlineData(5f,1.2f,0.985f,2,"D3D12")]
    public void Evaluate_ReportsTheBrokenExpectation(float Walk,float Jump,float Floor,int Errors,string Expected){
        var Failures=ThirdPersonCharacterVerification.Evaluate(Report(Walk,Jump,Floor,Errors),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains(Expected,StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_AcceptsAReportWithTheJumpAndLandingSounds(){
        var Failures=ThirdPersonCharacterVerification.Evaluate(Report(AudioRequests:15),out var Summary);
        Assert.Empty(Failures);
        Assert.Contains("audioRequests=15",Summary,StringComparison.Ordinal);
    }
    [Fact]
    public void Evaluate_RejectsAReportWhereNothingMadeASound(){
        var Failures=ThirdPersonCharacterVerification.Evaluate(Report(AudioRequests:0),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains("sound",StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_RejectsATruncatedRun(){
        var Failures=ThirdPersonCharacterVerification.Evaluate(Report(Frames:40),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains("frames",StringComparison.Ordinal));
    }
    [Fact]
    public void InputSchedule_HoldsWThenTapsSpace(){
        Assert.Equal("W@20-40;SpaceBar@50-51",ThirdPersonCharacterVerification.InputSchedule);
    }
}
