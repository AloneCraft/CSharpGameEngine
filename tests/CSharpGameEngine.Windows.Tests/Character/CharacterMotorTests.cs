using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>ADR-0067: deterministic kinematic movement against triangle-mesh geometry, with Unreal's ThirdPerson values.</summary>
public sealed class CharacterMotorTests {
    private const int TickRate=60;
    private static readonly LogicalScalar DeltaSeconds=LogicalScalar.One/LogicalScalar.FromInt64(TickRate);
    private static readonly CharacterMotion Settings=new();
    private sealed class Scene {
        private readonly DeterministicPhysicsWorld World=new();
        private readonly CharacterMotor Motor;
        private ulong NextId=1;
        public LogicalVector3 Position;
        public CharacterMotionState State;
        public MoveIntent Input;
        public Scene(float StartY=2f,float StartZ=0f){
            this.Motor=new CharacterMotor(this.World);
            this.Position=new LogicalVector3(0,StartY,StartZ);
            this.State.Facing=LogicalVector3.UnitF;
            this.AddQuad(new(-20,0,-20),new(20,0,-20),new(20,0,20),new(-20,0,20));
        }
        public Scene AddQuad(LogicalVector3 A,LogicalVector3 B,LogicalVector3 C,LogicalVector3 D){
            var Id=$"mesh{this.NextId}";
            this.World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh(Id,[A,B,C,D],[0,1,2,0,2,3]));
            Assert.Equal(PhysicsUpdateStatus.Success,this.World.Upsert(new PhysicsBodyDescription(
                new Entity((int)this.NextId),this.NextId,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id },null,
                LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
            this.NextId++;
            return this;
        }
        /// <summary>An axis-aligned box as six quads.</summary>
        public Scene AddBox(float MinX,float MinY,float MinZ,float MaxX,float MaxY,float MaxZ){
            LogicalVector3 P(float X,float Y,float Z)=>new(X,Y,Z);
            this.AddQuad(P(MinX,MaxY,MinZ),P(MaxX,MaxY,MinZ),P(MaxX,MaxY,MaxZ),P(MinX,MaxY,MaxZ));
            this.AddQuad(P(MinX,MinY,MinZ),P(MinX,MinY,MaxZ),P(MaxX,MinY,MaxZ),P(MaxX,MinY,MinZ));
            this.AddQuad(P(MinX,MinY,MinZ),P(MaxX,MinY,MinZ),P(MaxX,MaxY,MinZ),P(MinX,MaxY,MinZ));
            this.AddQuad(P(MinX,MinY,MaxZ),P(MinX,MaxY,MaxZ),P(MaxX,MaxY,MaxZ),P(MaxX,MinY,MaxZ));
            this.AddQuad(P(MinX,MinY,MinZ),P(MinX,MaxY,MinZ),P(MinX,MaxY,MaxZ),P(MinX,MinY,MaxZ));
            this.AddQuad(P(MaxX,MinY,MinZ),P(MaxX,MinY,MaxZ),P(MaxX,MaxY,MaxZ),P(MaxX,MaxY,MinZ));
            return this;
        }
        public void Run(int Ticks,LogicalVector3 Move,bool JumpFirstTick=false,Action? AfterTick=null){
            for(var Tick=0;Tick<Ticks;Tick++){
                this.Input.Direction=Move;
                this.Input.JumpRequested=JumpFirstTick&&Tick==0;
                this.Motor.Step(Settings,ref this.Position,ref this.State,ref this.Input,DeltaSeconds);
                AfterTick?.Invoke();
            }
        }
        public float Y=>ToSingle(this.Position.U);
        public float Z=>ToSingle(this.Position.F);
        public float X=>ToSingle(this.Position.R);
    }
    private static float ToSingle(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private static readonly LogicalVector3 Forward=LogicalVector3.UnitF;
    [Fact]
    public void FallsOntoTheFloorAndRestsAtItsHalfHeight(){
        var Scene=new Scene();
        Scene.Run(120,LogicalVector3.Zero);
        Assert.Equal(CharacterMotionMode.Walking,Scene.State.Mode);
        Assert.InRange(Scene.Y,0.899f,0.905f);
        Assert.Equal(LogicalVector3.Zero,Scene.State.Velocity);
    }
    [Fact]
    public void WalksToADestinationAndStopsThereWithoutOvershooting(){
        var Scene=new Scene(StartY:0.9f);
        Scene.Run(10,LogicalVector3.Zero);
        Scene.Input.MoveTo(new LogicalVector3(6,0,8));
        var Farthest=0f;
        for(var Tick=0;Tick<600&&Scene.Input.HasDestination;Tick++){
            Scene.Input.Steer(Settings,Scene.Position);
            Scene.Run(1,Scene.Input.Direction);
            Farthest=MathF.Max(Farthest,MathF.Sqrt(Scene.X*Scene.X+Scene.Z*Scene.Z));
        }
        Assert.False(Scene.Input.HasDestination);
        Scene.Run(60,LogicalVector3.Zero);
        // 目的地は(6, 8)で原点から10 m。減速して止まるので、着いた位置は目的地の0.5 m以内で、行き過ぎも0.5 m以内。
        Assert.InRange(MathF.Sqrt(((Scene.X-6f)*(Scene.X-6f))+((Scene.Z-8f)*(Scene.Z-8f))),0f,0.5f);
        Assert.InRange(Farthest,0f,10.5f);
        Assert.Equal(LogicalVector3.Zero,Scene.State.Velocity);
    }
    [Fact]
    public void StopCancelsTheDestinationAndTheDirection(){
        var Intent=new MoveIntent();
        Intent.MoveTo(new LogicalVector3(1,0,1));
        Intent.Steer(Settings,LogicalVector3.Zero);
        Assert.True(Intent.HasDestination);
        Assert.False(Intent.Direction.LengthSquared.IsZero);
        Intent.Stop();
        Assert.False(Intent.HasDestination);
        Assert.True(Intent.Direction.LengthSquared.IsZero);
    }
    [Fact]
    public void APassingWaypoint_IsSteeredToAtFullSpeedAndNeverEndsTheWalk(){
        // ADR-0079/M0106: 経路探索のプラグインが置く経由点。目的地(遠い)ではなく経由点へ、最高速度の向きで向く。近くても止まらない。
        var Intent=new MoveIntent { Waypoint=new LogicalVector3(0,0,0.05f),HasWaypoint=true,WaypointIsEnd=false };
        Intent.MoveTo(new LogicalVector3(40,0,0));
        Intent.Steer(Settings,LogicalVector3.Zero);
        Assert.True(Intent.HasDestination);
        Assert.Equal(Forward,Intent.Direction);
        // 重なったときは向きを持たず、目的地も取り消さない。
        Intent.Steer(Settings,new LogicalVector3(0,0,0.05f));
        Assert.True(Intent.HasDestination);
        Assert.True(Intent.Direction.LengthSquared.IsZero);
    }
    [Fact]
    public void AnEndWaypoint_SlowsDownAndStopsLikeADestination(){
        var Intent=new MoveIntent { Waypoint=new LogicalVector3(0,0,2),HasWaypoint=true,WaypointIsEnd=true };
        Intent.MoveTo(new LogicalVector3(40,0,0));
        Intent.Steer(Settings,new LogicalVector3(0,0,1.7f));
        Assert.True(Intent.HasDestination);
        Assert.True(Intent.Direction.LengthSquared<LogicalScalar.One,"An approach inside the braking distance is slower than full speed.");
        Intent.Steer(Settings,new LogicalVector3(0,0,1.95f));
        Assert.False(Intent.HasDestination);
        Assert.False(Intent.HasWaypoint);
    }
    [Fact]
    public void AnEndWaypoint_BrakesOverTheFixedPathBrakingDistanceWhenTheSettingsHaveOne(){
        // ADR-0078/M0112: UE の FixedPathBrakingDistance(TopDown は 200 cm)。設定が無ければ、制動から計算した距離(速さ 6、制動 20 では 0.9 m)の手前から減速する。
        // 固定の距離があるときは、経路の最後の点へ 2 m の手前から減速する。目的地へ直線で歩くとき(経由点が無いとき)には使わない。
        var Fixed=new CharacterMotion { FixedPathBrakingDistance=LogicalScalar.FromSingle(2f) };
        var Direct=new MoveIntent { Waypoint=new LogicalVector3(0,0,10),HasWaypoint=true,WaypointIsEnd=true };
        Direct.MoveTo(new LogicalVector3(0,0,10));
        Direct.Steer(Settings,new LogicalVector3(0,0,8.5f));
        Assert.Equal(Forward,Direct.Direction);
        var Braked=new MoveIntent { Waypoint=new LogicalVector3(0,0,10),HasWaypoint=true,WaypointIsEnd=true };
        Braked.MoveTo(new LogicalVector3(0,0,10));
        Braked.Steer(Fixed,new LogicalVector3(0,0,8.5f));
        var Scale=LogicalScalar.TrySquareRoot(Braked.Direction.LengthSquared,out var Length)?Length:LogicalScalar.Zero;
        Assert.InRange(Scale.TryToSingle(out var Value)?Value:float.NaN,0.7f,0.8f);
        var Straight=new MoveIntent();
        Straight.MoveTo(new LogicalVector3(0,0,10));
        Straight.Steer(Fixed,new LogicalVector3(0,0,8.5f));
        Assert.Equal(Forward,Straight.Direction);
    }
    [Fact]
    public void StopAndAMissingDestination_ClearTheWaypoint(){
        var Intent=new MoveIntent { Waypoint=new LogicalVector3(1,0,1),HasWaypoint=true };
        Intent.Steer(Settings,LogicalVector3.Zero);
        Assert.False(Intent.HasWaypoint);
        Intent.HasWaypoint=true;
        Intent.MoveTo(new LogicalVector3(3,0,3));
        Intent.Stop();
        Assert.False(Intent.HasWaypoint);
        Assert.False(Intent.HasDestination);
    }
    [Fact]
    public void WithoutAWaypoint_SteerIsTheStraightWalkItWas(){
        // 経路探索のプラグインが無い・経路が無いときは、MoveIntent は従来どおり目的地へ直線で向く。
        var Intent=new MoveIntent();
        Intent.MoveTo(new LogicalVector3(0,0,40));
        Intent.Steer(Settings,LogicalVector3.Zero);
        Assert.Equal(Forward,Intent.Direction);
        Assert.False(Intent.HasWaypoint);
    }
    [Fact]
    public void ASteerWithoutADestinationLeavesTheDirectionAlone(){
        var Intent=new MoveIntent { Direction=Forward };
        Intent.Steer(Settings,LogicalVector3.Zero);
        Assert.Equal(Forward,Intent.Direction);
    }
    [Fact]
    public void JumpReachesTheBallisticApex(){
        var Scene=new Scene(StartY:0.9f);
        Scene.Run(10,LogicalVector3.Zero);
        var Ground=Scene.Y;
        var Apex=Ground;
        Scene.Run(90,LogicalVector3.Zero,JumpFirstTick:true,AfterTick:()=>Apex=MathF.Max(Apex,Scene.Y));
        // v^2 / 2g = 5^2 / (2 * 9.80665) = 1.2747 m.
        Assert.InRange(Apex-Ground,1.2747f-0.06f,1.2747f+0.06f);
        Assert.Equal(CharacterMotionMode.Walking,Scene.State.Mode);
        Assert.InRange(Scene.Y,Ground-0.005f,Ground+0.005f);
    }
    [Fact]
    public void WalksAtMaxWalkSpeedAndBrakesToAStop(){
        var Scene=new Scene(StartY:0.9f);
        Scene.Run(120,Forward);
        Assert.InRange(ToSingle(Scene.State.Velocity.F),5.99f,6.01f);
        var Released=Scene.Z;
        Scene.Run(60,LogicalVector3.Zero);
        // Braking deceleration 20 m/s^2 without braking friction: 6^2 / (2 * 20) = 0.9 m.
        Assert.InRange(Scene.Z-Released,0.8f,1.0f);
        Assert.Equal(LogicalVector3.Zero,Scene.State.Velocity);
    }
    [Fact]
    public void SlidesAlongAWall(){
        var Scene=new Scene(StartY:0.9f).AddBox(1f,0f,-20f,2f,3f,20f);
        var Diagonal=new LogicalVector3(LogicalScalar.FromSingle(0.7071068f),LogicalScalar.Zero,LogicalScalar.FromSingle(0.7071068f));
        Scene.Run(120,Diagonal);
        Assert.InRange(Scene.X,0.6f,0.66f);
        Assert.True(Scene.Z>3f,$"z={Scene.Z}");
        Assert.InRange(Scene.Y,0.899f,0.905f);
    }
    [Fact]
    public void WalksUpAThirtyDegreeRampButNotAFiftyDegreeSlope(){
        // Ramps start at z=2 and rise along +Z.
        float Rise(float Degrees,float Run)=>MathF.Tan(Degrees*MathF.PI/180f)*Run;
        var Ramp=new Scene(StartY:0.9f).AddQuad(new(-3,0,2),new(3,0,2),new(3,Rise(30f,6f),8),new(-3,Rise(30f,6f),8));
        Ramp.Run(90,Forward);
        Assert.True(Ramp.Y>1.9f,$"30 degrees: y={Ramp.Y}");
        var Slope=new Scene(StartY:0.9f).AddQuad(new(-3,0,2),new(3,0,2),new(3,Rise(50f,6f),8),new(-3,Rise(50f,6f),8));
        Slope.Run(90,Forward);
        Assert.True(Slope.Y<1.05f,$"50 degrees: y={Slope.Y}");
        Assert.True(Slope.Z<2.2f,$"50 degrees: z={Slope.Z}");
    }
    [Fact]
    public void StepsOntoAFortyCentimetreStepButNotAFiftyCentimetreOne(){
        var Low=new Scene(StartY:0.9f).AddBox(-3f,0f,2f,3f,0.4f,8f);
        Low.Run(90,Forward);
        Assert.InRange(Low.Y,1.29f,1.31f);
        Assert.True(Low.Z>3f,$"z={Low.Z}");
        var High=new Scene(StartY:0.9f).AddBox(-3f,0f,2f,3f,0.5f,8f);
        High.Run(90,Forward);
        Assert.InRange(High.Y,0.899f,0.905f);
        Assert.InRange(High.Z,1.6f,1.66f);
    }
    [Fact]
    public void TurnsTowardTheInputAtTheRotationRate(){
        var Scene=new Scene(StartY:0.9f);
        Scene.Run(1,LogicalVector3.UnitR);
        // 500 degrees/s at 60 Hz: 8.333 degrees in one tick from +F toward +R.
        var Degrees=MathF.Atan2(ToSingle(Scene.State.Facing.R),ToSingle(Scene.State.Facing.F))*180f/MathF.PI;
        Assert.InRange(Degrees,8.3f,8.37f);
        Scene.Run(10,LogicalVector3.UnitR);
        Assert.Equal(LogicalVector3.UnitR,Scene.State.Facing);
        var Rotation=CharacterMotor.FacingRotation(Scene.State.Facing);
        Assert.True(Rotation.TryRotate(LogicalVector3.UnitF,out var Rotated));
        Assert.InRange(ToSingle(Rotated.R),0.9999f,1.0001f);
    }
    [Fact]
    public void RepeatedRunsAreBitIdentical(){
        (LogicalVector3,CharacterMotionState) Play(){
            var Scene=new Scene().AddBox(-3f,0f,2f,3f,0.4f,8f).AddBox(1f,0f,-20f,2f,3f,20f);
            Scene.Run(40,new LogicalVector3(LogicalScalar.FromSingle(0.6f),LogicalScalar.Zero,LogicalScalar.FromSingle(0.8f)));
            Scene.Run(30,Forward,JumpFirstTick:true);
            Scene.Run(30,-LogicalVector3.UnitR);
            return (Scene.Position,Scene.State);
        }
        Assert.Equal(Play(),Play());
    }
}
