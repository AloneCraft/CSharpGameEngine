using CSharpGameEngine.Input;
namespace CgeVehicle;
/// <summary>
/// UEのEnhanced Input(IA_Throttle、IA_Brake、IA_Steering、IA_Handbrake)に相当する操作の宣言。入力マップ・ID・据え付けは
/// Source Generatorが作り、値は毎フレーム、この構造体を<c>in</c>で受けたSystemへ渡される。
/// </summary>
[InputActions]
public partial struct PlayerActions {
    /// <summary>運転。Yがアクセル(上)とブレーキ・後退(下)、Xがハンドル(右が正)。WASDと矢印キー、左スティック。</summary>
    [Keys(Up:Key.W,Down:Key.S,Left:Key.A,Right:Key.D)]
    [Keys(Up:Key.Up,Down:Key.Down,Left:Key.Left,Right:Key.Right)]
    [Gamepad(Pad.LeftStick)]
    public Stick Drive;
    /// <summary>ハンドブレーキ。スペースキーとゲームパッドの南ボタン。</summary>
    [Keys(Key.Space)]
    [Gamepad(Pad.South)]
    public Button Handbrake;
}
