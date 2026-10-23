using System.Runtime.CompilerServices;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Vehicles;
/// <summary>
/// 車の 1 固定 tick(ADR-0078/M0109)。UE の ChaosWheeledVehicleMovementComponent の 1 回の物理の tick を、決定的な物理世界の上で同じ順に行う:
/// ゲームのスレッドの入力の処理(後退と 1 速の選択、アクセルとブレーキの読み替え、入力の速さ、眠り)、光線のサスペンション、ブレーキのトルク、
/// 空気抵抗、エンジンと変速、ばね・ダンパー・ロールバー、操舵、タイヤの摩擦。力は車輪ごとに 1 つにまとめて、その車輪の位置に
/// <see cref="PhysicsCommands.ForceAtPoint"/> で加え、空気の力は重心に <see cref="PhysicsCommands.Force"/> で加える。物理世界がこの tick を進めるときに効く。
/// UE の BP_VehicleAdvPawnBase の転倒の判定とリセットもここで行う。右はこのエンジンの座標系で、前 × 上(+R は左)。
/// </summary>
public sealed class VehicleMotor {
    /// <summary>UE の FSimpleWheelSim::Simulate の K(止めるのに要る力の係数)。</summary>
    private static readonly LogicalScalar StopForceFactor=LogicalScalar.ParseInvariant("0.4");
    /// <summary>UE のエンジンが目標の回転数へ近づく速さ(1/s。FSimpleEngineSim::Simulate の 4)。</summary>
    private static readonly LogicalScalar EngineFollowRate=LogicalScalar.FromInt64(4);
    /// <summary>UE の GVehicleDebugParams.ControlInputWakeTolerance。</summary>
    private static readonly LogicalScalar WakeTolerance=LogicalScalar.ParseInvariant("0.02");
    /// <summary>UE の GVehicleDebugParams.SleepCounterThreshold。</summary>
    private const int SleepCounterThreshold=15;
    /// <summary>UE の摩擦の円の判定の 0.05(内部の cm の力の二乗)を N の二乗に直した値。</summary>
    private static readonly LogicalScalar FrictionCircleThreshold=LogicalScalar.ParseInvariant("0.000005");
    /// <summary>車輪が固まったときに角速度に掛ける比(UE の 0.9)。</summary>
    private static readonly LogicalScalar LockedSpinDecay=LogicalScalar.ParseInvariant("0.9");
    private readonly DeterministicPhysicsWorld World;
    private readonly List<PhysicsRayHit>Hits=[];
    private readonly ConditionalWeakTable<VehicleWheelMount[],VehicleTuning>Tunings=[];
    public VehicleMotor(DeterministicPhysicsWorld World){
        this.World=World??throw new ArgumentNullException(nameof(World));
    }
    /// <summary>車の設定と車輪の取り付けから求めた値。設定が無ければ null。</summary>
    public VehicleTuning?TuningOf(in Vehicle Car){
        if(Car.Setup is not { }Setup||Car.Wheels is null){ return null; }
        if(this.Tunings.TryGetValue(Car.Wheels,out var Existing)&&ReferenceEquals(Existing.Setup,Setup)){ return Existing; }
        var Created=new VehicleTuning(Setup,Car.Wheels);
        this.Tunings.AddOrUpdate(Car.Wheels,Created);
        return Created;
    }
    /// <summary>車体の前・右(前 × 上)・上の向き。</summary>
    public static void Basis(LogicalQuaternion Rotation,out LogicalVector3 Forward,out LogicalVector3 Right,out LogicalVector3 Up){
        if(!Rotation.TryRotate(LogicalVector3.UnitF,out Forward)||!Rotation.TryRotate(LogicalVector3.UnitU,out Up)){
            Forward=LogicalVector3.UnitF;
            Up=LogicalVector3.UnitU;
        }
        Right=LogicalVector3.Cross(Forward,Up);
    }
    /// <summary>
    /// 車体の剛体 <paramref name="Body"/> に、<paramref name="Tick"/> の力の命令を積む。車の設定が無い、剛体が無い・動的でない、眠っているときは何もしない
    /// (眠っているかは状態に書く)。積んだら真。
    /// </summary>
    public bool Step(in Vehicle Car,ref VehicleState State,ref VehicleIntent Intent,CSharpGameEngine.Entities.Entity Body,ulong Tick,LogicalScalar DeltaSeconds){
        if(this.TuningOf(Car) is not { }Tuning||!this.World.TryGetBodyState(Body,out var BodyState)||BodyState.MotionType!=PhysicsMotionType.Dynamic){
            return false;
        }
        var Setup=Tuning.Setup;
        var Commands=this.World.Commands;
        var Source=BodyState.StableId;
        uint Sequence=0;
        Basis(BodyState.Rotation,out var Forward,out var Right,out var Up);
        var Velocity=BodyState.Velocity.Linear;
        State.ForwardSpeed=LogicalVector3.Dot(Velocity,Forward);
        State.ResetThisTick=false;
        // BP_VehicleAdvPawnBase: IA_Reset と、3 秒ごとの Flipped Check(2 回続けて上の向きが -0.2 より下ならリセット。前の結果はリセットでは消さない)。
        var Reset=Intent.ResetRequested;
        Intent.ResetRequested=false;
        if(Car.FlipCheckSeconds>LogicalScalar.Zero&&DeltaSeconds>LogicalScalar.Zero){
            var Interval=(int)Math.Max(1L,(long)((Car.FlipCheckSeconds/DeltaSeconds+VehicleChaos.Half).Raw>>64));
            State.FlipCheckTicks++;
            if(State.FlipCheckTicks>=Interval){
                State.FlipCheckTicks=0;
                if(Up.U<Car.FlippedUpDot){
                    if(State.PreviousFlipCheck){ Reset=true; }
                    else { State.PreviousFlipCheck=true; }
                }
                else {
                    State.PreviousFlipCheck=false;
                }
            }
        }
        if(Reset){
            this.ResetPose(Car,BodyState,Forward,Tick,Source,ref Sequence);
            State.ResetThisTick=true;
            State.Sleeping=false;
            State.SleepCounter=0;
            return true;
        }
        // UE の UpdateState(ゲームのスレッド): 後退と 1 速の選択、アクセルとブレーキの読み替え、入力の速さ。
        var RawThrottle=Unit(Intent.Throttle);
        var RawBrake=Unit(Intent.Brake);
        var RawSteering=Signed(Intent.Steering);
        var RawHandbrake=Intent.Handbrake?LogicalScalar.One:LogicalScalar.Zero;
        if(Setup.ReverseAsBrake){
            if(RawBrake>VehicleChaos.KindaSmallNumber&&State.Gear>=0&&State.TargetGear>=0){ SetGear(ref State,Tuning,-1,true); }
            else if(RawThrottle>VehicleChaos.KindaSmallNumber&&State.Gear<=0&&State.TargetGear<=0){ SetGear(ref State,Tuning,1,true); }
        }
        else if(Setup.Transmission.Automatic&&RawThrottle>VehicleChaos.KindaSmallNumber&&State.Gear==0&&State.TargetGear==0){
            SetGear(ref State,Tuning,1,true);
        }
        ThrottleAndBrake(Setup,State,RawThrottle,RawBrake,out var ModifiedThrottle,out var ModifiedBrake);
        State.Steering=VehicleChaos.InterpInput(Setup.SteeringInputRate,DeltaSeconds,State.Steering,RawSteering);
        State.Throttle=VehicleChaos.InterpInput(Setup.ThrottleInputRate,DeltaSeconds,State.Throttle,ModifiedThrottle);
        State.Brake=VehicleChaos.InterpInput(Setup.BrakeInputRate,DeltaSeconds,State.Brake,ModifiedBrake);
        State.Handbrake=VehicleChaos.InterpInput(Setup.HandbrakeInputRate,DeltaSeconds,State.Handbrake,RawHandbrake);
        var Throttle=VehicleChaos.ControlFunction(Setup.ThrottleInputRate,State.Throttle);
        var Brake=VehicleChaos.ControlFunction(Setup.BrakeInputRate,State.Brake);
        var Steering=VehicleChaos.ControlFunction(Setup.SteeringInputRate,State.Steering);
        var Handbrake=VehicleChaos.ControlFunction(Setup.HandbrakeInputRate,State.Handbrake);
        // UE の ProcessSleeping: 操作が無く、全輪が接地し、傾きが小さく、遅い状態が 15 回続いたら眠らせる。眠っている間は力を加えない。
        if(State.Sleeping&&!BodyState.IsSleeping){ State.SleepCounter=0; }
        State.Sleeping=BodyState.IsSleeping;
        var Pressed=Throttle>=WakeTolerance||Brake>=WakeTolerance||VehicleChaos.Abs(Steering-State.PreviousSteering)>=WakeTolerance;
        State.PreviousSteering=Steering;
        if(Pressed){
            State.Sleeping=false;
            State.SleepCounter=0;
        }
        else if(!State.Sleeping&&Tuning.WheelCount>0&&State.WheelsOnGround==Tuning.WheelCount&&Up.U>Setup.SleepSlopeLimit
            &&Velocity.LengthSquared<Setup.SleepThreshold*Setup.SleepThreshold){
            if(State.SleepCounter<SleepCounterThreshold){
                State.SleepCounter++;
            }
            else {
                State.Sleeping=true;
                Commands.Sleep(Tick,Source,Sequence++,Body);
            }
        }
        if(State.Sleeping){ return true; }
        this.Simulate(Tuning,ref State,BodyState,Forward,Right,Up,Throttle,Brake,Steering,Handbrake,Body,Car.CollidesWith,Tick,Source,ref Sequence,DeltaSeconds);
        return true;
    }
    /// <summary>UE の CalcThrottleBrakeInput。</summary>
    private static void ThrottleAndBrake(VehicleSetup Setup,in VehicleState State,LogicalScalar RawThrottle,LogicalScalar RawBrake,out LogicalScalar Throttle,out LogicalScalar Brake){
        Brake=RawBrake;
        Throttle=RawThrottle;
        if(!Setup.ReverseAsBrake){
            if(RawBrake<VehicleChaos.SmallNumber&&RawThrottle<VehicleChaos.SmallNumber&&VehicleChaos.Abs(State.ForwardSpeed)<Setup.StopThreshold){ Brake=LogicalScalar.One; }
            return;
        }
        if(RawThrottle>LogicalScalar.Zero){
            // 後ろへ動いているのに前へ進みたい: bThrottleAsBrake ならブレーキ。
            if(Setup.ThrottleAsBrake&&State.ForwardSpeed< -Setup.WrongDirectionThreshold){
                Brake=LogicalScalar.One;
                Throttle=LogicalScalar.Zero;
            }
        }
        else if(RawBrake>LogicalScalar.Zero){
            // 前へ動いているのに後ろへ下がりたい: ブレーキ。止まりかけ(後退のギア)なら、ブレーキの値でアクセルを踏む。
            if(State.ForwardSpeed>Setup.WrongDirectionThreshold){
                Brake=LogicalScalar.One;
                Throttle=LogicalScalar.Zero;
            }
            else if(State.TargetGear<0){
                Throttle=RawBrake;
                Brake=LogicalScalar.Zero;
            }
        }
        else if(!(State.ForwardSpeed<Setup.StopThreshold&&State.ForwardSpeed> -Setup.StopThreshold)){
            Brake=Setup.IdleBrakeInput;
        }
        Throttle=VehicleChaos.Clamp(Throttle,LogicalScalar.Zero,LogicalScalar.One);
        Brake=VehicleChaos.Clamp(Brake,LogicalScalar.Zero,LogicalScalar.One);
    }
    /// <summary>UE の FSimpleTransmissionSim::SetGear。すぐにでなければ、変速の時間だけニュートラルを通る。</summary>
    private static void SetGear(ref VehicleState State,VehicleTuning Tuning,int Gear,bool Immediate){
        var Transmission=Tuning.Setup.Transmission;
        Gear=Math.Clamp(Gear,-Transmission.ReverseGearRatios.Length,Transmission.ForwardGearRatios.Length);
        State.TargetGear=Gear;
        if(State.TargetGear==State.Gear){ return; }
        if(Immediate||Transmission.GearChangeTime.IsZero){
            State.Gear=State.TargetGear;
        }
        else {
            State.Gear=0;
            State.GearChangeTime=Transmission.GearChangeTime;
        }
    }
    /// <summary>
    /// UE の Manual Reset: 車をその場で <see cref="Vehicle.ResetLift"/> だけ上げ、Roll と Pitch を 0 に、Yaw はそのままにして、速度と角速度を 0 にする。
    /// UE は Actor の原点(<see cref="Vehicle.ResetPivot"/>)を動かすので、その点を基準に回す。
    /// </summary>
    private void ResetPose(in Vehicle Car,in PhysicsBodyState BodyState,LogicalVector3 Forward,ulong Tick,ulong Source,ref uint Sequence){
        var Yaw=VehicleChaos.Atan2(Forward.R,Forward.F);
        if(!LogicalQuaternion.TryCreateAxisAngle(LogicalVector3.UnitU,Yaw,out var Upright)){ Upright=LogicalQuaternion.Identity; }
        if(!BodyState.Rotation.TryRotate(Car.ResetPivot,out var PivotNow)||!Upright.TryRotate(Car.ResetPivot,out var PivotAfter)){
            PivotNow=PivotAfter=LogicalVector3.Zero;
        }
        var Origin=BodyState.Position+PivotNow+LogicalVector3.UnitU*Car.ResetLift;
        var Commands=this.World.Commands;
        Commands.Teleport(Tick,Source,Sequence++,BodyState.Entity,Origin-PivotAfter,Upright);
        Commands.SetVelocity(Tick,Source,Sequence++,BodyState.Entity,LogicalVector3.Zero,LogicalVector3.Zero);
    }
    /// <summary>UE の物理のスレッドの 1 tick(UpdateState・ApplyInput・UpdateSimulation)。</summary>
    private void Simulate(VehicleTuning Tuning,ref VehicleState State,in PhysicsBodyState BodyState,LogicalVector3 Forward,LogicalVector3 Right,LogicalVector3 Up,
        LogicalScalar Throttle,LogicalScalar Brake,LogicalScalar Steering,LogicalScalar Handbrake,CSharpGameEngine.Entities.Entity Body,PhysicsLayerMask Layers,ulong Tick,ulong Source,ref uint Sequence,LogicalScalar DeltaSeconds){
        var Setup=Tuning.Setup;
        var Count=Tuning.WheelCount;
        var Position=BodyState.Position;
        var Linear=BodyState.Velocity.Linear;
        var Angular=BodyState.Velocity.Angular;
        Span<VehicleWheelState>Wheels=stackalloc VehicleWheelState[4];
        for(var Index=0;Index<Count;Index++){ Wheels[Index]=State.WheelAt(Index); }
        Span<LogicalVector3>WheelWorld=stackalloc LogicalVector3[4];
        Span<LogicalVector3>WheelForce=stackalloc LogicalVector3[4];
        Span<LogicalScalar>LocalForward=stackalloc LogicalScalar[4];
        Span<LogicalScalar>LocalRight=stackalloc LogicalScalar[4];
        Span<LogicalScalar>HitDistance=stackalloc LogicalScalar[4];
        Span<LogicalVector3>HitPoint=stackalloc LogicalVector3[4];
        Span<LogicalVector3>HitNormal=stackalloc LogicalVector3[4];
        Span<bool>Traced=stackalloc bool[4];
        Span<bool>EngineBraking=stackalloc bool[4];
        Span<LogicalScalar>Loads=stackalloc LogicalScalar[4];
        // UE の UpdateState: 車輪の位置と速さ、サスペンションの光線、接地。
        var OnGround=0;
        for(var Index=0;Index<Count;Index++){
            var Wheel=Tuning.Wheels[Index];
            if(!BodyState.Rotation.TryRotate(Tuning.Mounts[Index].Position,out var Offset)){ throw new OverflowException(); }
            WheelWorld[Index]=Position+Offset;
            var PointVelocity=Linear+LogicalVector3.Cross(Angular,Offset);
            LocalForward[Index]=LogicalVector3.Dot(PointVelocity,Forward);
            LocalRight[Index]=LogicalVector3.Dot(PointVelocity,Right);
            var Start=WheelWorld[Index]+Up*Wheel.SuspensionMaxRaise;
            var Length=Wheel.SuspensionMaxRaise+Wheel.SuspensionMaxDrop+Wheel.Radius;
            Traced[Index]=this.TryTrace(Start,-Up,Length,Body,Layers,out HitDistance[Index],out HitPoint[Index],out HitNormal[Index],out var Surface);
            if(Traced[Index]){
                OnGround++;
                Wheels[Index].SurfaceFriction=Surface;
            }
            Wheels[Index].InContact=Traced[Index];
        }
        State.OnGround=OnGround>0;
        State.WheelsOnGround=OnGround;
        // UE の ApplyInput: エンジンへのアクセル(二乗)、ブレーキとエンジンブレーキとハンドブレーキのトルク。
        var EngineThrottle=Tuning.HasEngine?Throttle*Throttle:LogicalScalar.Zero;
        State.EngineThrottle=EngineThrottle;
        var EngineBrakingTorque=Tuning.HasEngine?State.EngineRpm*Setup.Engine.EngineBrakeEffect:LogicalScalar.Zero;
        for(var Index=0;Index<Count;Index++){
            var Wheel=Tuning.Wheels[Index];
            var EngineBrakingForce=Throttle<VehicleChaos.SmallNumber&&VehicleChaos.Abs(State.ForwardSpeed)>VehicleChaos.SmallNumber&&Tuning.EngineEnabled[Index]
                ?EngineBrakingTorque
                :LogicalScalar.Zero;
            if(Wheel.AffectedByBrake){
                var BrakeTorque=Wheel.MaxBrakeTorque*Brake;
                Wheels[Index].BrakeTorque=BrakeTorque+EngineBrakingForce;
                EngineBraking[Index]=VehicleChaos.Abs(EngineBrakingForce)>VehicleChaos.Abs(BrakeTorque);
            }
            else {
                // UE のとおり、ブレーキの効かない車輪にはアクセルに関わらずエンジンブレーキをかける。
                Wheels[Index].BrakeTorque=EngineBrakingTorque;
                EngineBraking[Index]=true;
            }
            if(Handbrake>LogicalScalar.Zero&&Wheel.AffectedByHandbrake){
                Wheels[Index].BrakeTorque=Handbrake*Wheel.MaxHandbrakeTorque;
                EngineBraking[Index]=false;
            }
        }
        // UE の ApplyAerodynamics: 重心に、前後の空気抵抗と上下のダウンフォース(進む向きと逆の符号)。
        var AirSign=State.ForwardSpeed<LogicalScalar.Zero?LogicalScalar.One:-LogicalScalar.One;
        var Common=VehicleChaos.AirDensity*State.ForwardSpeed*State.ForwardSpeed*AirSign;
        var Aerodynamic=Forward*(Tuning.DragConstant*Common)+Up*(Tuning.DownforceConstant*Common);
        if(!Aerodynamic.LengthSquared.IsZero){ this.World.Commands.Force(Tick,Source,Sequence++,Body,Aerodynamic); }
        // UE の ProcessMechanicalSimulation: エンジンの回転、自動変速、駆動トルク。
        if(Tuning.HasEngine){
            var WheelRpm=LogicalScalar.Zero;
            for(var Index=0;Index<Count;Index++){
                if(Tuning.EngineEnabled[Index]){ WheelRpm=VehicleChaos.Abs(Wheels[Index].Omega*VehicleChaos.RpmPerRadianPerSecond); }
            }
            var EngineRpmFromWheels=WheelRpm*Tuning.GearRatio(State.Gear);
            var WheelSpeedRpm=VehicleChaos.Abs(EngineRpmFromWheels);
            var OutOfGear=State.Gear==0||State.Gear!=State.TargetGear;
            this.SimulateEngine(Tuning,ref State,OutOfGear,EngineRpmFromWheels,EngineThrottle,DeltaSeconds);
            var Spinning=false;
            for(var Index=0;Index<Count;Index++){
                if(VehicleChaos.Abs(Wheels[Index].SlipMagnitude)>Tuning.Wheels[Index].SlipThreshold){ Spinning=true; }
            }
            SimulateTransmission(Tuning,ref State,State.OnGround&&!Spinning,DeltaSeconds);
            var TransmissionTorque=EngineThrottle*Tuning.EngineTorqueAt(State.EngineRpm)*Tuning.GearRatio(State.Gear)*Setup.Transmission.Efficiency;
            if(WheelSpeedRpm>Setup.Engine.MaxRpm){ TransmissionTorque=LogicalScalar.Zero; }
            for(var Index=0;Index<Count;Index++){
                Wheels[Index].DriveTorque=Tuning.EngineEnabled[Index]?TransmissionTorque*Tuning.TorqueRatio[Index]:LogicalScalar.Zero;
            }
        }
        else {
            for(var Index=0;Index<Count;Index++){ Wheels[Index].DriveTorque=LogicalScalar.Zero; }
        }
        // UE の ApplySuspensionForces: ばね・ダンパーの力を車輪の位置に上向きに加え、タイヤの荷重を決める。外れた車輪は伸びきる。
        for(var Index=0;Index<Count;Index++){
            var Wheel=Tuning.Wheels[Index];
            var MaximumLength=Wheel.SuspensionMaxRaise+Wheel.SuspensionMaxDrop;
            if(Traced[Index]){
                var Displacement=MaximumLength-VehicleChaos.Max(LogicalScalar.Zero,HitDistance[Index]-Wheel.Radius);
                var SpringSpeed=(Wheels[Index].LastDisplacement-Displacement)/DeltaSeconds;
                var SpringForce=Displacement*Wheel.SpringRate-SpringSpeed*Tuning.Damping[Index];
                Wheels[Index].SpringDisplacement=Displacement;
                Wheels[Index].LastDisplacement=Displacement;
                Wheels[Index].SpringForce=SpringForce;
                WheelForce[Index]+=Up*SpringForce;
                var Load=Wheel.WheelLoadRatio*SpringForce+(LogicalScalar.One-Wheel.WheelLoadRatio)*Tuning.RestingForce[Index];
                Wheels[Index].Load=Load;
                Loads[Index]=Load;
                Wheels[Index].SuspensionOffset=VehicleChaos.Clamp(LogicalVector3.Dot(HitPoint[Index]-WheelWorld[Index],Up)+Wheel.Radius,-Wheel.SuspensionMaxDrop,Wheel.SuspensionMaxRaise);
            }
            else {
                Wheels[Index].SpringDisplacement=LogicalScalar.Zero;
                Wheels[Index].SpringForce=LogicalScalar.Zero;
                Wheels[Index].Load=LogicalScalar.Zero;
                Loads[Index]=LogicalScalar.Zero;
                Wheels[Index].SuspensionOffset=-Wheel.SuspensionMaxDrop;
            }
        }
        foreach(var (First,Second) in Tuning.Axles){
            var Difference=(Loads[First]-Loads[Second])*Tuning.Wheels[First].RollbarScaling;
            WheelForce[First]+=Up*Difference;
            WheelForce[Second]-=Up*Difference;
        }
        // UE の ProcessSteering: 速さの効きを掛けた入力から、車輪ごとの舵角。
        var SteeringInput=Steering*Tuning.SteeringScale(State.ForwardSpeed);
        for(var Index=0;Index<Count;Index++){
            Wheels[Index].SteeringAngleDegrees=Tuning.WheelSteeringAngle(Index,SteeringInput);
        }
        // UE の ApplyWheelFrictionForces: 舵の向きのタイヤの力を、地面の法線と車の右から作る向きで、車輪の位置に加える。
        var MassPerWheel=Count>0?Setup.Mass/LogicalScalar.FromInt64(Count):LogicalScalar.Zero;
        for(var Index=0;Index<Count;Index++){
            var Wheel=Tuning.Wheels[Index];
            var Contact=Loads[Index]>VehicleChaos.SmallNumber;
            VehicleChaos.SinCos(Wheels[Index].SteeringAngleDegrees*VehicleChaos.RadiansPerDegree,out var Sin,out var Cos);
            var WheelForward=LocalForward[Index]*Cos+LocalRight[Index]*Sin;
            var WheelSide=-(LocalForward[Index]*Sin)+LocalRight[Index]*Cos;
            if(!Contact){
                WheelForward=LogicalScalar.Zero;
                WheelSide=LogicalScalar.Zero;
            }
            SimulateWheel(Wheel,Tuning.Inertia[Index],ref Wheels[Index],Contact,Contact?Loads[Index]:LogicalScalar.Zero,WheelForward,WheelSide,EngineBraking[Index],MassPerWheel,DeltaSeconds,out var Longitudinal,out var Lateral);
            if(!Contact||!Traced[Index]){ continue; }
            var LocalLongitudinal=Longitudinal*Cos-Lateral*Sin;
            var LocalLateral=Longitudinal*Sin+Lateral*Cos;
            var Normal=HitNormal[Index];
            var GroundForward=LogicalVector3.Cross(Normal,Right);
            var GroundRight=LogicalVector3.Cross(GroundForward,Normal);
            WheelForce[Index]+=GroundForward*LocalLongitudinal+GroundRight*LocalLateral;
        }
        for(var Index=0;Index<Count;Index++){
            if(!WheelForce[Index].LengthSquared.IsZero){ this.World.Commands.ForceAtPoint(Tick,Source,Sequence++,Body,WheelForce[Index],WheelWorld[Index]); }
        }
        State.Wheel0=Count>0?Wheels[0]:default;
        State.Wheel1=Count>1?Wheels[1]:default;
        State.Wheel2=Count>2?Wheels[2]:default;
        State.Wheel3=Count>3?Wheels[3]:default;
    }
    /// <summary>UE の FSimpleEngineSim::SetEngineRPM と Simulate。ギアが入っていなければ自由に回り、入っていれば車輪から決まる回転へ近づく。アイドルより下がらない。</summary>
    private void SimulateEngine(VehicleTuning Tuning,ref VehicleState State,bool FreeRunning,LogicalScalar EngineRpmFromWheels,LogicalScalar EngineThrottle,LogicalScalar DeltaSeconds){
        var Engine=Tuning.Setup.Engine;
        var IdleOmega=Engine.IdleRpm/VehicleChaos.RpmPerRadianPerSecond;
        var MaximumOmega=Engine.MaxRpm/VehicleChaos.RpmPerRadianPerSecond;
        var Omega=State.EngineOmega;
        if(FreeRunning){
            var Torque=EngineThrottle*Tuning.EngineTorqueAt(State.EngineRpm);
            Omega+=Torque*DeltaSeconds/Engine.RevUpMoi;
            var Fraction=MaximumOmega.IsZero?LogicalScalar.Zero:(Omega-VehicleChaos.Half*IdleOmega)/MaximumOmega;
            Omega-=Engine.RevDownRate*Fraction*Fraction*DeltaSeconds;
        }
        else {
            var Target=VehicleChaos.Clamp(VehicleChaos.Abs(EngineRpmFromWheels),Engine.IdleRpm,Engine.MaxRpm)/VehicleChaos.RpmPerRadianPerSecond;
            Omega+=(Target-Omega)*EngineFollowRate*DeltaSeconds;
        }
        if(Omega<IdleOmega){ Omega=IdleOmega; }
        State.EngineOmega=Omega;
        State.EngineRpm=Omega*VehicleChaos.RpmPerRadianPerSecond;
    }
    /// <summary>UE の FSimpleTransmissionSim::Simulate: 自動変速は、変速中でなく許されているとき、上げの回転数以上で 1 段上げ、下げの回転数以下で 1 段下げる(1 速より下へは下げない)。</summary>
    private static void SimulateTransmission(VehicleTuning Tuning,ref VehicleState State,bool AllowedToChangeGear,LogicalScalar DeltaSeconds){
        var Transmission=Tuning.Setup.Transmission;
        if(Transmission.Automatic&&State.Gear==State.TargetGear&&AllowedToChangeGear){
            if(State.EngineRpm>=Transmission.ChangeUpRpm){
                SetGear(ref State,Tuning,State.Gear>0?State.TargetGear+1:State.TargetGear-1,false);
            }
            else if(State.EngineRpm<=Transmission.ChangeDownRpm&&Math.Abs(State.Gear)>1){
                SetGear(ref State,Tuning,State.Gear>0?State.TargetGear-1:State.TargetGear+1,false);
            }
        }
        if(State.Gear!=State.TargetGear){
            State.GearChangeTime-=DeltaSeconds;
            if(State.GearChangeTime<=LogicalScalar.Zero){
                State.GearChangeTime=LogicalScalar.Zero;
                State.Gear=State.TargetGear;
            }
        }
    }
    /// <summary>
    /// UE の FSimpleWheelSim::Simulate: 縦はブレーキか駆動の力、横は滑りの角 × コーナリング剛性(止めるのに要る力まで)。両方を摩擦の円(荷重 × 地面の摩擦 ×
    /// 倍率)で切る。駆動の余りとブレーキの固まりで車輪の回転を決める。<paramref name="Longitudinal"/> と <paramref name="Lateral"/> は舵の向きの力(N)。
    /// </summary>
    private static void SimulateWheel(in VehicleWheelSetup Wheel,LogicalScalar Inertia,ref VehicleWheelState State,bool Contact,LogicalScalar Load,LogicalScalar Forward,LogicalScalar Side,
        bool EngineBraking,LogicalScalar MassPerWheel,LogicalScalar DeltaSeconds,out LogicalScalar Longitudinal,out LogicalScalar Lateral){
        Longitudinal=LogicalScalar.Zero;
        Lateral=LogicalScalar.Zero;
        var Radius=Wheel.Radius;
        var SlipAngle=VehicleChaos.SlipAngle(Side,Forward);
        var DriveForce=State.DriveTorque/Radius;
        var BrakeForce=State.BrakeTorque/Radius;
        var Grip=Load*State.SurfaceFriction*Wheel.FrictionForceMultiplier;
        var Braking=State.BrakeTorque>VehicleChaos.Abs(State.DriveTorque);
        var Locked=false;
        var Excess=LogicalScalar.Zero;
        if(Load>VehicleChaos.SmallNumber){
            if(VehicleChaos.Abs(DriveForce)>Grip){
                Excess=(VehicleChaos.Abs(DriveForce)-Grip)*Radius*(DriveForce<LogicalScalar.Zero?-LogicalScalar.One:LogicalScalar.One);
            }
            if(Braking){
                var StopForce=VehicleChaos.Abs(MassPerWheel*StopForceFactor*Forward/DeltaSeconds);
                Longitudinal=VehicleChaos.Clamp(BrakeForce,-StopForce,StopForce);
                if(Forward>LogicalScalar.Zero){ Longitudinal=-Longitudinal; }
            }
            else {
                Longitudinal=DriveForce;
            }
            var SideStopForce=VehicleChaos.Abs(MassPerWheel*StopForceFactor*Side/DeltaSeconds);
            Lateral=VehicleChaos.Min(SlipAngle*Wheel.CorneringStiffness,SideStopForce);
            if(Side>LogicalScalar.Zero){ Lateral=-Lateral; }
            var LengthSquared=Longitudinal*Longitudinal+Lateral*Lateral;
            if(LengthSquared>FrictionCircleThreshold){
                var Length=VehicleChaos.Sqrt(LengthSquared);
                var Clip=Grip/Length;
                if(Clip<LogicalScalar.One){
                    if(Braking&&!EngineBraking){ Locked=true; }
                    Longitudinal*=Clip*Wheel.SideSlipModifier;
                    Lateral*=Clip*Wheel.SideSlipModifier;
                }
            }
        }
        else if(VehicleChaos.Abs(State.Omega)<Wheel.MaxWheelspinRotation){
            Excess=State.DriveTorque;
        }
        var SlipOmega=Inertia.IsZero?LogicalScalar.Zero:VehicleChaos.Clamp(Excess/Inertia*DeltaSeconds,-Wheel.MaxWheelspinRotation,Wheel.MaxWheelspinRotation);
        if(Locked){
            State.Omega*=LockedSpinDecay;
        }
        else if(Contact){
            State.Omega=Forward/Radius+SlipOmega;
        }
        else {
            State.Omega+=SlipOmega;
        }
        // UE は回転の角を FMath::Modf で 1 回転の内に戻す(符号は残る)。
        var Angle=State.AngularPosition+State.Omega*DeltaSeconds;
        var Turns=Angle.Raw/LogicalMath.TwoPi.Raw;
        State.AngularPosition=Angle-LogicalMath.TwoPi*LogicalScalar.FromInt64((long)Turns);
        State.SlipMagnitude=State.Omega*Radius-Forward;
        State.SkidMagnitude=Side;
        if(!Contact){
            Longitudinal=LogicalScalar.Zero;
            Lateral=LogicalScalar.Zero;
        }
    }
    /// <summary>
    /// サスペンションの光線(UE の PerformSuspensionTraces の Raycast)。車体自身は飛ばす。当たれば始点からの距離、点、法線、地面の摩擦(UE の物理マテリアルの Friction)。
    /// </summary>
    private bool TryTrace(LogicalVector3 Start,LogicalVector3 Direction,LogicalScalar Length,CSharpGameEngine.Entities.Entity Body,PhysicsLayerMask Layers,
        out LogicalScalar Distance,out LogicalVector3 Point,out LogicalVector3 Normal,out LogicalScalar Friction){
        Distance=LogicalScalar.Zero;
        Point=LogicalVector3.Zero;
        Normal=LogicalVector3.UnitU;
        Friction=LogicalScalar.One;
        this.Hits.Clear();
        this.World.Raycast(new PhysicsRay(Start,Direction,Length),Layers,this.Hits);
        foreach(var Hit in this.Hits){
            if(Hit.Entity==Body){ continue; }
            Distance=Hit.Distance;
            Point=Hit.Point;
            Normal=Hit.Normal;
            if(this.World.TryGetMaterial(Hit.Entity,out var Material)){ Friction=Material.Friction; }
            return true;
        }
        return false;
    }
    private static LogicalScalar Unit(float Value)=>float.IsFinite(Value)?LogicalScalar.FromSingle(Math.Clamp(Value,0f,1f)):LogicalScalar.Zero;
    private static LogicalScalar Signed(float Value)=>float.IsFinite(Value)?LogicalScalar.FromSingle(Math.Clamp(Value,-1f,1f)):LogicalScalar.Zero;
}
