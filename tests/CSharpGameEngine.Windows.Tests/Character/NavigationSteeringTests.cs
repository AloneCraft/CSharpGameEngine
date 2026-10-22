using CSharpGameEngine.Entities;
using CSharpGameEngine.Navigation;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0079/M0106: ナビメッシュの経路を追って歩く(<c>NavigationSteering</c>)。決定的な物理世界の壁を、本物の<c>CharacterMotor</c>で回り込めること、
/// ナビメッシュが無いと従来どおり直線で壁に止まること、着けない目的地は行ける一番近い点で止まることを確かめる。
/// </summary>
public sealed class NavigationSteeringTests {
    private const int TickRate=60;
    private static readonly LogicalScalar DeltaSeconds=LogicalScalar.One/LogicalScalar.FromInt64(TickRate);
    private static readonly CharacterMotion Motion=new();
    private static readonly NavAgent Agent=new();
    private static LogicalVector3 P(float R,float U,float F)=>new(R,U,F);
    private static float F(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    /// <summary>
    /// 床(±20 m)と、R が ±0.25 m、F が -10〜3 m の高さ 2 m の壁。壁の北(F が 3 より大きい側)だけが通れる。
    /// ナビメッシュは同じ形を手で作った 3 つの長方形: 壁の西側(R -9.5〜-1)、東側(R 1〜9.5)、壁の北の端をまわる通路(R -1〜1、F 4〜9.5)。
    /// 壁から 0.75 m 以上離れた所だけが歩ける(半径 0.35 m + 余裕)。
    /// </summary>
    private sealed class Walled {
        private readonly DeterministicPhysicsWorld World=new();
        private readonly CharacterMotor Motor;
        private ulong NextId=1;
        public LogicalVector3 Position=P(-5f,0.9f,0f);
        public CharacterMotionState State;
        public MoveIntent Intent;
        public NavAgentState Walk;
        public readonly Navigator Navigator=new();
        public Walled(bool WithNavigation){
            this.Motor=new CharacterMotor(this.World);
            this.State.Facing=LogicalVector3.UnitF;
            this.AddQuad(P(-20,0,-20),P(20,0,-20),P(20,0,20),P(-20,0,20));
            this.AddBox(-0.25f,0f,-10f,0.25f,2f,3f);
            if(WithNavigation){ this.Navigator.Add("Walled",BuildMesh()); }
        }
        private static NavMesh BuildMesh(){
            var Settings=new NavMeshSettings(NavigationDefaults.AgentRadius,NavigationDefaults.AgentHeight,NavigationDefaults.AgentMaxClimb,NavigationDefaults.AgentMaxSlopeDegrees,
                LogicalScalar.One/LogicalScalar.FromInt64(2),LogicalScalar.One/LogicalScalar.FromInt64(10));
            // 格子の目 0.5 m、原点 (-10, -1, -10)、床の高さは段 10(= 0 m)。
            NavPolygon[]Polygons=[new(1,1,18,39,10),new(22,1,39,39,10),new(18,28,22,39,10)];
            NavPortal[]Portals=[new(0,2,18,28,18,39),new(2,1,22,28,22,39)];
            Assert.True(NavMesh.TryCreate(Settings,P(-10,-1,-10),40,40,Polygons,Portals,out var Mesh,out var Error),Error);
            return Mesh!;
        }
        private void AddQuad(LogicalVector3 A,LogicalVector3 B,LogicalVector3 C,LogicalVector3 D){
            var Id=$"mesh{this.NextId}";
            this.World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh(Id,[A,B,C,D],[0,1,2,0,2,3]));
            Assert.Equal(PhysicsUpdateStatus.Success,this.World.Upsert(new PhysicsBodyDescription(
                new Entity((int)this.NextId),this.NextId,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id },null,
                LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
            this.NextId++;
        }
        private void AddBox(float MinX,float MinY,float MinZ,float MaxX,float MaxY,float MaxZ){
            this.AddQuad(P(MinX,MaxY,MinZ),P(MaxX,MaxY,MinZ),P(MaxX,MaxY,MaxZ),P(MinX,MaxY,MaxZ));
            this.AddQuad(P(MinX,MinY,MinZ),P(MinX,MinY,MaxZ),P(MaxX,MinY,MaxZ),P(MaxX,MinY,MinZ));
            this.AddQuad(P(MinX,MinY,MinZ),P(MaxX,MinY,MinZ),P(MaxX,MaxY,MinZ),P(MinX,MaxY,MinZ));
            this.AddQuad(P(MinX,MinY,MaxZ),P(MinX,MaxY,MaxZ),P(MaxX,MaxY,MaxZ),P(MaxX,MinY,MaxZ));
            this.AddQuad(P(MinX,MinY,MinZ),P(MinX,MaxY,MinZ),P(MinX,MaxY,MaxZ),P(MinX,MinY,MaxZ));
            this.AddQuad(P(MaxX,MinY,MinZ),P(MaxX,MinY,MaxZ),P(MaxX,MaxY,MaxZ),P(MaxX,MaxY,MinZ));
        }
        /// <summary>1 tick: <c>NavigationSteering</c>、<c>MoveIntent.Steer</c>、<c>CharacterMotor.Step</c>(<c>CharacterMotionUpdate</c>と同じ順)。</summary>
        public void Tick(){
            new NavigationSteering().Update(new Transform { Position=this.Position },Motion,ref this.Intent,Agent,ref this.Walk,this.Navigator);
            this.Intent.Steer(Motion,this.Position);
            this.Motor.Step(Motion,ref this.Position,ref this.State,ref this.Intent,DeltaSeconds);
        }
        public int WalkUntilDone(int MaximumTicks){
            var Ticks=0;
            while(Ticks<MaximumTicks&&this.Intent.HasDestination){
                this.Tick();
                Ticks++;
            }
            return Ticks;
        }
        public float X=>F(this.Position.R);
        public float Z=>F(this.Position.F);
    }
    private static Walled Settled(bool WithNavigation){
        var Scene=new Walled(WithNavigation);
        for(var Tick=0;Tick<30;Tick++){ Scene.Tick(); }
        return Scene;
    }
    [Fact]
    public void WithNavigation_TheCharacterWalksAroundTheWallAndStopsAtTheGoal(){
        var Scene=Settled(WithNavigation:true);
        Scene.Intent.MoveTo(P(5f,0f,0f));
        var Ticks=Scene.WalkUntilDone(2400);
        Assert.False(Scene.Intent.HasDestination,$"The character did not arrive within {Ticks} ticks (at {Scene.X}, {Scene.Z}).");
        Assert.InRange(MathF.Sqrt((Scene.X-5f)*(Scene.X-5f)+Scene.Z*Scene.Z),0f,0.5f);
        Assert.False(Scene.Intent.HasWaypoint);
    }
    [Fact]
    public void WithoutNavigation_TheSameCharacterWalksStraightIntoTheWallAndStaysThere(){
        var Scene=Settled(WithNavigation:false);
        Scene.Intent.MoveTo(P(5f,0f,0f));
        var Ticks=Scene.WalkUntilDone(900);
        Assert.Equal(900,Ticks);
        Assert.True(Scene.Intent.HasDestination);
        Assert.InRange(Scene.X,-1f,-0.3f);
        Assert.False(Scene.Intent.HasWaypoint);
    }
    [Fact]
    public void AGoalInsideTheWall_IsWalkedToAsFarAsTheMeshGoesAndTheCharacterStops(){
        var Scene=Settled(WithNavigation:true);
        Scene.Intent.MoveTo(P(0f,0f,-5f));
        var Ticks=Scene.WalkUntilDone(1200);
        Assert.True(Ticks<1200,"The character kept pushing at the wall.");
        Assert.False(Scene.Intent.HasDestination);
        // 一番近い行ける点は壁の西側の縁(R が約 -1)。
        Assert.InRange(Scene.X,-1.6f,-0.6f);
        Assert.InRange(Scene.Z,-6f,-4f);
    }
    [Fact]
    public void ANewGoal_IsPlannedAgain_AndAFinishedWalkLeavesNoRouteBehind(){
        var Scene=Settled(WithNavigation:true);
        Scene.Intent.MoveTo(P(5f,0f,0f));
        for(var Tick=0;Tick<5;Tick++){ Scene.Tick(); }
        Assert.NotNull(Scene.Walk.Route);
        Assert.Equal(P(5f,0f,0f),Scene.Walk.Goal);
        Assert.True(Scene.Intent.HasWaypoint);
        // 目的地が大きく動いたら探し直す。小さく動いただけなら、今の経路を使い続ける。
        var Before=Scene.Walk.Route;
        Scene.Intent.MoveTo(P(5.05f,0f,0f));
        Scene.Tick();
        Assert.Same(Before,Scene.Walk.Route);
        Scene.Intent.MoveTo(P(-5f,0f,6f));
        Scene.Tick();
        Assert.NotSame(Before,Scene.Walk.Route);
        Assert.Equal(P(-5f,0f,6f),Scene.Walk.Goal);
        Scene.Intent.Stop();
        Scene.Tick();
        Assert.Null(Scene.Walk.Route);
        Assert.False(Scene.Walk.Planned);
        Assert.False(Scene.Intent.HasWaypoint);
    }
    [Fact]
    public void AStartOutsideEveryMesh_FallsBackToAStraightWalk(){
        var Scene=Settled(WithNavigation:true);
        Scene.Position=P(15f,0.9f,15f);
        for(var Tick=0;Tick<10;Tick++){ Scene.Tick(); }
        Scene.Intent.MoveTo(P(15f,0f,18f));
        Scene.Tick();
        Assert.Null(Scene.Walk.Route);
        Assert.True(Scene.Walk.Planned);
        Assert.False(Scene.Intent.HasWaypoint);
        var Ticks=Scene.WalkUntilDone(900);
        Assert.True(Ticks<900);
        Assert.InRange(Scene.Z,17.5f,18.2f);
    }
}
