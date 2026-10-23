using CSharpGameEngine.Input;
namespace Vehicle;
/// <summary>車の操作。アナログ軸(Axis)は-1〜1に丸められ、キーでは押している間±1になる(M0108で実装した書き方)。</summary>
[InputActions]
public partial struct PlayerActions {
    [Keys(Key.W,Key.Up)]
    [Gamepad(Pad.RightTrigger)]
    [Gamepad(Pad.West)]
    public Axis Throttle;
    [Keys(Key.S,Key.Down)]
    [Gamepad(Pad.LeftTrigger)]
    [Gamepad(Pad.South)]
    public Axis Brake;
    [Keys(Key.D,Key.Right)]
    [NegativeKeys(Key.A,Key.Left)]
    [Gamepad(Pad.LeftStick,StickAxis.X)]
    public Axis Steering;
    [Keys(Key.Space)]
    [Gamepad(Pad.LeftShoulder)]
    public Button Handbrake;
    [Keys(Key.Backspace)]
    public Button Reset;
    [Keys(Key.Tab)]
    public Button ToggleCamera;
    [Mouse(MouseAxis.DeltaX)]
    [Gamepad(Pad.RightStick,StickAxis.X)]
    public Look LookAround;
}
