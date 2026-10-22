using CSharpGameEngine.Input;
namespace FirstPerson;
/// <summary>一人称の操作。ThirdPersonと同じ宣言で足りる(操作の宣言は視点の置き方に依存しない)。</summary>
[InputActions]
public partial struct PlayerActions {
    [Keys(Up:Key.W,Down:Key.S,Left:Key.A,Right:Key.D)]
    [Keys(Up:Key.Up,Down:Key.Down,Left:Key.Left,Right:Key.Right)]
    [Gamepad(Pad.LeftStick)]
    public Stick Move;
    [Mouse(MouseAxis.Delta)]
    [Gamepad(Pad.RightStick)]
    public Look Look;
    [Keys(Key.Space)]
    [Gamepad(Pad.South)]
    public Button Jump;
}
