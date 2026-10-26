using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Animation;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0107：ABP_Unarmedのイベントグラフ（CalculateDirectionによるDirection、移動方向を向く間の±45°制限、加速度から求めるShouldMove）と
/// 足IKの地面トレース。
/// </summary>
public sealed class LocomotionObserverTests {
    // ボディが+Fを向くと、マネキンのRightクリップは+R側へ移動する（MannequinAssetTests.TheDirectionalClipsTravelTheWayTheyAreNamedを参照）。
    [Theory]
    [InlineData(0f,3f,0f,1f,0f)]
    [InlineData(3f,0f,0f,1f,90f)]
    [InlineData(-3f,0f,0f,1f,-90f)]
    [InlineData(0f,-3f,0f,1f,180f)]
    [InlineData(3f,3f,0f,1f,45f)]
    [InlineData(-3f,3f,0f,1f,-45f)]
    [InlineData(3f,-3f,0f,1f,135f)]
    [InlineData(-3f,-3f,0f,1f,-135f)]
    // +R方向を向くと、Rightクリップが移動する側は-Fとなる。
    [InlineData(0f,-3f,1f,0f,90f)]
    [InlineData(3f,0f,1f,0f,0f)]
    [InlineData(0f,3f,1f,0f,-90f)]
    // 立ち止まっていると方向はない。
    [InlineData(0f,0f,0f,1f,0f)]
    public void CalculateDirection_IsTheSignedAngleFromTheFacingToTheVelocity(float VelocityR,float VelocityF,float FacingR,float FacingF,float Expected){
        Assert.Equal(Expected,LocomotionObserver.CalculateDirection(VelocityR,VelocityF,FacingR,FacingF),3);
    }
    [Fact]
    public void CalculateDirection_IgnoresTheLengthsOfItsInputsAndRefusesAZeroFacing(){
        Assert.Equal(45f,LocomotionObserver.CalculateDirection(0.001f,0.001f,0f,5f),3);
        Assert.Equal(0f,LocomotionObserver.CalculateDirection(1f,1f,0f,0f));
    }
    private static (CharacterMotionState Motion,MoveIntent Intent,CharacterMotion Settings,Transform Body)Character(float VelocityR,float VelocityU,float VelocityF,float PushR,float PushF,bool FaceMovement=true,bool Falling=false){
        return (
            new CharacterMotionState { Velocity=new LogicalVector3(VelocityR,VelocityU,VelocityF),Mode=Falling?CharacterMotionMode.Falling:CharacterMotionMode.Walking },
            new MoveIntent { Direction=new LogicalVector3(PushR,0f,PushF) },
            new CharacterMotion { FaceMovement=FaceMovement },
            new Transform());
    }
    [Fact]
    public void Observe_MovingAndPushedShouldMove(){
        var Animator=new Locomotion();
        var (Motion,Intent,Settings,Body)=Character(0f,0f,3f,0f,1f);
        LocomotionObserver.Observe(ref Animator,Motion,Intent,Settings,Body);
        Assert.Equal(3f,Animator.Speed,4);
        Assert.True(Animator.ShouldMove);
        Assert.Equal(0f,Animator.Direction,3);
        Assert.False(Animator.Falling);
    }
    [Fact]
    public void Observe_CoastingOrStandingStillShouldNotMove(){
        var Animator=new Locomotion();
        // 速く動いていても押されていない（加速度なし）場合、BlueprintのShouldMoveはfalseとなりIdleを再生する。
        var (Motion,Intent,Settings,Body)=Character(0f,0f,3f,0f,0f);
        LocomotionObserver.Observe(ref Animator,Motion,Intent,Settings,Body);
        Assert.Equal(3f,Animator.Speed,4);
        Assert.False(Animator.ShouldMove);
        // 壁を押している状態（速度なし）も移動中ではない。
        (Motion,Intent,Settings,Body)=Character(0f,0f,0f,0f,1f);
        LocomotionObserver.Observe(ref Animator,Motion,Intent,Settings,Body);
        Assert.False(Animator.ShouldMove);
        // 速度が0.01 cm/s未満なら立ち止まっていると見なす。
        (Motion,Intent,Settings,Body)=Character(0f,0f,0.00005f,0f,1f);
        LocomotionObserver.Observe(ref Animator,Motion,Intent,Settings,Body);
        Assert.False(Animator.ShouldMove);
        (Motion,Intent,Settings,Body)=Character(0f,0f,0.0002f,0f,1f);
        LocomotionObserver.Observe(ref Animator,Motion,Intent,Settings,Body);
        Assert.True(Animator.ShouldMove);
    }
    [Fact]
    public void Observe_TurningTowardTheMovementLimitsTheDirectionButStrafingDoesNot(){
        var Animator=new Locomotion();
        // ボディから見て後ろ向きに走る（まだ旋回していない）場合、生の角度は180°だが、移動方向を向く間は±45°に制限する。
        var (Motion,Intent,Settings,Body)=Character(0f,0f,-3f,0f,-1f,FaceMovement:true);
        LocomotionObserver.Observe(ref Animator,Motion,Intent,Settings,Body);
        Assert.Equal(45f,Animator.Direction,3);
        (Motion,Intent,Settings,Body)=Character(0f,0f,-3f,0f,-1f,FaceMovement:false);
        LocomotionObserver.Observe(ref Animator,Motion,Intent,Settings,Body);
        Assert.Equal(180f,Animator.Direction,3);
        (Motion,Intent,Settings,Body)=Character(3f,0f,0f,1f,0f,FaceMovement:true);
        LocomotionObserver.Observe(ref Animator,Motion,Intent,Settings,Body);
        Assert.Equal(45f,Animator.Direction,3);
        (Motion,Intent,Settings,Body)=Character(-3f,0f,0f,-1f,0f,FaceMovement:true);
        LocomotionObserver.Observe(ref Animator,Motion,Intent,Settings,Body);
        Assert.Equal(-45f,Animator.Direction,3);
    }
    [Fact]
    public void Observe_UsesTheBodysRotationNotTheWorldAxes(){
        var Animator=new Locomotion();
        var (Motion,Intent,Settings,_)=Character(3f,0f,0f,1f,0f,FaceMovement:false);
        // +Rを向いて（4分の1回転）、+R方向へ移動する場合は真正面となる。
        var Body=new Transform { Rotation=CharacterMotor.FacingRotation(new LogicalVector3(1f,0f,0f)) };
        LocomotionObserver.Observe(ref Animator,Motion,Intent,Settings,Body);
        Assert.Equal(0f,Animator.Direction,2);
    }
    [Fact]
    public void Observe_FallingAndRisingSpeedFollowTheMovementState(){
        var Animator=new Locomotion();
        var (Motion,Intent,Settings,Body)=Character(0f,4.5f,0f,0f,0f,Falling:true);
        LocomotionObserver.Observe(ref Animator,Motion,Intent,Settings,Body);
        Assert.True(Animator.Falling);
        Assert.Equal(4.5f,Animator.VerticalSpeed,4);
    }
    private static DeterministicPhysicsWorld WorldWithFloors(params (float Height,float Extent)[]Floors){
        DeterministicPhysicsWorld World=new();
        for(var Index=0;Index<Floors.Length;Index++){
            var Id=$"floor{Index}";
            var (Height,Extent)=Floors[Index];
            World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh(Id,[new LogicalVector3(-Extent,Height,-Extent),new LogicalVector3(Extent,Height,-Extent),new LogicalVector3(Extent,Height,Extent),new LogicalVector3(-Extent,Height,Extent)],[0,1,2,0,2,3]));
            Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
                new Entity(Index+1),(ulong)(Index+1),new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id },null,
                LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        }
        return World;
    }
    private static FixedAffineTransform Mesh(float Height){
        Assert.Equal(FixedTransformStatus.Success,FixedAffineTransform.TryFromTransform(new Transform { Position=new LogicalVector3(0f,Height,0f) },out var Result));
        return Result;
    }
    [Theory]
    [InlineData(0f,0f)]
    [InlineData(0.05f,0.05f)]
    [InlineData(-0.1f,-0.1f)]
    [InlineData(0.29f,0.29f)]
    [InlineData(-0.49f,-0.49f)]
    public void GroundHeight_IsTheGroundUnderTheFootRelativeToTheMeshOrigin(float FloorHeight,float Expected){
        var Height=FootIkUpdate.GroundHeight([],WorldWithFloors((FloorHeight,5f)),Mesh(0f),new Vector3(0.1f,0.3f,0.05f),0.3f,0.5f);
        Assert.NotNull(Height);
        Assert.Equal(Expected,Height!.Value,3);
    }
    [Theory]
    // レイの範囲外では立てる面がない。開始点より上（0.3 m）または終端より下（0.5 m）にある面。
    [InlineData(0.31f)]
    [InlineData(-0.51f)]
    public void GroundHeight_FindsNothingBeyondTheTraceLength(float FloorHeight){
        Assert.Null(FootIkUpdate.GroundHeight([],WorldWithFloors((FloorHeight,5f)),Mesh(0f),new Vector3(0.1f,0.3f,0.05f),0.3f,0.5f));
    }
    [Fact]
    public void GroundHeight_FollowsTheMeshInTheWorldAndTakesTheNearestSurface(){
        // メッシュは実際の床（0 m）上のテーブル（2.05 m）に2 mの高さで立つ。レイ開始点より下で最も近い面はテーブル。
        var Height=FootIkUpdate.GroundHeight([],WorldWithFloors((0f,5f),(2.05f,5f)),Mesh(2f),new Vector3(0.1f,0.3f,0.05f),0.3f,0.5f);
        Assert.Equal(0.05f,Height!.Value,3);
        // テーブルの端から外れると、届く範囲に床だけが残る。
        Assert.Null(FootIkUpdate.GroundHeight([],WorldWithFloors((0f,5f),(2.05f,0.2f)),Mesh(2f),new Vector3(1f,0.3f,0.05f),0.3f,0.5f));
    }
    [Fact]
    public void GroundHeight_RefusesAnEmptyRayAndAnEmptyWorld(){
        Assert.Null(FootIkUpdate.GroundHeight([],new DeterministicPhysicsWorld(),Mesh(0f),new Vector3(0f,0.3f,0f),0.3f,0.5f));
        Assert.Null(FootIkUpdate.GroundHeight([],WorldWithFloors((0f,5f)),Mesh(0f),new Vector3(0f,0.3f,0f),0f,0f));
        Assert.Null(FootIkUpdate.GroundHeight([],WorldWithFloors((0f,5f)),Mesh(0f),new Vector3(float.PositiveInfinity,0.3f,0f),0.3f,0.5f));
    }
}
