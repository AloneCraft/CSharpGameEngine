using System.Numerics;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0107：スプリングアームのラグ（UEのCameraLagSpeed、CameraLagMaxTimeStep、CameraLagMaxDistance、CameraRotationLagSpeed）とSocketOffset。
/// TopDownのアームはLag 3で回転ラグなし。回転ラグとオフセットはVehicleのチェイスカメラ用（M0109）。
/// </summary>
public sealed class CameraLagTests {
    private static float ToSingle(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private static LogicalVector3 Point(float R,float U,float F)=>new(R,U,F);
    private const float Step=1f/60f;
    [Fact]
    public void InterpolateTo_CoversTheFractionDtTimesSpeedOfTheDistance(){
        var Moved=CameraLag.InterpolateTo(Point(0,0,0),Point(10,0,0),Step,3f);
        Assert.Equal(0.5f,ToSingle(Moved.R),3);
        // 目標から離れた状態で長いフレームがあっても、目標を通り越さない。
        Assert.Equal(10f,ToSingle(CameraLag.InterpolateTo(Point(0,0,0),Point(10,0,0),1f,3f).R),4);
        // 速度0なら即時追従し、UEのKINDA_SMALL_NUMBERより小さい距離は到達済みと見なす。
        Assert.Equal(Point(10,0,0),CameraLag.InterpolateTo(Point(0,0,0),Point(10,0,0),Step,0f));
        Assert.Equal(Point(0.0000005f,0,0),CameraLag.InterpolateTo(Point(0,0,0),Point(0.0000005f,0,0),Step,3f));
        // フレーム時間が負、または未指定の場合は移動しない。
        Assert.Equal(Point(0,0,0),CameraLag.InterpolateTo(Point(0,0,0),Point(10,0,0),-1f,3f));
        Assert.Equal(Point(0,0,0),CameraLag.InterpolateTo(Point(0,0,0),Point(10,0,0),float.NaN,3f));
    }
    [Fact]
    public void Pivot_ChasesAStepTargetByTheLagSpeedEveryFrame(){
        var Pivot=Point(0,0,0);
        var Target=Point(0,0,10);
        for(var Frame=0;Frame<60;Frame++){
            Pivot=CameraLag.Pivot(Pivot,Target,Step,3f,Step,0f);
        }
        // 速度3で1/60秒ずつ1秒進むと、距離の(1 - 3/60)^60が残る。
        Assert.Equal(10f*(1f-MathF.Pow(1f-(3f/60f),60f)),ToSingle(Pivot.F),2);
        for(var Frame=0;Frame<600;Frame++){
            Pivot=CameraLag.Pivot(Pivot,Target,Step,3f,Step,0f);
        }
        Assert.Equal(10f,ToSingle(Pivot.F),3);
    }
    [Fact]
    public void Pivot_ALongFrameIsSteppedLikeUnrealsSubsteppingAndNeverOvershoots(){
        // 目標は+F方向へ6 m/sで動く。60 fpsの定速状態では、ラグはおよそv / lagSpeed = 2 m。
        // UEのサブステップ（この処理も同様）では、追従点をラグ中のピボットから目標へ向けて移動するため、長いフレームほど遅れがやや大きくなる。30 fpsでは2.5 m。
        var Fast=Point(0,0,0);
        var Slow=Point(0,0,0);
        var Time=0f;
        for(var Frame=0;Frame<120;Frame++){
            Time+=Step;
            Fast=CameraLag.Pivot(Fast,Point(0,0,6f*Time),Step,3f,Step,0f);
            if(Frame%2==1){
                Slow=CameraLag.Pivot(Slow,Point(0,0,6f*Time),2f*Step,3f,Step,0f);
            }
        }
        Assert.InRange(6f*Time-ToSingle(Fast.F),1.8f,2.05f);
        Assert.InRange(6f*Time-ToSingle(Slow.F),2.2f,2.8f);
        // 1秒のフレームでも目標へ近づくが、到達する手前で止まる。
        var Long=CameraLag.Pivot(Point(0,0,0),Point(0,0,10),1f,3f,Step,0f);
        Assert.InRange(ToSingle(Long.F),5f,10f);
        // ステップ以下の長さのフレームでは、通常の補間となる。
        Assert.Equal(ToSingle(CameraLag.InterpolateTo(Point(0,0,0),Point(0,0,10),Step,3f).F),ToSingle(CameraLag.Pivot(Point(0,0,0),Point(0,0,10),Step,3f,Step,0f).F),5);
    }
    [Fact]
    public void Pivot_NeverFallsFurtherBehindThanTheMaximumDistance(){
        var Pivot=CameraLag.Pivot(Point(0,0,0),Point(0,0,100),Step,0.5f,Step,3f);
        Assert.Equal(97f,ToSingle(Pivot.F),3);
        // ラグがない場合（速度0、または時間経過なし）は目標そのものを使う。
        Assert.Equal(Point(0,0,100),CameraLag.Pivot(Point(0,0,0),Point(0,0,100),Step,0f,Step,3f));
        Assert.Equal(Point(0,0,100),CameraLag.Pivot(Point(0,0,0),Point(0,0,100),0f,3f,Step,0f));
    }
    [Fact]
    public void Rotation_SlerpsTheFractionPerFrameAndSpeedZeroFollowsAtOnce(){
        var From=Quaternion.Identity;
        var To=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2f);
        var Half=CameraLag.Rotation(From,To,0.05f,10f,Step);
        // 0.05秒は1/60秒で3ステップ、各ステップの係数は10/60。角度には(1 - 1/6)^3が残る。
        var Angle=2f*MathF.Acos(Math.Clamp(Quaternion.Dot(Half,To),-1f,1f));
        Assert.Equal((MathF.PI/2f)*MathF.Pow(1f-(10f/60f),3f),Angle,1);
        Assert.Equal(To,CameraLag.Rotation(From,To,Step,0f,Step));
        Assert.Equal(To,CameraLag.Rotation(From,To,0f,10f,Step));
        Assert.Equal(1f,Half.Length(),4);
    }
    private static (OrbitCameraSolver Solver,OrbitCamera Arm)Arm(LogicalVector3 Socket){
        var Arm=new OrbitCamera { Pivot=Point(0,0,0),Distance=LogicalScalar.FromSingle(4f),Collide=false,SocketOffset=Socket };
        return (new OrbitCameraSolver(new DeterministicPhysicsWorld()),Arm);
    }
    [Fact]
    public void TheSocketOffsetIsInTheCamerasOwnFrameAndAddedAfterTheArm(){
        var (Solver,Arm)=CameraLagTests.Arm(Point(0.5f,1f,0f));
        // +F方向を見る（Yaw 0）場合、アームはピボットの4 m後方に終端し、オフセット（R、U、F）をそのまま加える。
        Assert.True(Solver.TrySolveFrom(Arm,Point(0,2,0),LogicalQuaternion.Identity,out var Camera,out var Length));
        Assert.Equal(4f,ToSingle(Length),4);
        Assert.Equal(0.5f,ToSingle(Camera.R),4);
        Assert.Equal(3f,ToSingle(Camera.U),4);
        Assert.Equal(-4f,ToSingle(Camera.F),4);
        // 上軸まわりに4分の1回転すると、視点と一緒にオフセットも回る。カメラの+Rはワールドの-F方向となる。
        var Quarter=PlayerControl.ControlRotation(new ViewAngles { YawDegrees=90f });
        Assert.True(Solver.TrySolveFrom(Arm,Point(0,2,0),Quarter,out var Turned,out _));
        Assert.Equal(-4f,ToSingle(Turned.R),3);
        Assert.Equal(-0.5f,ToSingle(Turned.F),3);
    }
    [Fact]
    public void AnArmWithoutASocketOffsetIsWhatItWasBefore(){
        var (Solver,Arm)=CameraLagTests.Arm(LogicalVector3.Zero);
        Assert.True(Solver.TrySolveFrom(Arm,Point(1,2,3),LogicalQuaternion.Identity,out var Camera,out _));
        Assert.Equal(Point(1,2,-1),Camera);
        Assert.True(Solver.TrySolve(Arm,Point(1,2,3),LogicalQuaternion.Identity,LogicalQuaternion.Identity,out var Same,out _));
        Assert.Equal(Camera,Same);
    }
    [Fact]
    public void TheLagDefaultsAreOffSoExistingArmsAreNotChanged(){
        var Arm=new OrbitCamera();
        Assert.Equal(LogicalScalar.Zero,Arm.Lag);
        Assert.Equal(LogicalScalar.Zero,Arm.RotationLag);
        Assert.Equal(LogicalScalar.Zero,Arm.LagMaxDistance);
        Assert.Equal(LogicalVector3.Zero,Arm.SocketOffset);
        Assert.InRange(ToSingle(Arm.LagMaxTimeStep),0.01666f,0.01667f);
    }
}
