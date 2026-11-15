using CSharpGameEngine.Authoring;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Vehicles;
/// <summary>
/// 車のエンジン・変速・操舵・車輪・サスペンションの設定一式(ADR-0078/M0109、ADR-0079 のデータアセット)。UE の ChaosWheeledVehicleMovementComponent と
/// ChaosVehicleWheel のクラスの値に当たる。値は <c>Assets/**/*.cgeasset</c> に書き、コードが <see cref="Vehicle.Setup"/> へ入れる(シーンからデータアセットを
/// 参照する仕組みはまだ無い。Plugins-And-Assets.md §7)。単位はエンジンの単位(m、kg、N、N·m、m/s、rpm)で、UE の cm の値は 100 で割った値を書く。
/// 既定値は UE のクラスの既定値。
/// </summary>
[DataAsset]
public sealed class VehicleSetup {
    /// <summary>車の質量(kg)。物理の剛体の質量もこの値にする(UE の UpdateMassProperties)。</summary>
    public LogicalScalar Mass=LogicalScalar.FromInt64(1500);
    /// <summary>空気抵抗の係数(UE の DragCoefficient)。</summary>
    public LogicalScalar DragCoefficient=LogicalScalar.ParseInvariant("0.3");
    /// <summary>ダウンフォースの係数(UE の DownforceCoefficient)。前進では車を地面へ押し、後退では持ち上げる(UE の式のまま)。</summary>
    public LogicalScalar DownforceCoefficient=LogicalScalar.ParseInvariant("0.3");
    /// <summary>空気抵抗の面積を決める車体の幅(m。UE の ChassisWidth の cm を 100 で割る)。</summary>
    public LogicalScalar ChassisWidth=LogicalScalar.ParseInvariant("1.8");
    /// <summary>空気抵抗の面積を決める車体の高さ(m)。面積は幅 × 高さ。</summary>
    public LogicalScalar ChassisHeight=LogicalScalar.ParseInvariant("1.4");
    public VehicleEngineSetup Engine=new();
    public VehicleTransmissionSetup Transmission=new();
    public VehicleSteeringSetup Steering=new();
    /// <summary>駆動輪の決め方(UE の DifferentialSetup)。</summary>
    public VehicleDifferential Differential=VehicleDifferential.RearWheelDrive;
    /// <summary>四輪駆動のときの後輪の取り分(UE の FrontRearSplit。前輪は 1 から引いた残り)。</summary>
    public LogicalScalar FrontRearSplit=LogicalScalar.ParseInvariant("0.5");
    /// <summary><see cref="VehicleAxle.Front"/> の車輪の設定(UE の前輪の ChaosVehicleWheel のクラス)。</summary>
    public VehicleWheelSetup FrontWheel=new();
    /// <summary><see cref="VehicleAxle.Rear"/> の車輪の設定。</summary>
    public VehicleWheelSetup RearWheel=new();
    /// <summary>アクセルの値が変わる速さ(UE の ThrottleInputRate)。</summary>
    public VehicleInputRate ThrottleInputRate=new(6,10,VehicleInputCurve.Linear);
    public VehicleInputRate BrakeInputRate=new(6,10,VehicleInputCurve.Linear);
    /// <summary>ハンドルの値が変わる速さ(UE の既定は二乗の曲線)。</summary>
    public VehicleInputRate SteeringInputRate=new(LogicalScalar.ParseInvariant("2.5"),5,VehicleInputCurve.Squared);
    public VehicleInputRate HandbrakeInputRate=new(12,12,VehicleInputCurve.Linear);
    /// <summary>ブレーキを踏み続けると止まったあとに後退する(UE の bReverseAsBrake)。</summary>
    public bool ReverseAsBrake=true;
    /// <summary>後退中にアクセルを踏むとブレーキになる(UE の bThrottleAsBrake。既定は偽)。</summary>
    public bool ThrottleAsBrake;
    /// <summary>止まっているとみなす前後の速さ(m/s。UE の StopThreshold 10 cm/s)。</summary>
    public LogicalScalar StopThreshold=LogicalScalar.ParseInvariant("0.1");
    /// <summary>進む向きと逆に踏んだとき、これより速ければブレーキにする(m/s。UE の WrongDirectionThreshold 100 cm/s)。</summary>
    public LogicalScalar WrongDirectionThreshold=LogicalScalar.One;
    /// <summary>何も踏んでいないときのブレーキ(UE の IdleBrakeInput)。</summary>
    public LogicalScalar IdleBrakeInput;
    /// <summary>この速さ(m/s)より遅く、全輪が接地し、傾きが <see cref="SleepSlopeLimit"/> 以内で、操作が無い状態が続くと眠る(UE の SleepThreshold 10 cm/s)。</summary>
    public LogicalScalar SleepThreshold=LogicalScalar.ParseInvariant("0.1");
    /// <summary>眠れる傾き: 車体の上の向きの鉛直成分がこれより大きい(UE の SleepSlopeLimit。cos 30°)。</summary>
    public LogicalScalar SleepSlopeLimit=LogicalScalar.ParseInvariant("0.866");
}
/// <summary>UE の FVehicleEngineConfig。</summary>
public struct VehicleEngineSetup() {
    /// <summary>
    /// トルク曲線(回転数 rpm → 値)。値は最大値で割って使う(UE の FillEngineSetup)。UE と同じく、0 から <see cref="MaxRpm"/> を 20 等分した 21 点で標本を取り、
    /// その間を直線で補う。最初の点より前と最後の点より後は端の値。
    /// </summary>
    public VehicleCurveKey[] TorqueCurve=[new(0,1)];
    /// <summary>トルク曲線の最大値に当たるトルク(N·m)。</summary>
    public LogicalScalar MaxTorque=LogicalScalar.FromInt64(300);
    public LogicalScalar MaxRpm=LogicalScalar.FromInt64(4500);
    /// <summary>アイドル回転数(rpm)。エンジンはこれより下がらない。</summary>
    public LogicalScalar IdleRpm=LogicalScalar.FromInt64(1200);
    /// <summary>アクセルを離したとき駆動輪にかかるブレーキ(N·m)の、エンジン回転数(rpm)に対する比(UE の EngineBrakeEffect)。</summary>
    public LogicalScalar EngineBrakeEffect=LogicalScalar.ParseInvariant("0.05");
    /// <summary>ギアが入っていないときに回転が上がる慣性(UE の EngineRevUpMOI)。</summary>
    public LogicalScalar RevUpMoi=LogicalScalar.FromInt64(5);
    /// <summary>ギアが入っていないときに回転が下がる速さ(UE の EngineRevDownRate)。</summary>
    public LogicalScalar RevDownRate=LogicalScalar.FromInt64(600);
}
/// <summary>UE の FVehicleTransmissionConfig。</summary>
public struct VehicleTransmissionSetup() {
    /// <summary>自動変速(UE の bUseAutomaticGears)。</summary>
    public bool Automatic=true;
    /// <summary>ブレーキで後退へ入れる(UE の bUseAutoReverse。今の実装では <see cref="VehicleSetup.ReverseAsBrake"/> が後退を選ぶ)。</summary>
    public bool AutoReverse=true;
    /// <summary>最終減速比(UE の FinalRatio)。</summary>
    public LogicalScalar FinalRatio=LogicalScalar.ParseInvariant("3.08");
    /// <summary>前進のギア比(1 速から)。</summary>
    public LogicalScalar[]ForwardGearRatios=[LogicalScalar.ParseInvariant("2.85"),LogicalScalar.ParseInvariant("2.02"),LogicalScalar.ParseInvariant("1.35"),LogicalScalar.One];
    /// <summary>後退のギア比。</summary>
    public LogicalScalar[]ReverseGearRatios=[LogicalScalar.ParseInvariant("2.86")];
    /// <summary>この回転数(rpm)以上で 1 段上げる。</summary>
    public LogicalScalar ChangeUpRpm=LogicalScalar.FromInt64(4500);
    /// <summary>この回転数(rpm)以下で 1 段下げる(1 速より下へは下げない)。</summary>
    public LogicalScalar ChangeDownRpm=LogicalScalar.FromInt64(2000);
    /// <summary>変速の間ニュートラルになる時間(秒)。</summary>
    public LogicalScalar GearChangeTime=LogicalScalar.ParseInvariant("0.4");
    /// <summary>伝達の効率(0 から 1)。</summary>
    public LogicalScalar Efficiency=LogicalScalar.ParseInvariant("0.9");
}
/// <summary>UE の FVehicleSteeringConfig。</summary>
public struct VehicleSteeringSetup() {
    public VehicleSteeringType Type=VehicleSteeringType.AngleRatio;
    /// <summary><see cref="VehicleSteeringType.AngleRatio"/> で内側の車輪の角度に掛ける比。</summary>
    public LogicalScalar AngleRatio=LogicalScalar.ParseInvariant("0.7");
    /// <summary>
    /// 速さ → ハンドルの効き(0 から 1)。横軸は UE のコードのとおりマイル毎時(UE の ProcessSteering が CmSToMPH で変換する。エディタの表示は km/h ではない)。
    /// 0 から最後の点までを 20 等分した 21 点で標本を取る。
    /// </summary>
    public VehicleCurveKey[] SteeringCurve=[new(0,1),new(20,LogicalScalar.ParseInvariant("0.8")),new(60,LogicalScalar.ParseInvariant("0.4")),new(120,LogicalScalar.ParseInvariant("0.3"))];
}
/// <summary>UE の ChaosVehicleWheel(1 種類の車輪とそのサスペンション)。</summary>
public struct VehicleWheelSetup() {
    /// <summary>車輪の半径(m)。</summary>
    public LogicalScalar Radius=LogicalScalar.ParseInvariant("0.32");
    public LogicalScalar Width=LogicalScalar.ParseInvariant("0.2");
    /// <summary>車輪の質量(kg)。回転の慣性 0.5 × 質量 × 半径² を決める。</summary>
    public LogicalScalar Mass=LogicalScalar.FromInt64(20);
    /// <summary>
    /// 横滑りの角(rad)に掛けて横の力(N)にする係数(N/rad)。UE のエディタの値 1000 は、内部で 10000 倍され cm の単位なので、ここでは 100 倍した 100000。
    /// </summary>
    public LogicalScalar CorneringStiffness=LogicalScalar.FromInt64(100000);
    /// <summary>地面の摩擦 × 荷重に掛けて、出せる力の上限にする(UE の FrictionForceMultiplier)。</summary>
    public LogicalScalar FrictionForceMultiplier=LogicalScalar.FromInt64(2);
    /// <summary>摩擦の円で切られたときに掛ける比(UE の SideSlipModifier)。</summary>
    public LogicalScalar SideSlipModifier=LogicalScalar.One;
    /// <summary>車輪の周速と地面の速さの差がこれ(m/s)を超えると空転とみなし、自動変速を止める(UE の SlipThreshold 20 cm/s)。</summary>
    public LogicalScalar SlipThreshold=LogicalScalar.ParseInvariant("0.2");
    /// <summary>横の速さがこれ(m/s)を超えると横滑り(UE の SkidThreshold)。</summary>
    public LogicalScalar SkidThreshold=LogicalScalar.ParseInvariant("0.2");
    /// <summary>空転で増やせる角速度の上限(rad/s。UE の MaxWheelspinRotation)。</summary>
    public LogicalScalar MaxWheelspinRotation=LogicalScalar.FromInt64(30);
    /// <summary>最大の舵角(度。UE の MaxSteerAngle)。</summary>
    [Angle(AngleUnit.Degrees)]
    public LogicalScalar MaxSteerAngleDegrees=LogicalScalar.FromInt64(50);
    /// <summary>ブレーキの最大トルク(N·m)。</summary>
    public LogicalScalar MaxBrakeTorque=LogicalScalar.FromInt64(1500);
    /// <summary>ハンドブレーキの最大トルク(N·m)。</summary>
    public LogicalScalar MaxHandbrakeTorque=LogicalScalar.FromInt64(3000);
    public bool AffectedByBrake=true;
    public bool AffectedByHandbrake;
    public bool AffectedBySteering=true;
    /// <summary>駆動輪か。<see cref="VehicleSetup.Differential"/> が前輪・後輪・四輪のときはそれが優先する(UE の IsWheelPowered)。</summary>
    public bool AffectedByEngine;
    /// <summary>サスペンションが縮める長さ(m。UE の SuspensionMaxRaise)。光線は休みの位置のこれだけ上から飛ばす。</summary>
    public LogicalScalar SuspensionMaxRaise=LogicalScalar.ParseInvariant("0.1");
    /// <summary>サスペンションが伸びる長さ(m。UE の SuspensionMaxDrop)。</summary>
    public LogicalScalar SuspensionMaxDrop=LogicalScalar.ParseInvariant("0.1");
    /// <summary>ばね定数(N/m)。UE のエディタの値(250)は N/cm なので 100 倍する。</summary>
    public LogicalScalar SpringRate=LogicalScalar.FromInt64(25000);
    /// <summary>ばねの予圧(N)。UE の力で押すサスペンションでは使われない値で、ここでも使わない(UE の制約のサスペンションの値)。</summary>
    public LogicalScalar SpringPreload=LogicalScalar.FromInt64(5000);
    /// <summary>減衰比(0 で減衰なし、1 で臨界減衰)。減衰係数は 比 × 2 × √(ばね定数 × ばね上の質量)。</summary>
    public LogicalScalar SuspensionDampingRatio=LogicalScalar.ParseInvariant("0.5");
    /// <summary>車輪の荷重を、ばねの力と静止の荷重のどちらに寄せるか(1 でばねの力だけ。UE の WheelLoadRatio)。</summary>
    public LogicalScalar WheelLoadRatio=LogicalScalar.ParseInvariant("0.5");
    /// <summary>同じ車軸の左右の荷重の差に掛けて戻す力にする(UE の RollbarScaling)。</summary>
    public LogicalScalar RollbarScaling=LogicalScalar.ParseInvariant("0.15");
}
/// <summary>UE の FVehicleInputRateConfig。入力の値は 1 秒にこれだけしか変わらない。</summary>
public struct VehicleInputRate {
    /// <summary>絶対値が増えるときの速さ(1/s)。</summary>
    public LogicalScalar RiseRate;
    /// <summary>絶対値が減るときの速さ(1/s)。</summary>
    public LogicalScalar FallRate;
    public VehicleInputCurve Curve;
    public VehicleInputRate(LogicalScalar RiseRate,LogicalScalar FallRate,VehicleInputCurve Curve){
        this.RiseRate=RiseRate;
        this.FallRate=FallRate;
        this.Curve=Curve;
    }
}
/// <summary>UE の FRichCurve の 1 点(線形の補間)。</summary>
public struct VehicleCurveKey {
    public LogicalScalar Time;
    public LogicalScalar Value;
    public VehicleCurveKey(LogicalScalar Time,LogicalScalar Value){
        this.Time=Time;
        this.Value=Value;
    }
}
/// <summary>UE の EInputFunctionType(ユーザー定義の曲線は無い)。</summary>
public enum VehicleInputCurve {
    Linear,
    Squared,
}
/// <summary>UE の ESteeringType。</summary>
public enum VehicleSteeringType {
    SingleAngle,
    AngleRatio,
    Ackermann,
}
/// <summary>UE の EVehicleDifferential。</summary>
public enum VehicleDifferential {
    Undefined,
    AllWheelDrive,
    FrontWheelDrive,
    RearWheelDrive,
}
