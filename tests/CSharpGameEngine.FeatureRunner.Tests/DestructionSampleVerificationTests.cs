using System.Globalization;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class DestructionSampleVerificationTests {
    private static JsonElement Report(int D3D12Errors=0,bool DebugLayer=true)=>
        JsonDocument.Parse(JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=DestructionSampleVerification.Frames,d3d12Errors=D3D12Errors,d3d12DebugLayerEnabled=DebugLayer })).RootElement.Clone();
    /// <summary>
    /// サンプルの出力として実行時に表示される内容：歩行前に壁と落下する木箱が壊れ、歩行中に木箱が壊れ、爆発で最後の木箱が壊れ、破片が落ち着く。各パラメーターはその一部を損なう。
    /// </summary>
    private static string Output(int BrokenBeforeWalk=2,int BrokenByWalk=3,bool ExplosionBreaks=true,int Loose=64,float Lowest=0.076f,float FinalSpeed=0.014f,int Frames=DestructionSampleVerification.Frames,int Destructibles=6,int Strained=12,int Levels=2,float Dip=0.076f,bool Pushed=true){
        StringBuilder Text=new();
        Text.AppendLine("CGE-AUTOMATION-CAPTURE path=final.png d3d12Errors=0");
        for(var Frame=1;Frame<=Frames;Frame++){
            var Broken=Frame<DestructionSampleVerification.WalkStartFrame?BrokenBeforeWalk
                :Frame<DestructionSampleVerification.FireFrame?BrokenBeforeWalk+BrokenByWalk
                :Frame<=DestructionSampleVerification.ExplodeFrame||!ExplosionBreaks?BrokenBeforeWalk+BrokenByWalk
                :BrokenBeforeWalk+BrokenByWalk+1;
            var Speed=Frame==Frames?FinalSpeed:Pushed&&Frame>DestructionSampleVerification.ExplodeFrame?4f:1.5f;
            var Low=Frame==40?Dip:Lowest;
            Text.AppendLine(string.Create(CultureInfo.InvariantCulture,$"CGE-DESTRUCTION-SAMPLE frame={Frame} tick={Frame*8} destructibles={Destructibles} broken={Broken} bonds={Broken*30} loose={(Frame*Loose)/Frames} sleeping=0 lowest={Low:F3} fastest={Speed:F3} strained={(Frame==60?Strained:0)} levels={Levels}"));
        }
        return Text.ToString();
    }
    [Fact]
    public void Evaluate_AcceptsARunThatBreaksEverythingAndSettles(){
        var Failures=DestructionSampleVerification.Evaluate(Report(),DestructionSampleVerification.Parse(Output()),out var Summary);
        Assert.Empty(Failures);
        Assert.Contains("broken=2/5/5/6",Summary,StringComparison.Ordinal);
        Assert.Contains("levels=2 mostStrained=12",Summary,StringComparison.Ordinal);
    }
    [Fact]
    public void Parse_ReadsOnlyTheSampleLines(){
        var Lines=DestructionSampleVerification.Parse("noise\r\nCGE-DESTRUCTION-SAMPLE frame=3 tick=24 destructibles=6 broken=1 bonds=49 loose=10 sleeping=2 lowest=0.385 fastest=0.225\r\nCGE-DESTRUCTION-SAMPLE frame=x\n");
        var Line=Assert.Single(Lines);
        Assert.Equal(new DestructionSampleVerification.SampleFrame(3,24,6,1,49,10,2,0.385f,0.225f),Line);
        // M0124：行に記録がある場合の内部ひずみとレベル。
        var Newer=Assert.Single(DestructionSampleVerification.Parse("CGE-DESTRUCTION-SAMPLE frame=4 tick=32 destructibles=6 broken=1 bonds=49 loose=10 sleeping=2 lowest=0.385 fastest=0.225 strained=7 levels=2"));
        Assert.Equal(7,Newer.Strained);
        Assert.Equal(2,Newer.Levels);
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
    [InlineData("cluster level(s)",9)]
    [InlineData("internal strain",10)]
    [InlineData("fell through",11)]
    [InlineData("nor pushed",12)]
    public void Evaluate_ReportsTheFailedExpectation(string Expected,int Case){
        var Text=Case switch {
            2=>Output(BrokenBeforeWalk:1),
            3=>Output(BrokenByWalk:0),
            4=>Output(ExplosionBreaks:false),
            5=>Output(Loose:10),
            6=>Output(Lowest:-0.2f),
            7=>Output(FinalSpeed:0.5f),
            8=>Output(Destructibles:5),
            9=>Output(Levels:1),
            10=>Output(Strained:0),
            11=>Output(Dip:-0.2f),
            12=>Output(BrokenByWalk:4,ExplosionBreaks:false,Pushed:false),
            _=>Output(),
        };
        var Failures=DestructionSampleVerification.Evaluate(Report(Case==1?2:0),DestructionSampleVerification.Parse(Text),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains(Expected,StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_AcceptsAWalkThatBreaksEveryCrateAndAShallowMomentaryDip(){
        // M0124：歩行で最後の木箱も壊れる（内部ひずみ）。爆発で破片が押し出され、高速の破片は 1 フレームだけ床に 1 mm めり込む。
        var Failures=DestructionSampleVerification.Evaluate(Report(),DestructionSampleVerification.Parse(Output(BrokenByWalk:4,ExplosionBreaks:false,Dip:-0.001f)),out var Summary);
        Assert.Empty(Failures);
        Assert.Contains("broken=2/6/6/6",Summary,StringComparison.Ordinal);
    }
    [Fact]
    public void Evaluate_RejectsARunWhoseDebugLayerWasNotEnabled(){
        var Disabled=DestructionSampleVerification.Evaluate(Report(DebugLayer:false),DestructionSampleVerification.Parse(Output()),out _);
        Assert.Contains(Disabled,Failure=>Failure.Contains("debug layer was not enabled",StringComparison.Ordinal));
        var Missing=JsonDocument.Parse(JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=DestructionSampleVerification.Frames,d3d12Errors=0 })).RootElement.Clone();
        var MissingFailures=DestructionSampleVerification.Evaluate(Missing,DestructionSampleVerification.Parse(Output()),out _);
        Assert.Contains(MissingFailures,Failure=>Failure.Contains("debug layer was not enabled",StringComparison.Ordinal));
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
