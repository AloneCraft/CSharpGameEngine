using System.Text.Json;
using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class ClickToMoveVerificationTests {
    /// <summary>テンプレート実行のような形式のレポート：停止し、クリックし、その地点まで歩き、そこで止まる。</summary>
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
            ?JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=Frames,d3d12Errors=D3D12Errors,d3d12DebugLayerEnabled=true,audioRequests=Requests,cameraPositions=Positions,renderObjectCounts=RenderObjects })
            :JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=Frames,d3d12Errors=D3D12Errors,d3d12DebugLayerEnabled=true,cameraPositions=Positions,renderObjectCounts=RenderObjects });
        var Element=JsonDocument.Parse(Json).RootElement.Clone();
        // カウントを含まない実行には、そのプロパティ自体が存在しない。null ではない。
        return RenderObjects is null?JsonDocument.Parse(Json.Replace(",\"renderObjectCounts\":null","")).RootElement.Clone():Element;
    }
    /// <summary>レンダラーのフレームあたりオブジェクト数：<paramref name="Level"/> 個のオブジェクトと、<paramref name="From"/> から <paramref name="To"/> の直前までのマーカー。</summary>
    private static int[]Objects(int From,int To,int Level=40,int Marker=1)=>[.. Enumerable.Range(0,ClickToMoveVerification.Frames).Select(Frame=>Frame>=From&&Frame<To?Level+Marker:Level)];
    [Theory]
    [InlineData("straight",false)]
    [InlineData("straight",true)]
    [InlineData("navigation",false)]
    [InlineData("navigation",true)]
    [InlineData("hold",false)]
    [InlineData("hold",true)]
    public void Evaluate_RejectsDisabledOrMissingDebugLayer(string Mode,bool Missing){
        var Input=Mode switch { "navigation"=>NavigatedReport(),"hold"=>LongPressReport(),_=>Report() };
        var Json=Input.GetRawText().Replace("\"d3d12DebugLayerEnabled\":true,",Missing?string.Empty:"\"d3d12DebugLayerEnabled\":false,");
        var ReportWithoutDebug=JsonDocument.Parse(Json).RootElement;
        var Failures=Mode switch {
            "navigation"=>ClickToMoveVerification.EvaluateNavigated(ReportWithoutDebug,out _),
            "hold"=>ClickToMoveVerification.EvaluateLongPress(ReportWithoutDebug,out _),
            _=>ClickToMoveVerification.Evaluate(ReportWithoutDebug,out _),
        };
        Assert.Contains(Failures,Failure=>Failure.Contains("not enabled",StringComparison.Ordinal));
    }
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
    /// 移動を行った実行のレポート (M0106)：カメラは (-6.36, 13.78, -6.36) から開始する。キャラクターは <paramref name="Route"/> に沿って歩く（開始地点からの R、F オフセットで、8 フレームごとに 1 区間）。歩行開始から 12 フレームの間（ランプ）にカメラの高さが <paramref name="Descent"/> だけ下がり、その後は一定になる。
    /// </summary>
    private static JsonElement NavigatedReport((float R,float F)[]?Route=null,float Descent=2.1f,float DropFrame=0f,int D3D12Errors=0){
        Route??=[(0f,0f),(0.9f,4.5f),(1.3f,7.7f),(2.2f,9.0f),(6.0f,8.3f),(6.79f,6.84f)];
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
            // 落下：下降全体が 1 フレームで起きる。
            if(DropFrame>0f){ Fell=Frame>=DropFrame?Descent:0f; }
            Positions.Add([-6.36f+R,13.78f-Fell,-6.36f+F]);
        }
        var Json=JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=ClickToMoveVerification.Frames,d3d12Errors=D3D12Errors,d3d12DebugLayerEnabled=true,audioRequests=12,cameraPositions=Positions });
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
        var Failures=ClickToMoveVerification.EvaluateNavigated(NavigatedReport(Route:[(0f,0f),(6.79f,6.84f)]),out _);
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
        // 直線移動（ナビゲーション無効）は M0106 以前の歩行であり、経路が何であっても同じレポートを使える。
        Assert.Empty(ClickToMoveVerification.Evaluate(NavigatedReport(Route:[(0f,0f),(6.79f,6.84f)],Descent:0f),out _));
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
        Assert.Equal("MouseLeft@20-24",ClickToMoveVerification.InputSchedule);
    }
    [Fact]
    public void Evaluate_AcceptsAMarkerThatAppearsAfterTheClickAndGoesAway(){
        var Failures=ClickToMoveVerification.Evaluate(Report(RenderObjects:Objects(24,30)),out var Summary);
        Assert.Empty(Failures);
        Assert.Contains("renderObjects=40->41->40 markerFrames=6",Summary,StringComparison.Ordinal);
    }
    [Fact]
    public void Evaluate_RejectsAMarkerThatAppearsWhileTheButtonIsStillDown(){
        // M0105 は押下時に生成した。UE（および M0108）は短い押下を離した時に生成する。
        var Failures=ClickToMoveVerification.Evaluate(Report(RenderObjects:Objects(ClickToMoveVerification.ClickStartFrame,ClickToMoveVerification.ClickStartFrame+7)),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains("still down",StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_AcceptsAMarkerThatAppearsInTheReleaseFrame(){
        Assert.Empty(ClickToMoveVerification.Evaluate(Report(RenderObjects:Objects(ClickToMoveVerification.ClickEndFrame,ClickToMoveVerification.ClickEndFrame+5)),out _));
    }
    [Fact]
    public void TheShortPressIsClearlyUnderUnrealsHalfSecondThreshold(){
        // 自動操作の 1 フレームは最大 0.1 秒を数える。長押し時間は ClickEndFrame-ClickStartFrame フレームであり、離した時にその数だけ 0.1 秒として読み取る。
        var HeldSeconds=(ClickToMoveVerification.ClickEndFrame-ClickToMoveVerification.ClickStartFrame)*0.1f;
        Assert.True(HeldSeconds<0.5f-0.05f);
        Assert.True((ClickToMoveVerification.LongPressEndFrame-ClickToMoveVerification.ClickStartFrame)*0.1f>0.5f+1f);
        Assert.Equal("MouseLeft@20-45",ClickToMoveVerification.LongPressSchedule);
    }
    /// <summary>長押し：キャラクターは保持中に <paramref name="HeldMeters"/> 進み（均等に）、その後ブレーキ中にさらに <paramref name="AfterMeters"/> 進んでから停止する。</summary>
    private static JsonElement LongPressReport(float HeldMeters=15f,float AfterMeters=0.4f,float Drift=0f,int D3D12Errors=0,int[]?RenderObjects=null){
        var Positions=new List<float[]?>();
        for(var Frame=0;Frame<ClickToMoveVerification.LongPressFrames;Frame++){
            if(Frame<3){
                Positions.Add(null);
                continue;
            }
            var HeldFrames=ClickToMoveVerification.LongPressEndFrame-ClickToMoveVerification.ClickStartFrame;
            var Walked=Frame<ClickToMoveVerification.ClickStartFrame?0f:Frame<ClickToMoveVerification.LongPressEndFrame?(Frame-ClickToMoveVerification.ClickStartFrame+1)/(float)HeldFrames*HeldMeters:HeldMeters+AfterMeters*Math.Min(1f,(Frame-ClickToMoveVerification.LongPressEndFrame+1)/5f);
            var Moving=Frame>=ClickToMoveVerification.LongPressFrames-ClickToMoveVerification.SettledFrames?Drift*(Frame-(ClickToMoveVerification.LongPressFrames-ClickToMoveVerification.SettledFrames))/ClickToMoveVerification.SettledFrames:0f;
            Positions.Add([0f,1f,Walked+Moving]);
        }
        var Json=JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=ClickToMoveVerification.LongPressFrames,d3d12Errors=D3D12Errors,d3d12DebugLayerEnabled=true,cameraPositions=Positions,focusPositions=Positions,renderObjectCounts=RenderObjects??Enumerable.Repeat(40,ClickToMoveVerification.LongPressFrames).ToArray() });
        return JsonDocument.Parse(Json).RootElement.Clone();
    }
    [Fact]
    public void EvaluateLongPress_AcceptsAHoldThatFollowsTheCursorThenBrakesWithoutAMarker(){
        var Failures=ClickToMoveVerification.EvaluateLongPress(LongPressReport(),out var Summary);
        Assert.Empty(Failures);
        Assert.Contains("walkWhileHeld=15.000",Summary,StringComparison.Ordinal);
        Assert.Contains("renderObjects=40->40",Summary,StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(1f,0.4f,0f,0,"Holding the button")]
    [InlineData(15f,6f,0f,0,"after the button was released")]
    [InlineData(15f,0.4f,3f,0,"still moved")]
    [InlineData(15f,0.4f,0f,2,"debug layer")]
    public void EvaluateLongPress_ReportsTheFailedExpectation(float Held,float After,float Drift,int Errors,string Expected){
        var Failures=ClickToMoveVerification.EvaluateLongPress(LongPressReport(Held,After,Drift,Errors),out _);
        Assert.Contains(Failures,Failure=>Failure.Contains(Expected,StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void EvaluateLongPress_RejectsAMarkerFromALongPress(){
        var Counts=Enumerable.Range(0,ClickToMoveVerification.LongPressFrames).Select(static Frame=>Frame>=ClickToMoveVerification.LongPressEndFrame&&Frame<ClickToMoveVerification.LongPressEndFrame+5?41:40).ToArray();
        Assert.Contains(ClickToMoveVerification.EvaluateLongPress(LongPressReport(RenderObjects:Counts),out _),Failure=>Failure.Contains("long press showed a marker",StringComparison.Ordinal));
    }
    [Fact]
    public void EvaluateLongPress_RejectsAShortReportAndAWrongSchema(){
        Assert.Contains(ClickToMoveVerification.EvaluateLongPress(Report(Frames:40),out _),Failure=>Failure.Contains("Expected",StringComparison.Ordinal));
        Assert.Contains(ClickToMoveVerification.EvaluateLongPress(JsonDocument.Parse("{\"schema\":\"x\"}").RootElement,out _),Failure=>Failure.Contains("schema",StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_RejectsAClickThatShowsNoMarker(){
        Assert.Contains(ClickToMoveVerification.Evaluate(Report(RenderObjects:Objects(0,0)),out _),Failure=>Failure.Contains("no marker",StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_RejectsAMarkerThatStaysAfterItsLifetime(){
        Assert.Contains(ClickToMoveVerification.Evaluate(Report(RenderObjects:Objects(24,ClickToMoveVerification.Frames)),out _),Failure=>Failure.Contains("did not go away",StringComparison.Ordinal));
    }
    [Fact]
    public void Evaluate_DoesNotCountAMarkerThatWasAlreadyThereBeforeTheClick(){
        // シーンには開始時点からオブジェクトがもう 1 つあった。マーカーとなるのはクリック後の増加だけ。
        Assert.Contains(ClickToMoveVerification.Evaluate(Report(RenderObjects:Objects(0,ClickToMoveVerification.Frames)),out _),Failure=>Failure.Contains("no marker",StringComparison.Ordinal));
    }
    /// <summary>
    /// ADR-0078/M0107：カメラはキャラクターに遅れて追従するため、<c>focusPositions</c>（キャラクターの位置）を含むレポートはその位置で判定する。ここではカメラは開始位置から動かず、すべての検査に失敗する一方、キャラクターは適切な経路を歩く。
    /// </summary>
    [Fact]
    public void EvaluateNavigated_JudgesTheCharactersPositionsWhenTheReportHasThemNotTheLaggingCamera(){
        var Route=new (float R,float F)[] { (0f,0f),(0.9f,4.5f),(1.3f,7.7f),(2.2f,9.0f),(6.0f,8.3f),(6.79f,6.84f) };
        var Focus=new List<float[]?>();
        var Camera=new List<float[]?>();
        for(var Frame=0;Frame<ClickToMoveVerification.Frames;Frame++){
            if(Frame<3){
                Focus.Add(null);
                Camera.Add(null);
                continue;
            }
            var Walked=Math.Clamp((Frame-ClickToMoveVerification.ClickStartFrame+1)/8f,0f,Route.Length-1f);
            var Leg=Math.Min((int)Walked,Route.Length-2);
            var Along=Walked-Leg;
            Focus.Add([Route[Leg].R+((Route[Leg+1].R-Route[Leg].R)*Along),1f,Route[Leg].F+((Route[Leg+1].F-Route[Leg].F)*Along)]);
            Camera.Add([0f,13f,0f]);
        }
        var Json=JsonSerializer.Serialize(new { schema="cge-automation-report/1",frames=ClickToMoveVerification.Frames,d3d12Errors=0,d3d12DebugLayerEnabled=true,audioRequests=12,cameraPositions=Camera,focusPositions=Focus });
        var Failures=ClickToMoveVerification.EvaluateNavigated(JsonDocument.Parse(Json).RootElement,out var Summary);
        Assert.Empty(Failures);
        Assert.Contains("final=(6.79,6.84)",Summary,StringComparison.Ordinal);
    }
}
