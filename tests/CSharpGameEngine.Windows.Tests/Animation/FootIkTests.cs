using System.Numerics;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Runtime.Animation;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0107：2ボーンIKとマネキンの足IK（CR_Mannequin_FootIK）。テスト用スケルトンは、1 m上の骨盤と左右の脚からなる。
/// 太ももからふくらはぎへは0.5 m下かつ0.05 m前方、ふくらはぎから足へは0.5 m下かつ0.05 m後方、ボールジョイントは足首の0.1 m前方。
/// </summary>
public sealed class FootIkTests {
    private const int Root=0;
    private const int Pelvis=1;
    private const int ThighLeft=2;
    private const int CalfLeft=3;
    private const int FootLeft=4;
    private const int BallLeft=5;
    private const int ThighRight=6;
    private const int CalfRight=7;
    private const int FootRight=8;
    private const int BallRight=9;
    private static Skeleton Legs(float KneeForward=0.05f){
        string[]Names=["root","pelvis","thigh_l","calf_l","foot_l","ball_l","thigh_r","calf_r","foot_r","ball_r"];
        int[]Parents=[-1,0,1,2,3,4,1,6,7,8];
        Vector3[]Translations=[
            Vector3.Zero,new Vector3(0f,1f,0f),
            new Vector3(0.1f,0f,0f),new Vector3(0f,-0.5f,KneeForward),new Vector3(0f,-0.5f,-KneeForward),new Vector3(0f,0f,0.1f),
            new Vector3(-0.1f,0f,0f),new Vector3(0f,-0.5f,KneeForward),new Vector3(0f,-0.5f,-KneeForward),new Vector3(0f,0f,0.1f),
        ];
        return Skeleton.FromImported(new ImportedSkin {
            JointNames=Names,
            ParentIndices=Parents,
            RestTranslations=Translations,
            RestRotations=[.. Enumerable.Repeat(Quaternion.Identity,Names.Length)],
            RestScales=[.. Enumerable.Repeat(Vector3.One,Names.Length)],
            InverseBindMatrices=[.. Enumerable.Repeat(Matrix4x4.Identity,Names.Length)],
            SkeletonTransform=Matrix4x4.Identity,
        });
    }
    private static Matrix4x4[]Model(Skeleton Skeleton,ReadOnlySpan<JointPose>Pose){
        var Result=new Matrix4x4[Skeleton.JointCount];
        Skeleton.ComputeModel(Pose,Result);
        return Result;
    }
    private static Vector3 Position(Matrix4x4[]Model,int Joint)=>Model[Joint].Translation;
    [Fact]
    public void TwoBoneIk_PutsTheEndJointOnAReachableTargetWithoutStretchingOrTurningTheFoot(){
        var Skeleton=Legs();
        var Pose=Skeleton.Rest.ToArray();
        var Before=Model(Skeleton,Pose);
        var UpperLength=Vector3.Distance(Position(Before,ThighLeft),Position(Before,CalfLeft));
        var LowerLength=Vector3.Distance(Position(Before,CalfLeft),Position(Before,FootLeft));
        var Target=new Vector3(0.12f,0.1f,0.15f);
        Assert.True(TwoBoneIk.Solve(Skeleton,Pose,new Matrix4x4[Skeleton.JointCount],ThighLeft,CalfLeft,FootLeft,Target,Vector3.UnitZ));
        var After=Model(Skeleton,Pose);
        Assert.True(Vector3.Distance(Target,Position(After,FootLeft))<1e-4f);
        Assert.Equal(UpperLength,Vector3.Distance(Position(After,ThighLeft),Position(After,CalfLeft)),4);
        Assert.Equal(LowerLength,Vector3.Distance(Position(After,CalfLeft),Position(After,FootLeft)),4);
        // 太ももは元の位置を保ち、足は向きを保つ。ボールジョイントは足に固定して追従する。
        Assert.True(Vector3.Distance(Position(Before,ThighLeft),Position(After,ThighLeft))<1e-6f);
        Assert.True(Vector3.Distance(Vector3.TransformNormal(Vector3.UnitZ,Before[FootLeft]),Vector3.TransformNormal(Vector3.UnitZ,After[FootLeft]))<1e-4f);
        Assert.True(Vector3.Distance(Position(After,BallLeft)-Position(After,FootLeft),Position(Before,BallLeft)-Position(Before,FootLeft))<1e-4f);
        // 反対側の脚は変更しない。
        Assert.True(Vector3.Distance(Position(Before,FootRight),Position(After,FootRight))<1e-6f);
    }
    [Fact]
    public void TwoBoneIk_KeepsTheKneeOnTheSideItWasOn(){
        var Skeleton=Legs();
        var Pose=Skeleton.Rest.ToArray();
        Assert.True(TwoBoneIk.Solve(Skeleton,Pose,new Matrix4x4[Skeleton.JointCount],ThighLeft,CalfLeft,FootLeft,new Vector3(0.1f,0.3f,0.05f),-Vector3.UnitZ));
        Assert.True(Position(Model(Skeleton,Pose),CalfLeft).Z>0.1f);
    }
    [Fact]
    public void TwoBoneIk_AnUnreachableTargetIsApproachedStraightAlongTheLine(){
        var Skeleton=Legs();
        var Pose=Skeleton.Rest.ToArray();
        var Before=Model(Skeleton,Pose);
        var Reach=Vector3.Distance(Position(Before,ThighLeft),Position(Before,CalfLeft))+Vector3.Distance(Position(Before,CalfLeft),Position(Before,FootLeft));
        Assert.True(TwoBoneIk.Solve(Skeleton,Pose,new Matrix4x4[Skeleton.JointCount],ThighLeft,CalfLeft,FootLeft,new Vector3(0.1f,-3f,0f),Vector3.UnitZ));
        var After=Model(Skeleton,Pose);
        var Offset=Position(After,FootLeft)-Position(After,ThighLeft);
        Assert.Equal(Reach,Offset.Length(),3);
        Assert.True(Vector3.Distance(Vector3.Normalize(Offset),-Vector3.UnitY)<1e-3f);
    }
    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void TwoBoneIk_AStraightLimbBendsTowardTheHint(float HintZ){
        var Skeleton=Legs(KneeForward:0f);
        var Pose=Skeleton.Rest.ToArray();
        Assert.True(TwoBoneIk.Solve(Skeleton,Pose,new Matrix4x4[Skeleton.JointCount],ThighLeft,CalfLeft,FootLeft,new Vector3(0.1f,0.2f,0f),new Vector3(0f,0f,HintZ)));
        var After=Model(Skeleton,Pose);
        Assert.True(Vector3.Distance(new Vector3(0.1f,0.2f,0f),Position(After,FootLeft))<1e-3f);
        Assert.True(Position(After,CalfLeft).Z*HintZ>0.1f);
    }
    [Fact]
    public void TwoBoneIk_DegenerateInputLeavesThePoseAlone(){
        var Skeleton=Legs();
        var Pose=Skeleton.Rest.ToArray();
        var Scratch=new Matrix4x4[Skeleton.JointCount];
        var Thigh=Position(Model(Skeleton,Pose),ThighLeft);
        Assert.False(TwoBoneIk.Solve(Skeleton,Pose,Scratch,ThighLeft,CalfLeft,FootLeft,Thigh,Vector3.UnitZ));
        Assert.False(TwoBoneIk.Solve(Skeleton,Pose,Scratch,ThighLeft,CalfLeft,FootLeft,new Vector3(float.NaN,0f,0f),Vector3.UnitZ));
        Assert.Equal(Skeleton.Rest.ToArray().Select(Joint=>Joint.Rotation),Pose.Select(Joint=>Joint.Rotation));
        Assert.Throws<ArgumentException>(()=>TwoBoneIk.Solve(Skeleton,Pose,Scratch,ThighLeft,FootLeft,CalfLeft,Vector3.Zero,Vector3.UnitZ));
        Assert.Throws<ArgumentException>(()=>TwoBoneIk.Solve(Skeleton,Pose,new Matrix4x4[2],ThighLeft,CalfLeft,FootLeft,Vector3.Zero,Vector3.UnitZ));
    }
    [Fact]
    public void FootIkPose_SmoothsTheOffsetsAndTheLowerFootDrivesThePelvis(){
        var Skeleton=Legs();
        var Ik=new FootIkPose(Skeleton,new FootIk());
        Ik.Update(0.05f,true,0f,0f);
        Assert.True(Ik.IsNeutral);
        // 速度10で0.05秒進むと、残り距離の半分に到達する。
        Ik.Update(0.05f,true,-0.1f,0.04f);
        Assert.Equal(-0.1f,Ik.TargetOffset(FootIkPose.Left),5);
        Assert.Equal(-0.05f,Ik.FootOffset(FootIkPose.Left),5);
        Assert.Equal(0.02f,Ik.FootOffset(FootIkPose.Right),5);
        Assert.Equal(-0.05f,Ik.PelvisOffset,5);
        Assert.False(Ik.IsNeutral);
        // レイが何も検出しない場合、または値が1でない場合、その足には補正を行わない。
        Ik.Update(0.05f,true,null,float.NaN);
        Assert.Equal(0f,Ik.TargetOffset(FootIkPose.Left));
        Assert.Equal(0f,Ik.TargetOffset(FootIkPose.Right));
        Assert.Equal(-0.025f,Ik.FootOffset(FootIkPose.Left),5);
    }
    [Fact]
    public void FootIkPose_WhileFallingBothTargetsReturnToZero(){
        var Skeleton=Legs();
        var Ik=new FootIkPose(Skeleton,new FootIk { InterpolationSpeed=10f });
        Ik.Update(0.05f,true,-0.1f,-0.1f);
        Assert.Equal(-0.1f,Ik.FootOffset(FootIkPose.Left),5);
        Ik.Update(0.05f,false,-0.1f,-0.1f);
        Assert.Equal(0f,Ik.TargetOffset(FootIkPose.Left));
        Assert.Equal(-0.05f,Ik.FootOffset(FootIkPose.Left),5);
        for(var Step=0;Step<200;Step++){
            Ik.Update(0.05f,false,-0.1f,-0.1f);
        }
        Assert.True(Ik.IsNeutral||MathF.Abs(Ik.FootOffset(FootIkPose.Left))<1e-4f);
    }
    [Fact]
    public void FootIkPose_ApplyLowersThePelvisAndPlacesEachFootOnItsOwnGround(){
        var Skeleton=Legs();
        var Pose=Skeleton.Rest.ToArray();
        var Before=Model(Skeleton,Pose);
        var Ik=new FootIkPose(Skeleton,new FootIk());
        // 左足の地面はメッシュ原点より10 cm低く、右足の地面は原点と同じ高さ。
        Ik.Update(0.05f,true,-0.1f,0f);
        Ik.Apply(Pose,new Matrix4x4[Skeleton.JointCount]);
        var After=Model(Skeleton,Pose);
        Assert.Equal(0.9f,Position(After,Pelvis).Y,4);
        Assert.Equal(-0.1f,Position(After,FootLeft).Y,3);
        Assert.Equal(0f,Position(After,FootRight).Y,3);
        Assert.Equal(Position(Before,FootLeft).X,Position(After,FootLeft).X,3);
        Assert.Equal(Position(Before,FootLeft).Z,Position(After,FootLeft).Z,3);
        Assert.Equal(Position(Before,FootRight).Z,Position(After,FootRight).Z,3);
        foreach(var (Thigh,Calf,Foot) in new[] { (ThighLeft,CalfLeft,FootLeft),(ThighRight,CalfRight,FootRight) }){
            Assert.Equal(Vector3.Distance(Position(Before,Thigh),Position(Before,Calf)),Vector3.Distance(Position(After,Thigh),Position(After,Calf)),4);
            Assert.Equal(Vector3.Distance(Position(Before,Calf),Position(Before,Foot)),Vector3.Distance(Position(After,Calf),Position(After,Foot)),4);
        }
    }
    [Fact]
    public void FootIkPose_AStepUpLiftsTheFootAndLeavesThePelvisWhereTheOtherFootStands(){
        var Skeleton=Legs();
        var Pose=Skeleton.Rest.ToArray();
        var Ik=new FootIkPose(Skeleton,new FootIk());
        Ik.Update(0.05f,true,0.2f,0f);
        Assert.Equal(0f,Ik.PelvisOffset);
        Ik.Apply(Pose,new Matrix4x4[Skeleton.JointCount]);
        var After=Model(Skeleton,Pose);
        Assert.Equal(1f,Position(After,Pelvis).Y,4);
        Assert.Equal(0.2f,Position(After,FootLeft).Y,3);
        Assert.True(Position(After,CalfLeft).Z>0.05f);
    }
    [Fact]
    public void FootIkPose_NeutralOffsetsLeaveTheAnimatedPoseExactlyAsItWas(){
        var Skeleton=Legs();
        var Pose=Skeleton.Rest.ToArray();
        var Ik=new FootIkPose(Skeleton,new FootIk());
        Ik.Update(0.05f,true,0f,0f);
        Ik.Apply(Pose,new Matrix4x4[Skeleton.JointCount]);
        for(var Joint=0;Joint<Skeleton.JointCount;Joint++){
            Assert.Equal(Skeleton.Rest[Joint].Translation,Pose[Joint].Translation);
            Assert.Equal(Skeleton.Rest[Joint].Rotation,Pose[Joint].Rotation);
        }
    }
    [Fact]
    public void FootIkPose_TheRayStartsAboveTheAnkleAndAheadOfItTowardTheToes(){
        var Skeleton=Legs();
        var Pose=Skeleton.Rest.ToArray();
        var Ik=new FootIkPose(Skeleton,new FootIk { TraceTop=0.3f,TraceForward=0.05f });
        var Origin=Ik.TraceOrigin(Model(Skeleton,Pose),FootIkPose.Left);
        Assert.Equal(0.1f,Origin.X,4);
        Assert.Equal(0.3f,Origin.Y,4);
        Assert.Equal(0.05f,Origin.Z,4);
        var Right=Ik.TraceOrigin(Model(Skeleton,Pose),FootIkPose.Right);
        Assert.Equal(-0.1f,Right.X,4);
        var NoBall=new FootIkPose(Skeleton,new FootIk { LeftBall="",RightBall="" }).TraceOrigin(Model(Skeleton,Pose),FootIkPose.Left);
        Assert.Equal(0f,NoBall.Z,4);
    }
    [Fact]
    public void FootIkPose_RefusesASkeletonWithoutTheNamedJointsOrNotInAChain(){
        var Skeleton=Legs();
        var Missing=Assert.Throws<ArgumentException>(()=>new FootIkPose(Skeleton,new FootIk { Pelvis="hips" }));
        Assert.Contains("hips",Missing.Message);
        Assert.Throws<ArgumentException>(()=>new FootIkPose(Skeleton,new FootIk { LeftCalf="calf_r" }));
        Assert.Throws<ArgumentNullException>(()=>new FootIkPose(null!,new FootIk()));
    }
}
