using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0109: 物理の契約に足した命令(点に加える力、トルク、速度の設定、眠り)と減衰、表面の材質、箱と三角形メッシュの複数点の接触。
/// 車両(L5 のプラグイン)はこの契約だけを使う。
/// </summary>
public sealed class PhysicsForceCommandTests {
    private static readonly LogicalScalar Tick=LogicalScalar.One/LogicalScalar.FromInt64(60);
    private static float F(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private static DeterministicPhysicsWorld Floating(out Entity Body,RigidBody?Settings=null,LogicalVector3?HalfExtents=null){
        DeterministicPhysicsWorld World=new();
        Body=new Entity(1);
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            Body,1,new Collider { Shape=ColliderShape.Box,HalfExtents=HalfExtents??new LogicalVector3(1,1,1) },
            Settings??new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=2,GravityFactor=0 },
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        return World;
    }
    private static PhysicsBodyState BodyOf(DeterministicPhysicsWorld World,Entity Body)=>World.TryGetBodyState(Body,out var State)?State:throw new InvalidOperationException();
    [Fact]
    public void AForceAtTheCentreOfMassMovesWithoutTurning(){
        var World=Floating(out var Body);
        World.Commands.ForceAtPoint(1,1,0,Body,new LogicalVector3(0,0,120),LogicalVector3.Zero);
        World.Step(1);
        var State=BodyOf(World,Body);
        // Δv = F / m × Δt = 120 / 2 / 60。
        Assert.Equal(1f,F(State.Velocity.Linear.F),4);
        Assert.Equal(LogicalVector3.Zero,State.Velocity.Angular);
    }
    [Fact]
    public void AnOffCentreForceAlsoTurnsTheBodyByItsMoment(){
        var World=Floating(out var Body);
        // +R に 1 m 離れた点を +F に押す: 回りの軸は R × F = -U(このエンジンの基底は R × U = F、U × F = R、F × R = U)。
        World.Commands.ForceAtPoint(1,1,0,Body,new LogicalVector3(0,0,120),new LogicalVector3(1,0,0));
        World.Step(1);
        var State=BodyOf(World,Body);
        Assert.Equal(1f,F(State.Velocity.Linear.F),4);
        // 1 辺 2 m の箱(2 kg)の慣性 m(a² + b²)/3 = 4/3: Δω = τ / I × Δt = 120 / (4/3) / 60 = 1.5 rad/s。
        Assert.Equal(-1.5f,F(State.Velocity.Angular.U),3);
        Assert.Equal(0f,F(State.Velocity.Angular.R),4);
    }
    [Fact]
    public void ATorqueTurnsTheBodyWithoutMovingIt(){
        var World=Floating(out var Body);
        World.Commands.Torque(1,1,0,Body,new LogicalVector3(0,0,80));
        World.Step(1);
        var State=BodyOf(World,Body);
        Assert.Equal(LogicalVector3.Zero,State.Velocity.Linear);
        Assert.Equal(1f,F(State.Velocity.Angular.F),3);
    }
    [Fact]
    public void SetVelocityReplacesBothVelocitiesAndSleepStopsTheBody(){
        var World=Floating(out var Body);
        World.Commands.SetVelocity(1,1,0,Body,new LogicalVector3(3,0,0),new LogicalVector3(0,2,0));
        World.Step(1);
        var State=BodyOf(World,Body);
        Assert.Equal(3f,F(State.Velocity.Linear.R),4);
        Assert.Equal(2f,F(State.Velocity.Angular.U),4);
        World.Commands.Sleep(2,1,0,Body);
        World.Step(2);
        State=BodyOf(World,Body);
        Assert.True(State.IsSleeping);
        Assert.Equal(PhysicsVelocity.Zero,State.Velocity);
        Assert.Contains(World.Events,Event=>Event.Type==PhysicsEventType.Sleep);
        var Position=State.Position;
        World.Step(3);
        Assert.Equal(Position,BodyOf(World,Body).Position);
        // 後の力は起こす。
        World.Commands.Force(4,1,0,Body,new LogicalVector3(60,0,0));
        World.Step(4);
        Assert.False(BodyOf(World,Body).IsSleeping);
    }
    [Fact]
    public void AngularDampingSlowsTheSpinByItsRateEachTickAndZeroKeepsIt(){
        var Damped=Floating(out var DampedBody,new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=2,GravityFactor=0,AngularDamping=3,AllowSleep=false });
        var Free=Floating(out var FreeBody,new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=2,GravityFactor=0,AllowSleep=false });
        foreach(var (World,Body) in new[]{(Damped,DampedBody),(Free,FreeBody)}){
            World.Commands.SetVelocity(1,1,0,Body,LogicalVector3.Zero,new LogicalVector3(0,2,0));
            for(ulong Index=1;Index<=60;Index++){ World.Step(Index); }
        }
        // UE の AngularDamping 3: 毎 tick ω × (1 - 3/60)。1 秒で 0.95^60 ≈ 0.046 倍。
        Assert.Equal(2f*MathF.Pow(0.95f,60),F(BodyOf(Damped,DampedBody).Velocity.Angular.U),3);
        Assert.Equal(2f,F(BodyOf(Free,FreeBody).Velocity.Angular.U),4);
    }
    [Fact]
    public void LinearDampingSlowsTheBodyEachTick(){
        var World=Floating(out var Body,new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=2,GravityFactor=0,LinearDamping=6,AllowSleep=false });
        World.Commands.SetVelocity(1,1,0,Body,new LogicalVector3(0,0,10),LogicalVector3.Zero);
        World.Step(1);
        Assert.Equal(9f,F(BodyOf(World,Body).Velocity.Linear.F),4);
    }
    [Fact]
    public void TheMaterialOfABodyIsReadByItsEntity(){
        DeterministicPhysicsWorld World=new();
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(5),5,new Collider { Shape=ColliderShape.Box,Material=new PhysicsMaterial { Friction=LogicalScalar.FromSingle(0.7f),Restitution=LogicalScalar.FromSingle(0.3f) } },null,
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        Assert.True(World.TryGetMaterial(new Entity(5),out var Material));
        Assert.Equal(0.7f,F(Material.Friction),4);
        Assert.False(World.TryGetMaterial(new Entity(6),out _));
    }
    [Fact]
    public void ABoxRestsFlatOnATriangleMeshFloorOnFourCornersWithoutTurningOrSinking(){
        DeterministicPhysicsWorld World=new();
        // 50 m の格子の床(三角形メッシュ)。箱の 4 つの角が別々の三角形に乗る。
        List<LogicalVector3>Vertices=[];
        List<int>Indices=[];
        for(var Row=0;Row<3;Row++){ for(var Column=0;Column<3;Column++){ Vertices.Add(new LogicalVector3(-50+Column*50,0,-50+Row*50)); } }
        for(var Row=0;Row<2;Row++){ for(var Column=0;Column<2;Column++){ var A=Row*3+Column;Indices.AddRange([A,A+1,A+4,A,A+4,A+3]); } }
        World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh("floor",Vertices,Indices));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset="floor" },null,LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(2),2,new Collider { Shape=ColliderShape.Box,HalfExtents=new LogicalVector3(LogicalScalar.FromSingle(0.95f),LogicalScalar.FromSingle(0.45f),LogicalScalar.FromSingle(2.25f)) },
            new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=1500,AllowSleep=false },new LogicalVector3(LogicalScalar.FromSingle(0.3f),1,LogicalScalar.FromSingle(-0.2f)),
            LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        for(ulong Index=1;Index<=600;Index++){ World.Step(Index); }
        List<PhysicsContactManifold>Manifolds=[];
        World.CollectContactManifolds(Manifolds);
        var State=BodyOf(World,new Entity(2));
        // M0109 より前は一番深い 1 点だけで、箱は揺れて回り、床へ沈んだ。
        Assert.Equal(4,Assert.Single(Manifolds).Points.Count);
        Assert.InRange(F(State.Position.U),0.44f,0.46f);
        Assert.True(State.Rotation.TryRotate(LogicalVector3.UnitU,out var Up));
        Assert.InRange(F(Up.U),0.9999f,1.0001f);
        Assert.True(F(State.Velocity.Angular.LengthSquared)<1e-6f);
        Assert.True(F(State.Velocity.Linear.LengthSquared)<1e-6f);
    }
    [Theory]
    [InlineData(0.026f,0f)]
    [InlineData(-0.026f,0f)]
    [InlineData(0.026f,0.3f)]
    [InlineData(-0.026f,0.3f)]
    public void AFastFlatBoxTiltedAFewDegreesHitsAWallWithoutSpinning(float PitchRadians,float Restitution){
        // M0109: 30 m/s は 1 tick で 0.5 m。傾いた箱の前の面は、下の角が数 mm 壁へ入った tick に、上の角は数 mm 手前にある。入った角だけを接触の点にすると、
        // 衝撃の全部が下の 2 点に掛かり、箱が 2〜5 rad/s で回った。手前の角も(その tick に壁へ届くなら)同じ値に止めると、止まりきるまで回らない。
        DeterministicPhysicsWorld World=new();
        World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh("wall",[new(-60,-8,20),new(60,-8,20),new(60,8,20),new(-60,8,20)],[0,1,2,0,2,3]));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset="wall",Material=new PhysicsMaterial { Restitution=LogicalScalar.FromSingle(Restitution) } },null,
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(2),2,new Collider { Shape=ColliderShape.Box,HalfExtents=new LogicalVector3(LogicalScalar.FromSingle(0.95f),LogicalScalar.FromSingle(0.45f),LogicalScalar.FromSingle(2.25f)),Material=new PhysicsMaterial { Restitution=LogicalScalar.FromSingle(Restitution) } },
            new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=1500,GravityFactor=0,AllowSleep=false },new LogicalVector3(0,0,10),
            LogicalQuaternion.FromEulerRuf(new LogicalVector3(LogicalScalar.FromSingle(PitchRadians),0,0)),new PhysicsVelocity { Linear=new LogicalVector3(0,0,30) })));
        var Strongest=0f;
        for(ulong Index=1;Index<=60;Index++){
            World.Step(Index);
            var Angular=BodyOf(World,new Entity(2)).Velocity.Angular;
            Strongest=MathF.Max(Strongest,MathF.Max(MathF.Abs(F(Angular.R)),MathF.Max(MathF.Abs(F(Angular.U)),MathF.Abs(F(Angular.F)))));
        }
        var State=BodyOf(World,new Entity(2));
        // 範囲の根拠: 直したあとの回転は 0.9 rad/s 以下(傾いた面の下の角が先に当たる分)。直す前は 2〜4 rad/s(反発 0)。
        Assert.True(Strongest<1.2f,$"the box spun at {Strongest:F2} rad/s");
        Assert.True(F(State.Position.F)<17.8f,$"the box went through the wall: {F(State.Position.F)}");
    }
    [Fact]
    public void ABoxDrivenIntoAThinWallStopsOnTheSideItCameFrom(){
        DeterministicPhysicsWorld World=new();
        World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh("wall",[new(-5,-5,10),new(5,-5,10),new(5,5,10),new(-5,5,10)],[0,1,2,0,2,3]));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset="wall" },null,LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(2),2,new Collider { Shape=ColliderShape.Box,HalfExtents=new LogicalVector3(1,1,1) },
            new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=2,GravityFactor=0,AllowSleep=false },LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        for(ulong Index=1;Index<=240;Index++){
            World.Commands.Force(Index,2,0,new Entity(2),new LogicalVector3(0,0,40));
            World.Step(Index);
        }
        var State=BodyOf(World,new Entity(2));
        Assert.InRange(F(State.Position.F),8.9f,9.01f);
    }
}
