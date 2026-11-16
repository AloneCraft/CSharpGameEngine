using CSharpGameEngine.Input;
namespace CgeDestructionSample;
/// <summary>ThirdPerson テンプレートの操作（UE の IMC_Default と IMC_MouseLook）に、サンプル独自の操作を2つ加える。ボールを撃ち、爆発を起こす。</summary>
[InputActions]
public partial struct PlayerActions {
    /// <summary>移動：WASD、矢印キー、左スティック。</summary>
    [Keys(Up:Key.W,Down:Key.S,Left:Key.A,Right:Key.D)]
    [Keys(Up:Key.Up,Down:Key.Down,Left:Key.Left,Right:Key.Right)]
    [Gamepad(Pad.LeftStick)]
    public Stick Move;
    /// <summary>視点操作：マウスと右スティック。</summary>
    [Mouse(MouseAxis.Delta)]
    [Gamepad(Pad.RightStick)]
    public Look Look;
    /// <summary>ジャンプ：スペースキーと南ボタン。</summary>
    [Keys(Key.Space)]
    [Gamepad(Pad.South)]
    public Button Jump;
    /// <summary>カメラの向いている方向へボールを撃つ：Fキーと東ボタン。</summary>
    [Keys(Key.F)]
    [Gamepad(Pad.East)]
    public Button Fire;
    /// <summary>キャラクターの前方に爆発（ひずみ場と力場）を発生させる：Eキーと西ボタン。</summary>
    [Keys(Key.E)]
    [Gamepad(Pad.West)]
    public Button Explode;
}
