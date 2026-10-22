using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>ADR-0077: 簡易の車両モデル。決定的な物理世界の床・坂・壁の上で、前後の速度、ハンドル、接地を確かめる。</summary>
public sealed class VehicleMotorTests {
    private const int TickRate=60;
    private static readonly LogicalScalar DeltaSeconds=LogicalScalar.One/LogicalScalar.FromInt64(TickRate);
    private static readonly Vehicle Settings=new();
    private static float ToSingle(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private sealed class Track {
        private readonly DeterministicPhysicsWorld World=new();
        private readonly VehicleMotor Motor;
        private ulong NextId=1;
        public LogicalVector3 Position;
        public LogicalQuaternion Rotation=LogicalQuaternion.Identity;
        public VehicleState State;
        public VehicleIntent Intent;
        public Vehicle Settings=VehicleMotorTests.Settings;
        public Track(bool Floor=true,float StartHeight=0.55f){
            this.Motor=new VehicleMotor(this.World);
            this.Position=new LogicalVector3(0,StartHeight,0);
            if(Floor){
                // 30秒走り続けても端に着かない広さ(最高速度42 m/s×30秒=1.3 km)。
                this.AddQuad(new(-3000,0,-3000),new(3000,0,-3000),new(3000,0,3000),new(-3000,0,3000));
            }
        }
        public Track AddQuad(LogicalVector3 A,LogicalVector3 B,LogicalVector3 C,LogicalVector3 D){
            var Id=$"mesh{this.NextId}";
            this.World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh(Id,[A,B,C,D],[0,1,2,0,2,3]));
            Assert.Equal(PhysicsUpdateStatus.Success,this.World.Upsert(new PhysicsBodyDescription(
                new Entity((int)this.NextId),this.NextId,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id },null,
                LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
            this.NextId++;
            return this;
        }
        public void Run(int Ticks,float Drive=0f,float Steering=0f,bool Handbrake=false){
            this.Intent=new VehicleIntent { Drive=Drive,Steering=Steering,Handbrake=Handbrake };
            for(var Tick=0;Tick<Ticks;Tick++){
                this.Motor.Step(this.Settings,ref this.Position,ref this.Rotation,ref this.State,this.Intent,DeltaSeconds);
            }
        }
        public float X=>ToSingle(this.Position.R);
        public float Y=>ToSingle(this.Position.U);
        public float Z=>ToSingle(this.Position.F);
        public float Speed=>ToSingle(this.State.ForwardSpeed);
        /// <summary>車の右(前×上)向きの速度(m/s)。横滑りの大きさ。</summary>
        public float Lateral{
            get{
                VehicleMotor.Basis(this.Rotation,out _,out var Right,out _);
                return ToSingle(LogicalVector3.Dot(this.State.Velocity,Right));
            }
        }
        /// <summary>車の前向きがワールドの+Fから回った角度(度。+Rの側が正)。</summary>
        public float HeadingDegrees{
            get{
                VehicleMotor.Basis(this.Rotation,out var Forward,out _,out _);
                return MathF.Atan2(ToSingle(Forward.R),ToSingle(Forward.F))*180f/MathF.PI;
            }
        }
    }
    [Fact]
    public void RestsOnTheFloorAtItsRideHeight(){
        var Car=new Track();
        Car.Run(120);
        Assert.True(Car.State.OnGround);
        Assert.InRange(Car.Y,0.549f,0.551f);
        Assert.Equal(0,Car.State.Gear);
    }
    [Fact]
    public void FallsOntoTheFloorWhenStartedInTheAir(){
        var Car=new Track(StartHeight:3f);
        Car.Run(10);
        Assert.False(Car.State.OnGround);
        Assert.True(Car.Y<3f);
        Car.Run(200);
        Assert.True(Car.State.OnGround);
        Assert.InRange(Car.Y,0.54f,0.56f);
    }
    [Fact]
    public void ThrottleAcceleratesForwardTowardTheTopSpeedWithoutExceedingIt(){
        var Car=new Track();
        Car.Run(60,Drive:1f);
        var OneSecond=Car.Speed;
        // 1秒で約10 m/s(加速度10 m/s²、最高速度に近いほど弱まる)。
        Assert.InRange(OneSecond,8f,10.5f);
        Assert.True(Car.Z>4f);
        Car.Run(60*30,Drive:1f);
        Assert.InRange(Car.Speed,40f,42.01f);
        Assert.True(Car.State.Gear>=4);
    }
    [Fact]
    public void ReleasingTheThrottleCoastsToAStopWithoutReversing(){
        var Car=new Track();
        Car.Run(120,Drive:1f);
        Car.Run(60*30);
        Assert.InRange(Car.Speed,-0.001f,0.001f);
        Assert.Equal(0,Car.State.Gear);
    }
    [Fact]
    public void ThePedalAgainstTheDirectionOfTravelBrakesThenReverses(){
        var Car=new Track();
        Car.Run(120,Drive:1f);
        var Fast=Car.Speed;
        Car.Run(30,Drive:-1f);
        // ブレーキ(16 m/s²)で0.5秒に8 m/s落ちる。まだ前進している。
        Assert.InRange(Fast-Car.Speed,7f,9f);
        Assert.True(Car.Speed>0f);
        Car.Run(60*6,Drive:-1f);
        Assert.True(Car.Speed<-5f);
        Assert.InRange(Car.Speed,-10.01f,-5f);
        Assert.Equal(-1,Car.State.Gear);
    }
    [Fact]
    public void SteeringRightTurnsTowardTheDriversRight(){
        var Car=new Track();
        Car.Run(90,Drive:1f);
        var Before=Car.HeadingDegrees;
        Car.Run(60,Drive:0.5f,Steering:1f);
        // 前×上の向きが右。このエンジンでは+Rが左なので、右に曲がると向きの角度(+Rの側が正)は小さくなる。
        Assert.True(Car.HeadingDegrees<Before-5f,$"heading {Car.HeadingDegrees} before {Before}");
        Assert.True(Car.X<-0.5f,$"x {Car.X}");
    }
    [Fact]
    public void FullLockAtSpeedIsLimitedToTheLateralAcceleration(){
        var Car=new Track();
        Car.Run(60*20,Drive:1f);
        var Fast=Car.Speed;
        Assert.True(Fast>35f,$"speed {Fast}");
        var Before=Car.HeadingDegrees;
        Car.Run(60,Drive:1f,Steering:1f);
        // 14 m/s²を速度で割った角速度(rad/s)が上限。1秒で回る角度はそれ以下(速度は加速で少し変わる)。
        var Limit=14f/Fast*180f/MathF.PI;
        var Turned=MathF.Abs(Car.HeadingDegrees-Before);
        Assert.True(Turned>5f,$"turned {Turned}");
        Assert.True(Turned<Limit*1.1f,$"turned {Turned} limit {Limit}");
    }
    [Fact]
    public void SteeringLeftTurnsTheOtherWay(){
        var Car=new Track();
        Car.Run(90,Drive:1f);
        var Before=Car.HeadingDegrees;
        Car.Run(60,Drive:0.5f,Steering:-1f);
        Assert.True(Car.HeadingDegrees>Before+5f);
        Assert.True(Car.X>0.5f);
    }
    [Fact]
    public void ANotMovingCarDoesNotTurn(){
        var Car=new Track();
        Car.Run(60,Drive:0f,Steering:1f);
        Assert.InRange(Car.HeadingDegrees,-0.01f,0.01f);
    }
    [Fact]
    public void TheHandbrakeLetsTheCarSlideSidewaysLongerThanGrip(){
        var Grip=new Track();
        var Slide=new Track();
        foreach(var Car in new[]{Grip,Slide}){
            Car.Run(180,Drive:1f);
        }
        Grip.Run(40,Drive:0f,Steering:1f);
        Slide.Run(40,Drive:0f,Steering:1f,Handbrake:true);
        // 車の右(前×上)向きの速度が横滑り。ハンドブレーキではグリップが弱いので、同じハンドルでも横に滑る。
        Assert.True(MathF.Abs(Grip.Lateral)<2f,$"grip lateral {Grip.Lateral}");
        Assert.True(MathF.Abs(Slide.Lateral)>MathF.Abs(Grip.Lateral)+2f,$"slide {Slide.Lateral} grip {Grip.Lateral}");
    }
    [Fact]
    public void StopsAtAWallInFrontOfTheCar(){
        var Car=new Track();
        Car.AddQuad(new(-20,0,30),new(20,0,30),new(20,10,30),new(-20,10,30));
        Car.Run(60*6,Drive:1f);
        // 前の端は車軸の0.9 m先。壁(z=30)の手前で止まり、通り抜けない。
        Assert.True(Car.Z<30f,$"z {Car.Z}");
        Assert.True(Car.Z>25f,$"z {Car.Z}");
    }
    [Fact]
    public void ClimbsARampAndTiltsToItsSlope(){
        var Car=new Track();
        // 長さ20 mで高さ3 mの坂(z=20から40)。傾きは約8.5度。
        Car.AddQuad(new(-20,0,20),new(20,0,20),new(20,3,40),new(-20,3,40));
        Car.AddQuad(new(-20,3,40),new(20,3,40),new(20,3,200),new(-20,3,200));
        Car.Run(60*5,Drive:1f);
        Assert.True(Car.State.OnGround);
        Assert.True(Car.Z>40f,$"z {Car.Z}");
        // 坂の上の面に乗っている(車輪の接地高さは3 m、車体はその上)。
        Assert.InRange(Car.Y,3.5f,3.7f);
    }
    [Fact]
    public void LeavesTheGroundAtTheEndOfARampAndLandsAgain(){
        var Car=new Track();
        Car.AddQuad(new(-20,0,20),new(20,0,20),new(20,3,40),new(-20,3,40));
        var Airborne=false;
        var Peak=0f;
        Car.Run(60,Drive:1f);
        for(var Tick=0;Tick<60*8;Tick++){
            Car.Run(1,Drive:1f);
            Airborne|=!Car.State.OnGround;
            Peak=MathF.Max(Peak,Car.Y);
        }
        Assert.True(Airborne,"the car never left the ramp");
        // 坂の上の端の高さ3 mに車体の中心の高さ0.55 mを足した所の少し下までは上がる(前の車輪が先に浮くので、平均の高さは少し低い)。
        Assert.True(Peak>3.2f,$"peak {Peak}");
        Assert.True(Car.State.OnGround);
        Assert.InRange(Car.Y,0.54f,0.56f);
    }
    [Fact]
    public void ARunIsDeterministic(){
        static (LogicalVector3,LogicalQuaternion) Drive(){
            var Car=new Track();
            Car.Run(90,Drive:1f);
            Car.Run(120,Drive:0.7f,Steering:0.6f);
            Car.Run(60,Drive:-1f,Steering:-0.3f,Handbrake:true);
            return (Car.Position,Car.Rotation);
        }
        Assert.Equal(Drive(),Drive());
    }
    [Theory]
    [InlineData(0f,0)]
    [InlineData(0.3f,0)]
    [InlineData(5f,1)]
    [InlineData(21f,3)]
    [InlineData(41f,5)]
    [InlineData(60f,5)]
    [InlineData(-3f,-1)]
    public void TheGearFollowsTheForwardSpeed(float Speed,int Expected){
        Assert.Equal(Expected,VehicleMotor.GearOf(Settings,LogicalScalar.FromSingle(Speed)));
    }
    [Fact]
    public void ANoGearVehicleShowsGearZero(){
        var NoGear=Settings with { GearCount=0 };
        Assert.Equal(0,VehicleMotor.GearOf(NoGear,LogicalScalar.FromSingle(30f)));
    }
}
