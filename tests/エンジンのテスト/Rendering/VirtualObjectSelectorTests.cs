using Xunit;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>
/// VirtualObject の段の選び方（M0133、ADR-0100 決定 6）。Omni の VirtualObject の受入基準と同じ約束を固定する。
/// </summary>
public sealed class VirtualObjectSelectorTests {
    private static readonly float[]Enter=[20f,80f,320f];
    private const uint AllReady=uint.MaxValue;
    [Fact]
    public void MorePixelsNeverLowersTheLevelAndFewerNeverRaisesIt(){
        var Selector=new VirtualObjectSelector(Enter);
        var Level=Selector.Step(1f,0,Started:false,AllReady,out _);
        var Previous=Level;
        for(var Pixels=1f;Pixels<2000f;Pixels*=1.07f){
            Level=Selector.Step(Pixels,Level,Started:true,AllReady,out _);
            Assert.True(Level>=Previous,$"the level fell from {Previous} to {Level} at {Pixels} pixels");
            Previous=Level;
        }
        Assert.Equal(3,Level);
        for(var Pixels=2000f;Pixels>0.5f;Pixels/=1.07f){
            Level=Selector.Step(Pixels,Level,Started:true,AllReady,out _);
            Assert.True(Level<=Previous,$"the level rose from {Previous} to {Level} at {Pixels} pixels");
            Previous=Level;
        }
        Assert.Equal(0,Level);
    }
    [Fact]
    public void SmallSwingsAroundABoundaryDoNotSwitchBackAndForth(){
        var Selector=new VirtualObjectSelector(Enter);
        // 境は 80 画素。h = 1.25 なので、64〜100 の間では今の段のまま。
        var Below=Selector.Step(70f,0,Started:false,AllReady,out _);
        Assert.Equal(1,Below);
        var Transitions=0;
        var Level=Below;
        for(var Step=0;Step<200;Step++){
            var Next=Selector.Step(Step%2==0?92f:68f,Level,Started:true,AllReady,out _);
            if(Next!=Level){ Transitions++; }
            Level=Next;
        }
        Assert.Equal(0,Transitions);
        Assert.Equal(2,Selector.Step(100f,Level,Started:true,AllReady,out _));
        Assert.Equal(1,Selector.Step(63.9f,2,Started:true,AllReady,out _));
    }
    [Fact]
    public void AnUnreadyLevelIsNotChosenUntilItIsReady(){
        var Selector=new VirtualObjectSelector(Enter);
        // 段 2 が準備できていない: 望む段は 2 でも、今の段 1 を描き続ける（望む段は、準備を進めるために返す）。
        var Level=Selector.Step(150f,1,Started:true,ReadyMask:0b0011,out var Desired);
        Assert.Equal(2,Desired);
        Assert.Equal(1,Level);
        Level=Selector.Step(150f,Level,Started:true,ReadyMask:0b0111,out _);
        Assert.Equal(2,Level);
        // 段 2 を飛ばして、準備できた段 3 へ上がれる。
        Assert.Equal(3,Selector.Step(1000f,1,Started:true,ReadyMask:0b1011,out _));
    }
    [Fact]
    public void ARapidApproachJumpsToTheHighestReadyLevelAtOnce(){
        var Selector=new VirtualObjectSelector(Enter);
        Assert.Equal(0,Selector.Step(5f,0,Started:false,AllReady,out _));
        Assert.Equal(3,Selector.Step(5000f,0,Started:true,AllReady,out _));
        // 初回はヒステリシスなし: 境の少し上なら、その段から始める。
        Assert.Equal(1,Selector.Step(21f,0,Started:false,AllReady,out _));
    }
    [Fact]
    public void GoingDownUsesTheLowestReadyLevelFromTheDesiredOne(){
        var Selector=new VirtualObjectSelector(Enter);
        // 段 3 から 1 画素へ: 望む段は 0（いつも準備できている）。
        Assert.Equal(0,Selector.Step(1f,3,Started:true,ReadyMask:0b1000,out var Desired));
        Assert.Equal(0,Desired);
        // 望む段 1 が準備できていなければ、その上の準備できた段（2）。
        Assert.Equal(2,Selector.Step(30f,3,Started:true,ReadyMask:0b1100,out Desired));
        Assert.Equal(1,Desired);
        // どれも準備できていなければ今の段のまま。
        Assert.Equal(3,Selector.Step(30f,3,Started:true,ReadyMask:0b1000,out _));
    }
    [Fact]
    public void InvalidSettingsAndPixelsAreRejectedOrClamped(){
        Assert.Throws<ArgumentException>(()=>new VirtualObjectSelector([20f,10f]));
        Assert.Throws<ArgumentException>(()=>new VirtualObjectSelector([0f,10f]));
        Assert.Throws<ArgumentException>(()=>new VirtualObjectSelector([float.NaN]));
        Assert.Throws<ArgumentException>(()=>new VirtualObjectSelector(Enter,0.9f));
        Assert.Throws<ArgumentException>(()=>new VirtualObjectSelector(Enumerable.Range(1,VirtualObjectSelector.MaximumLevels).Select(static Value=>(float)Value).ToArray()));
        var Selector=new VirtualObjectSelector(Enter);
        Assert.Equal(4,Selector.LevelCount);
        Assert.Equal(0,Selector.Ideal(float.NaN));
        Assert.Equal(0,Selector.Ideal(-5f));
        Assert.Equal(0,Selector.Select(float.NaN,2));
        Assert.Equal(3,Selector.Ideal(float.PositiveInfinity));
        // 段の無い物（閾値なし）は、いつも段 0。
        Assert.Equal(0,new VirtualObjectSelector([]).Step(1e6f,0,Started:false,AllReady,out _));
    }
}
