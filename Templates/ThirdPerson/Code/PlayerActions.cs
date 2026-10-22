using CSharpGameEngine.Input;
namespace ThirdPerson;
/// <summary>
/// プレイヤーの操作の宣言。これだけで入力マップ・ID・据え付けが生成される。
/// 値は毎フレーム、このstructを引数に取ったSystemへシングルトンとして渡される。
/// </summary>
[InputActions]
public partial struct PlayerActions {
    /// <summary>移動。キー割り当ての(Up,Down,Left,Right)はWASDと矢印の両方。</summary>
    [Keys(Up:Key.W,Down:Key.S,Left:Key.A,Right:Key.D)]
    [Keys(Up:Key.Up,Down:Key.Down,Left:Key.Left,Right:Key.Right)]
    [Gamepad(Pad.LeftStick)]
    public Stick Move;
    /// <summary>視点。値は「このフレームに回す量(turns)」で、マウスとスティックの単位差はエンジンが吸収する。</summary>
    [Mouse(MouseAxis.Delta)]
    [Gamepad(Pad.RightStick)]
    public Look Look;
    /// <summary>ジャンプ。<c>Pressed</c>は押したフレームだけtrue。</summary>
    [Keys(Key.Space)]
    [Gamepad(Pad.South)]
    public Button Jump;
}
