using CSharpGameEngine.Input;
namespace CgeThirdPerson;
/// <summary>
/// UEのEnhanced Input(IMC_Default、IMC_MouseLook)に相当する操作の宣言。入力マップ・ID・据え付けはSource Generatorが作り、
/// 値は毎フレームこの構造体を<c>in</c>で受けたSystemへ渡される。
/// </summary>
[InputActions]
public partial struct PlayerActions {
    /// <summary>移動。WASDと矢印キー、左スティック。</summary>
    [Keys(Up:Key.W,Down:Key.S,Left:Key.A,Right:Key.D)]
    [Keys(Up:Key.Up,Down:Key.Down,Left:Key.Left,Right:Key.Right)]
    [Gamepad(Pad.LeftStick)]
    public Stick Move;
    /// <summary>視点。マウスの移動量と右スティック。値はこのフレームに回す量(turns)。</summary>
    [Mouse(MouseAxis.Delta)]
    [Gamepad(Pad.RightStick)]
    public Look Look;
    /// <summary>ジャンプ。スペースキーとゲームパッドの南ボタン。</summary>
    [Keys(Key.Space)]
    [Gamepad(Pad.South)]
    public Button Jump;
}
