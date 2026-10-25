using CSharpGameEngine.Input;
namespace CgeDestructionSample;
/// <summary>The ThirdPerson template's controls (UE's IMC_Default and IMC_MouseLook) and two of the sample's own: shoot a ball, set off an explosion.</summary>
[InputActions]
public partial struct PlayerActions {
    /// <summary>Move: WASD, the arrow keys and the left stick.</summary>
    [Keys(Up:Key.W,Down:Key.S,Left:Key.A,Right:Key.D)]
    [Keys(Up:Key.Up,Down:Key.Down,Left:Key.Left,Right:Key.Right)]
    [Gamepad(Pad.LeftStick)]
    public Stick Move;
    /// <summary>Look: the mouse and the right stick.</summary>
    [Mouse(MouseAxis.Delta)]
    [Gamepad(Pad.RightStick)]
    public Look Look;
    /// <summary>Jump: space and the south button.</summary>
    [Keys(Key.Space)]
    [Gamepad(Pad.South)]
    public Button Jump;
    /// <summary>Shoot a ball the way the camera faces: F and the east button.</summary>
    [Keys(Key.F)]
    [Gamepad(Pad.East)]
    public Button Fire;
    /// <summary>Set off an explosion (a strain field and a force field) in front of the character: E and the west button.</summary>
    [Keys(Key.E)]
    [Gamepad(Pad.West)]
    public Button Explode;
}
