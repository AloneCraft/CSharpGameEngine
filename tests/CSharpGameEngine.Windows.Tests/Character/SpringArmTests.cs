using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>ADR-0067: control rotation and the spring arm that shortens at obstacles.</summary>
public sealed class SpringArmTests {
    private static readonly SpringArm Arm=new() { PivotOffset=new LogicalVector3(0f,0.0849f,0f),TargetArmLength=LogicalScalar.FromSingle(4f) };
    private static readonly LogicalVector3 Owner=new(0f,1f,0f);
    private static float ToSingle(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private static DeterministicPhysicsWorld WorldWith(params LogicalVector3[][]Quads){
        DeterministicPhysicsWorld World=new();
        for(var Index=0;Index<Quads.Length;Index++){
            var Id=$"quad{Index}";
            World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh(Id,Quads[Index],[0,1,2,0,2,3]));
            Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
                new Entity(Index+1),(ulong)(Index+1),new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id },null,
                LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        }
        return World;
    }
    private static LogicalVector3[] Floor=>[new(-20,0,-20),new(20,0,-20),new(20,0,20),new(-20,0,20)];
    private static LogicalVector3[] Wall=>[new(-5,0,-2),new(-5,5,-2),new(5,5,-2),new(5,0,-2)];
    private static LogicalVector3[] Ceiling=>[new(-5,1.05f,-5),new(-5,1.05f,5),new(5,1.05f,5),new(5,1.05f,-5)];
    private static LogicalScalar Solve(DeterministicPhysicsWorld World,PlayerController Controller,out LogicalVector3 Camera){
        Assert.True(new SpringArmSolver(World).TrySolve(Arm,Owner,LogicalQuaternion.Identity,PlayerControl.ControlRotation(Controller),out Camera,out var Length));
        return Length;
    }
    [Fact]
    public void ControlRotation_YawTurnsTowardRightAndPositivePitchLooksUp(){
        Assert.True(PlayerControl.ControlRotation(new PlayerController { ControlYawDegrees=90f }).TryRotate(LogicalVector3.UnitF,out var Right));
        Assert.InRange(ToSingle(Right.R),0.9999f,1.0001f);
        Assert.True(PlayerControl.ControlRotation(new PlayerController { ControlPitchDegrees=30f }).TryRotate(LogicalVector3.UnitF,out var Up));
        Assert.InRange(ToSingle(Up.U),0.4999f,0.5001f);
        Assert.InRange(ToSingle(Up.F),0.8659f,0.8662f);
        // The camera's up stays on the world-up side: no roll from the yaw-then-pitch order.
        Assert.True(PlayerControl.ControlRotation(new PlayerController { ControlYawDegrees=90f,ControlPitchDegrees=30f }).TryRotate(LogicalVector3.UnitU,out var CameraUp));
        Assert.InRange(ToSingle(CameraUp.U),0.8659f,0.8662f);
    }
    [Fact]
    public void AddLook_ClampsPitchAndWrapsYaw(){
        PlayerController Controller=new();
        PlayerControl.AddLook(ref Controller,170f,120f);
        PlayerControl.AddLook(ref Controller,30f,0f);
        Assert.Equal(160f,Controller.ControlYawDegrees,3);
        Assert.Equal(89f,Controller.ControlPitchDegrees);
        PlayerControl.AddLook(ref Controller,float.NaN,-500f);
        Assert.Equal(160f,Controller.ControlYawDegrees,3);
        Assert.Equal(-89f,Controller.ControlPitchDegrees);
    }
    [Fact]
    public void MoveDirection_IsRelativeToControlYawAndClampedToUnitLength(){
        var Forward=PlayerControl.MoveDirection(new PlayerController { ControlYawDegrees=90f,ControlPitchDegrees=60f },0f,1f);
        Assert.InRange(ToSingle(Forward.R),0.9999f,1.0001f);
        Assert.Equal(LogicalScalar.Zero,Forward.U);
        var Diagonal=PlayerControl.MoveDirection(new PlayerController(),1f,1f);
        Assert.InRange(ToSingle(Diagonal.R),-0.7072f,-0.7071f);
        Assert.InRange(ToSingle(Diagonal.F),0.7071f,0.7072f);
    }
    /// <summary>
    /// The renderer's view is Matrix4x4.CreateLookAt (right-handed), so a camera looking along F with
    /// up U sees Forward x Up on its right. Right input must move toward it and positive yaw input
    /// (Unreal's AddControllerYawInput) must turn toward it; the ThirdPerson template had both mirrored.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(-135f)]
    public void RightInputAndPositiveYaw_GoTowardTheScreenRight(float YawDegrees){
        var Controller=new PlayerController { ControlYawDegrees=YawDegrees };
        var View=ViewBasis(Controller);
        var Right=PlayerControl.MoveDirection(Controller,1f,0f);
        Assert.True(Vector3.Dot(ToVector(Right),View.ScreenRight)>0.999f,$"right move {ToVector(Right)} vs screen right {View.ScreenRight}");
        PlayerControl.AddLook(ref Controller,10f,0f);
        Assert.True(Vector3.Dot(ViewBasis(Controller).Forward,View.ScreenRight)>0.1f,"positive yaw turned away from the screen right");
    }
    private static (Vector3 Forward,Vector3 ScreenRight)ViewBasis(in PlayerController Controller){
        Assert.True(PlayerControl.ControlRotation(Controller).TryRotate(LogicalVector3.UnitF,out var Forward));
        var View=Matrix4x4.CreateLookAt(Vector3.Zero,ToVector(Forward),Vector3.UnitY);
        // Row-vector view matrix: the first column is the camera's screen-right axis in world space.
        return (ToVector(Forward),new Vector3(View.M11,View.M21,View.M31));
    }
    private static Vector3 ToVector(LogicalVector3 Value)=>new(ToSingle(Value.R),ToSingle(Value.U),ToSingle(Value.F));
    [Fact]
    public void ArmReachesItsTargetLengthInTheOpen(){
        var Length=Solve(WorldWith(Floor),new PlayerController(),out var Camera);
        Assert.Equal(LogicalScalar.FromSingle(4f),Length);
        Assert.InRange(ToSingle(Camera.U),1.0848f,1.0850f);
        Assert.InRange(ToSingle(Camera.F),-4.0001f,-3.9999f);
    }
    [Fact]
    public void ArmStopsAProbeRadiusBeforeAWallBehind(){
        // A zero-thickness wall at z = -2; the full arm's end lies beyond it and must not tunnel.
        var Length=Solve(WorldWith(Floor,Wall),new PlayerController(),out var Camera);
        Assert.InRange(ToSingle(Length),1.879f,1.881f);
        Assert.InRange(ToSingle(Camera.F),-1.881f,-1.879f);
    }
    [Fact]
    public void ArmShortensAgainstTheFloorWhenLookingUp(){
        // Looking up 60 degrees drops the arm at 60 degrees: (1.0849 - 0.12) / sin 60 = 1.1142 m.
        var Length=Solve(WorldWith(Floor),new PlayerController { ControlPitchDegrees=60f },out var Camera);
        Assert.InRange(ToSingle(Length),1.113f,1.1155f);
        Assert.InRange(ToSingle(Camera.U),0.1195f,0.1215f);
    }
    [Fact]
    public void ArmCollapsesToThePivotWhenThePivotIsInsideGeometry(){
        var Length=Solve(WorldWith(Ceiling),new PlayerController(),out var Camera);
        Assert.Equal(LogicalScalar.Zero,Length);
        Assert.InRange(ToSingle(Camera.U),1.0848f,1.0850f);
    }
    [Fact]
    public void DisabledCollisionIgnoresTheWall(){
        var World=WorldWith(Wall);
        Assert.True(new SpringArmSolver(World).TrySolve(Arm with { DoCollisionTest=false },Owner,LogicalQuaternion.Identity,LogicalQuaternion.Identity,out _,out var Length));
        Assert.Equal(LogicalScalar.FromSingle(4f),Length);
    }
}
