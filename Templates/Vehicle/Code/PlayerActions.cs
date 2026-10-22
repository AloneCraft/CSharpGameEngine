using CSharpGameEngine.Input;
namespace Vehicle;
/// <summary>車の操作。アナログ軸(Axis)は-1〜1または0〜1に正規化され、キーボードでは押している間1になる。</summary>
[InputActions]
public partial struct PlayerActions {
    [Keys(Key.W,Key.Up)]
    [Gamepad(Trigger.Right)]
    public Axis Throttle;
    [Keys(Key.S,Key.Down)]
    [Gamepad(Trigger.Left)]
    public Axis Brake;
    [Keys(Negative:Key.A,Positive:Key.D)]
    [Keys(Negative:Key.Left,Positive:Key.Right)]
    [Gamepad(Pad.LeftStick,Component.X)]
    public Axis Steering;
    [Keys(Key.Space)]
    [Gamepad(Pad.West)]
    public Button Handbrake;
    [Keys(Key.R)]
    public Button Reset;
    [Keys(Key.C)]
    [Gamepad(Pad.North)]
    public Button ToggleCamera;
    [Mouse(MouseAxis.Delta)]
    [Gamepad(Pad.RightStick)]
    public Look Look;
}
