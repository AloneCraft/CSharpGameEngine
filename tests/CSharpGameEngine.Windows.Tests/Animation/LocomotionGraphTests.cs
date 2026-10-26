using System.Numerics;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Runtime.Animation;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0107：ABP_Unarmedのグラフ。ブレンド空間BS_Idle_Walk_Run（3 m/sと6 m/sの8方向、速度0のIdle。UEと同様に三角形分割）、
/// Locomotion内にネストしたIdle／Move状態機械、および閾値とクロスフェード時間を持つJump、Fall Loop、Land状態からなる。
/// 各クリップは子ジョイントを異なるx位置に保つため、姿勢からどのクリップがどの割合でブレンドされたか正確に分かる。
/// </summary>
public sealed class LocomotionGraphTests {
    private const float IdleMarker=0f;
    private const float WalkMarker=100f;
    private const float JogMarker=200f;
    private const float JumpMarker=1000f;
    private const float FallMarker=2000f;
    private const float LandMarker=3000f;
    private static Skeleton TwoJoints(){
        var ChildRest=Matrix4x4.CreateTranslation(0f,1f,0f);
        Assert.True(Matrix4x4.Invert(ChildRest,out var ChildInverse));
        return Skeleton.FromImported(new ImportedSkin {
            JointNames=["root","child"],
            ParentIndices=[-1,0],
            RestTranslations=[Vector3.Zero,Vector3.UnitY],
            RestRotations=[Quaternion.Identity,Quaternion.Identity],
            RestScales=[Vector3.One,Vector3.One],
            InverseBindMatrices=[Matrix4x4.Identity,ChildInverse],
            SkeletonTransform=Matrix4x4.Identity,
        });
    }
    private static AnimationClip Marker(Skeleton Skeleton,float Marker,float Duration)=>AnimationClip.Bind(new ImportedAnimationClip {
        Name=$"marker{Marker}",
        DurationSeconds=Duration,
        Tracks=[new ImportedJointTrack { JointName="child",Translation=new ImportedKeys<Vector3> { Times=[0f,Duration],Values=[new Vector3(Marker,1f,0f),new Vector3(Marker,1f,0f)],Step=false } }],
    },Skeleton);
    private static LocomotionStateMachine Machine(Skeleton Skeleton,float Interpolation=LocomotionStateMachine.WeightInterpolationSpeed){
        var Walk=Enumerable.Range(0,LocomotionDirection.Count).Select(Direction=>Marker(Skeleton,WalkMarker+Direction,1f)).ToArray();
        var Jog=Enumerable.Range(0,LocomotionDirection.Count).Select(Direction=>Marker(Skeleton,JogMarker+Direction,1f)).ToArray();
        return new LocomotionStateMachine(Skeleton,new LocomotionClips(Marker(Skeleton,IdleMarker,2f),Walk,Jog,Marker(Skeleton,JumpMarker,0.5f),Marker(Skeleton,FallMarker,1f),Marker(Skeleton,LandMarker,0.5f)),3f,6f,Interpolation);
    }
    private static float ChildX(ReadOnlySpan<JointPose>Pose)=>Pose[1].Translation.X;
    private static LocomotionInput Moving(float Speed,float Direction=0f)=>new(Speed,Direction,true,false,0f);
    private static readonly LocomotionInput Standing=new(0f,0f,false,false,0f);
    private static readonly LocomotionInput Rising=new(0f,0f,false,true,5f);
    private static readonly LocomotionInput Dropping=new(0f,0f,false,true,-3f);
    /// <summary><paramref name="Step"/>ごとに進め、<paramref name="Seconds"/>ほどグラフを実行する。</summary>
    private static void Run(LocomotionStateMachine Machine,Span<JointPose>Pose,float Seconds,in LocomotionInput Input,float Step=0.02f){
        for(var Elapsed=0f;Elapsed<Seconds-1e-4f;Elapsed+=Step){
            Machine.Update(Step,Input,Pose);
        }
    }
    [Theory]
    [InlineData(0f,3f,WalkMarker+LocomotionDirection.Forward)]
    [InlineData(45f,3f,WalkMarker+LocomotionDirection.ForwardRight)]
    [InlineData(90f,3f,WalkMarker+LocomotionDirection.Right)]
    [InlineData(135f,3f,WalkMarker+LocomotionDirection.BackwardRight)]
    [InlineData(180f,3f,WalkMarker+LocomotionDirection.Backward)]
    [InlineData(-180f,3f,WalkMarker+LocomotionDirection.Backward)]
    [InlineData(-135f,3f,WalkMarker+LocomotionDirection.BackwardLeft)]
    [InlineData(-90f,3f,WalkMarker+LocomotionDirection.Left)]
    [InlineData(-45f,3f,WalkMarker+LocomotionDirection.ForwardLeft)]
    [InlineData(0f,6f,JogMarker+LocomotionDirection.Forward)]
    [InlineData(90f,6f,JogMarker+LocomotionDirection.Right)]
    [InlineData(-90f,6f,JogMarker+LocomotionDirection.Left)]
    [InlineData(180f,6f,JogMarker+LocomotionDirection.Backward)]
    [InlineData(90f,0f,IdleMarker)]
    [InlineData(0f,12f,JogMarker+LocomotionDirection.Forward)]
    public void BlendSpace_PlaysTheOneClipAtEachGridPoint(float Direction,float Speed,float Expected){
        var Machine=LocomotionGraphTests.Machine(TwoJoints());
        var Pose=new JointPose[2];
        Run(Machine,Pose,1.5f,Moving(Speed,Direction));
        Assert.Equal(LocomotionMoveState.Move,Machine.MoveState);
        Assert.Equal(Expected,ChildX(Pose),2);
    }
    [Theory]
    // 左半分のセルは左下から右上へ切る。この点は対角線上にあり、前方左歩行と前方ジョギングの間にある。
    [InlineData(-22.5f,4.5f,0.5f*(WalkMarker+LocomotionDirection.ForwardLeft)+0.5f*(JogMarker+LocomotionDirection.Forward))]
    // 右半分は鏡像。対角線は右下から左上へ走るため、同じ点は前方右歩行と前方ジョギングをブレンドする。
    [InlineData(22.5f,4.5f,0.5f*(WalkMarker+LocomotionDirection.ForwardRight)+0.5f*(JogMarker+LocomotionDirection.Forward))]
    // 上段の2方向の中間点では、ジョギング用の2クリップだけを使う。
    [InlineData(22.5f,6f,0.5f*(JogMarker+LocomotionDirection.Forward)+0.5f*(JogMarker+LocomotionDirection.ForwardRight))]
    // Idleと前方歩行の中間点：Idleと歩行クリップ。
    [InlineData(0f,1.5f,0.5f*IdleMarker+0.5f*(WalkMarker+LocomotionDirection.Forward))]
    // 下側三角形の対角線上で4分の1進んだ位置。3つの頂点で重みを分け合う。
    [InlineData(-33.75f,3.75f,0.75f*(WalkMarker+LocomotionDirection.ForwardLeft)+0.25f*(JogMarker+LocomotionDirection.Forward))]
    public void BlendSpace_MixesTheTriangleThatHoldsTheInput(float Direction,float Speed,float Expected){
        var Machine=LocomotionGraphTests.Machine(TwoJoints());
        var Pose=new JointPose[2];
        Run(Machine,Pose,1.5f,Moving(Speed,Direction));
        Assert.Equal(Expected,ChildX(Pose),2);
    }
    [Fact]
    public void BlendSpace_WeightsSumToOneEverywhereAndAreNeverNegative(){
        var Space=new BlendSpace2D(Enumerable.Range(0,9).Select(Column=>-180f+(45f*Column)).ToArray(),[0f,3f,6f]);
        var Weights=new float[Space.SampleCount];
        for(var Direction=-200f;Direction<=200f;Direction+=7.3f){
            for(var Speed=-1f;Speed<=8f;Speed+=0.37f){
                Space.TargetWeights(Direction,Speed,Weights);
                Assert.Equal(1f,Weights.Sum(),4);
                Assert.All(Weights,Weight=>Assert.InRange(Weight,0f,1f));
                Assert.InRange(Weights.Count(Weight=>Weight>0f),1,3);
            }
        }
    }
    [Fact]
    public void BlendSpace_NonFiniteInputsFallBackToTheOrigin(){
        var Space=new BlendSpace2D([-180f,0f,180f],[0f,3f]);
        var Weights=new float[Space.SampleCount];
        Space.TargetWeights(float.NaN,float.PositiveInfinity,Weights);
        Assert.Equal(1f,Weights[Space.SampleIndex(0,0)],4);
    }
    [Fact]
    public void BlendSpace_SmoothsTowardTheTargetAtTheInterpolationSpeed(){
        var Space=new BlendSpace2D([-180f,0f,180f],[0f,3f,6f],InterpolationSpeed:5f);
        var Far=Space.SampleIndex(2,2);
        var Near=Space.SampleIndex(1,2);
        Space.Update(0.1f,0f,6f);
        // 最初のクエリでは目標へスナップする。その後は速度5で0.1秒ごとに残り距離の半分を進む。
        Assert.Equal(1f,Space.Weights[Near],4);
        Space.Update(0.1f,180f,6f);
        Assert.Equal(0.5f,Space.Weights[Near],3);
        Assert.Equal(0.5f,Space.Weights[Far],3);
        Space.Update(0.1f,180f,6f);
        Assert.Equal(0.25f,Space.Weights[Near],3);
        Assert.Equal(1f,Space.Weights.ToArray().Sum(),4);
        for(var Step=0;Step<80;Step++){
            Space.Update(0.1f,180f,6f);
        }
        Assert.Equal(1f,Space.Weights[Far],4);
        Space.Update(0f,0f,6f);
        Assert.Equal(1f,Space.Weights[Far],4);
        Space.Reset();
        Space.Update(0.1f,0f,6f);
        Assert.Equal(1f,Space.Weights[Near],4);
    }
    [Fact]
    public void BlendSpace_ASuddenTurnFadesBetweenTheClipsInsteadOfSnapping(){
        var Machine=LocomotionGraphTests.Machine(TwoJoints());
        var Pose=new JointPose[2];
        Run(Machine,Pose,1.5f,Moving(6f,0f));
        Assert.Equal(JogMarker+LocomotionDirection.Forward,ChildX(Pose),2);
        // 速度5で0.1秒進めると、新しい方向までの距離の半分を移動する。
        Machine.Update(0.1f,Moving(6f,90f),Pose);
        Assert.Equal(0.5f*(JogMarker+LocomotionDirection.Forward)+0.5f*(JogMarker+LocomotionDirection.Right),ChildX(Pose),2);
        Assert.Equal(1f,Machine.BlendWeights.ToArray().Sum(),4);
    }
    [Theory]
    [InlineData(-180f,LocomotionDirection.Backward)]
    [InlineData(-135f,LocomotionDirection.BackwardLeft)]
    [InlineData(-90f,LocomotionDirection.Left)]
    [InlineData(-45f,LocomotionDirection.ForwardLeft)]
    [InlineData(0f,LocomotionDirection.Forward)]
    [InlineData(45f,LocomotionDirection.ForwardRight)]
    [InlineData(90f,LocomotionDirection.Right)]
    [InlineData(135f,LocomotionDirection.BackwardRight)]
    [InlineData(180f,LocomotionDirection.Backward)]
    public void Direction_OfAngle_NamesTheGridColumn(float Degrees,int Expected){
        Assert.Equal(Expected,LocomotionDirection.OfAngle(Degrees));
    }
    [Fact]
    public void Locomotion_UnnamedDirectionsUseTheForwardClip(){
        var Animator=new Locomotion { Walk="walk.glb",WalkRight="walk_right.glb",Jog="jog.glb" };
        Assert.Equal("walk.glb",Animator.WalkClip(LocomotionDirection.Forward));
        Assert.Equal("walk_right.glb",Animator.WalkClip(LocomotionDirection.Right));
        Assert.Equal("walk.glb",Animator.WalkClip(LocomotionDirection.BackwardLeft));
        Assert.Equal("jog.glb",Animator.JogClip(LocomotionDirection.Left));
        Assert.Equal(string.Empty,new Locomotion().JogClip(LocomotionDirection.Right));
    }
    [Fact]
    public void LocomotionMoveStates_FollowShouldMoveNotTheSpeed(){
        var Machine=LocomotionGraphTests.Machine(TwoJoints());
        var Pose=new JointPose[2];
        Run(Machine,Pose,1.5f,Moving(6f));
        Assert.Equal(JogMarker,ChildX(Pose),2);
        // 高速で移動中でも、押されていなければ（惰性移動または壁を押している状態）0.3秒のCubicフェードでIdleへ移る。0.15秒時点で半分まで進む。
        Machine.Update(0.15f,new LocomotionInput(6f,0f,false,false,0f),Pose);
        Assert.Equal(LocomotionMoveState.Idle,Machine.MoveState);
        Assert.Equal(0.5f*JogMarker,ChildX(Pose),2);
        Run(Machine,Pose,0.5f,new LocomotionInput(6f,0f,false,false,0f));
        Assert.Equal(IdleMarker,ChildX(Pose),2);
        // 押されているがまだ移動していない状態。速度0のMove姿勢はIdleと同じなので、見た目は変わらない。
        Machine.Update(0.02f,new LocomotionInput(0f,0f,true,false,0f),Pose);
        Assert.Equal(LocomotionMoveState.Move,Machine.MoveState);
        Run(Machine,Pose,0.5f,new LocomotionInput(0f,0f,true,false,0f));
        Assert.Equal(IdleMarker,ChildX(Pose),2);
    }
    [Fact]
    public void IdleToMove_UsesTheTwoTenthsSecondCubicCrossFade(){
        var Machine=LocomotionGraphTests.Machine(TwoJoints());
        var Pose=new JointPose[2];
        Run(Machine,Pose,0.5f,Standing);
        Machine.Update(0.05f,Moving(3f),Pose);
        var Weight=PoseFade.Cubic(0.05f/LocomotionStateMachine.IdleToMoveBlendSeconds);
        Assert.Equal(Weight*WalkMarker,ChildX(Pose),2);
        Assert.InRange(Weight,0.15f,0.16f);
    }
    [Theory]
    [InlineData(0.99f,LocomotionState.Fall)]
    [InlineData(1.0f,LocomotionState.Fall)]
    [InlineData(1.01f,LocomotionState.Jump)]
    [InlineData(5f,LocomotionState.Jump)]
    [InlineData(-3f,LocomotionState.Fall)]
    public void LeavingTheGround_StartsJumpOnlyAboveOneMeterPerSecondUp(float VerticalSpeed,LocomotionState Expected){
        var Machine=LocomotionGraphTests.Machine(TwoJoints());
        var Pose=new JointPose[2];
        Run(Machine,Pose,0.3f,Standing);
        Machine.Update(0.02f,new LocomotionInput(0f,0f,false,true,VerticalSpeed),Pose);
        Assert.Equal(Expected,Machine.State);
    }
    [Fact]
    public void Jump_CrossFadesInOverTheirTimesAndFallsWhenThreeTenthsOfASecondRemain(){
        var Machine=LocomotionGraphTests.Machine(TwoJoints());
        var Pose=new JointPose[2];
        Run(Machine,Pose,0.5f,Standing);
        Machine.Update(0.05f,Rising,Pose);
        Assert.Equal(LocomotionState.Jump,Machine.State);
        // 状態へ入った時点で、Idleからの0.15秒のCubicフェードが3分の1まで進んでいる。
        Assert.Equal(PoseFade.Cubic(0.05f/LocomotionStateMachine.ToJumpBlendSeconds)*JumpMarker,ChildX(Pose),2);
        // Jumpクリップは0.5秒で、0.08秒から再生する。状態に0.12秒いると残り0.3秒となる。
        Machine.Update(0.05f,Rising,Pose);
        Assert.Equal(LocomotionState.Jump,Machine.State);
        Machine.Update(0.05f,Rising,Pose);
        Assert.Equal(LocomotionState.Jump,Machine.State);
        Machine.Update(0.05f,Rising,Pose);
        Assert.Equal(LocomotionState.Fall,Machine.State);
        Run(Machine,Pose,0.5f,Dropping);
        Assert.Equal(LocomotionState.Fall,Machine.State);
        Assert.Equal(FallMarker,ChildX(Pose),2);
    }
    [Fact]
    public void FallLoop_UsesTheFourTenthsSecondFade(){
        var Machine=LocomotionGraphTests.Machine(TwoJoints());
        var Pose=new JointPose[2];
        Run(Machine,Pose,0.5f,Standing);
        Machine.Update(0.1f,Dropping,Pose);
        Assert.Equal(LocomotionState.Fall,Machine.State);
        Assert.Equal(PoseFade.Cubic(0.1f/LocomotionStateMachine.ToFallBlendSeconds)*FallMarker,ChildX(Pose),2);
    }
    [Fact]
    public void Landing_GoesToLandThenBackToLocomotionWhenFourTenthsRemainOrTheCharacterShouldMove(){
        var Machine=LocomotionGraphTests.Machine(TwoJoints());
        var Pose=new JointPose[2];
        Run(Machine,Pose,0.3f,Dropping);
        Assert.Equal(LocomotionState.Fall,Machine.State);
        Machine.Update(0.02f,Standing,Pose);
        Assert.Equal(LocomotionState.Land,Machine.State);
        Run(Machine,Pose,0.08f,Standing,Step:0.04f);
        Assert.Equal(LocomotionState.Land,Machine.State);
        // 0.5秒のLandクリップは、状態に0.1秒いると残り0.4秒。3回目の0.04秒ステップでしきい値を越える。
        Machine.Update(0.04f,Standing,Pose);
        Assert.Equal(LocomotionState.Locomotion,Machine.State);
        var Again=LocomotionGraphTests.Machine(TwoJoints());
        Run(Again,Pose,0.3f,Dropping);
        Run(Again,Pose,0.04f,Standing);
        Assert.Equal(LocomotionState.Land,Again.State);
        // 着地中に押されると、0.5秒のフェードで直ちにLand状態を離れる。
        Again.Update(0.02f,Moving(3f),Pose);
        Assert.Equal(LocomotionState.Locomotion,Again.State);
        Assert.InRange(ChildX(Pose),1f,LandMarker-1f);
    }
    [Fact]
    public void Land_ToFallingAgainStartsJumpOrFallFromThere(){
        var Machine=LocomotionGraphTests.Machine(TwoJoints());
        var Pose=new JointPose[2];
        Run(Machine,Pose,0.3f,Dropping);
        Machine.Update(0.02f,Standing,Pose);
        Assert.Equal(LocomotionState.Land,Machine.State);
        Machine.Update(0.02f,Rising,Pose);
        Assert.Equal(LocomotionState.Jump,Machine.State);
    }
    [Fact]
    public void AnInterruptedFadeContinuesFromWhatWasOnScreen(){
        var Machine=LocomotionGraphTests.Machine(TwoJoints());
        var Pose=new JointPose[2];
        Run(Machine,Pose,0.5f,Standing);
        Machine.Update(0.05f,Rising,Pose);
        var Before=ChildX(Pose);
        Assert.InRange(Before,200f,300f);
        // すぐに着地した場合、LandへのフェードはIdleやJumpからではなく、半分まで進んだJump姿勢から開始する。
        Machine.Update(0.02f,Standing,Pose);
        Assert.Equal(LocomotionState.Land,Machine.State);
        var Weight=PoseFade.Cubic(0.02f/LocomotionStateMachine.ToLandBlendSeconds);
        Assert.Equal(((1f-Weight)*Before)+(Weight*LandMarker),ChildX(Pose),1);
    }
    [Fact]
    public void ABadFrameStepNeverSkipsTheFadeOrProducesNaN(){
        var Machine=LocomotionGraphTests.Machine(TwoJoints());
        var Pose=new JointPose[2];
        Machine.Update(float.NaN,Moving(float.NaN,float.PositiveInfinity),Pose);
        Machine.Update(-1f,Moving(-5f,float.NaN),Pose);
        Assert.True(float.IsFinite(ChildX(Pose)));
        Machine.Update(100f,Moving(3f,0f),Pose);
        Assert.Equal(WalkMarker,ChildX(Pose),2);
    }
    [Fact]
    public void TheMachineRefusesClipSetsAndSpeedsItCannotUse(){
        var Skeleton=TwoJoints();
        var Clip=Marker(Skeleton,0f,1f);
        Assert.Throws<ArgumentException>(()=>new LocomotionStateMachine(Skeleton,new LocomotionClips(Clip,[Clip],[Clip],Clip,Clip,Clip),3f,6f));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new LocomotionStateMachine(Skeleton,LocomotionClips.ForwardOnly(Clip,Clip,Clip,Clip,Clip,Clip),6f,3f));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new LocomotionStateMachine(Skeleton,LocomotionClips.ForwardOnly(Clip,Clip,Clip,Clip,Clip,Clip),0f,3f));
        Assert.Throws<ArgumentException>(()=>new LocomotionStateMachine(Skeleton,LocomotionClips.ForwardOnly(Clip,Clip,Clip,Clip,Clip,Clip),3f,6f).Update(0.1f,Standing,new JointPose[1]));
    }
    [Fact]
    public void ForwardOnlyClips_IgnoreTheDirection(){
        var Skeleton=TwoJoints();
        var Machine=new LocomotionStateMachine(Skeleton,LocomotionClips.ForwardOnly(Marker(Skeleton,0f,1f),Marker(Skeleton,1f,1f),Marker(Skeleton,2f,1f),Marker(Skeleton,10f,1f),Marker(Skeleton,20f,1f),Marker(Skeleton,30f,1f)),3f,6f);
        var Pose=new JointPose[2];
        Run(Machine,Pose,1.5f,Moving(4.5f,135f));
        Assert.Equal(1.5f,ChildX(Pose),3);
    }
    [Fact]
    public void TheBlendSpacePhaseRunsAtTheWeightedLengthOfItsClips(){
        // 実際のマネキンでは後退歩行が2.13秒、前進歩行が1.5秒。ブレンド中の動きに合わせ、共有正規化位相の進み方も変える必要がある。
        var Skeleton=TwoJoints();
        var Walk=Enumerable.Range(0,LocomotionDirection.Count).Select(Direction=>Marker(Skeleton,WalkMarker+Direction,Direction==LocomotionDirection.Backward?2f:1f)).ToArray();
        var Jog=Enumerable.Range(0,LocomotionDirection.Count).Select(Direction=>Marker(Skeleton,JogMarker+Direction,1f)).ToArray();
        var Machine=new LocomotionStateMachine(Skeleton,new LocomotionClips(Marker(Skeleton,IdleMarker,2f),Walk,Jog,Marker(Skeleton,JumpMarker,0.5f),Marker(Skeleton,FallMarker,1f),Marker(Skeleton,LandMarker,0.5f)),3f,6f);
        var Pose=new JointPose[2];
        Machine.Update(0.01f,Moving(3f,0f),Pose);
        var Start=Machine.BlendPhase;
        Run(Machine,Pose,0.5f,Moving(3f,0f),Step:0.01f);
        Assert.Equal(0.5f,(Machine.BlendPhase-Start+1f)%1f,2);
        var Backward=LocomotionGraphTests.Machine(Skeleton);
        Backward.Update(0.01f,Moving(3f,180f),Pose);
        Start=Backward.BlendPhase;
        Run(Backward,Pose,0.5f,Moving(3f,180f),Step:0.01f);
        Assert.Equal(0.5f,(Backward.BlendPhase-Start+1f)%1f,2);
        // 後退クリップを2秒にすると、同じ0.5秒が位相の4分の1に当たる。
        var Slow=new LocomotionStateMachine(Skeleton,new LocomotionClips(Marker(Skeleton,IdleMarker,2f),Walk,Jog,Marker(Skeleton,JumpMarker,0.5f),Marker(Skeleton,FallMarker,1f),Marker(Skeleton,LandMarker,0.5f)),3f,6f);
        Slow.Update(0.01f,Moving(3f,180f),Pose);
        Start=Slow.BlendPhase;
        Run(Slow,Pose,0.5f,Moving(3f,180f),Step:0.01f);
        Assert.Equal(0.25f,(Slow.BlendPhase-Start+1f)%1f,2);
    }
    [Fact]
    public void PoseMixer_AveragesRotationsAlongTheShorterArcAndIgnoresZeroWeights(){
        var Mixer=new PoseMixer(1);
        var Turn=Quaternion.CreateFromAxisAngle(Vector3.UnitY,0.4f);
        var A=new[] { new JointPose { Translation=new Vector3(2f,0f,0f),Rotation=Turn,Scale=Vector3.One } };
        // 同じ回転を符号反転して表しても向きは同じ。平均時に打ち消し合ってはならない。
        var B=new[] { new JointPose { Translation=new Vector3(4f,0f,0f),Rotation=Quaternion.Negate(Turn),Scale=new Vector3(3f) } };
        Mixer.Begin();
        Mixer.Add(A,1f);
        Mixer.Add(B,1f);
        Mixer.Add(B,0f);
        Mixer.Add(B,float.NaN);
        var Pose=new JointPose[1];
        Mixer.Resolve(Pose);
        Assert.Equal(2f,Mixer.TotalWeight);
        Assert.Equal(3f,Pose[0].Translation.X,4);
        Assert.Equal(2f,Pose[0].Scale.X,4);
        Assert.InRange(MathF.Abs(Quaternion.Dot(Pose[0].Rotation,Turn)),0.99999f,1.00001f);
        Mixer.Begin();
        Mixer.Resolve(Pose);
        Assert.Equal(Quaternion.Identity,Pose[0].Rotation);
        Assert.Equal(Vector3.One,Pose[0].Scale);
    }
    [Fact]
    public void PoseFade_FollowsTheCubicCurveAndCanBeRestartedFromTheLastPose(){
        var Rest=new[] { new JointPose { Rotation=Quaternion.Identity,Scale=Vector3.One } };
        var Fade=new PoseFade(Rest);
        var Target=new[] { new JointPose { Translation=new Vector3(10f,0f,0f),Rotation=Quaternion.Identity,Scale=Vector3.One } };
        var Output=new JointPose[1];
        Fade.Apply(0.1f,Target,Output);
        Assert.Equal(10f,Output[0].Translation.X);
        Fade.Start(0.3f);
        Fade.Apply(0.15f,Rest,Output);
        Assert.Equal(5f,Output[0].Translation.X,3);
        Assert.Equal(0.5f,Fade.Weight,4);
        // フェードの途中で再開する場合も、新しいフェードは画面に表示されていた姿勢（5）から開始する。
        Fade.Start(0.3f);
        Fade.Apply(0.0f,Target,Output);
        Assert.Equal(5f,Output[0].Translation.X,3);
        Fade.Apply(10f,Target,Output);
        Assert.Equal(10f,Output[0].Translation.X,3);
        Assert.False(Fade.Active);
        Fade.Start(0f);
        Fade.Apply(0.1f,Rest,Output);
        Assert.Equal(0f,Output[0].Translation.X);
        Assert.Equal(0f,PoseFade.Cubic(-1f));
        Assert.Equal(1f,PoseFade.Cubic(7f));
        Assert.Equal(0f,PoseFade.Cubic(float.NaN));
    }
}
