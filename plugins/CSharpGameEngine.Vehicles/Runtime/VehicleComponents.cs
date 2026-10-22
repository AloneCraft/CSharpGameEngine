using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Vehicles;
/// <summary>
/// 簡易の車両モデル(ADR-0077)。UEのChaos Vehicleのような車輪ごとの物理ではなく、アーケード風に、前後の速度・横滑り・ハンドル・
/// 接地を<see cref="LogicalScalar"/>で解く。値はメートル・秒・度。Transformの位置は車体の中心で、車は根のノードにする。
/// 接地は4つの車輪の位置から下へ飛ばす光線で調べ、地面の高さと傾きに車体を合わせる。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,14)]
public struct Vehicle() {
    /// <summary>前輪と後輪の間(m)。ハンドルを切ったときの旋回半径を決める。</summary>
    public LogicalScalar Wheelbase=LogicalScalar.FromSingle(2.6f);
    /// <summary>左右の車輪の間(m)。</summary>
    public LogicalScalar Track=LogicalScalar.FromSingle(1.6f);
    /// <summary>車体の中心から、車輪が地面に触れる高さまでの距離(m)。静止したときの車体の高さ。</summary>
    public LogicalScalar RideHeight=LogicalScalar.FromSingle(0.55f);
    /// <summary>サスペンションが伸びられる長さ(m)。車輪がこれより遠い地面は、接地とみなさない。</summary>
    public LogicalScalar SuspensionTravel=LogicalScalar.FromSingle(0.3f);
    /// <summary>前進の最高速度(m/s)。</summary>
    public LogicalScalar TopSpeed=LogicalScalar.FromSingle(42f);
    /// <summary>後退の最高速度(m/s)。</summary>
    public LogicalScalar ReverseSpeed=LogicalScalar.FromSingle(10f);
    /// <summary>発進の加速度(m/s²)。速度が最高速度に近づくほど弱まる。</summary>
    public LogicalScalar Acceleration=LogicalScalar.FromSingle(10f);
    /// <summary>ブレーキの減速度(m/s²)。</summary>
    public LogicalScalar BrakeDeceleration=LogicalScalar.FromSingle(16f);
    /// <summary>アクセルを離したときの自然な減速度(m/s²)。</summary>
    public LogicalScalar RollingDeceleration=LogicalScalar.FromSingle(1.5f);
    /// <summary>ハンドルを全部切ったときの、前輪の角度(度)。</summary>
    [Angle(AngleUnit.Degrees)]
    public LogicalScalar MaxSteeringDegrees=LogicalScalar.FromSingle(34f);
    /// <summary>ハンドルの角度がこの速度(m/s)で半分に絞られる。速いほど切れなくなる。</summary>
    public LogicalScalar SteeringFalloffSpeed=LogicalScalar.FromSingle(28f);
    /// <summary>ハンドルを切る・戻す速さ(1/s)。1で全部切るまで1秒。</summary>
    public LogicalScalar SteeringRate=LogicalScalar.FromSingle(4f);
    /// <summary>
    /// 曲がるときに出せる横方向の加速度の上限(m/s²。10でおよそ1G)。ハンドルを切っても、速いほど旋回の速さ(速度×旋回の角速度)がこれに
    /// 抑えられる。0以下なら抑えない。
    /// </summary>
    public LogicalScalar MaxLateralAcceleration=LogicalScalar.FromSingle(14f);
    /// <summary>横方向の速度が消える速さ(1/s)。大きいほどレールの上のように曲がり、小さいほど滑る。</summary>
    public LogicalScalar Grip=LogicalScalar.FromSingle(7f);
    /// <summary>ハンドブレーキ中の<see cref="Grip"/>。</summary>
    public LogicalScalar HandbrakeGrip=LogicalScalar.FromSingle(0.8f);
    /// <summary>空中の重力の倍率。</summary>
    public LogicalScalar GravityScale=LogicalScalar.One;
    /// <summary>変速の表示用に、前進の速度をこの数で等分してギアを決める。0ならギアを出さない(<see cref="VehicleState.Gear"/>は0)。</summary>
    public int GearCount=5;
    public PhysicsLayerMask CollidesWith=PhysicsLayerMask.All;
}
/// <summary>車の状態。再生が決定的に続くよう、スナップショットに入る。</summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,15)]
public struct VehicleState {
    /// <summary>ワールドの速度(m/s)。</summary>
    public LogicalVector3 Velocity;
    /// <summary>今のハンドルの切れ具合(-1から1。右が正)。<see cref="VehicleIntent.Steering"/>へ<see cref="Vehicle.SteeringRate"/>で近づく。</summary>
    public LogicalScalar Steering;
    /// <summary>車輪のどれかが地面に触れているとき true。</summary>
    public bool OnGround;
    /// <summary>車の前向きの速度(m/s)。後退は負。</summary>
    public LogicalScalar ForwardSpeed;
    /// <summary>1から<see cref="Vehicle.GearCount"/>のギア(後退は-1、停止は0)。表示用で、速度から決まる。</summary>
    public int Gear;
}
/// <summary>
/// 運転者の望み(ADR-0077)。ゲームのコードが毎フレーム書き、車の動きが毎tick読む。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,16)]
public struct VehicleIntent {
    /// <summary>アクセルとブレーキ(-1から1)。正は前進、負は後退。進んでいる向きと逆に踏むとブレーキになる。</summary>
    public float Drive;
    /// <summary>ハンドル(-1から1)。右が正。</summary>
    public float Steering;
    /// <summary>ハンドブレーキ。</summary>
    public bool Handbrake;
}
