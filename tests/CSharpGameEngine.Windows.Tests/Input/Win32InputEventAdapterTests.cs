using CSharpGameEngine.Core;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Windows.Tests.Input;
public sealed class Win32InputEventAdapterTests {
    [Fact]
    public void RepeatedKey_IsCollapsedAndFocusLossReleases(){
        long Tick=0;
        var Adapter=new Win32InputEventAdapter(WindowId.Primary,()=>new MonotonicTimestamp(++Tick));
        Adapter.RecordKey(0x50,true);
        Adapter.RecordKey(0x50,true);
        Adapter.RecordFocusLost();
        var Events=Adapter.Drain();
        Assert.Collection(Events,
            EventValue=>Assert.Equal(InputEventKind.Pressed,EventValue.Kind),
            EventValue=>{Assert.Equal(InputEventKind.Released,EventValue.Kind);Assert.Equal(InputReleaseReason.FocusLost,EventValue.ReleaseReason);});
        Assert.True(Events[0].Timestamp<Events[1].Timestamp);
        Assert.Equal("P",Events[0].Control.Code);
    }
    [Fact]
    public void Gamepad_EmitsConnectionButtonAndStickChangesAndReleasesOnDisconnect(){
        var Adapter=new Win32InputEventAdapter(WindowId.Primary,()=>new MonotonicTimestamp(1));
        Adapter.RecordGamepad(new GamepadSnapshot(true,0x1000,0f,0f,new System.Numerics.Vector2(0.5f,-0.25f),default));
        var Connected=Adapter.Drain();
        Assert.Collection(Connected,
            EventValue=>Assert.Equal(InputEventKind.Connected,EventValue.Kind),
            EventValue=>{Assert.Equal(InputEventKind.Pressed,EventValue.Kind);Assert.Equal("Gamepad_FaceButton_Bottom",EventValue.Control.Code);},
            EventValue=>{Assert.Equal(Win32InputEventAdapter.GamepadLeftStick,EventValue.Control);Assert.Equal(new InputValue(0.5f,-0.25f),EventValue.Value);});
        Assert.All(Connected,EventValue=>Assert.Equal(Adapter.GamepadDevice,EventValue.DeviceId));
        Adapter.RecordGamepad(new GamepadSnapshot(true,0x1000,0f,0f,new System.Numerics.Vector2(0.5f,-0.25f),default));
        Assert.Empty(Adapter.Drain());
        Adapter.RecordGamepad(default);
        var Disconnected=Adapter.Drain();
        Assert.Contains(Disconnected,EventValue=>EventValue.Control.Code=="Gamepad_FaceButton_Bottom"&&EventValue.Kind==InputEventKind.Released&&EventValue.ReleaseReason==InputReleaseReason.DeviceRemoved);
        Assert.Equal(InputEventKind.Disconnected,Disconnected[^1].Kind);
    }
    [Fact]
    public void CaptureAndDeviceRemoval_ReleaseOnlyOwnedState(){
        var Adapter=new Win32InputEventAdapter(WindowId.Primary,()=>new MonotonicTimestamp(1));
        Adapter.RecordKey(0x31,true);
        Adapter.RecordMouseButton("Left",true);
        _=Adapter.Drain();
        Adapter.RecordCaptureReleased();
        var Capture=Assert.Single(Adapter.Drain());
        Assert.Equal(InputReleaseReason.CaptureReleased,Capture.ReleaseReason);
        Adapter.RecordDeviceRemoved();
        var Removed=Adapter.Drain();
        Assert.Contains(Removed,EventValue=>EventValue.Control.Code=="1"&&EventValue.Kind==InputEventKind.Released);
        Assert.Equal(2,Removed.Count(EventValue=>EventValue.Kind==InputEventKind.Disconnected));
    }
    [Fact]
    public void MouseAxes_AreTimestampedAndSequenced(){
        var Adapter=new Win32InputEventAdapter(WindowId.Primary,()=>new MonotonicTimestamp(9));
        Adapter.RecordMouseDelta(4,-2);
        Adapter.RecordMouseWheel(1);
        var Events=Adapter.Drain();
        Assert.Equal([1UL,2UL],Events.Select(static EventValue=>EventValue.Sequence));
        Assert.Equal(InputControlKind.RelativeAxis2D,Events[0].Control.Kind);
        Assert.True(Win32InputEventAdapter.TimestampFrequency>0);
    }
}
