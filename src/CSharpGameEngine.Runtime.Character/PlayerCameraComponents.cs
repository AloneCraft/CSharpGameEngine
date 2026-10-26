using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Input;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// プレイヤーの制御回転（Unrealコントローラーの回転）を、キャラクタールート上の度数で表す。
/// Yaw 0は+F方向を向き、+R側へ増加する。Pitchが正なら上を向く。プレゼンテーションデータであり、視点入力でフレームごとに変化する。
/// 移動処理はカメラ相対の方向を作るため、このYawを読む。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,13)]
public struct ViewAngles() {
    [Angle(AngleUnit.Degrees)]
    public float YawDegrees=0f;
    [Angle(AngleUnit.Degrees)]
    public float PitchDegrees=0f;
    [Angle(AngleUnit.Degrees)]
    public float MinPitchDegrees=-89f;
    [Angle(AngleUnit.Degrees)]
    public float MaxPitchDegrees=89f;
    /// <summary>この視点へ、今フレームの視点入力(turns)を足す。Xは右へ、Yは上へ。上下の制限は<see cref="MinPitchDegrees"/>と<see cref="MaxPitchDegrees"/>(ADR-0077)。</summary>
    public void Turn(Look Look)=>PlayerControl.AddLook(ref this,Look.X*360f,Look.Y*360f);
    /// <summary>移動入力(Xは右、Yは前)を、この視点の向きを基準にしたワールドの水平な方向にする。長さは1に丸められる(ADR-0077)。</summary>
    public readonly LogicalVector3 Planar(Stick Move)=>PlayerControl.MoveDirection(this,Move.X,Move.Y);
}
/// <summary>
/// キャラクタールートの直接の子であるカメラを、親の<see cref="ViewAngles"/>回転の後方へ配置する。
/// UnrealのbUsePawnControlRotationを有効にしたSpringArmComponentと同様、ピボットからスイープするプローブ球で障害物に応じてアームを短くする。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Camera,1)]
public struct OrbitCamera() {
    /// <summary>親のローカル座標系でのピボット位置（Unrealのスプリングアーム相対位置）。</summary>
    public LogicalVector3 Pivot=LogicalVector3.Zero;
    public LogicalScalar Distance=LogicalScalar.FromSingle(3f);
    public LogicalScalar Radius=LogicalScalar.FromSingle(0.12f);
    public bool Collide=true;
    public PhysicsLayerMask CollidesWith=PhysicsLayerMask.All;
    /// <summary>
    /// アーム先端からカメラ自身の座標系（R、U、F）で表したカメラのオフセット（UEのSocketOffset）。
    /// 障害物に応じてアームを短くした後に加えるため、プローブはアーム本体に沿って移動する。
    /// </summary>
    public LogicalVector3 SocketOffset=LogicalVector3.Zero;
    /// <summary>UEのCameraLagSpeed（毎秒、ADR-0078/M0107）。残り距離に対して毎秒この割合だけ、アームのピボットを目標へ近づける。0なら即座に追従。</summary>
    public LogicalScalar Lag=LogicalScalar.Zero;
    /// <summary>UEのCameraLagMaxTimeStep（秒）。これより長いフレームをこの長さのステップに分割して遅延させ、フレームレートへの依存を防ぐ。</summary>
    public LogicalScalar LagMaxTimeStep=LogicalScalar.FromSingle(1f/60f);
    /// <summary>UEのCameraLagMaxDistance（m）。ピボットが遅れてよい最大距離。0は制限なし。</summary>
    public LogicalScalar LagMaxDistance=LogicalScalar.Zero;
    /// <summary>UEのCameraRotationLagSpeed（毎秒）。ビュー回転を制御回転へ近づける速度（slerp）。0なら即座に追従。</summary>
    public LogicalScalar RotationLag=LogicalScalar.Zero;
}
/// <summary>前フレームからラグのあるスプリングアームが保持する値（<see cref="OrbitCamera.Lag"/>、<see cref="OrbitCamera.RotationLag"/>）。スナップショットには含めない。</summary>
public struct OrbitCameraRuntime:IRuntimeComponent<OrbitCamera> {
    public LogicalVector3 Pivot;
    public System.Numerics.Quaternion Rotation;
    public bool HasPrevious;
}
