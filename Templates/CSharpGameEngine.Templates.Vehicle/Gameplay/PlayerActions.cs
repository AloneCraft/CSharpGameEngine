using CSharpGameEngine.Input;
namespace CgeVehicle;
/// <summary>
/// UEのEnhanced Input(IA_Throttle、IA_Brake、IA_Steering、IA_Handbrake、IA_LookAround)に相当する操作の宣言。入力マップ・ID・据え付けは
/// Source Generatorが作り、値は毎フレーム、この構造体を<c>in</c>で受けたSystemへ渡される。
/// アクセル・ブレーキ・ハンドルは1軸のアナログ値(<see cref="Axis"/>)で、キーは押している間±1、トリガーとスティックは倒した量になる。
/// </summary>
[InputActions]
public partial struct PlayerActions {
    /// <summary>アクセル(0から1)。W、上矢印、ゲームパッドの右トリガーと西ボタン(UEの<c>IA_Throttle</c>)。</summary>
    [Keys(Key.W,Key.Up)]
    [Gamepad(Pad.RightTrigger)]
    [Gamepad(Pad.West)]
    public Axis Throttle;
    /// <summary>ブレーキ・後退(0から1)。S、下矢印、ゲームパッドの左トリガーと南ボタン(UEの<c>IA_Brake</c>)。</summary>
    [Keys(Key.S,Key.Down)]
    [Gamepad(Pad.LeftTrigger)]
    [Gamepad(Pad.South)]
    public Axis Brake;
    /// <summary>ハンドル(-1から1。右が正)。D/A、右/左矢印、左スティックの横(UEの<c>IA_Steering</c>)。</summary>
    [Keys(Key.D,Key.Right)]
    [NegativeKeys(Key.A,Key.Left)]
    [Gamepad(Pad.LeftStick,StickAxis.X)]
    public Axis Steering;
    /// <summary>ハンドブレーキ。スペースキーとゲームパッドの左ショルダー(UEの<c>IA_Handbrake</c>)。</summary>
    [Keys(Key.Space)]
    [Gamepad(Pad.LeftShoulder)]
    public Button Handbrake;
    /// <summary>見回し(横だけ)。マウスの横の動きと右スティックの横(UEの<c>IA_LookAround</c>。後ろの腕を回す処理はChaosの車両と同じM0109で作る)。</summary>
    [Mouse(MouseAxis.DeltaX)]
    [Gamepad(Pad.RightStick,StickAxis.X)]
    public Look LookAround;
}
