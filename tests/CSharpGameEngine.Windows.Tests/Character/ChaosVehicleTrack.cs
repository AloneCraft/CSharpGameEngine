using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Vehicles;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0109: Chaos 相当の車を、決定的な物理世界の上で直接動かす試験の土台。車体はテンプレートの SportsCar と同じ動的な箱の剛体で、
/// 毎 tick、<see cref="VehicleMotor"/> がその tick の力の命令を積み、物理世界がその tick を進める(ゲームの System と同じ順)。
/// 設定は <see cref="SportsCar"/>(テンプレートの SportsCarSetup.cgeasset と同じ値。<c>ChaosVehicleTests.TheTemplateAssetCarriesTheValuesTheTestsDriveWith</c> が照合する)。
/// </summary>
internal sealed class ChaosVehicleTrack {
    internal const int TickRate=60;
    internal static readonly LogicalScalar DeltaSeconds=LogicalScalar.One/LogicalScalar.FromInt64(TickRate);
    /// <summary>重心から見た車輪の休みの位置(テンプレートの SportsCar.json と同じ。UE の Phys_Wheel_* の骨と重心から)。</summary>
    internal static readonly VehicleWheelMount[]SportsCarWheels=[
        new(){ Position=new(LogicalScalar.ParseInvariant("0.9"),LogicalScalar.ParseInvariant("-0.2509"),LogicalScalar.ParseInvariant("1.4196")),Axle=VehicleAxle.Front },
        new(){ Position=new(LogicalScalar.ParseInvariant("-0.9"),LogicalScalar.ParseInvariant("-0.2509"),LogicalScalar.ParseInvariant("1.4196")),Axle=VehicleAxle.Front },
        new(){ Position=new(LogicalScalar.ParseInvariant("0.9"),LogicalScalar.ParseInvariant("-0.2359"),LogicalScalar.ParseInvariant("-1.2224")),Axle=VehicleAxle.Rear },
        new(){ Position=new(LogicalScalar.ParseInvariant("-0.9"),LogicalScalar.ParseInvariant("-0.2359"),LogicalScalar.ParseInvariant("-1.2224")),Axle=VehicleAxle.Rear },
    ];
    internal static LogicalScalar L(string Text)=>LogicalScalar.ParseInvariant(Text);
    /// <summary>UE の BP_VehicleAdvSportsCar の VehicleMovementComp と SportsCar_WheelsFront / Rear の値(SI 単位)。</summary>
    internal static VehicleSetup SportsCar(){
        VehicleWheelSetup Front=new(){
            Radius=L("0.39"),Width=L("0.35"),Mass=20,CorneringStiffness=100000,FrictionForceMultiplier=3,SideSlipModifier=1,SlipThreshold=L("0.2"),SkidThreshold=L("0.2"),
            MaxWheelspinRotation=30,MaxSteerAngleDegrees=40,MaxBrakeTorque=4500,MaxHandbrakeTorque=6000,AffectedByBrake=true,AffectedByHandbrake=false,
            AffectedBySteering=true,AffectedByEngine=false,SuspensionMaxRaise=L("0.1"),SuspensionMaxDrop=L("0.1"),SpringRate=25000,SpringPreload=5000,
            SuspensionDampingRatio=L("0.5"),WheelLoadRatio=L("0.5"),RollbarScaling=L("0.15"),
        };
        var Rear=Front;
        Rear.Radius=L("0.4");
        Rear.Width=L("0.4");
        Rear.FrictionForceMultiplier=4;
        Rear.SlipThreshold=1;
        Rear.SkidThreshold=1;
        Rear.MaxSteerAngleDegrees=0;
        Rear.MaxBrakeTorque=1500;
        Rear.AffectedByHandbrake=true;
        Rear.AffectedBySteering=false;
        Rear.AffectedByEngine=true;
        return new VehicleSetup {
            Mass=1500,DragCoefficient=L("0.31"),DownforceCoefficient=L("0.3"),ChassisWidth=L("1.8"),ChassisHeight=L("1.44"),
            Engine=new VehicleEngineSetup {
                TorqueCurve=[new(1000,L("0.5")),new(1800,L("0.9")),new(3500,1),new(6000,L("0.8")),new(7000,0)],
                MaxTorque=750,MaxRpm=7000,IdleRpm=900,EngineBrakeEffect=L("0.2"),RevUpMoi=5,RevDownRate=600,
            },
            Transmission=new VehicleTransmissionSetup {
                Automatic=true,AutoReverse=true,FinalRatio=L("2.81"),ForwardGearRatios=[L("4.25"),L("2.52"),L("1.66"),L("1.22"),1],ReverseGearRatios=[L("4.04")],
                ChangeUpRpm=6000,ChangeDownRpm=2000,GearChangeTime=L("0.2"),Efficiency=L("0.9"),
            },
            Steering=new VehicleSteeringSetup {
                Type=VehicleSteeringType.Ackermann,AngleRatio=L("0.7"),
                SteeringCurve=[new(0,1),new(0,1),new(20,L("0.8")),new(20,L("0.8")),new(60,L("0.4")),new(60,L("0.4")),new(120,L("0.3")),new(120,L("0.3"))],
            },
            Differential=VehicleDifferential.RearWheelDrive,FrontRearSplit=L("0.5"),FrontWheel=Front,RearWheel=Rear,
            ThrottleInputRate=new(6,10,VehicleInputCurve.Linear),BrakeInputRate=new(6,10,VehicleInputCurve.Linear),
            SteeringInputRate=new(L("2.5"),5,VehicleInputCurve.Squared),HandbrakeInputRate=new(12,12,VehicleInputCurve.Linear),
            ReverseAsBrake=true,ThrottleAsBrake=false,StopThreshold=L("0.1"),WrongDirectionThreshold=1,IdleBrakeInput=0,SleepThreshold=L("0.1"),SleepSlopeLimit=L("0.866"),
        };
    }
    internal readonly DeterministicPhysicsWorld World=new();
    internal readonly VehicleMotor Motor;
    internal readonly Entity Car=new(1000);
    private ulong NextShape=1;
    internal ulong Tick=1;
    internal Vehicle Vehicle;
    internal VehicleState State;
    internal VehicleIntent Intent;
    internal ChaosVehicleTrack(bool Floor=true,float StartHeight=0.7f,VehicleSetup?Setup=null,LogicalScalar?Friction=null,LogicalQuaternion?Rotation=null){
        this.Motor=new VehicleMotor(this.World);
        Setup??=SportsCar();
        if(Floor){
            // 走り続けても端に着かない広さを、100 m の格子の三角形に分ける(数 km の三角形 1 枚と車体の箱の接触は、物理の GJK が桁あふれする)。
            this.AddGrid(-2000,2000,100,LogicalScalar.Zero,Friction??LogicalScalar.One);
        }
        this.Vehicle=new Vehicle {
            Setup=Setup,
            Wheels=SportsCarWheels,
            ResetLift=L("0.5"),
            ResetPivot=new(0,L("-0.5009"),L("0.0696")),
            FlipCheckSeconds=3,
            FlippedUpDot=L("-0.2"),
        };
        Assert.Equal(PhysicsUpdateStatus.Success,this.World.Upsert(new PhysicsBodyDescription(
            this.Car,1000,new Collider { Shape=ColliderShape.Box,HalfExtents=new(L("0.95"),L("0.45"),L("2.25")),Material=new PhysicsMaterial { Friction=L("0.7") } },
            new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=Setup.Mass,LinearDamping=L("0.01"),AngularDamping=3 },
            new LogicalVector3(0,LogicalScalar.FromSingle(StartHeight),0),Rotation??LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
    }
    /// <summary>R は -100 から 100、F は -100 から <paramref name="Length"/> m の、まっすぐな長い道(最高速の試験)。</summary>
    internal ChaosVehicleTrack AddStrip(int Length){
        var Id=$"mesh{this.NextShape}";
        List<LogicalVector3>Vertices=[];
        List<int>Indices=[];
        for(var Row=0;Row*100-100<=Length;Row++){
            Vertices.Add(new LogicalVector3(-100,0,Row*100-100));
            Vertices.Add(new LogicalVector3(100,0,Row*100-100));
            if(Row>0){ Indices.AddRange([Row*2-2,Row*2-1,Row*2+1,Row*2-2,Row*2+1,Row*2]); }
        }
        this.World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh(Id,Vertices,Indices));
        Assert.Equal(PhysicsUpdateStatus.Success,this.World.Upsert(new PhysicsBodyDescription(
            new Entity((int)this.NextShape),this.NextShape,
            new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id,Material=new PhysicsMaterial { Friction=LogicalScalar.One } },null,
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        this.NextShape++;
        return this;
    }
    /// <summary>高さ <paramref name="Height"/> の水平な床を、<paramref name="From"/> から <paramref name="To"/>(R と F)まで <paramref name="Cell"/> m の格子で 1 つのメッシュにする。</summary>
    internal ChaosVehicleTrack AddGrid(int From,int To,int Cell,LogicalScalar Height,LogicalScalar Friction){
        var Id=$"mesh{this.NextShape}";
        var Count=(To-From)/Cell+1;
        List<LogicalVector3>Vertices=[];
        List<int>Indices=[];
        for(var Row=0;Row<Count;Row++){
            for(var Column=0;Column<Count;Column++){
                Vertices.Add(new LogicalVector3(From+Column*Cell,Height,From+Row*Cell));
            }
        }
        for(var Row=0;Row+1<Count;Row++){
            for(var Column=0;Column+1<Count;Column++){
                var A=Row*Count+Column;
                Indices.AddRange([A,A+1,A+Count+1,A,A+Count+1,A+Count]);
            }
        }
        this.World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh(Id,Vertices,Indices));
        Assert.Equal(PhysicsUpdateStatus.Success,this.World.Upsert(new PhysicsBodyDescription(
            new Entity((int)this.NextShape),this.NextShape,
            new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id,Material=new PhysicsMaterial { Friction=Friction } },null,
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        this.NextShape++;
        return this;
    }
    internal ChaosVehicleTrack AddQuad(LogicalVector3 A,LogicalVector3 B,LogicalVector3 C,LogicalVector3 D,LogicalScalar?Friction=null){
        var Id=$"mesh{this.NextShape}";
        this.World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh(Id,[A,B,C,D],[0,1,2,0,2,3]));
        Assert.Equal(PhysicsUpdateStatus.Success,this.World.Upsert(new PhysicsBodyDescription(
            new Entity((int)this.NextShape),this.NextShape,
            new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id,Material=new PhysicsMaterial { Friction=Friction??LogicalScalar.One } },null,
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        this.NextShape++;
        return this;
    }
    /// <summary><paramref name="Ticks"/> tick だけ、同じ操作で走る。</summary>
    internal void Run(int Ticks,float Throttle=0f,float Brake=0f,float Steering=0f,bool Handbrake=false){
        for(var Index=0;Index<Ticks;Index++){
            this.Intent.Throttle=Throttle;
            this.Intent.Brake=Brake;
            this.Intent.Steering=Steering;
            this.Intent.Handbrake=Handbrake;
            this.Motor.Step(this.Vehicle,ref this.State,ref this.Intent,this.Car,this.Tick,DeltaSeconds);
            this.World.Step(this.Tick);
            this.Tick++;
        }
    }
    internal PhysicsBodyState Body=>this.World.TryGetBodyState(this.Car,out var Body)?Body:throw new InvalidOperationException("The car's body is missing.");
    internal static float F(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    internal float X=>F(this.Body.Position.R);
    internal float Y=>F(this.Body.Position.U);
    internal float Z=>F(this.Body.Position.F);
    internal float Speed=>F(this.State.ForwardSpeed);
    internal float KilometersPerHour=>this.Speed*3.6f;
    internal float UpDot{
        get{
            VehicleMotor.Basis(this.Body.Rotation,out _,out _,out var Up);
            return F(Up.U);
        }
    }
    /// <summary>車の前向きがワールドの +F から回った角度(度。+R の側が正)。</summary>
    internal float HeadingDegrees{
        get{
            VehicleMotor.Basis(this.Body.Rotation,out var Forward,out _,out _);
            return MathF.Atan2(F(Forward.R),F(Forward.F))*180f/MathF.PI;
        }
    }
    internal float Lateral{
        get{
            VehicleMotor.Basis(this.Body.Rotation,out _,out var Right,out _);
            return F(LogicalVector3.Dot(this.Body.Velocity.Linear,Right));
        }
    }
    internal string Describe()=>FormattableString.Invariant(
        $"t={this.Tick} pos=({this.X:F3},{this.Y:F3},{this.Z:F3}) v={this.Speed:F2}m/s gear={this.State.Gear}/{this.State.TargetGear} rpm={F(this.State.EngineRpm):F0} up={this.UpDot:F3} head={this.HeadingDegrees:F1} lat={this.Lateral:F2} ground={this.State.WheelsOnGround} sleep={this.State.Sleeping} w0=(off {F(this.State.Wheel0.SuspensionOffset):F3} load {F(this.State.Wheel0.Load):F0} steer {F(this.State.Wheel0.SteeringAngleDegrees):F1}) w2=(off {F(this.State.Wheel2.SuspensionOffset):F3} load {F(this.State.Wheel2.Load):F0} omega {F(this.State.Wheel2.Omega):F1} slip {F(this.State.Wheel2.SlipMagnitude):F2})");
}
