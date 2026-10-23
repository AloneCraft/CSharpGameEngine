using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Vehicles;
/// <summary>
/// Chaos 相当の車(ADR-0078/M0109。UE の ChaosWheeledVehicleMovementComponent に当たる)。車は根のノードで、同じ Entity の <c>Collider</c> と
/// <c>RigidBody</c>(動的)が車体の剛体になる。Transform の位置は剛体の重心。車輪は剛体に付いた光線のサスペンションで、毎 tick、車輪ごとの
/// ばね・ダンパーの力とタイヤの摩擦の力を、その車輪の位置に加える(<c>PhysicsCommands.ForceAtPoint</c>)。エンジン・変速・操舵・タイヤは
/// <see cref="Setup"/> の値で UE と同じ式で解く。値はメートル・秒・kg。
/// スナップショットには入らない: <see cref="Setup"/>(コードが入れるデータアセットの参照)と <see cref="Wheels"/>(配列)はスナップショットの
/// 値の型の規則に合わず、どちらも実行中に変わらない設定なので、復元は読み直したシーン(とコード)の値を保つ。変わる状態は <see cref="VehicleState"/>。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,14)]
[EcsSnapshotTransient]
public struct Vehicle() {
    /// <summary>エンジン・変速・車輪の設定(データアセット)。コードが入れる。無い車は動かない。</summary>
    public VehicleSetup?Setup;
    /// <summary>車輪の休みの位置(車体の剛体の座標。UE の車輪の骨の位置)と車軸。4 つまで。</summary>
    public VehicleWheelMount[]Wheels=[];
    /// <summary>車輪の光線が当たる層。車体自身には当たらない。</summary>
    public PhysicsLayerMask CollidesWith=PhysicsLayerMask.All;
    /// <summary>リセットで車を持ち上げる高さ(m。UE の BP_VehicleAdvPawnBase の Manual Reset の 50 cm)。</summary>
    public LogicalScalar ResetLift=LogicalScalar.ParseInvariant("0.5");
    /// <summary>
    /// UE の Actor の原点(車のメッシュの原点)の、剛体の重心からの位置(車体の座標、m)。リセットは UE と同じくこの点を持ち上げ、この点のまわりに
    /// 向きを直す。0 なら重心のまわり。
    /// </summary>
    public LogicalVector3 ResetPivot;
    /// <summary>転倒を調べる間隔(秒。UE の Flipped Check のタイマー 3 秒)。0 なら調べない。</summary>
    public LogicalScalar FlipCheckSeconds=LogicalScalar.FromInt64(3);
    /// <summary>車体の上の向きの鉛直成分がこれより小さいと転倒とみなす(UE の -0.2)。2 回続けて転倒ならリセットする。</summary>
    public LogicalScalar FlippedUpDot=LogicalScalar.ParseInvariant("-0.2");
}
/// <summary>車輪 1 つの取り付け(UE の FChaosWheelSetup)。</summary>
public struct VehicleWheelMount {
    /// <summary>休みの位置(車体の剛体の座標、m)。光線は ここ + 上 × <see cref="VehicleWheelSetup.SuspensionMaxRaise"/> から下へ飛ぶ。</summary>
    public LogicalVector3 Position;
    /// <summary>前輪か後輪か。<see cref="VehicleSetup.FrontWheel"/> と <see cref="VehicleSetup.RearWheel"/> のどちらを使うかと、左右のロールバーの組を決める。</summary>
    public VehicleAxle Axle;
}
public enum VehicleAxle {
    Front,
    Rear,
}
/// <summary>
/// 車の状態(ADR-0078/M0109)。再生が決定的に続くよう、スナップショットに入る。値は UE の車両の出力(GetForwardSpeed、GetCurrentGear、GetEngineRotationSpeed、
/// 車輪ごとの FWheelsOutput)に当たる。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,15)]
public struct VehicleState {
    /// <summary>最初の tick の準備(剛体の向きと質量)を済ませた。</summary>
    public bool Initialized;
    /// <summary>車の前向きの速さ(m/s)。後退は負。</summary>
    public LogicalScalar ForwardSpeed;
    /// <summary>今のギア(後退は -1、ニュートラルと変速中は 0、前進は 1 から)。UE の GetCurrentGear。</summary>
    public int Gear;
    /// <summary>変速の行き先のギア(UE の GetTargetGear)。</summary>
    public int TargetGear;
    /// <summary>変速の残り時間(秒)。</summary>
    public LogicalScalar GearChangeTime;
    /// <summary>エンジンの角速度(rad/s)。</summary>
    public LogicalScalar EngineOmega;
    /// <summary>エンジンの回転数(rpm。UE の GetEngineRotationSpeed)。</summary>
    public LogicalScalar EngineRpm;
    /// <summary>エンジンに渡ったアクセル(0 から 1。入力の速さで均したアクセルの二乗)。</summary>
    public LogicalScalar EngineThrottle;
    /// <summary>入力の速さで均したアクセル・ブレーキ・ハンドル・ハンドブレーキ(UE の ThrottleInput など)。</summary>
    public LogicalScalar Throttle;
    public LogicalScalar Brake;
    public LogicalScalar Steering;
    public LogicalScalar Handbrake;
    /// <summary>前の tick にエンジンへ渡したハンドルの値(眠りから覚めるかの判定)。</summary>
    public LogicalScalar PreviousSteering;
    /// <summary>車輪のどれかが地面に触れている(UE の !bVehicleInAir)。</summary>
    public bool OnGround;
    /// <summary>地面に触れている車輪の数。</summary>
    public int WheelsOnGround;
    /// <summary>眠っている(UE の bSleeping)。眠っている間は力を加えない。</summary>
    public bool Sleeping;
    public int SleepCounter;
    /// <summary>転倒の判定から経った tick 数。</summary>
    public int FlipCheckTicks;
    /// <summary>前の転倒の判定で転倒していた(UE の Previous Flip Check)。</summary>
    public bool PreviousFlipCheck;
    /// <summary>この tick にリセットした(力を加えない)。</summary>
    public bool ResetThisTick;
    public VehicleWheelState Wheel0;
    public VehicleWheelState Wheel1;
    public VehicleWheelState Wheel2;
    public VehicleWheelState Wheel3;
    /// <summary><paramref name="Index"/> 番目の車輪の状態(0 から 3)。</summary>
    public readonly VehicleWheelState WheelAt(int Index)=>Index switch { 0=>this.Wheel0,1=>this.Wheel1,2=>this.Wheel2,3=>this.Wheel3,_=>throw new ArgumentOutOfRangeException(nameof(Index)) };
}
/// <summary>車輪 1 つの状態(UE の FSimpleWheelSim・FSimpleSuspensionSim と FWheelsOutput)。</summary>
public struct VehicleWheelState {
    /// <summary>光線が地面に当たった。</summary>
    public bool InContact;
    /// <summary>車輪の角速度(rad/s。前進で正)。</summary>
    public LogicalScalar Omega;
    /// <summary>車輪の回転の角度(rad。-2π から 2π)。</summary>
    public LogicalScalar AngularPosition;
    /// <summary>舵角(度。右へ切ると正)。</summary>
    public LogicalScalar SteeringAngleDegrees;
    /// <summary>ばねの縮み(m。0 で伸びきり、Raise + Drop で縮みきり)。</summary>
    public LogicalScalar SpringDisplacement;
    /// <summary>前の tick のばねの縮み(ダンパーの速さを決める)。</summary>
    public LogicalScalar LastDisplacement;
    /// <summary>ばねの力(N。上向きが正)。</summary>
    public LogicalScalar SpringForce;
    /// <summary>タイヤの荷重(N)。</summary>
    public LogicalScalar Load;
    /// <summary>表示の車輪の上下(m。休みの位置から上が正。-MaxDrop から MaxRaise。UE の GetSuspensionOffset)。</summary>
    public LogicalScalar SuspensionOffset;
    /// <summary>車輪の周速 - 地面の速さ(m/s。UE の GetSlipMagnitude)。</summary>
    public LogicalScalar SlipMagnitude;
    /// <summary>車輪の横の速さ(m/s。UE の GetSkidMagnitude)。</summary>
    public LogicalScalar SkidMagnitude;
    /// <summary>車輪の下の地面の摩擦。</summary>
    public LogicalScalar SurfaceFriction;
    /// <summary>この tick の駆動トルクとブレーキトルク(N·m)。</summary>
    public LogicalScalar DriveTorque;
    public LogicalScalar BrakeTorque;
}
/// <summary>
/// 運転者の望み(ADR-0077)。ゲームのコードが毎フレーム書き、車が毎 tick 読む。UE の SetThrottleInput・SetBrakeInput・SetSteeringInput・SetHandbrakeInput に当たる。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,16)]
public struct VehicleIntent {
    /// <summary>アクセル(0 から 1)。</summary>
    public float Throttle;
    /// <summary>ブレーキ(0 から 1)。止まっていればそのまま後退する(<see cref="VehicleSetup.ReverseAsBrake"/>)。</summary>
    public float Brake;
    /// <summary>ハンドル(-1 から 1)。右が正。</summary>
    public float Steering;
    public bool Handbrake;
    /// <summary>次の tick にリセットする(<see cref="Reset"/>)。車が読んだら消す。</summary>
    public bool ResetRequested;
    /// <summary>
    /// UE の BP_VehicleAdvPawnBase の IA_Reset: 車をその場で <see cref="Vehicle.ResetLift"/> だけ持ち上げ、Roll と Pitch を 0 に、Yaw はそのままにして、
    /// 速度と角速度を 0 にする。
    /// </summary>
    public void Reset()=>this.ResetRequested=true;
}
/// <summary>
/// 車の子の車輪の見た目(ADR-0078/M0109。UE の ABP の WheelController)。<see cref="VehicleWheelPose"/> が親の <see cref="VehicleState"/> の
/// <see cref="Wheel"/> 番目の車輪の回転・舵角・サスペンションの上下を、この Entity の Transform に書く。最初に見た Transform を休みの姿勢として覚える。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,17)]
public struct VehicleWheelVisual {
    /// <summary><see cref="Vehicle.Wheels"/> の何番目の車輪か。</summary>
    public int Wheel;
    public LogicalVector3 RestPosition;
    public LogicalQuaternion RestRotation;
    public bool Captured;
    /// <summary>
    /// 車輪の見た目の姿勢(車体の座標): 休みの位置から上へサスペンションの上下だけ動かし、休みの向きに、右の軸のまわりの回転の角と、上の軸のまわりの舵角
    /// (右へ切ると右へ向く)を重ねる(UE の WheelController の RotOffset と LocOffset)。
    /// </summary>
    public static bool TryPose(in VehicleWheelVisual Visual,in VehicleWheelState Wheel,out LogicalVector3 Position,out LogicalQuaternion Rotation){
        Position=Visual.RestPosition+LogicalVector3.UnitU*Wheel.SuspensionOffset;
        Rotation=Visual.RestRotation;
        return LogicalQuaternion.TryCreateAxisAngle(LogicalVector3.UnitU,-(Wheel.SteeringAngleDegrees*VehicleChaos.RadiansPerDegree),out var Steer)
            &&LogicalQuaternion.TryCreateAxisAngle(LogicalVector3.UnitR,Wheel.AngularPosition,out var Spin)
            &&LogicalQuaternion.TryMultiply(Steer,Spin,out var SteerSpin)
            &&LogicalQuaternion.TryMultiply(SteerSpin,Visual.RestRotation,out Rotation)
            &&Rotation.TryNormalize(out Rotation);
    }
}
/// <summary>
/// 追従カメラの見回し(ADR-0078/M0109。UE の BP_VehicleAdvPawnBase の BackSpringArm の Yaw)。根の <c>ViewAngles</c> の Yaw を、車の向きに
/// <see cref="YawDegrees"/> を足した値にする(腕は Yaw だけを継ぎ、Pitch と Roll は継がない)。<see cref="YawDegrees"/> は毎フレーム
/// FInterpTo(現在, 0, Δt, <see cref="ReturnSpeed"/>)で 0 へ戻る。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,18)]
public struct VehicleCameraLook() {
    /// <summary>見回しの角度(度。右が正)。ゲームのコードが足す(UE の IA_LookAround の AddLocalRotation)。</summary>
    [Angle(AngleUnit.Degrees)]
    public float YawDegrees=0f;
    /// <summary>元の角度へ戻る速さ(1/s。UE の InterpsToOriginalRotation の InterpSpeed 1)。</summary>
    public float ReturnSpeed=1f;
    /// <summary>腕の上下の角度(度。UE の腕の Pitch は継がず相対の 0)。</summary>
    [Angle(AngleUnit.Degrees)]
    public float PitchDegrees=0f;
}
