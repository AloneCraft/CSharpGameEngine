using System.Text.Json;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Vehicles;
using Xunit.Abstractions;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0109: Chaos 相当の車の外から見える振る舞い。車体は動的な剛体で、UE の SportsCar の値(<see cref="ChaosVehicleTrack.SportsCar"/>)で、
/// 決定的な物理世界の上を走る。範囲は UE の式と値から見積もった物理の量で決め、実装の途中の値は比べない。
/// </summary>
public sealed class ChaosVehicleTests(ITestOutputHelper Output) {
    private static float F(LogicalScalar Value)=>ChaosVehicleTrack.F(Value);
    /// <summary>止まるまで(または上限の tick まで)1 tick ずつ進め、かかった tick 数を返す。</summary>
    private static int RunUntil(ChaosVehicleTrack Car,Func<ChaosVehicleTrack,bool>Done,int Limit,float Throttle=0f,float Brake=0f,float Steering=0f,bool Handbrake=false){
        for(var Tick=0;Tick<Limit;Tick++){
            if(Done(Car)){ return Tick; }
            Car.Run(1,Throttle,Brake,Steering,Handbrake);
        }
        return Limit;
    }
    [Fact]
    public void RestsOnItsSuspensionsWithEveryWheelOnTheGroundThenSleeps(){
        var Car=new ChaosVehicleTrack();
        Car.Run(180);
        Output.WriteLine(Car.Describe());
        Assert.Equal(4,Car.State.WheelsOnGround);
        Assert.True(Car.State.OnGround);
        // 重心は UE の骨と重心から地面の上 0.5〜0.65 m(車輪の半径 0.39/0.40 m、骨は重心の 0.25 m 下、ばねが静止の荷重で少し縮む)。
        Assert.InRange(Car.Y,0.5f,0.65f);
        Assert.InRange(Car.UpDot,0.999f,1.0001f);
        // 静止の荷重はばね上の質量 × 重力: 4 輪で車の重さ(1500 kg × 9.81 m/s²)を支える。
        var Total=F(Car.State.Wheel0.Load+Car.State.Wheel1.Load+Car.State.Wheel2.Load+Car.State.Wheel3.Load);
        Assert.InRange(Total,1500f*9.80665f*0.98f,1500f*9.80665f*1.02f);
        // ばねは静止の荷重で縮み、車輪は休みの位置より上にある(UE の SuspensionOffset が正)。
        Assert.InRange(F(Car.State.Wheel0.SuspensionOffset),0.01f,0.1f);
        Assert.InRange(F(Car.State.Wheel2.SuspensionOffset),0.01f,0.1f);
        // UE の ProcessSleeping: 操作が無く、全輪が接地して遅い状態が続くと眠り、物理の剛体も眠る。
        Assert.True(Car.State.Sleeping);
        Assert.True(Car.Body.IsSleeping);
        var Before=Car.Body.Position;
        Car.Run(180);
        Assert.Equal(Before,Car.Body.Position);
    }
    [Fact]
    public void AHeavierCarSinksFurtherOnTheSameSprings(){
        var Light=new ChaosVehicleTrack();
        var HeavySetup=ChaosVehicleTrack.SportsCar();
        HeavySetup.Mass=2000;
        var Heavy=new ChaosVehicleTrack(Setup:HeavySetup);
        Light.Run(180);
        Heavy.Run(180);
        Assert.True(Heavy.Y<Light.Y-0.02f,$"heavy {Heavy.Y} light {Light.Y}");
        Assert.True(F(Heavy.State.Wheel2.SuspensionOffset)>F(Light.State.Wheel2.SuspensionOffset));
    }
    [Fact]
    public void FallsFromAHeightLandsOnItsWheelsAndSettles(){
        var Car=new ChaosVehicleTrack(StartHeight:3f);
        Car.Run(20);
        Assert.Equal(0,Car.State.WheelsOnGround);
        Assert.False(Car.State.OnGround);
        // 空中の車輪は伸びきる(UE の -MaxDrop)。
        Assert.Equal(-0.1f,F(Car.State.Wheel0.SuspensionOffset),3);
        var Landed=RunUntil(Car,static Car=>Car.State.WheelsOnGround==4,120);
        Assert.True(Landed<120);
        Car.Run(240);
        Assert.InRange(Car.Y,0.5f,0.65f);
        Assert.InRange(Car.UpDot,0.999f,1.0001f);
    }
    [Fact]
    public void FullThrottleReachesOneHundredKilometresPerHourInAboutFourSecondsAndChangesGearAtSixThousand(){
        var Car=new ChaosVehicleTrack();
        Car.Run(60);
        var ChangeRpm=0f;
        var Ticks=0;
        var NeutralTicks=0;
        for(;Ticks<60*10&&Car.KilometersPerHour<100f;Ticks++){
            var Before=Car.State.TargetGear;
            var Rpm=F(Car.State.EngineRpm);
            Car.Run(1,Throttle:1f);
            if(Before==1&&Car.State.TargetGear==2){ ChangeRpm=Rpm; }
            if(Car.State.Gear==0&&Car.State.TargetGear==2){ NeutralTicks++; }
        }
        Output.WriteLine(FormattableString.Invariant($"0-100 km/h {Ticks/60f:F2} s, change up at {ChangeRpm:F0} rpm, neutral {NeutralTicks} ticks. {Car.Describe()}"));
        // UE の SportsCar: 750 N·m × 1 速 4.25 × 2.81 × 0.9 を後輪 2 つで、1500 kg。2 速へは 6000 rpm(ChangeUpRPM)で上げ、0.2 秒ニュートラルを通る。
        Assert.InRange(Ticks/60f,3f,5.5f);
        Assert.InRange(ChangeRpm,5900f,7000f);
        Assert.InRange(NeutralTicks,11,13);
        Assert.Equal(2,Car.State.Gear);
    }
    [Fact]
    public void TheTopSpeedIsLimitedByTheAirInFifthGear(){
        var Car=new ChaosVehicleTrack(Floor:false).AddStrip(6000);
        Car.Run(60);
        Car.Run(60*50,Throttle:1f);
        var Earlier=Car.Speed;
        Car.Run(60*10,Throttle:1f);
        Output.WriteLine(FormattableString.Invariant($"top {Car.KilometersPerHour:F1} km/h (+{(Car.Speed-Earlier)*3.6f:F2} km/h in the last 10 s) {Car.Describe()}"));
        Assert.Equal(5,Car.State.Gear);
        // 5 速(2.81)の 7000 rpm の車輪の速さ(約 105 m/s)より遅く、空気抵抗(0.5 × 1.225 × 1.8 × 1.44 × 0.31 × v²)と駆動力が釣り合って速さが止まる。
        Assert.InRange(Car.Speed,60f,105f);
        Assert.InRange(Car.Speed-Earlier,-0.5f,1.5f);
    }
    [Fact]
    public void FullBrakesStopFromOneHundredKilometresPerHourInAbout20Metres(){
        var Car=new ChaosVehicleTrack();
        Car.Run(60);
        RunUntil(Car,static Car=>Car.KilometersPerHour>=100f,60*10,Throttle:1f);
        var Start=Car.Z;
        var Ticks=RunUntil(Car,static Car=>Car.Speed<=0.5f,60*10,Brake:1f);
        var Distance=Car.Z-Start;
        Output.WriteLine(FormattableString.Invariant($"stopped in {Distance:F2} m and {Ticks/60f:F2} s. {Car.Describe()}"));
        // 前輪 4500 N·m、後輪 1500 N·m のブレーキは、荷重 × 摩擦の倍率(前 3、後 4)の摩擦の円より弱く、約 2 G で止まる(v² / 2a ≈ 20 m)。
        Assert.InRange(Distance,12f,35f);
        Assert.InRange(Ticks/60f,1f,3f);
        Assert.InRange(Car.HeadingDegrees,-2f,2f);
    }
    [Fact]
    public void HoldingTheBrakeAfterStoppingReversesInReverseGear(){
        var Car=new ChaosVehicleTrack();
        Car.Run(60);
        Car.Run(120,Brake:1f);
        Assert.Equal(-1,Car.State.Gear);
        Assert.True(Car.Speed< -1f,$"speed {Car.Speed}");
        Assert.True(Car.Z< -1f,$"z {Car.Z}");
    }
    [Fact]
    public void SteeringRightTurnsRightWithAckermannAnglesAndATightRadiusAtLowSpeed(){
        var Car=new ChaosVehicleTrack();
        Car.Run(60);
        RunUntil(Car,static Car=>Car.Speed>=5f,600,Throttle:1f);
        Car.Run(120,Throttle:0.15f,Steering:1f);
        var Start=Car.HeadingDegrees;
        var StartX=Car.X;
        var StartZ=Car.Z;
        Car.Run(60,Throttle:0.15f,Steering:1f);
        var Turned=Start-Car.HeadingDegrees;
        var Rate=Turned*MathF.PI/180f;
        var Radius=Car.Speed/Rate;
        Output.WriteLine(FormattableString.Invariant($"turned {Turned:F1} degrees in 1 s at {Car.Speed:F2} m/s: radius {Radius:F2} m. FL {F(Car.State.Wheel0.SteeringAngleDegrees):F1} FR {F(Car.State.Wheel1.SteeringAngleDegrees):F1}"));
        // 右(前 × 上 = -R)へ曲がる: 向きの角(+R の側が正)は減る。
        Assert.True(Turned>20f,$"turned {Turned}");
        Assert.True(Car.X<StartX||Car.Z!=StartZ);
        // ホイールベース 2.64 m、舵角 30° 前後: 運動学の半径 L / tan(δ) ≈ 4.6 m に、タイヤの滑りが少し足される。
        Assert.InRange(Radius,3.5f,9f);
        // アッカーマン: 右へ切ると右(内側)の前輪が左(外側)より大きく切れ、後輪は切れない。
        var Left=F(Car.State.Wheel0.SteeringAngleDegrees);
        var Right=F(Car.State.Wheel1.SteeringAngleDegrees);
        Assert.True(Right>Left&&Left>0f,$"left {Left} right {Right}");
        Assert.Equal(0f,F(Car.State.Wheel2.SteeringAngleDegrees));
    }
    [Fact]
    public void SteeringLeftTurnsTheOtherWayAndAStandingCarDoesNotTurn(){
        var Car=new ChaosVehicleTrack();
        Car.Run(60);
        Car.Run(120,Steering:-1f);
        Assert.InRange(Car.HeadingDegrees,-0.05f,0.05f);
        // 止まっていても前輪は切れる(UE の速さの効きは 0 km/h で 1)。
        Assert.True(F(Car.State.Wheel0.SteeringAngleDegrees)<-30f);
        RunUntil(Car,static Car=>Car.Speed>=5f,600,Throttle:1f,Steering:-1f);
        Car.Run(60,Throttle:0.15f,Steering:-1f);
        Assert.True(Car.HeadingDegrees>15f,$"heading {Car.HeadingDegrees}");
    }
    [Fact]
    public void FullLockAtFiveMetresPerSecondTurnsTheFrontWheelsTheirFortyDegreesOnTheInside(){
        var Tuning=new VehicleTuning(ChaosVehicleTrack.SportsCar(),ChaosVehicleTrack.SportsCarWheels);
        // UE の GetSteeringAngle(Ackermann): 内側の車輪が MaxSteerAngle、外側は棒の幾何で小さい。入力 0 は 0。
        var Inside=F(Tuning.WheelSteeringAngle(1,LogicalScalar.One));
        var Outside=F(Tuning.WheelSteeringAngle(0,LogicalScalar.One));
        Assert.Equal(40f,Inside,2);
        Assert.InRange(Outside,20f,39.9f);
        Assert.Equal(0f,F(Tuning.WheelSteeringAngle(0,LogicalScalar.Zero)),3);
        Assert.Equal(-Inside,F(Tuning.WheelSteeringAngle(0,-LogicalScalar.One)),2);
        Assert.Equal(0f,F(Tuning.WheelSteeringAngle(2,LogicalScalar.One)));
        // 速さの効き(横軸はマイル毎時): 0 で 1、60 mph で 0.4、120 mph より先は 0.3。
        Assert.Equal(1f,F(Tuning.SteeringScale(LogicalScalar.Zero)),3);
        Assert.Equal(0.4f,F(Tuning.SteeringScale(LogicalScalar.FromSingle(60f/2.23694185f))),2);
        Assert.Equal(0.3f,F(Tuning.SteeringScale(LogicalScalar.FromInt64(200))),3);
    }
    [Fact]
    public void TheEngineFollowsUesTorqueCurveGearsAndSprungMasses(){
        var Tuning=new VehicleTuning(ChaosVehicleTrack.SportsCar(),ChaosVehicleTrack.SportsCarWheels);
        // FC_Torque_SportsCar を最大値で割った曲線 × 750 N·m: 3500 rpm が最大、7000 rpm で 0、アイドル(900)は 1000 rpm の 0.5 倍の付近。
        Assert.Equal(750f,F(Tuning.EngineTorqueAt(3500)),1);
        Assert.Equal(0f,F(Tuning.EngineTorqueAt(7000)),3);
        Assert.InRange(F(Tuning.EngineTorqueAt(900)),350f,420f);
        Assert.True(Tuning.EngineTorqueAt(1800)>Tuning.EngineTorqueAt(1000));
        Assert.True(Tuning.EngineTorqueAt(6500)<Tuning.EngineTorqueAt(5000));
        Assert.Equal(4.25f*2.81f,F(Tuning.GearRatio(1)),3);
        Assert.Equal(2.81f,F(Tuning.GearRatio(5)),3);
        Assert.Equal(-4.04f*2.81f,F(Tuning.GearRatio(-1)),3);
        Assert.Equal(0f,F(Tuning.GearRatio(0)));
        // 後輪駆動: 後輪 2 つが半分ずつ。
        Assert.Equal(new[]{false,false,true,true},Tuning.EngineEnabled);
        Assert.Equal(0.5f,F(Tuning.TorqueRatio[2]),3);
        // ばね上の質量: 合計が車の質量で、重心から遠い前輪の方が軽い(てこの比)。左右は同じ。
        Assert.Equal(1500f,F(Tuning.SprungMass.Aggregate(LogicalScalar.Zero,static (Sum,Mass)=>Sum+Mass)),2);
        Assert.Equal(F(Tuning.SprungMass[0]),F(Tuning.SprungMass[1]),3);
        Assert.True(Tuning.SprungMass[0]<Tuning.SprungMass[2]);
        Assert.Equal(750f*1.2224f/(1.4196f+1.2224f),F(Tuning.SprungMass[0]),1);
    }
    [Fact]
    public void InputRatesRiseAndFallAtUesSpeeds(){
        var Rate=new VehicleInputRate(6,10,VehicleInputCurve.Linear);
        var Tick=ChaosVehicleTrack.DeltaSeconds;
        Assert.Equal(0.1f,F(VehicleChaos.InterpInput(Rate,Tick,LogicalScalar.Zero,LogicalScalar.One)),4);
        Assert.Equal(1f-10f/60f,F(VehicleChaos.InterpInput(Rate,Tick,LogicalScalar.One,LogicalScalar.Zero)),4);
        Assert.Equal(0.25f,F(VehicleChaos.ControlFunction(new VehicleInputRate(1,1,VehicleInputCurve.Squared),LogicalScalar.FromSingle(0.5f))),4);
        Assert.Equal(-0.25f,F(VehicleChaos.ControlFunction(new VehicleInputRate(1,1,VehicleInputCurve.Squared),LogicalScalar.FromSingle(-0.5f))),4);
    }
    [Fact]
    public void ACarDrivenOffARampFliesAndLandsOnItsWheels(){
        var Car=new ChaosVehicleTrack();
        Car.AddQuad(new(-20,0,20),new(20,0,20),new(20,3,40),new(-20,3,40));
        Car.Run(60);
        var Airborne=0;
        var Peak=0f;
        for(var Tick=0;Tick<60*8;Tick++){
            Car.Run(1,Throttle:1f);
            if(Car.State.WheelsOnGround==0){ Airborne++; }
            Peak=MathF.Max(Peak,Car.Y);
        }
        Output.WriteLine(FormattableString.Invariant($"airborne {Airborne} ticks, peak {Peak:F2} m. {Car.Describe()}"));
        // 坂の上の端(3 m)から飛び出して 1 秒以上浮き、全輪で着地して、上を向いたまま走り続ける。
        Assert.True(Airborne>=30,$"airborne {Airborne}");
        Assert.True(Peak>3.5f,$"peak {Peak}");
        Assert.Equal(4,Car.State.WheelsOnGround);
        Assert.InRange(Car.UpDot,0.99f,1.0001f);
        Assert.InRange(Car.Y,0.5f,0.7f);
        Assert.True(Car.Z>60f);
    }
    [Fact]
    public void StopsAtAWallInFrontOfTheCar(){
        var Car=new ChaosVehicleTrack();
        Car.AddQuad(new(-20,0,30),new(20,0,30),new(20,10,30),new(-20,10,30));
        Car.Run(60);
        Car.Run(60*6,Throttle:1f);
        // 車体の箱(前後 2.25 m)が壁(z = 30)の手前で止まり、通り抜けない。
        Assert.InRange(Car.Z,26.5f,30f-2.2f);
        Assert.InRange(Car.UpDot,0.99f,1.0001f);
    }
    [Fact]
    public void AnUpsideDownCarIsResetAfterTwoFlippedChecksThreeSecondsApart(){
        var Car=new ChaosVehicleTrack(StartHeight:1.5f,Rotation:LogicalQuaternion.FromEulerRuf(new LogicalVector3(0,0,LogicalMath.Pi)));
        Car.Run(170);
        Assert.True(Car.UpDot< -0.9f);
        // UE の Flipped Check: 3 秒ごと。最初の判定で記録し、次の判定(6 秒)でリセットする。
        Car.Run(20);
        Assert.True(Car.State.PreviousFlipCheck);
        Assert.True(Car.UpDot< -0.9f);
        Car.Run(165);
        Assert.True(Car.UpDot< -0.9f);
        Car.Run(10);
        Assert.InRange(Car.UpDot,0.999f,1.0001f);
        Car.Run(240);
        Assert.Equal(4,Car.State.WheelsOnGround);
        Assert.InRange(Car.Y,0.5f,0.65f);
    }
    [Fact]
    public void TheResetLiftsTheCarHalfAMetreUprightKeepsItsYawAndStopsIt(){
        var Car=new ChaosVehicleTrack(Rotation:LogicalQuaternion.FromEulerRuf(new LogicalVector3(LogicalScalar.FromSingle(0.3f),LogicalScalar.FromSingle(0.7f),LogicalScalar.FromSingle(0.4f))));
        Car.Run(30);
        var Heading=Car.HeadingDegrees;
        Car.Intent.Reset();
        Car.Run(1);
        Assert.True(Car.State.ResetThisTick);
        Assert.False(Car.Intent.ResetRequested);
        Assert.InRange(Car.UpDot,0.9999f,1.0001f);
        Assert.Equal(Heading,Car.HeadingDegrees,0);
        Assert.True(F(Car.Body.Velocity.Angular.LengthSquared)<1e-6f);
    }
    [Fact]
    public void ALowFrictionSurfaceSpinsTheRearWheelsAndAcceleratesSlower(){
        var Grippy=new ChaosVehicleTrack();
        var Icy=new ChaosVehicleTrack(Friction:LogicalScalar.FromSingle(0.1f));
        foreach(var Car in new[]{Grippy,Icy}){
            Car.Run(60);
            Car.Run(60,Throttle:1f);
        }
        Output.WriteLine("grippy "+Grippy.Describe());
        Output.WriteLine("icy "+Icy.Describe());
        // タイヤの力は 荷重 × 地面の摩擦 × 倍率 までで、それを超える駆動は車輪を空転させる(UE の ExcessTorque)。
        Assert.True(Icy.Speed<Grippy.Speed*0.6f,$"icy {Icy.Speed} grippy {Grippy.Speed}");
        Assert.True(F(Icy.State.Wheel2.SlipMagnitude)>F(Grippy.State.Wheel2.SlipMagnitude)+0.5f);
        Assert.InRange(F(Icy.State.Wheel2.SurfaceFriction),0.099f,0.101f);
    }
    [Fact]
    public void TheHandbrakeLocksTheRearWheelsSoTheCarSlidesSideways(){
        var Grip=new ChaosVehicleTrack();
        var Slide=new ChaosVehicleTrack();
        foreach(var Car in new[]{Grip,Slide}){
            Car.Run(60);
            RunUntil(Car,static Car=>Car.Speed>=15f,600,Throttle:1f);
        }
        Grip.Run(45,Steering:1f);
        Slide.Run(45,Steering:1f,Handbrake:true);
        Output.WriteLine("grip "+Grip.Describe());
        Output.WriteLine("slide "+Slide.Describe());
        // ハンドブレーキ(後輪 6000 N·m)は後輪を止め、摩擦の円を縦の力が占めて横の力が減るので、後ろが流れる: 車体の横滑りの角が大きい。
        var SlideAngle=MathF.Atan2(MathF.Abs(Slide.Lateral),MathF.Abs(Slide.Speed))*180f/MathF.PI;
        var GripAngle=MathF.Atan2(MathF.Abs(Grip.Lateral),MathF.Abs(Grip.Speed))*180f/MathF.PI;
        Assert.True(SlideAngle>GripAngle*2f,$"slide {SlideAngle} grip {GripAngle}");
        Assert.True(MathF.Abs(F(Slide.State.Wheel2.Omega))<MathF.Abs(F(Grip.State.Wheel2.Omega)));
    }
    [Fact]
    public void ReleasingTheThrottleInGearSlowsTheCarByEngineBraking(){
        var Braking=new ChaosVehicleTrack();
        var NoEngineBrakeSetup=ChaosVehicleTrack.SportsCar();
        NoEngineBrakeSetup.Engine=NoEngineBrakeSetup.Engine with { EngineBrakeEffect=LogicalScalar.Zero };
        var Free=new ChaosVehicleTrack(Setup:NoEngineBrakeSetup);
        foreach(var Car in new[]{Braking,Free}){
            Car.Run(60);
            Car.Run(150,Throttle:1f);
        }
        Assert.Equal(Braking.Speed,Free.Speed,1);
        Braking.Run(120);
        Free.Run(120);
        // UE の EngineBrakeEffect: アクセルを離すと、回転数 × 0.2 のブレーキが駆動輪にかかる。0 なら空気抵抗だけで減る。
        Assert.True(Braking.Speed<Free.Speed-1f,$"braking {Braking.Speed} free {Free.Speed}");
        Assert.True(Free.Speed>0f);
    }
    [Fact]
    public void WithoutGroundTheWheelsFindNothingAndSpinUpUnderThrottle(){
        var Car=new ChaosVehicleTrack(Floor:false,StartHeight:100f);
        Car.Run(30,Throttle:1f);
        Assert.Equal(0,Car.State.WheelsOnGround);
        Assert.Equal(0f,F(Car.State.Wheel2.Load));
        // UE: 接地していない駆動輪は、駆動トルク / 慣性で回り、MaxWheelspinRotation(30 rad/s)の 1 tick の上限ごとに増える。
        Assert.True(F(Car.State.Wheel2.Omega)>1f,$"omega {F(Car.State.Wheel2.Omega)}");
        Assert.Equal(0f,F(Car.State.Wheel0.Omega));
        Assert.True(Car.Y<100f);
    }
    [Fact]
    public void ACarWithoutASetupIsLeftToThePhysicsAlone(){
        var Car=new ChaosVehicleTrack(StartHeight:2f);
        Car.Vehicle.Setup=null;
        Car.Run(60,Throttle:1f);
        Assert.Equal(0,Car.State.WheelsOnGround);
        Assert.Equal(0f,Car.Speed,3);
        // 車輪の力が無いので、車体の箱が床に落ちて止まる。
        Assert.InRange(Car.Y,0.4f,0.5f);
    }
    [Fact]
    public void ARunIsDeterministic(){
        static (PhysicsBodyState Body,VehicleState State) Drive(){
            var Car=new ChaosVehicleTrack();
            Car.Run(60);
            Car.Run(150,Throttle:1f);
            Car.Run(120,Throttle:0.7f,Steering:0.6f);
            Car.Run(60,Brake:1f,Steering:-0.3f,Handbrake:true);
            Car.Run(60,Throttle:0.4f,Steering:-1f);
            return (Car.Body,Car.State);
        }
        var First=Drive();
        var Second=Drive();
        Assert.Equal(First.Body.Position,Second.Body.Position);
        Assert.Equal(First.Body.Rotation,Second.Body.Rotation);
        Assert.Equal(First.Body.Velocity,Second.Body.Velocity);
        Assert.Equal(First.State,Second.State);
    }
    [Fact]
    public void TheDrivingStateGoesIntoASnapshotAndTheSetupStaysWithTheScene(){
        // スナップショットは値の型だけを書く。設定(データアセットの参照と車輪の配列)は入らず、復元は読み直したシーンの値を保つ。
        Assert.True(CSharpGameEngine.Entities.EcsSnapshotComponentCodec<Vehicle>.IsTransient);
        var Car=new ChaosVehicleTrack();
        Car.Run(60);
        Car.Run(90,Throttle:1f,Steering:0.4f);
        var Driving=Car.State;
        Assert.False(CSharpGameEngine.Entities.EcsSnapshotComponentCodec<VehicleState>.IsTransient);
        var Payload=CSharpGameEngine.Entities.EcsSnapshotComponentCodec<VehicleState>.Serialize(Driving);
        Assert.True(CSharpGameEngine.Entities.EcsSnapshotComponentCodec<VehicleState>.TryDeserialize(Payload,out var Restored,out var Error),Error);
        Assert.Equal(Driving,Restored);
        Assert.True(Restored.Gear>0);
    }
    private static string RepositoryRoot(){
        var Root=AppContext.BaseDirectory;
        while(Root is not null&&!File.Exists(Path.Combine(Root,"CSharpGameEngine.slnx"))){ Root=Path.GetDirectoryName(Root); }
        return Root??throw new InvalidOperationException("CSharpGameEngine.slnx was not found above the test binaries.");
    }
    /// <summary>取り込んだ(エンジンの座標の)メッシュの、R・U・F それぞれの大きさ(m)。</summary>
    private static (float R,float U,float F)MeshSize(string Asset){
        var Result=GltfImporter.ImportScene(Path.Combine(RepositoryRoot(),"Templates","CSharpGameEngine.Templates.Vehicle","Assets","Vehicles","SportsCar",Asset));
        Assert.True(Result.Success,string.Join("; ",Result.Diagnostics.Select(static Item=>Item.Message)));
        var Points=Result.Scene!.Meshes.SelectMany(static Mesh=>Mesh.Positions).ToArray();
        return (Points.Max(static P=>P.X)-Points.Min(static P=>P.X),Points.Max(static P=>P.Y)-Points.Min(static P=>P.Y),Points.Max(static P=>P.Z)-Points.Min(static P=>P.Z));
    }
    /// <summary>シーンの子ノードの休みの Yaw(回転数。EulerRuf の U)。</summary>
    private static float SceneYawTurns(string Node){
        using var Scene=JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(),"Templates","CSharpGameEngine.Templates.Vehicle","Scenes","SportsCar.json")));
        static JsonElement?Find(JsonElement Current,string Name){
            if(Current.GetProperty("name").GetString()==Name){ return Current; }
            foreach(var Child in Current.GetProperty("children").EnumerateArray()){ if(Find(Child,Name) is { }Found){ return Found; } }
            return null;
        }
        var Found=Find(Scene.RootElement.GetProperty("root"),Node)??throw new InvalidOperationException(Node);
        var Transform=Found.GetProperty("components").EnumerateArray().First(static Item=>Item.GetProperty("type").GetString()=="CSharpGameEngine.Entities.Transform");
        return Transform.GetProperty("members").GetProperty("Rotation").GetProperty("EulerRuf").GetProperty("U").GetSingle();
    }
    [Fact]
    public void TheSceneTurnsTheCarMeshesSoTheBodyRunsAlongForwardAndEachWheelAxleLiesAlongTheSideAxis(){
        // M0109 の修正: 取り込んだメッシュの軸(車体は長い辺が R、車輪の車軸は F)に、シーンの休みの向きを掛けたとき、車体は F が長く、車輪の車軸(一番短い辺)は R になる。
        // 車輪の休みの向きが 0 と半回転だったときは、車軸が前後を向いていた(横から見ると接地面が見えた)。
        static (float R,float U,float F)Turned((float R,float U,float F)Size,float Turns){
            Assert.InRange(Math.Abs(Turns*4f-MathF.Round(Turns*4f)),0f,1e-4f);
            return Math.Abs((int)MathF.Round(Turns*4f))%2==1?(Size.F,Size.U,Size.R):Size;
        }
        var Body=Turned(MeshSize("SM_SportsCar.glb"),SceneYawTurns("Body"));
        Assert.True(Body.F>Body.R*1.5f,$"body {Body}");
        var Glass=Turned(MeshSize("SM_SportsCar_Glass.glb"),SceneYawTurns("Glass"));
        Assert.True(Glass.F>Glass.R*1.5f,$"glass {Glass}");
        var Wheel=MeshSize("SM_SportsCar_Wheel.glb");
        foreach(var Name in new[]{"Wheel_FL","Wheel_FR","Wheel_BL","Wheel_BR"}){
            var Resting=Turned(Wheel,SceneYawTurns(Name));
            Assert.True(Resting.R<0.5f&&Resting.U>0.7f&&Resting.F>0.7f,$"{Name}: the axle lies along R when R is the short side, got {Resting}");
        }
        // 左右の車輪は互いに半回転(車輪の向きは左右で入れ替わる)。
        Assert.Equal(0.5f,Math.Abs(SceneYawTurns("Wheel_FL")-SceneYawTurns("Wheel_FR")),3);
        Assert.Equal(SceneYawTurns("Wheel_FL"),SceneYawTurns("Wheel_BL"),3);
        Assert.Equal(SceneYawTurns("Wheel_FR"),SceneYawTurns("Wheel_BR"),3);
    }
    [Theory]
    [InlineData("Wheel_FL",0f,0f)]
    [InlineData("Wheel_FR",1.3f,0f)]
    [InlineData("Wheel_FL",5.9f,30f)]
    [InlineData("Wheel_BR",-2.4f,-40f)]
    public void TheWheelMeshAxleStaysAlongTheCarsSideAxisWhateverItsSpinAndTurnsWithTheSteeringAboutUp(string Node,float Spin,float SteerDegrees){
        var Size=MeshSize("SM_SportsCar_Wheel.glb");
        // 取り込んだ車輪メッシュの車軸(一番短い辺)の向き。
        var Axle=Size.R<Size.F?LogicalVector3.UnitR:LogicalVector3.UnitF;
        Assert.True(Math.Min(Size.R,Size.F)<Size.U*0.6f);
        var Rest=LogicalQuaternion.FromEulerRuf(new LogicalVector3(0,LogicalScalar.FromSingle(SceneYawTurns(Node)*2f*MathF.PI),0));
        var Visual=new VehicleWheelVisual { RestRotation=Rest,RestPosition=LogicalVector3.Zero,Captured=true };
        var Wheel=new VehicleWheelState { AngularPosition=LogicalScalar.FromSingle(Spin),SteeringAngleDegrees=LogicalScalar.FromSingle(SteerDegrees) };
        Assert.True(VehicleWheelVisual.TryPose(Visual,Wheel,out _,out var Pose));
        Assert.True(Pose.TryRotate(Axle,out var World));
        // 回転の角では車軸は動かない。舵角だけ、上の軸のまわりに水平に回る。
        Assert.InRange(MathF.Abs(F(World.U)),0f,1e-3f);
        Assert.Equal(MathF.Cos(SteerDegrees*MathF.PI/180f),MathF.Abs(F(World.R)),3);
    }
    [Fact]
    public void AFullThrottleHeadOnImpactAtAWallStopsTheCarWithoutSpinningOrTumblingIt(){
        // M0109 の修正: 速い車が壁へ当たる tick は、前の下の角だけが壁に入り(上の角は数 mm 手前)、その 2 点に衝撃のすべてが掛かって、車体が
        // 回り出し(ヨー・ロールとも 2〜5 rad/s)、横倒しになった。数 mm 手前の角も接触の点に加え、同じ値に止める。
        var Car=new ChaosVehicleTrack();
        Car.AddQuad(new(-60,0,62.5f),new(60,0,62.5f),new(60,15,62.5f),new(-60,15,62.5f));
        Car.Run(60);
        var Strongest=0f;
        var Lowest=1f;
        var Hit=false;
        for(var Tick=0;Tick<60*12;Tick++){
            Car.Run(1,Throttle:1f);
            if(Car.Z>50f){ Hit=true; }
            if(Car.Z>59f){
                var Angular=Car.Body.Velocity.Angular;
                Strongest=MathF.Max(Strongest,MathF.Max(MathF.Abs(F(Angular.R)),MathF.Max(MathF.Abs(F(Angular.U)),MathF.Abs(F(Angular.F)))));
                Lowest=MathF.Min(Lowest,Car.UpDot);
            }
        }
        Assert.True(Hit);
        Output.WriteLine($"strongest {Strongest:F2} rad/s, lowest up {Lowest:F3}, final z {Car.Z:F2}");
        // 範囲の根拠: 直したあとの値は 0.5 rad/s 以下、上は 1.000。直す前は 5 rad/s 以上で、上の向きは 0.88 まで傾いた。
        Assert.InRange(Strongest,0f,1.0f);
        Assert.InRange(Lowest,0.99f,1.0001f);
        Assert.InRange(Car.Z,59.5f,60.5f);
        Assert.InRange(MathF.Abs(Car.HeadingDegrees),0f,3f);
    }
    [Fact]
    public void TheGearIsNeutralOnlyForTheChangeTimeOfAShiftAndTheNextGearFollows(){
        // UE の変速は、ニュートラルを通って GearChangeTime(0.2 s)待つ(TransmissionSystem.cpp: CurrentGear = 0)。N の表示は、走行中にはその間だけ。
        var Car=new ChaosVehicleTrack();
        Car.AddStrip(2500);
        Car.Run(60);
        var Runs=new List<(int Length,int Before,int After)>();
        var Neutral=0;
        var Before=0;
        var Previous=0;
        for(var Tick=0;Tick<60*40;Tick++){
            Car.Run(1,Throttle:1f);
            var Gear=Car.State.Gear;
            if(Gear==0&&Car.Speed>5f){
                if(Neutral==0){ Before=Previous; }
                Neutral++;
            }
            else if(Neutral>0){
                Runs.Add((Neutral,Before,Gear));
                Neutral=0;
            }
            if(Gear!=0){ Previous=Gear; }
        }
        Assert.True(Runs.Count>=4,$"shifts {Runs.Count}");
        foreach(var Run in Runs){
            Assert.InRange(Run.Length,10,14);
            Assert.Equal(Run.Before+1,Run.After);
        }
    }
    [Fact]
    public void TheWheelPoseRaisesTurnsAndSpinsTheWheelFromItsRest(){
        var Visual=new VehicleWheelVisual { Wheel=0,RestPosition=new LogicalVector3(LogicalScalar.FromSingle(0.9f),LogicalScalar.FromSingle(-0.25f),LogicalScalar.FromSingle(1.4f)),RestRotation=LogicalQuaternion.Identity,Captured=true };
        var Wheel=new VehicleWheelState { SuspensionOffset=LogicalScalar.FromSingle(0.05f),SteeringAngleDegrees=LogicalScalar.FromInt64(30),AngularPosition=LogicalScalar.Zero };
        Assert.True(VehicleWheelVisual.TryPose(Visual,Wheel,out var Position,out var Rotation));
        Assert.Equal(-0.2f,F(Position.U),4);
        Assert.Equal(0.9f,F(Position.R),4);
        // 右へ 30° 切った車輪の前は、前 × 上(-R)の側へ向く。
        Assert.True(Rotation.TryRotate(LogicalVector3.UnitF,out var Forward));
        Assert.Equal(MathF.Sin(MathF.PI/6f),-F(Forward.R),3);
        // 前進で回る車輪の前の端は下がる(UE の Pitch = -回転の角)。
        var Rolling=new VehicleWheelState { AngularPosition=LogicalScalar.FromSingle(0.5f) };
        Assert.True(VehicleWheelVisual.TryPose(Visual,Rolling,out _,out var Rolled));
        Assert.True(Rolled.TryRotate(LogicalVector3.UnitF,out var Front));
        Assert.True(F(Front.U)< -0.4f,$"front {F(Front.U)}");
    }
    [Fact]
    public void TheTemplateAssetCarriesTheValuesTheTestsDriveWith(){
        // テンプレートの SportsCarSetup.cgeasset(UE の値)と、この試験が使う値が同じであること。
        var Root=AppContext.BaseDirectory;
        while(Root is not null&&!File.Exists(Path.Combine(Root,"CSharpGameEngine.slnx"))){ Root=Path.GetDirectoryName(Root); }
        Assert.NotNull(Root);
        using var Asset=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"Templates","CSharpGameEngine.Templates.Vehicle","Assets","Vehicles","SportsCar","SportsCarSetup.cgeasset")));
        var Members=Asset.RootElement.GetProperty("members");
        var Setup=ChaosVehicleTrack.SportsCar();
        Assert.Equal("CSharpGameEngine.Vehicles.VehicleSetup",Asset.RootElement.GetProperty("type").GetString());
        Assert.Equal(F(Setup.Mass),Members.GetProperty("Mass").GetSingle());
        Assert.Equal(F(Setup.DragCoefficient),Members.GetProperty("DragCoefficient").GetSingle());
        var Engine=Members.GetProperty("Engine");
        Assert.Equal(F(Setup.Engine.MaxTorque),Engine.GetProperty("MaxTorque").GetSingle());
        Assert.Equal(F(Setup.Engine.IdleRpm),Engine.GetProperty("IdleRpm").GetSingle());
        Assert.Equal(Setup.Engine.TorqueCurve.Select(static Key=>(F(Key.Time),F(Key.Value))),Engine.GetProperty("TorqueCurve").EnumerateArray().Select(static Key=>(Key.GetProperty("Time").GetSingle(),Key.GetProperty("Value").GetSingle())));
        var Transmission=Members.GetProperty("Transmission");
        Assert.Equal(Setup.Transmission.ForwardGearRatios.Select(F),Transmission.GetProperty("ForwardGearRatios").EnumerateArray().Select(static Ratio=>Ratio.GetSingle()));
        Assert.Equal(F(Setup.Transmission.FinalRatio),Transmission.GetProperty("FinalRatio").GetSingle());
        foreach(var (Name,Wheel) in new[]{("FrontWheel",Setup.FrontWheel),("RearWheel",Setup.RearWheel)}){
            var Json=Members.GetProperty(Name);
            Assert.Equal(F(Wheel.Radius),Json.GetProperty("Radius").GetSingle());
            Assert.Equal(F(Wheel.FrictionForceMultiplier),Json.GetProperty("FrictionForceMultiplier").GetSingle());
            Assert.Equal(F(Wheel.MaxBrakeTorque),Json.GetProperty("MaxBrakeTorque").GetSingle());
            Assert.Equal(F(Wheel.SpringRate),Json.GetProperty("SpringRate").GetSingle());
            Assert.Equal(Wheel.AffectedByEngine,Json.GetProperty("AffectedByEngine").GetBoolean());
            Assert.Equal(F(Wheel.MaxSteerAngleDegrees)/360f,Json.GetProperty("MaxSteerAngle").GetSingle(),5);
        }
    }
}
