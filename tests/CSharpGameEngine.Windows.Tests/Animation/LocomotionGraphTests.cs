using System.Numerics;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Runtime.Animation;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0107: ABP_Unarmed's graph. The blend space BS_Idle_Walk_Run (eight directions at 3 and 6 m/s, Idle at 0, triangulated like UE),
/// the Idle / Move state machine nested in Locomotion, and the Jump, Fall Loop and Land states with their thresholds and cross-fade times.
/// Every clip holds the child joint at a distinct x, so the pose says exactly which clips are mixed and in what proportion.
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
    /// <summary>Runs the graph for about <paramref name="Seconds"/> in steps of <paramref name="Step"/>.</summary>
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
    // Left half cells are cut lower-left to upper-right: this point is on that diagonal, between the walk-forward-left and jog-forward clips.
    [InlineData(-22.5f,4.5f,0.5f*(WalkMarker+LocomotionDirection.ForwardLeft)+0.5f*(JogMarker+LocomotionDirection.Forward))]
    // The right half is the mirror: the diagonal runs lower-right to upper-left, so the same point mixes walk-forward-right and jog-forward.
    [InlineData(22.5f,4.5f,0.5f*(WalkMarker+LocomotionDirection.ForwardRight)+0.5f*(JogMarker+LocomotionDirection.Forward))]
    // On the top row halfway between two directions it is simply the two jog clips.
    [InlineData(22.5f,6f,0.5f*(JogMarker+LocomotionDirection.Forward)+0.5f*(JogMarker+LocomotionDirection.ForwardRight))]
    // Halfway from Idle to walking forward: Idle and the walk clip.
    [InlineData(0f,1.5f,0.5f*IdleMarker+0.5f*(WalkMarker+LocomotionDirection.Forward))]
    // A quarter of the way along the diagonal's lower triangle: three corners share the weight.
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
        // The first query snaps; from then on half the distance is covered per 0.1 s at speed 5.
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
        // One 0.1 s step at speed 5 covers half of the way to the new direction.
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
        // Still running fast but no longer pushed (coasting or pressing a wall): Idle takes over with the 0.3 s cubic fade, which is half done at 0.15 s.
        Machine.Update(0.15f,new LocomotionInput(6f,0f,false,false,0f),Pose);
        Assert.Equal(LocomotionMoveState.Idle,Machine.MoveState);
        Assert.Equal(0.5f*JogMarker,ChildX(Pose),2);
        Run(Machine,Pose,0.5f,new LocomotionInput(6f,0f,false,false,0f));
        Assert.Equal(IdleMarker,ChildX(Pose),2);
        // Pushed but not moving yet: the Move state at speed 0 is Idle's pose, so nothing visible changes.
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
        // Entering the state, the cubic fade from Idle (0.15 s) is a third done.
        Assert.Equal(PoseFade.Cubic(0.05f/LocomotionStateMachine.ToJumpBlendSeconds)*JumpMarker,ChildX(Pose),2);
        // The jump clip is 0.5 s long and starts at 0.08 s: 0.3 s remain after 0.12 s in the state.
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
        // The 0.5 s land clip has 0.4 s left after 0.1 s in the state: the third 0.04 s step crosses the line.
        Machine.Update(0.04f,Standing,Pose);
        Assert.Equal(LocomotionState.Locomotion,Machine.State);
        var Again=LocomotionGraphTests.Machine(TwoJoints());
        Run(Again,Pose,0.3f,Dropping);
        Run(Again,Pose,0.04f,Standing);
        Assert.Equal(LocomotionState.Land,Again.State);
        // Pushing while landing leaves Land at once, with the 0.5 s fade.
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
        // Touching down at once: the Land fade starts from the half-faded jump pose, not from Idle or from Jump.
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
        // The real Mannequin's backward walk is 2.13 s long and its forward walk 1.5 s: one normalized phase must still advance at the rate of what is mixed.
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
        // With the 2 s backward clip the same half second is a quarter of its phase.
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
        // The same rotation written as its negation is the same orientation; averaging must not cancel it.
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
        // Restarted mid-fade: the new fade begins at 5, the pose that was on screen.
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
