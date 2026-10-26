using CSharpGameEngine.Core;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Input;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Windows.Tests.Input;
/// <summary>
/// M0108：<c>Button.HeldSeconds</c>が参照する押下時間と、<c>[InputActions]</c>が生成する1軸バインディング（<c>Axis</c>、スティックまたはマウス移動量の1軸）。
/// ここではジェネレーターが出力する形でバインディングを構築するため、ゲームをコンパイルせず、生成コードが依存するサーバー動作を固定できる。
/// </summary>
public sealed class InputActionBindingTests {
    private static readonly WindowId Window=new(StableId.Parse("35177e2f-4d15-4e5e-8a79-cc7cc65ad2a0"));
    private static readonly InputDeviceId Device=new(StableId.Parse("568d0766-0f20-48a0-b632-cd1ab6d07778"));
    private static readonly InputContextId Context=new(StableId.Parse("29a45747-ae2b-49dc-8926-6f03784d0d48"));
    private static readonly InputActionId Hold=new(StableId.Parse("b22ac9df-06c5-41ca-9b89-dbca9d4c8e26"));
    private static readonly InputActionId Other=new(StableId.Parse("fd58d501-7e71-4751-b023-e666d664b0bb"));
    private static readonly InputActionId Throttle=new(StableId.Parse("0c7a8ef4-3b1d-4d3e-8c0a-6c9a8f4b1a01"));
    private static readonly InputActionId Steering=new(StableId.Parse("0c7a8ef4-3b1d-4d3e-8c0a-6c9a8f4b1a02"));
    private static readonly InputActionId LookX=new(StableId.Parse("0c7a8ef4-3b1d-4d3e-8c0a-6c9a8f4b1a03"));
    private static readonly InputActionId LookY=new(StableId.Parse("0c7a8ef4-3b1d-4d3e-8c0a-6c9a8f4b1a04"));
    private static readonly InputControl Space=new(InputDeviceKind.Keyboard,"SpaceBar",InputControlKind.Button);
    private static readonly InputControl KeyA=new(InputDeviceKind.Keyboard,"A",InputControlKind.Button);
    private static readonly InputControl KeyD=new(InputDeviceKind.Keyboard,"D",InputControlKind.Button);
    private static readonly InputControl MouseDelta=new(InputDeviceKind.Mouse,"Delta",InputControlKind.RelativeAxis2D);
    private static InputServer ServerOf(params InputBinding[]Bindings){
        var Server=new InputServer(new InputActionMap([new InputContext(Context,"Test",0,Bindings)]));
        Server.SetContextActive(Context,true,new MonotonicTimestamp(1));
        return Server;
    }
    private static InputEvent Event(long Tick,ulong Sequence,InputControl Control,InputEventKind Kind,InputValue Value)=>new(new MonotonicTimestamp(Tick),Window,Device,Sequence,Control,Kind,Value);
    private static void Frame(InputServer Server,params InputEvent[]Events)=>Server.ProcessEvents(Events);
    [Fact]
    public void AHold_ReadsZeroOnThePressFrameThenGrowsByEachFramesDeltaAndTheReleaseFrameReadsTheWholeTime(){
        var Server=ServerOf();
        Frame(Server);
        Assert.Equal(0f,Server.TrackHold(Hold,true,true,false,0.125f));
        Frame(Server);
        Assert.Equal(0.125f,Server.TrackHold(Hold,true,false,false,0.125f));
        Frame(Server);
        Assert.Equal(0.25f,Server.TrackHold(Hold,true,false,false,0.125f));
        Frame(Server);
        Assert.Equal(0.375f,Server.TrackHold(Hold,false,false,true,0.125f));
        Frame(Server);
        Assert.Equal(0f,Server.TrackHold(Hold,false,false,false,0.125f));
    }
    [Fact]
    public void AHoldOfExactlyTheThreshold_ReadsExactlyTheThresholdOnRelease(){
        // 0.5秒はUEのPressedThreshold。押下フレームの後に0.125秒のフレームが4回続くため、解放フレームは0.5を返し、「< 0.5」はfalseとなる。
        var Server=ServerOf();
        Frame(Server);
        Server.TrackHold(Hold,true,true,false,0.125f);
        for(var Index=0;Index<3;Index++){
            Frame(Server);
            Server.TrackHold(Hold,true,false,false,0.125f);
        }
        Frame(Server);
        var Release=Server.TrackHold(Hold,false,false,true,0.125f);
        Assert.Equal(0.5f,Release);
        Assert.False(Release<0.5f);
    }
    [Fact]
    public void ReadingTwiceInOneFrame_DoesNotAdvanceTheClock(){
        // 複数のシステムが同じ[InputActions]構造体を受け取り、それぞれのディスパッチで再び読み込む。
        var Server=ServerOf();
        Frame(Server);
        Server.TrackHold(Hold,true,true,false,0.1f);
        Frame(Server);
        var First=Server.TrackHold(Hold,true,false,false,0.1f);
        var Again=Server.TrackHold(Hold,true,false,false,0.1f);
        Assert.Equal(0.1f,First,6);
        Assert.Equal(First,Again);
        Frame(Server);
        Assert.Equal(0.2f,Server.TrackHold(Hold,true,false,false,0.1f),6);
    }
    [Fact]
    public void APressAndReleaseInsideOneFrame_ReadsZero(){
        var Server=ServerOf();
        Frame(Server);
        Assert.Equal(0f,Server.TrackHold(Hold,false,true,true,0.1f));
        Frame(Server);
        Assert.Equal(0f,Server.TrackHold(Hold,false,false,false,0.1f));
    }
    [Fact]
    public void ASecondPress_StartsFromZeroAgain(){
        var Server=ServerOf();
        Frame(Server);
        Server.TrackHold(Hold,true,true,false,0.1f);
        Frame(Server);
        Server.TrackHold(Hold,true,false,false,0.1f);
        Frame(Server);
        Server.TrackHold(Hold,false,false,true,0.1f);
        Frame(Server);
        Assert.Equal(0f,Server.TrackHold(Hold,true,true,false,0.1f));
        Frame(Server);
        Assert.Equal(0.1f,Server.TrackHold(Hold,true,false,false,0.1f),6);
    }
    [Fact]
    public void ARepressInTheReleaseFrame_ReportsTheOldHoldAndStartsANewOne(){
        var Server=ServerOf();
        Frame(Server);
        Server.TrackHold(Hold,true,true,false,0.25f);
        Frame(Server);
        Server.TrackHold(Hold,true,false,false,0.25f);
        Frame(Server);
        Assert.Equal(0.5f,Server.TrackHold(Hold,true,true,true,0.25f));
        Frame(Server);
        Assert.Equal(0.25f,Server.TrackHold(Hold,true,false,false,0.25f));
    }
    [Fact]
    public void AHeldButtonWhoseStartWasNeverSeen_StartsCountingFromTheFirstFrameItIsSeen(){
        // キーを押したままコンテキストを再度有効にする。HeldにはなるがPressedにはならない。
        var Server=ServerOf();
        Frame(Server);
        Assert.Equal(0f,Server.TrackHold(Hold,true,false,false,0.1f));
        Frame(Server);
        Assert.Equal(0.1f,Server.TrackHold(Hold,true,false,false,0.1f),6);
    }
    [Theory]
    [InlineData(-0.5f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(0f)]
    public void ANegativeOrNonFiniteDelta_CountsAsZero(float Delta){
        var Server=ServerOf();
        Frame(Server);
        Server.TrackHold(Hold,true,true,false,Delta);
        Frame(Server);
        Assert.Equal(0f,Server.TrackHold(Hold,true,false,false,Delta));
        Frame(Server);
        Assert.Equal(0f,Server.TrackHold(Hold,false,false,true,Delta));
    }
    [Fact]
    public void TwoActions_KeepTheirOwnClocks(){
        var Server=ServerOf();
        Frame(Server);
        Server.TrackHold(Hold,true,true,false,0.1f);
        Frame(Server);
        Server.TrackHold(Hold,true,false,false,0.1f);
        Assert.Equal(0f,Server.TrackHold(Other,true,true,false,0.1f));
        Frame(Server);
        Assert.Equal(0.2f,Server.TrackHold(Hold,true,false,false,0.1f),6);
        Assert.Equal(0.1f,Server.TrackHold(Other,true,false,false,0.1f),6);
    }
    [Fact]
    public void TheFrameNumber_CountsProcessedFrames(){
        var Server=ServerOf();
        Assert.Equal(0UL,Server.FrameNumber);
        Frame(Server);
        Frame(Server);
        Assert.Equal(2UL,Server.FrameNumber);
    }
    [Fact]
    public void ARealKeyDrivesTheHoldThroughTheActionPhases(){
        // 生成されたReadが計算するのと同じフラグ。StartedでPressed、CanceledでReleased、アクション値が0以外の間はHeld。
        var Server=ServerOf(new InputBinding(Hold,Space));
        var Seen=new List<float>();
        InputEvent[][]Frames=[
            [Event(1,1,Space,InputEventKind.Pressed,InputValue.Pressed)],
            [],
            [],
            [Event(2,2,Space,InputEventKind.Released,InputValue.Zero)],
            [],
        ];
        foreach(var Events in Frames){
            Server.ProcessEvents(Events);
            var Pressed=Server.CurrentFrame.Any(Value=>Value.ActionId==Hold&&Value.Phase==InputActionPhase.Started);
            var Released=Server.CurrentFrame.Any(Value=>Value.ActionId==Hold&&Value.Phase==InputActionPhase.Canceled);
            var Held=!Server.ReadValue(Hold).IsZero;
            Seen.Add(Server.TrackHold(Hold,Held,Pressed,Released,0.125f));
        }
        Assert.Equal([0f,0.125f,0.25f,0.375f,0f],Seen);
    }
    private static InputControl Trigger=>Win32InputEventAdapter.GamepadRightTrigger;
    [Theory]
    [InlineData(0f,0f)]
    [InlineData(0.05f,0f)]
    [InlineData(0.1f,0f)]
    [InlineData(0.55f,0.5f)]
    [InlineData(1f,1f)]
    public void ATriggerWithADeadZone_ReadsZeroInsideItAndRescalesTheRest(float Raw,float Expected){
        var Server=ServerOf(new InputBinding(Throttle,Trigger,DeadZone:0.1f));
        Server.ProcessEvents([Event(2,1,Trigger,InputEventKind.ValueChanged,new InputValue(Raw,0))]);
        Assert.Equal(Expected,Server.ReadValue(Throttle).X,5);
    }
    [Fact]
    public void AScaleAfterTheDeadZone_AndAnInversion_ApplyInThatOrder(){
        var Scaled=ServerOf(new InputBinding(Throttle,Trigger,DeadZone:0.1f,Scale:2f));
        Scaled.ProcessEvents([Event(2,1,Trigger,InputEventKind.ValueChanged,new InputValue(0.55f,0))]);
        Assert.Equal(1f,Scaled.ReadValue(Throttle).X,5);
        var Inverted=ServerOf(new InputBinding(Throttle,Trigger,DeadZone:0.1f,Modifiers:[new InputNegateModifier()]));
        Inverted.ProcessEvents([Event(2,1,Trigger,InputEventKind.ValueChanged,new InputValue(1f,0))]);
        Assert.Equal(-1f,Inverted.ReadValue(Throttle).X);
    }
    [Fact]
    public void ASteeringAxisFromKeys_IsPlusOneMinusOneAndCancelsWhenBothAreDown(){
        var Server=ServerOf(
            new InputBinding(Steering,KeyD),
            new InputBinding(Steering,KeyA,Modifiers:[new InputNegateModifier()]));
        Server.ProcessEvents([Event(2,1,KeyD,InputEventKind.Pressed,InputValue.Pressed)]);
        Assert.Equal(1f,Server.ReadValue(Steering).X);
        Server.ProcessEvents([Event(3,2,KeyA,InputEventKind.Pressed,InputValue.Pressed)]);
        Assert.Equal(0f,Server.ReadValue(Steering).X);
        Server.ProcessEvents([Event(4,3,KeyD,InputEventKind.Released,InputValue.Zero)]);
        Assert.Equal(-1f,Server.ReadValue(Steering).X);
        Assert.Equal(Axis.FromSum(-1f).Value,Server.ReadValue(Steering).X);
    }
    [Fact]
    public void OneAxisOfAStickIsItsOwnControlWithItsOwnDeadZone(){
        // Gamepad_LeftXはもう一方の軸に引きずられない。少しXノイズを含みながら上へ押したスティックでは方向を変えない。
        var Adapter=new Win32InputEventAdapter(WindowId.Primary,()=>new MonotonicTimestamp(1));
        var Server=ServerOf(new InputBinding(Steering,Win32InputEventAdapter.GamepadLeftX,DeadZone:0.2f));
        Adapter.RecordGamepad(new GamepadSnapshot(true,0,0f,0f,new System.Numerics.Vector2(0.1f,0.95f),default));
        Server.ProcessEvents(Adapter.Drain());
        Assert.Equal(0f,Server.ReadValue(Steering).X);
        Adapter.RecordGamepad(new GamepadSnapshot(true,0,0f,0f,new System.Numerics.Vector2(0.6f,0.95f),default));
        Server.ProcessEvents(Adapter.Drain());
        Assert.Equal(0.5f,Server.ReadValue(Steering).X,5);
        Adapter.RecordGamepad(new GamepadSnapshot(true,0,0f,0f,new System.Numerics.Vector2(0f,0.95f),default));
        Server.ProcessEvents(Adapter.Drain());
        Assert.Equal(0f,Server.ReadValue(Steering).X);
    }
    [Fact]
    public void TheYAxisOfAStickSwizzledIntoALookMovesTheLooksY(){
        var Server=ServerOf(new InputBinding(LookY,Win32InputEventAdapter.GamepadRightY,DeadZone:0.2f,Modifiers:[new InputSwizzleModifier()]));
        var Adapter=new Win32InputEventAdapter(WindowId.Primary,()=>new MonotonicTimestamp(1));
        Adapter.RecordGamepad(new GamepadSnapshot(true,0,0f,0f,default,new System.Numerics.Vector2(0.9f,0.6f)));
        Server.ProcessEvents(Adapter.Drain());
        var Value=Server.ReadValue(LookY);
        Assert.Equal(0f,Value.X);
        Assert.Equal(0.5f,Value.Y,5);
    }
    [Fact]
    public void TheMouseDeltaMask_KeepsOneAxisAndTheMouseYIsStillNegatedForLookUp(){
        var XOnly=ServerOf(new InputBinding(LookX,MouseDelta,Modifiers:[new InputMaskModifier(Y:true)]));
        XOnly.ProcessEvents([Event(2,1,MouseDelta,InputEventKind.ValueChanged,new InputValue(7,-3))]);
        Assert.Equal(new InputValue(7,0),XOnly.ReadValue(LookX));
        var YOnly=ServerOf(new InputBinding(LookY,MouseDelta,Modifiers:[new InputMaskModifier(X:true),new InputNegateModifier(X:false,Y:true)]));
        YOnly.ProcessEvents([Event(2,1,MouseDelta,InputEventKind.ValueChanged,new InputValue(7,-3))]);
        Assert.Equal(new InputValue(0,3),YOnly.ReadValue(LookY));
        // マウス移動がないフレームでは、状態を残さない。
        YOnly.ProcessEvents([]);
        Assert.Equal(InputValue.Zero,YOnly.ReadValue(LookY));
    }
    [Fact]
    public void TheMaskModifier_ZeroesTheSelectedAxesOnly(){
        Assert.Equal(new InputValue(0,2),new InputMaskModifier(X:true).Apply(new InputValue(1,2)));
        Assert.Equal(new InputValue(1,0),new InputMaskModifier(Y:true).Apply(new InputValue(1,2)));
        Assert.Equal(new InputValue(1,2),new InputMaskModifier().Apply(new InputValue(1,2)));
        Assert.Equal(InputValue.Zero,new InputMaskModifier(true,true).Apply(new InputValue(1,2)));
    }
    [Fact]
    public void AStickMove_AlsoReportsEachAxisAsItsOwnControl(){
        var Adapter=new Win32InputEventAdapter(WindowId.Primary,()=>new MonotonicTimestamp(1));
        Adapter.RecordGamepad(new GamepadSnapshot(true,0,0f,0f,new System.Numerics.Vector2(0.5f,0f),default));
        var Events=Adapter.Drain();
        Assert.Contains(Events,Value=>Value.Control==Win32InputEventAdapter.GamepadLeftStick);
        Assert.Contains(Events,Value=>Value.Control==Win32InputEventAdapter.GamepadLeftX&&Value.Value==new InputValue(0.5f,0));
        Assert.DoesNotContain(Events,Value=>Value.Control==Win32InputEventAdapter.GamepadLeftY);
        Adapter.RecordGamepad(new GamepadSnapshot(true,0,0f,0f,new System.Numerics.Vector2(0.5f,-1f),new System.Numerics.Vector2(0f,0.25f)));
        Events=Adapter.Drain();
        Assert.DoesNotContain(Events,Value=>Value.Control==Win32InputEventAdapter.GamepadLeftX);
        Assert.Contains(Events,Value=>Value.Control==Win32InputEventAdapter.GamepadLeftY&&Value.Value==new InputValue(-1f,0));
        Assert.Contains(Events,Value=>Value.Control==Win32InputEventAdapter.GamepadRightY&&Value.Value==new InputValue(0.25f,0));
    }
    [Fact]
    public void AScriptedGamepad_ConnectsOnTheFirstCallAndKeepsWhatEarlierCallsSet(){
        var Adapter=new Win32InputEventAdapter(WindowId.Primary,()=>new MonotonicTimestamp(1));
        Adapter.InjectGamepad("GamepadRightTrigger",0.75f);
        var First=Adapter.Drain();
        Assert.Contains(First,Value=>Value.Kind==InputEventKind.Connected);
        Assert.Contains(First,Value=>Value.Control==Win32InputEventAdapter.GamepadRightTrigger&&Value.Value==new InputValue(0.75f,0));
        Adapter.InjectGamepad("GamepadLeftX",-0.5f);
        Adapter.InjectGamepad("GamepadSouth",1f);
        var Second=Adapter.Drain();
        Assert.DoesNotContain(Second,Value=>Value.Control==Win32InputEventAdapter.GamepadRightTrigger);
        Assert.Contains(Second,Value=>Value.Control==Win32InputEventAdapter.GamepadLeftX&&Value.Value==new InputValue(-0.5f,0));
        Assert.Contains(Second,Value=>Value.Control.Code=="Gamepad_FaceButton_Bottom"&&Value.Kind==InputEventKind.Pressed);
        Adapter.InjectGamepad("GamepadSouth",0f);
        Adapter.InjectGamepad("GamepadRightTrigger",0f);
        var Third=Adapter.Drain();
        Assert.Contains(Third,Value=>Value.Control.Code=="Gamepad_FaceButton_Bottom"&&Value.Kind==InputEventKind.Released);
        Assert.Contains(Third,Value=>Value.Control==Win32InputEventAdapter.GamepadRightTrigger&&Value.Value==InputValue.Zero);
    }
    [Fact]
    public void AScriptedGamepad_TreatsANonFiniteValueAsZeroAndRefusesAnUnknownControl(){
        var Adapter=new Win32InputEventAdapter(WindowId.Primary,()=>new MonotonicTimestamp(1));
        Adapter.InjectGamepad("GamepadLeftTrigger",float.NaN);
        Assert.DoesNotContain(Adapter.Drain(),Value=>Value.Control==Win32InputEventAdapter.GamepadLeftTrigger);
        Assert.Throws<ArgumentException>(()=>Adapter.InjectGamepad("GamepadMiddle",1f));
        Assert.False(Win32InputEventAdapter.IsGamepadInjectionCode("GamepadMiddle"));
        Assert.False(Win32InputEventAdapter.IsGamepadInjectionCode("W"));
        Assert.True(Win32InputEventAdapter.IsGamepadInjectionCode("GamepadRightTrigger"));
        Assert.True(Win32InputEventAdapter.IsGamepadInjectionCode("GamepadRightShoulder"));
    }
}
