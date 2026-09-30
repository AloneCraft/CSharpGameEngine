namespace CSharpGameEngine.Runtime.Animation;
public enum LocomotionState:byte {
    Locomotion,
    Jump,
    Fall,
    Land,
}
/// <summary>The six clips of the locomotion graph, bound to one skeleton.</summary>
public sealed record LocomotionClips(AnimationClip Idle,AnimationClip Walk,AnimationClip Jog,AnimationClip Jump,AnimationClip Fall,AnimationClip Land);
/// <summary>
/// Unreal ThirdPerson's unarmed locomotion (ABP_Unarmed with BS_Idle_Walk_Run, ADR-0066) as a small
/// state machine. Locomotion blends Idle → Walk → Jog by ground speed along the blend space's forward
/// column (the character turns toward its movement, so the direction axis stays at zero); Walk and Jog
/// share one normalized phase so the feet stay in step. Leaving the ground starts Jump (rising) or Fall,
/// Jump runs into the looping Fall, and touching down plays Land, which moving cancels. Every state
/// change cross-fades from a snapshot of the last output pose.
/// </summary>
public sealed class LocomotionStateMachine {
    /// <summary>Ground speed above which Land yields to Locomotion before the clip ends (m/s).</summary>
    public const float LandCancelSpeed=1f;
    private readonly Skeleton Skeleton;
    private readonly LocomotionClips Clips;
    private readonly JointPose[]From;
    private readonly JointPose[]To;
    private readonly JointPose[]Snapshot;
    private readonly JointPose[]Scratch;
    private float BlendElapsed;
    private bool Blending;
    private float IdleTime;
    private float Phase;
    public LocomotionStateMachine(Skeleton Skeleton,LocomotionClips Clips,float WalkSpeed,float JogSpeed,float BlendSeconds){
        ArgumentNullException.ThrowIfNull(Skeleton);
        ArgumentNullException.ThrowIfNull(Clips);
        if(!(WalkSpeed>0f)||!(JogSpeed>WalkSpeed)||!(BlendSeconds>=0f)){
            throw new ArgumentOutOfRangeException(nameof(WalkSpeed),"Speeds must satisfy 0 < walk < jog and the blend time must be non-negative.");
        }
        this.Skeleton=Skeleton;
        this.Clips=Clips;
        this.WalkSpeed=WalkSpeed;
        this.JogSpeed=JogSpeed;
        this.BlendSeconds=BlendSeconds;
        this.From=new JointPose[Skeleton.JointCount];
        this.To=new JointPose[Skeleton.JointCount];
        this.Snapshot=new JointPose[Skeleton.JointCount];
        this.Scratch=new JointPose[Skeleton.JointCount];
        Skeleton.Rest.CopyTo(this.Snapshot);
    }
    public float WalkSpeed { get; }
    public float JogSpeed { get; }
    public float BlendSeconds { get; }
    public LocomotionState State { get; private set; }
    public float StateTime { get; private set; }
    /// <summary>Advances by <paramref name="DeltaSeconds"/> and writes the local pose.</summary>
    public void Update(float DeltaSeconds,float Speed,bool Falling,float VerticalSpeed,Span<JointPose>Pose){
        if(Pose.Length<this.Skeleton.JointCount){
            throw new ArgumentException($"The pose needs {this.Skeleton.JointCount} joints.",nameof(Pose));
        }
        DeltaSeconds=float.IsFinite(DeltaSeconds)?Math.Max(DeltaSeconds,0f):0f;
        Speed=float.IsFinite(Speed)?Math.Max(Speed,0f):0f;
        this.StateTime+=DeltaSeconds;
        var Next=this.NextState(Speed,Falling,VerticalSpeed);
        if(Next!=this.State){
            this.Snapshot.AsSpan().CopyTo(this.From);
            this.Blending=this.BlendSeconds>0f;
            this.BlendElapsed=0f;
            this.State=Next;
            this.StateTime=0f;
        }
        this.AdvanceLocomotionClocks(DeltaSeconds,Speed);
        this.EvaluateState(Speed,this.To);
        if(this.Blending){
            this.BlendElapsed+=DeltaSeconds;
            var Weight=Math.Clamp(this.BlendElapsed/this.BlendSeconds,0f,1f);
            JointPose.Blend(this.From,this.To,Weight,Pose);
            this.Blending=Weight<1f;
        }
        else {
            this.To.AsSpan().CopyTo(Pose);
        }
        Pose[..this.Skeleton.JointCount].CopyTo(this.Snapshot);
    }
    private LocomotionState NextState(float Speed,bool Falling,float VerticalSpeed){
        var Airborne=VerticalSpeed>0f?LocomotionState.Jump:LocomotionState.Fall;
        return this.State switch {
            LocomotionState.Locomotion=>Falling?Airborne:LocomotionState.Locomotion,
            LocomotionState.Jump=>!Falling?LocomotionState.Land
                :this.StateTime>=this.Clips.Jump.DurationSeconds-this.BlendSeconds?LocomotionState.Fall
                :LocomotionState.Jump,
            LocomotionState.Fall=>Falling?LocomotionState.Fall:LocomotionState.Land,
            LocomotionState.Land=>Falling?Airborne
                :Speed>LandCancelSpeed||this.StateTime>=this.Clips.Land.DurationSeconds-this.BlendSeconds?LocomotionState.Locomotion
                :LocomotionState.Land,
            _=>LocomotionState.Locomotion,
        };
    }
    private void AdvanceLocomotionClocks(float DeltaSeconds,float Speed){
        this.IdleTime+=DeltaSeconds;
        // Walk and Jog advance one normalized phase at the rate of their blended length.
        var JogWeight=Math.Clamp((Speed-this.WalkSpeed)/(this.JogSpeed-this.WalkSpeed),0f,1f);
        var Length=float.Lerp(this.Clips.Walk.DurationSeconds,this.Clips.Jog.DurationSeconds,JogWeight);
        if(Length>0f){
            this.Phase=(this.Phase+(DeltaSeconds/Length))%1f;
        }
    }
    private void EvaluateState(float Speed,Span<JointPose>Pose){
        switch(this.State){
            case LocomotionState.Jump:
                this.Clips.Jump.Sample(this.Skeleton,this.StateTime,false,Pose);
                break;
            case LocomotionState.Fall:
                this.Clips.Fall.Sample(this.Skeleton,this.StateTime,true,Pose);
                break;
            case LocomotionState.Land:
                this.Clips.Land.Sample(this.Skeleton,this.StateTime,false,Pose);
                break;
            default:
                this.EvaluateLocomotion(Speed,Pose);
                break;
        }
    }
    /// <summary>The blend-space pose for <paramref name="Speed"/>: Idle to Walk up to WalkSpeed, Walk to Jog up to JogSpeed, Jog beyond.</summary>
    public void EvaluateLocomotion(float Speed,Span<JointPose>Pose){
        var Scratch=this.Scratch;
        if(Speed<=0f){
            this.Clips.Idle.Sample(this.Skeleton,this.IdleTime,true,Pose);
        }
        else if(Speed<this.WalkSpeed){
            this.Clips.Idle.Sample(this.Skeleton,this.IdleTime,true,Pose);
            this.Clips.Walk.Sample(this.Skeleton,this.Phase*this.Clips.Walk.DurationSeconds,true,Scratch);
            JointPose.Blend(Pose,Scratch,Speed/this.WalkSpeed,Pose);
        }
        else if(Speed<this.JogSpeed){
            this.Clips.Walk.Sample(this.Skeleton,this.Phase*this.Clips.Walk.DurationSeconds,true,Pose);
            this.Clips.Jog.Sample(this.Skeleton,this.Phase*this.Clips.Jog.DurationSeconds,true,Scratch);
            JointPose.Blend(Pose,Scratch,(Speed-this.WalkSpeed)/(this.JogSpeed-this.WalkSpeed),Pose);
        }
        else {
            this.Clips.Jog.Sample(this.Skeleton,this.Phase*this.Clips.Jog.DurationSeconds,true,Pose);
        }
    }
}
