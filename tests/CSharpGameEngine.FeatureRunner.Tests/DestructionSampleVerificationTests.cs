using System.Globalization;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class DestructionSampleVerificationTests {
    private static JsonElement Report(int D3D12Errors=0)=>
        JsonDocument.Parse(JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=DestructionSampleVerification.Frames,d3d12Errors=D3D12Errors })).RootElement.Clone();
    /// <summary>
    /// The sample's output as the run prints it: the wall and the falling crate break before the walk, the walk breaks crates, the explosion breaks the last
    /// one, and the pieces settle. Each parameter spoils one part.
    /// </summary>
    private static string Output(int BrokenBeforeWalk=2,int BrokenByWalk=3,bool ExplosionBreaks=true,int Loose=64,float Lowest=0.076f,float FinalSpeed=0.014f,int Frames=DestructionSampleVerification.Frames,int Destructibles=6){
        StringBuilder Text=new();
        Text.AppendLine("CGE-AUTOMATION-CAPTURE path=final.png d3d12Errors=0");
        for(var Frame=1;Frame<=Frames;Frame++){
            var Broken=Frame<DestructionSampleVerification.WalkStartFrame?BrokenBeforeWalk
                :Frame<DestructionSampleVerification.FireFrame?BrokenBeforeWalk+BrokenByWalk
                :Frame<=DestructionSampleVerification.ExplodeFrame||!ExplosionBreaks?BrokenBeforeWalk+BrokenByWalk
                :BrokenBeforeWalk+BrokenByWalk+1;
            var Speed=Frame==Frames?FinalSpeed:1.5f;
            Text.AppendLine(string.Create(CultureInfo.InvariantCulture,$"CGE-DESTRUCTION-SAMPLE frame={Frame} tick={Frame*8} destructibles={Destructibles} broken={Broken} bonds={Broken*30} loose={(Frame*Loose)/Frames} sleeping=0 lowest={Lowest:F3} fastest={Speed:F3}"));
        }
        return Text.ToString();
    }
    [Fact]
    public void Evaluate_AcceptsARunThatBreaksEverythingAndSettles(){
        var Failures=DestructionSampleVerification.Evaluate(Report(),DestructionSampleVerification.Parse(Output()),out var Summary);
        Assert.Empty(Failures);
        Assert.Contains("broken=2/5/5/6",Summary,StringComparison.Ordinal);
    }
    [Fact]
    public void Parse_ReadsOnlyTheSampleLines(){
        var Lines=DestructionSampleVerification.Parse("noise\r\nCGE-DESTRUCTION-SAMPLE frame=3 tick=24 destructibles=6 broken=1 bonds=49 loose=10 sleeping=2 lowest=0.385 fastest=0.225\r\nCGE-DESTRUCTION-SAMPLE frame=x\n");
        var Line=Assert.Single(Lines);
        Assert.Equal(new DestructionSampleVerification.SampleFrame(3,24,6,1,49,10,2,0.385f,0.225f),Line);
    }
    [Theory]
    [InlineData("debug",1)]
    [InlineData("before the character walked",2)]
    [InlineData("Walking into the crates",3)]
    [InlineData("explosion left",4)]
    [InlineData("loose piece(s)",5)]
    [InlineData("fell through",6)]
    [InlineData("settle",7)]
    [InlineData("destructible(s); expected",8)]
    public void Evaluate_ReportsTheFailedExpectation(string Expected,int Case){
        var Text=Case switch {
            2=>Output(BrokenBeforeWalk:1),
            3=>Output(BrokenByWalk:0),
            4=>Output(ExplosionBreaks:false),
            5=>Output(Loose:10),
            6=>Output(Lowest:-0.2f),
            7=>Output(FinalSpeed:0.5f),
            8=>Output(Destructibles:5),
            _=>Output(),
        };
        var Failures=DestructionSampleVerification.Evaluate(Report(Case==1?2:0),DestructionSampleVerification.Parse(Text),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains(Expected,StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_ReportsMissingFrames(){
        var Failures=DestructionSampleVerification.Evaluate(Report(),DestructionSampleVerification.Parse(Output(Frames:100)),out _);
        Assert.Contains(Failures,static Failure=>Failure.Contains("for each of",StringComparison.Ordinal));
    }
    [Fact]
    public void CompareCaches_RequiresTheSameBytesThreeTimes(){
        byte[]Bake=[1,2,3];
        Assert.Empty(DestructionSampleVerification.CompareCaches(Bake,[1,2,3],[1,2,3],out var Summary));
        Assert.Contains("deterministic=True playbackMatches=True",Summary,StringComparison.Ordinal);
        Assert.Contains(DestructionSampleVerification.CompareCaches(Bake,[1,2,4],[1,2,3],out _),static Failure=>Failure.Contains("Two bakes",StringComparison.Ordinal));
        Assert.Contains(DestructionSampleVerification.CompareCaches(Bake,[1,2,3],[1,2],out _),static Failure=>Failure.Contains("playback does not reproduce",StringComparison.Ordinal));
        Assert.Contains(DestructionSampleVerification.CompareCaches([],[],[],out _),static Failure=>Failure.Contains("empty",StringComparison.Ordinal));
    }
}
