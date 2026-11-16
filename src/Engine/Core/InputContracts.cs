namespace CSharpGameEngine.Core;
public readonly record struct MonotonicTimestamp(long Ticks):IComparable<MonotonicTimestamp> {
    public int CompareTo(MonotonicTimestamp Other)=>this.Ticks.CompareTo(Other.Ticks);
    public static bool operator<(MonotonicTimestamp Left,MonotonicTimestamp Right)=>Left.Ticks<Right.Ticks;
    public static bool operator>(MonotonicTimestamp Left,MonotonicTimestamp Right)=>Left.Ticks>Right.Ticks;
    public static bool operator<=(MonotonicTimestamp Left,MonotonicTimestamp Right)=>Left.Ticks<=Right.Ticks;
    public static bool operator>=(MonotonicTimestamp Left,MonotonicTimestamp Right)=>Left.Ticks>=Right.Ticks;
}
public readonly record struct WindowId(StableId Value) {
    public static WindowId Primary { get;}=new(StableId.Parse("e93faabe-6c41-51c1-9dbc-2b388f49c947"));
    public bool IsValid=>this.Value.IsValid;
    public override string ToString()=>this.Value.ToString();
}
public readonly record struct InputDeviceId(StableId Value) {
    public bool IsValid=>this.Value.IsValid;
    public override string ToString()=>this.Value.ToString();
}
public readonly record struct InputContextId(StableId Value) {
    public bool IsValid=>this.Value.IsValid;
    public override string ToString()=>this.Value.ToString();
}
public readonly record struct InputActionId(StableId Value) {
    public bool IsValid=>this.Value.IsValid;
    public override string ToString()=>this.Value.ToString();
}
public enum InputDeviceKind { Keyboard,Mouse,Gamepad }
/// <summary>
/// 相対値（マウスの移動量、ホイールなど）は、保持値ではなくイベントごとの増分を報告する。
/// 入力サーバーは処理対象フレーム中に値を累積し、保持状態としては記憶しない。
/// </summary>
public enum InputControlKind { Button,Axis1D,Axis2D,RelativeAxis1D,RelativeAxis2D }
public readonly record struct InputControl(InputDeviceKind DeviceKind,string Code,InputControlKind Kind=InputControlKind.Button) {
    public bool IsValid=>!string.IsNullOrWhiteSpace(this.Code);
}
public readonly record struct InputValue(float X,float Y) {
    public static InputValue Zero=>default;
    public static InputValue Pressed=>new(1,0);
    public bool IsZero=>this.X==0&&this.Y==0;
}
public enum InputEventKind { Connected,Disconnected,Pressed,Released,ValueChanged }
public enum InputReleaseReason { None,PhysicalRelease,FocusLost,CaptureReleased,DeviceRemoved,WindowDestroyed,ContextChanged,Rebound }
public sealed record InputEvent(
    MonotonicTimestamp Timestamp,
    WindowId WindowId,
    InputDeviceId DeviceId,
    ulong Sequence,
    InputControl Control,
    InputEventKind Kind,
    InputValue Value,
    InputReleaseReason ReleaseReason=InputReleaseReason.None);
public enum InputActionPhase { Started,Performed,Canceled }
public sealed record InputActionValue(
    MonotonicTimestamp Timestamp,
    WindowId WindowId,
    InputDeviceId DeviceId,
    ulong Sequence,
    InputContextId ContextId,
    InputActionId ActionId,
    InputActionPhase Phase,
    InputValue RawValue,
    InputValue Value,
    InputReleaseReason ReleaseReason=InputReleaseReason.None);
