namespace CSharpGameEngine.Runtime.Animation;
public enum LocomotionState:byte {
    Locomotion,
    Jump,
    Fall,
    Land,
}
/// <summary>The state of the Locomotion state machine nested in the Locomotion state: Idle, or the walk/run blend space.</summary>
public enum LocomotionMoveState:byte {
    Idle,
    Move,
}
/// <summary>What ABP_Unarmed's event graph (BlueprintUpdateAnimation) hands the animation graph each frame; speeds in m/s, the direction in degrees.</summary>
/// <param name="GroundSpeed">The length of the horizontal velocity.</param>
/// <param name="Direction">The signed angle (−180 to 180) from the body's forward to the horizontal velocity, positive toward the side the Right clips travel to.</param>
/// <param name="ShouldMove">Moving and being pushed (ground speed above 0.01 cm/s and a non-zero acceleration).</param>
/// <param name="IsFalling">The movement mode is falling.</param>
/// <param name="VerticalSpeed">Upward speed; the Jump state needs more than <see cref="LocomotionStateMachine.JumpVerticalSpeed"/>.</param>
public readonly record struct LocomotionInput(float GroundSpeed,float Direction,bool ShouldMove,bool IsFalling,float VerticalSpeed);
/// <summary>
/// The clips of the locomotion graph, bound to one skeleton: Idle, Jump, Fall loop, Land and the eight directions of walking and jogging
/// (indexed by <see cref="LocomotionDirection"/>).
/// </summary>
public sealed record LocomotionClips(AnimationClip Idle,AnimationClip[]Walk,AnimationClip[]Jog,AnimationClip Jump,AnimationClip Fall,AnimationClip Land){
    /// <summary>Every direction takes the same clip: the forward-only locomotion graph the template had before the eight directions.</summary>
    public static LocomotionClips ForwardOnly(AnimationClip Idle,AnimationClip Walk,AnimationClip Jog,AnimationClip Jump,AnimationClip Fall,AnimationClip Land)=>
        new(Idle,[.. Enumerable.Repeat(Walk,LocomotionDirection.Count)],[.. Enumerable.Repeat(Jog,LocomotionDirection.Count)],Jump,Fall,Land);
}
/// <summary>The eight directions of BS_Idle_Walk_Run, clockwise from the front as seen from above: the index into the walk and jog clip arrays.</summary>
public static class LocomotionDirection {
    public const int Count=8;
    public const int Forward=0;
    public const int ForwardRight=1;
    public const int Right=2;
    public const int BackwardRight=3;
    public const int Backward=4;
    public const int BackwardLeft=5;
    public const int Left=6;
    public const int ForwardLeft=7;
    /// <summary>The direction whose angle (degrees, 0 forward and positive toward the Right clips' side) is the grid value <paramref name="Degrees"/>; ±180 are both Backward.</summary>
    public static int OfAngle(float Degrees)=>(((int)MathF.Round(Degrees/45f))%Count+Count)%Count;
}
/// <summary>
/// Unreal ThirdPerson's unarmed animation graph (ABP_Unarmed, ADR-0066, ADR-0078/M0107) as a state machine. The Main States machine has the
/// states Locomotion, Jump, Fall Loop and Land. Leaving the ground from Locomotion or Land goes to Jump when the upward speed is above
/// <see cref="JumpVerticalSpeed"/> (0.15 s cross-fade) and to Fall Loop otherwise (0.4 s); Jump runs into the Fall Loop when its clip has
/// <see cref="JumpToFallSeconds"/> left (0.3 s); touching down from Jump or Fall Loop goes to Land (0.1 s); Land returns to Locomotion
/// when its clip has <see cref="LandToLocomotionSeconds"/> left (0.4 s) or when the character should move (0.5 s). The Locomotion state is
/// itself a state machine: Idle, and the walk/run blend space (<see cref="BlendSpace2D"/> with the eight directions at the walk and jog
/// speeds and Idle at speed 0). Idle goes to Move when <see cref="LocomotionInput.ShouldMove"/> (0.2 s) and back when not (0.3 s). Every
/// cross-fade uses the Cubic curve and starts from a snapshot of the last output pose, so a transition interrupted by another one continues
/// from what was on screen. The clips of the blend space share one normalized phase, advanced at the rate of their weighted length, so
/// the feet stay in step through the blend.
/// </summary>
public sealed class LocomotionStateMachine {
    /// <summary>Upward speed (m/s) above which leaving the ground starts Jump instead of Fall Loop (Velocity.Z &gt; 100 cm/s).</summary>
    public const float JumpVerticalSpeed=1f;
    /// <summary>Where the Jump clip starts (the Jump state's StartPosition), in seconds.</summary>
    public const float JumpStartSeconds=0.08f;
    public const float ToJumpBlendSeconds=0.15f;
    public const float ToFallBlendSeconds=0.4f;
    public const float JumpToFallSeconds=0.3f;
    public const float ToLandBlendSeconds=0.1f;
    public const float LandToLocomotionSeconds=0.4f;
    public const float LandToMoveBlendSeconds=0.5f;
    public const float IdleToMoveBlendSeconds=0.2f;
    public const float MoveToIdleBlendSeconds=0.3f;
    /// <summary>The blend space's target weight interpolation speed per second.</summary>
    public const float WeightInterpolationSpeed=5f;
    private readonly Skeleton Skeleton;
    private readonly LocomotionClips Clips;
    private readonly BlendSpace2D BlendSpace;
    private readonly AnimationClip[]SampleClips;
    private readonly PoseMixer Mixer;
    private readonly PoseFade MainFade;
    private readonly PoseFade MoveFade;
    private readonly JointPose[]Scratch;
    private readonly JointPose[]IdlePose;
    private readonly JointPose[]MovePose;
    private readonly JointPose[]LocomotionPose;
    private readonly JointPose[]StatePose;
    private readonly Dictionary<AnimationClip,float>ClipWeights=new();
    private float IdleTime;
    private float Phase;
    public LocomotionStateMachine(Skeleton Skeleton,LocomotionClips Clips,float WalkSpeed,float JogSpeed,float InterpolationSpeed=WeightInterpolationSpeed){
        ArgumentNullException.ThrowIfNull(Skeleton);
        ArgumentNullException.ThrowIfNull(Clips);
        if(!(WalkSpeed>0f)||!(JogSpeed>WalkSpeed)||!float.IsFinite(JogSpeed)){
            throw new ArgumentOutOfRangeException(nameof(WalkSpeed),"Speeds must satisfy 0 < walk < jog.");
        }
        if(Clips.Walk is not{ Length:LocomotionDirection.Count }||Clips.Jog is not{ Length:LocomotionDirection.Count }){
            throw new ArgumentException($"The walk and jog clips need {LocomotionDirection.Count} directions each.",nameof(Clips));
        }
        this.Skeleton=Skeleton;
        this.Clips=Clips;
        this.WalkSpeed=WalkSpeed;
        this.JogSpeed=JogSpeed;
        var Directions=new float[9];
        for(var Column=0;Column<Directions.Length;Column++){
            Directions[Column]=-180f+(45f*Column);
        }
        this.BlendSpace=new BlendSpace2D(Directions,[0f,WalkSpeed,JogSpeed],InterpolationSpeed);
        this.SampleClips=new AnimationClip[this.BlendSpace.SampleCount];
        for(var Column=0;Column<Directions.Length;Column++){
            var Direction=LocomotionDirection.OfAngle(Directions[Column]);
            this.SampleClips[this.BlendSpace.SampleIndex(Column,0)]=Clips.Idle;
            this.SampleClips[this.BlendSpace.SampleIndex(Column,1)]=Clips.Walk[Direction];
            this.SampleClips[this.BlendSpace.SampleIndex(Column,2)]=Clips.Jog[Direction];
        }
        var Count=Skeleton.JointCount;
        this.Mixer=new PoseMixer(Count);
        this.MainFade=new PoseFade(Skeleton.Rest);
        this.MoveFade=new PoseFade(Skeleton.Rest);
        this.Scratch=new JointPose[Count];
        this.IdlePose=new JointPose[Count];
        this.MovePose=new JointPose[Count];
        this.LocomotionPose=new JointPose[Count];
        this.StatePose=new JointPose[Count];
    }
    public float WalkSpeed { get; }
    public float JogSpeed { get; }
    public LocomotionState State { get; private set; }
    /// <summary>The Locomotion state's inner state (kept while another state is current).</summary>
    public LocomotionMoveState MoveState { get; private set; }
    /// <summary>Seconds since the current main state was entered.</summary>
    public float StateTime { get; private set; }
    /// <summary>The blend space's current (smoothed) sample weights.</summary>
    public ReadOnlySpan<float>BlendWeights=>this.BlendSpace.Weights;
    public int BlendSampleCount=>this.BlendSpace.SampleCount;
    /// <summary>The shared normalized phase (0 to 1) of the blend space's clips.</summary>
    public float BlendPhase=>this.Phase;
    /// <summary>Advances by <paramref name="DeltaSeconds"/> and writes the local pose.</summary>
    public void Update(float DeltaSeconds,in LocomotionInput Input,Span<JointPose>Pose){
        if(Pose.Length<this.Skeleton.JointCount){
            throw new ArgumentException($"The pose needs {this.Skeleton.JointCount} joints.",nameof(Pose));
        }
        var Count=this.Skeleton.JointCount;
        DeltaSeconds=float.IsFinite(DeltaSeconds)?Math.Max(DeltaSeconds,0f):0f;
        var Speed=float.IsFinite(Input.GroundSpeed)?Math.Max(Input.GroundSpeed,0f):0f;
        var Direction=float.IsFinite(Input.Direction)?Input.Direction:0f;
        this.StateTime+=DeltaSeconds;
        if(this.NextState(Input) is{ } Next&&Next.State!=this.State){
            this.State=Next.State;
            this.StateTime=0f;
            this.MainFade.Start(Next.BlendSeconds);
        }
        if(this.State==LocomotionState.Locomotion){
            this.UpdateLocomotion(DeltaSeconds,Speed,Direction,Input.ShouldMove,this.LocomotionPose);
            this.LocomotionPose.AsSpan().CopyTo(this.StatePose);
        }
        else {
            this.EvaluateAirOrLand(this.StatePose);
        }
        this.MainFade.Apply(DeltaSeconds,this.StatePose,Pose[..Count]);
    }
    private readonly record struct Transition(LocomotionState State,float BlendSeconds);
    private Transition? NextState(in LocomotionInput Input){
        var Falling=Input.IsFalling;
        var Airborne=new Transition(Input.VerticalSpeed>JumpVerticalSpeed?LocomotionState.Jump:LocomotionState.Fall,Input.VerticalSpeed>JumpVerticalSpeed?ToJumpBlendSeconds:ToFallBlendSeconds);
        switch(this.State){
            case LocomotionState.Locomotion:
                return Falling?Airborne:null;
            case LocomotionState.Jump:
                if(!Falling){
                    return new Transition(LocomotionState.Land,ToLandBlendSeconds);
                }
                return this.Clips.Jump.DurationSeconds-(this.StateTime+JumpStartSeconds)<=JumpToFallSeconds?new Transition(LocomotionState.Fall,JumpToFallSeconds):null;
            case LocomotionState.Fall:
                return Falling?null:new Transition(LocomotionState.Land,ToLandBlendSeconds);
            case LocomotionState.Land:
                if(Falling){
                    return Airborne;
                }
                if(Input.ShouldMove){
                    return new Transition(LocomotionState.Locomotion,LandToMoveBlendSeconds);
                }
                return this.Clips.Land.DurationSeconds-this.StateTime<=LandToLocomotionSeconds?new Transition(LocomotionState.Locomotion,LandToLocomotionSeconds):null;
            default:
                return null;
        }
    }
    private void UpdateLocomotion(float DeltaSeconds,float Speed,float Direction,bool ShouldMove,Span<JointPose>Output){
        var Count=this.Skeleton.JointCount;
        var Wanted=ShouldMove?LocomotionMoveState.Move:LocomotionMoveState.Idle;
        if(Wanted!=this.MoveState){
            this.MoveState=Wanted;
            if(Wanted==LocomotionMoveState.Move){
                // The blend space player starts afresh on entry: its weights begin at the first query's, not at the last run's.
                this.BlendSpace.Reset();
            }
            this.MoveFade.Start(Wanted==LocomotionMoveState.Move?IdleToMoveBlendSeconds:MoveToIdleBlendSeconds);
        }
        this.IdleTime+=DeltaSeconds;
        if(this.MoveState==LocomotionMoveState.Idle){
            this.Clips.Idle.Sample(this.Skeleton,this.IdleTime,true,this.IdlePose);
            this.MoveFade.Apply(DeltaSeconds,this.IdlePose,Output[..Count]);
            return;
        }
        this.EvaluateBlendSpace(DeltaSeconds,Direction,Speed,this.MovePose);
        this.MoveFade.Apply(DeltaSeconds,this.MovePose,Output[..Count]);
    }
    private void EvaluateAirOrLand(Span<JointPose>Pose){
        switch(this.State){
            case LocomotionState.Jump:
                this.Clips.Jump.Sample(this.Skeleton,this.StateTime+JumpStartSeconds,false,Pose);
                break;
            case LocomotionState.Fall:
                this.Clips.Fall.Sample(this.Skeleton,this.StateTime,true,Pose);
                break;
            default:
                this.Clips.Land.Sample(this.Skeleton,this.StateTime,false,Pose);
                break;
        }
    }
    /// <summary>Advances the shared phase and the smoothed weights, then mixes the clips by weight.</summary>
    private void EvaluateBlendSpace(float DeltaSeconds,float Direction,float Speed,Span<JointPose>Pose){
        this.BlendSpace.Update(DeltaSeconds,Direction,Speed);
        var Length=this.GatherClipWeights();
        if(Length>0f){
            this.Phase=(this.Phase+(DeltaSeconds/Length))%1f;
        }
        this.MixClips(Pose);
    }
    /// <summary>The blend-space pose for the given input at the current phase, with the weights snapped (not smoothed); nothing advances.</summary>
    public void EvaluateLocomotion(float Direction,float GroundSpeed,Span<JointPose>Pose){
        if(Pose.Length<this.Skeleton.JointCount){
            throw new ArgumentException($"The pose needs {this.Skeleton.JointCount} joints.",nameof(Pose));
        }
        var Weights=new float[this.BlendSpace.SampleCount];
        this.BlendSpace.TargetWeights(Direction,GroundSpeed,Weights);
        this.ClipWeights.Clear();
        for(var Sample=0;Sample<Weights.Length;Sample++){
            if(Weights[Sample]>0f){
                this.ClipWeights[this.SampleClips[Sample]]=this.ClipWeights.GetValueOrDefault(this.SampleClips[Sample])+Weights[Sample];
            }
        }
        this.MixClips(Pose);
    }
    /// <summary>Sums the smoothed sample weights per clip (the same clip fills several samples) and returns their weighted length.</summary>
    private float GatherClipWeights(){
        this.ClipWeights.Clear();
        var Weights=this.BlendSpace.Weights;
        var Length=0f;
        for(var Sample=0;Sample<Weights.Length;Sample++){
            if(Weights[Sample]<=0f){
                continue;
            }
            var Clip=this.SampleClips[Sample];
            this.ClipWeights[Clip]=this.ClipWeights.GetValueOrDefault(Clip)+Weights[Sample];
            Length+=Weights[Sample]*Clip.DurationSeconds;
        }
        return Length;
    }
    private void MixClips(Span<JointPose>Pose){
        this.Mixer.Begin();
        foreach(var (Clip,Weight) in this.ClipWeights){
            Clip.Sample(this.Skeleton,this.Phase*Clip.DurationSeconds,true,this.Scratch);
            this.Mixer.Add(this.Scratch,Weight);
        }
        this.Mixer.Resolve(Pose[..this.Skeleton.JointCount]);
    }
}
