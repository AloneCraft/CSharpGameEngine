using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
public enum CharacterMotionMode:byte {
    Walking,
    Falling,
}
/// <summary>
/// Kinematic character movement (ADR-0067), after Unreal's CharacterMovementComponent. Values are
/// metres, seconds and degrees with Y up; the defaults are Unreal's ThirdPerson Blueprint values.
/// The Entity's Transform position is the capsule centre. <see cref="CapsuleHalfHeight"/> includes
/// the hemispheres, as in Unreal.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,10)]
public struct CharacterMotion() {
    public LogicalScalar CapsuleRadius=LogicalScalar.FromSingle(0.35f);
    public LogicalScalar CapsuleHalfHeight=LogicalScalar.FromSingle(0.9f);
    public LogicalScalar WalkSpeed=LogicalScalar.FromSingle(6f);
    public LogicalScalar MinAnalogWalkSpeed=LogicalScalar.FromSingle(0.2f);
    public LogicalScalar Acceleration=LogicalScalar.FromSingle(15f);
    public LogicalScalar Braking=LogicalScalar.FromSingle(20f);
    public LogicalScalar AirBraking=LogicalScalar.FromSingle(15f);
    public LogicalScalar GroundFriction=LogicalScalar.FromSingle(8f);
    /// <summary>Friction while braking without input (Unreal's separate braking friction × factor).</summary>
    public LogicalScalar BrakingFriction=LogicalScalar.Zero;
    public LogicalScalar JumpSpeed=LogicalScalar.FromSingle(5f);
    public LogicalScalar AirControl=LogicalScalar.FromSingle(0.35f);
    public LogicalScalar AirControlBoostMultiplier=LogicalScalar.FromSingle(2f);
    public LogicalScalar AirControlBoostVelocityThreshold=LogicalScalar.FromSingle(0.25f);
    public LogicalScalar GravityScale=LogicalScalar.One;
    public LogicalScalar StepHeight=LogicalScalar.FromSingle(0.45f);
    [Angle(AngleUnit.Degrees)]
    public LogicalScalar MaxSlopeDegrees=LogicalScalar.FromSingle(44.765083f);
    [Angle(AngleUnit.Degrees)]
    public LogicalScalar TurnRateDegrees=LogicalScalar.FromSingle(500f);
    public bool FaceMovement=true;
    /// <summary>地面を歩いて、この距離(m)進むごとに <see cref="Footstep"/> を1回送る。0以下なら送らない。</summary>
    public LogicalScalar StepLength=LogicalScalar.FromSingle(1.2f);
    public PhysicsLayerMask CollidesWith=PhysicsLayerMask.All;
}
/// <summary>Authoritative movement state; part of the snapshot so replays continue deterministically.</summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,11)]
public struct CharacterMotionState {
    public LogicalVector3 Velocity;
    public CharacterMotionMode Mode;
    /// <summary>Horizontal unit facing; zero until the first tick initializes it from the Transform.</summary>
    public LogicalVector3 Facing;
    /// <summary>前回の <see cref="Footstep"/> からの歩行距離(m)。</summary>
    public LogicalScalar StrideDistance;
    /// <summary>True while standing on a walkable floor (ADR-0077). Derived from <see cref="Mode"/>, so it is not stored.</summary>
    public readonly bool OnGround=>this.Mode==CharacterMotionMode.Walking;
    public readonly bool Falling=>this.Mode==CharacterMotionMode.Falling;
}
/// <summary>
/// What the player wants this fixed tick (ADR-0077): a world-space horizontal direction whose length
/// (at most 1) scales acceleration and speed, and a jump request consumed by the next tick.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,12)]
public struct MoveIntent {
    public LogicalVector3 Direction;
    public bool JumpRequested;
    /// <summary>歩いて向かう地点(ADR-0077)。<see cref="HasDestination"/>のあいだ、毎tick<see cref="Direction"/>をこの地点へ向けて書き換える。</summary>
    public LogicalVector3 Destination;
    public bool HasDestination;
    /// <summary>
    /// 目的地の手前の経由点(ADR-0079/M0106)。経路探索のプラグイン(Navigation)が置く。<see cref="HasWaypoint"/>のあいだ、<see cref="Steer"/>は
    /// <see cref="Destination"/>ではなくここへ向ける。<see cref="WaypointIsEnd"/>が false なら通過点(最高速度のまま、着いたかどうかは置いた側が決める)、
    /// true なら最後の点(<see cref="Destination"/>と同じく減速して止まり、着いたら<see cref="Stop"/>)。置くプラグインが無ければ使われない。
    /// </summary>
    public LogicalVector3 Waypoint;
    public bool HasWaypoint;
    public bool WaypointIsEnd;
    /// <summary>Asks for a jump; the next tick consumes it if the character is on the ground.</summary>
    public void Jump()=>this.JumpRequested=true;
    /// <summary>
    /// 地点を指して、そこまで歩かせる。高さを無視して向かう。経路探索のプラグイン(Navigation)があれば、その経路(経由点)に沿って歩き、無ければ直線で歩く(障害物はコリジョンで止まる)。
    /// 着いたら<see cref="HasDestination"/>は false に戻る。
    /// </summary>
    public void MoveTo(LogicalVector3 Point){
        this.Destination=Point;
        this.HasDestination=true;
    }
    /// <summary>目的地を取り消して、その場に止まる。</summary>
    public void Stop(){
        this.HasDestination=false;
        this.HasWaypoint=false;
        this.Direction=default;
    }
    private static readonly LogicalScalar ArrivalRadius=LogicalScalar.FromSingle(0.15f);
    private static readonly LogicalScalar SlowestApproach=LogicalScalar.FromSingle(0.25f);
    /// <summary>
    /// 目的地(<see cref="MoveTo"/>)、または経由点(<see cref="Waypoint"/>)へ向けて<see cref="Direction"/>を書く。毎tick、キャラクターの現在位置から呼ぶ。
    /// 通過点でない地点には、制動距離の手前から長さを縮めて減速するので、行き過ぎずに止まる。着いたら目的地を取り消す。
    /// </summary>
    public void Steer(in CharacterMotion Settings,LogicalVector3 Position){
        if(!this.HasDestination){
            this.HasWaypoint=false;
            return;
        }
        var Target=this.HasWaypoint?this.Waypoint:this.Destination;
        var Passing=this.HasWaypoint&&!this.WaypointIsEnd;
        var Planar=new LogicalVector3(Target.R-Position.R,LogicalScalar.Zero,Target.F-Position.F);
        var HasDistance=LogicalScalar.TrySquareRoot(Planar.LengthSquared,out var Distance);
        if(Passing){
            // 通過点: 最高速度のまま向ける。重なったときは向きを持たない(次の tick に置く側が次の点へ進める)。
            this.Direction=HasDistance&&!Distance.IsZero?Planar/Distance:default;
            return;
        }
        if(!HasDistance||Distance<=ArrivalRadius){
            this.Stop();
            return;
        }
        var Ramp=Settings.Braking>LogicalScalar.Zero?Settings.WalkSpeed*Settings.WalkSpeed/(Settings.Braking*LogicalScalar.FromInt64(2)):LogicalScalar.One;
        var Scale=Ramp>LogicalScalar.Zero?Distance/Ramp:LogicalScalar.One;
        Scale=Scale<SlowestApproach?SlowestApproach:Scale>LogicalScalar.One?LogicalScalar.One:Scale;
        this.Direction=Planar/Distance*Scale;
    }
}
