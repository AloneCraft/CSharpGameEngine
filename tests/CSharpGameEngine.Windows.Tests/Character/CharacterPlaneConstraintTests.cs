using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0107：キネマティックキャラクターに対するUEの平面制約（bConstrainToPlane、PlaneConstraintNormalとOrigin、bSnapToPlaneAtStart）。
/// TopDownテンプレートでは有効にするが、法線は0のままなので何も制限しない
///（Blueprintで確認：PlaneConstraintAxisSettingはCustom、法線は0）。
/// </summary>
public sealed class CharacterPlaneConstraintTests {
    private static readonly LogicalScalar DeltaSeconds=LogicalScalar.One/LogicalScalar.FromInt64(60);
    private sealed class Walker {
        private readonly DeterministicPhysicsWorld World=new();
        private readonly CharacterMotor Motor;
        private readonly CharacterMotion Settings;
        public LogicalVector3 Position;
        public CharacterMotionState State;
        public MoveIntent Input;
        public Walker(CharacterMotion Settings,float X=0f,float Y=0.9f,float Z=0f){
            this.Settings=Settings;
            this.Motor=new CharacterMotor(this.World);
            this.Position=new LogicalVector3(X,Y,Z);
            this.State.Facing=LogicalVector3.UnitF;
            this.World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh("floor",[new(-20,0,-20),new(20,0,-20),new(20,0,20),new(-20,0,20)],[0,1,2,0,2,3]));
            Assert.Equal(PhysicsUpdateStatus.Success,this.World.Upsert(new PhysicsBodyDescription(
                new Entity(1),1UL,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset="floor" },null,
                LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        }
        public void Run(int Ticks,LogicalVector3 Move,bool Jump=false){
            for(var Tick=0;Tick<Ticks;Tick++){
                this.Input.Direction=Move;
                this.Input.JumpRequested=Jump&&Tick==0;
                this.Motor.Step(this.Settings,ref this.Position,ref this.State,ref this.Input,DeltaSeconds);
            }
        }
        public float X=>ToSingle(this.Position.R);
        public float Y=>ToSingle(this.Position.U);
        public float Z=>ToSingle(this.Position.F);
    }
    private static float ToSingle(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private static CharacterMotion Constrained(LogicalVector3 Normal,LogicalVector3 Origin=default,bool Snap=true)=>new() {
        ConstrainToPlane=true,PlaneNormal=Normal,PlaneOrigin=Origin,SnapToPlaneAtStart=Snap,
    };
    [Fact]
    public void AZeroNormalConstrainsNothing_AsInUnrealsTopDownCharacter(){
        var Free=new Walker(new CharacterMotion());
        var Zero=new Walker(Constrained(LogicalVector3.Zero));
        var Diagonal=new LogicalVector3(0.6f,0f,0.8f);
        Free.Run(150,Diagonal);
        Zero.Run(150,Diagonal);
        Free.Run(60,Diagonal,Jump:true);
        Zero.Run(60,Diagonal,Jump:true);
        Assert.Equal(Free.Position,Zero.Position);
        Assert.Equal(Free.State.Velocity,Zero.State.Velocity);
        Assert.Equal(Free.State.Mode,Zero.State.Mode);
        Assert.True(Free.Z>3f,"the character did walk");
    }
    [Fact]
    public void ANormalAlongRRemovesAllMovementAcrossThePlane(){
        var Walker=new Walker(Constrained(LogicalVector3.UnitR,Snap:false),X:0.5f);
        Walker.Run(120,new LogicalVector3(0.6f,0f,0.8f));
        // 斜めに移動しても+F方向への移動だけとなり、キャラクターが開始した平面からのオフセットは変化しない。
        Assert.True(Walker.Z>2f);
        Assert.Equal(0.5f,Walker.X,5);
        Assert.Equal(0f,ToSingle(Walker.State.Velocity.R));
    }
    [Fact]
    public void SnapToPlaneAtStartMovesTheCharacterOntoThePlaneOnTheFirstTickOnly(){
        var Snapped=new Walker(Constrained(LogicalVector3.UnitR,new LogicalVector3(2f,0f,0f)),X:0.5f);
        Snapped.Run(1,LogicalVector3.Zero);
        Assert.Equal(2f,Snapped.X,5);
        Assert.True(Snapped.State.PlaneSnapped);
        // 後続の移動入力が平面を横切る方向でも、平面上にとどまる。
        Snapped.Run(60,new LogicalVector3(1f,0f,0f));
        Assert.Equal(2f,Snapped.X,5);
        var NotSnapped=new Walker(Constrained(LogicalVector3.UnitR,new LogicalVector3(2f,0f,0f),Snap:false),X:0.5f);
        NotSnapped.Run(60,LogicalVector3.Zero);
        Assert.Equal(0.5f,NotSnapped.X,5);
    }
    [Fact]
    public void ANormalThatIsNotAUnitVectorIsNormalized(){
        var Unit=new Walker(Constrained(LogicalVector3.UnitR,Snap:false));
        var Long=new Walker(Constrained(new LogicalVector3(5f,0f,0f),Snap:false));
        Unit.Run(90,new LogicalVector3(0.6f,0f,0.8f));
        Long.Run(90,new LogicalVector3(0.6f,0f,0.8f));
        Assert.Equal(Unit.Position,Long.Position);
    }
    [Fact]
    public void AHorizontalPlaneKeepsTheCharacterFromLeavingItByJumping(){
        var Walker=new Walker(Constrained(LogicalVector3.UnitU,new LogicalVector3(0f,0.9f,0f)));
        Walker.Run(30,LogicalVector3.Zero);
        var Highest=Walker.Y;
        for(var Tick=0;Tick<60;Tick++){
            Walker.Run(1,LogicalVector3.Zero,Jump:Tick==0);
            Highest=MathF.Max(Highest,Walker.Y);
        }
        Assert.InRange(Highest,0.899f,0.905f);
        Assert.Equal(0f,ToSingle(Walker.State.Velocity.U),4);
    }
    [Fact]
    public void WalkingStillWorksOnAPlaneParallelToTheGround(){
        var Walker=new Walker(Constrained(LogicalVector3.UnitU,new LogicalVector3(0f,0.9f,0f)));
        Walker.Run(120,new LogicalVector3(0f,0f,1f));
        Assert.True(Walker.Z>5f);
        Assert.InRange(Walker.Y,0.899f,0.905f);
    }
}
