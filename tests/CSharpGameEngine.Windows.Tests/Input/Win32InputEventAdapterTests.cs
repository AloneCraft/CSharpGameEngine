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
        Assert.Equal(InputControlKind.Axis2D,Events[0].Control.Kind);
        Assert.True(Win32InputEventAdapter.TimestampFrequency>0);
    }
}
