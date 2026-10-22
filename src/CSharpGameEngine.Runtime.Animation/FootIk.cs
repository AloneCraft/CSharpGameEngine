using System.Numerics;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// The mannequin's foot IK (UE's CR_Mannequin_FootIK, ADR-0078/M0107) on a <see cref="SkeletalMesh"/> node with a <see cref="Locomotion"/>. Each
/// frame a ray goes down from above each foot (<see cref="TraceTop"/> above the mesh origin to <see cref="TraceBottom"/> below it, starting
/// <see cref="TraceForward"/> ahead of the ankle); the ground height under it, relative to the mesh origin, is the foot's target offset (zero
/// where the ray finds nothing, and zero for both feet while the character is falling). The offsets approach their targets at
/// <see cref="InterpolationSpeed"/> per second; the pelvis follows the lower of the two. The feet are then put back onto the ground: the
/// pelvis moves by its offset and each leg is solved with <see cref="TwoBoneIk"/> so the foot lands on its own offset (the Control Rig solves
/// them with a full-body IK node; the legs of a standing character need nothing more). The joint names are the mannequin's.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,4)]
public struct FootIk() {
    public string Pelvis="pelvis";
    public string LeftThigh="thigh_l";
    public string LeftCalf="calf_l";
    public string LeftFoot="foot_l";
    public string LeftBall="ball_l";
    public string RightThigh="thigh_r";
    public string RightCalf="calf_r";
    public string RightFoot="foot_r";
    public string RightBall="ball_r";
    /// <summary>How far above the mesh origin the ray starts (m): UE's root bone Z + 30 cm.</summary>
    public float TraceTop=0.3f;
    /// <summary>How far below the mesh origin the ray ends (m): UE's root bone Z - 50 cm.</summary>
    public float TraceBottom=0.5f;
    /// <summary>How far ahead of the ankle, toward the toes, the ray runs (m): UE's +5 cm along the mesh's forward.</summary>
    public float TraceForward=0.05f;
    /// <summary>UE's AlphaInterp speed (both directions), per second.</summary>
    public float InterpolationSpeed=10f;
}
public struct FootIkRuntime:IRuntimeComponent<FootIk> {
    public FootIkPose?Pose;
    public Matrix4x4[]?Scratch;
    public string?Error;
}
/// <summary>The foot IK state and solve of one skeleton; the system that traces the ground feeds it (see <see cref="FootIk"/>).</summary>
public sealed class FootIkPose {
    private readonly Skeleton Skeleton;
    private readonly int PelvisJoint;
    private readonly int[]Joints;
    private readonly float TraceTop;
    private readonly float TraceForward;
    private readonly float InterpolationSpeed;
    private readonly float[]Target=new float[2];
    private readonly float[]Current=new float[2];
    private float PelvisCurrent;
    private bool Initialized;
    public const int Left=0;
    public const int Right=1;
    private const int Thigh=0;
    private const int Calf=1;
    private const int Foot=2;
    private const int Ball=3;
    public FootIkPose(Skeleton Skeleton,in FootIk Settings){
        ArgumentNullException.ThrowIfNull(Skeleton);
        this.Skeleton=Skeleton;
        this.PelvisJoint=Find(Skeleton,Settings.Pelvis);
        this.Joints=[
            Find(Skeleton,Settings.LeftThigh),Find(Skeleton,Settings.LeftCalf),Find(Skeleton,Settings.LeftFoot),FindOptional(Skeleton,Settings.LeftBall),
            Find(Skeleton,Settings.RightThigh),Find(Skeleton,Settings.RightCalf),Find(Skeleton,Settings.RightFoot),FindOptional(Skeleton,Settings.RightBall),
        ];
        for(var Side=0;Side<2;Side++){
            if(Skeleton.ParentOf(this.Joints[(Side*4)+Calf])!=this.Joints[(Side*4)+Thigh]||Skeleton.ParentOf(this.Joints[(Side*4)+Foot])!=this.Joints[(Side*4)+Calf]){
                throw new ArgumentException("The thigh, calf and foot joints must form a chain.");
            }
        }
        this.TraceTop=Settings.TraceTop;
        this.TraceForward=Settings.TraceForward;
        this.InterpolationSpeed=float.IsFinite(Settings.InterpolationSpeed)?Math.Max(Settings.InterpolationSpeed,0f):0f;
    }
    /// <summary>The foot's smoothed vertical offset from the ground (m); positive lifts it.</summary>
    public float FootOffset(int Side)=>this.Current[Side];
    public float TargetOffset(int Side)=>this.Target[Side];
    /// <summary>The pelvis offset: the lower of the two smoothed foot offsets.</summary>
    public float PelvisOffset=>this.PelvisCurrent;
    /// <summary>
    /// Where the ray of <paramref name="Side"/> starts, in model space: above the ankle (at <see cref="FootIk.TraceTop"/>, model up is +Y) and a little ahead of it,
    /// toward the toes when the skeleton has a ball joint. <paramref name="Model"/> holds the animated pose's model matrices.
    /// </summary>
    public Vector3 TraceOrigin(ReadOnlySpan<Matrix4x4>Model,int Side){
        var Ankle=Model[this.Joints[(Side*4)+Foot]].Translation;
        var Ahead=Vector3.Zero;
        var BallJoint=this.Joints[(Side*4)+Ball];
        if(BallJoint>=0){
            var Toes=Model[BallJoint].Translation-Ankle;
            Toes.Y=0f;
            if(Toes.LengthSquared()>1e-8f){
                Ahead=Vector3.Normalize(Toes)*this.TraceForward;
            }
        }
        return new Vector3(Ankle.X+Ahead.X,this.TraceTop,Ankle.Z+Ahead.Z);
    }
    /// <summary>
    /// Advances the smoothing. <paramref name="Trace"/> is false while the character is falling (both targets go to zero). A ground height (relative to the
    /// mesh origin, up positive) is null where the ray hit nothing, which also means a target of zero.
    /// </summary>
    public void Update(float DeltaSeconds,bool Trace,float?LeftGround,float?RightGround){
        this.Target[Left]=Trace&&LeftGround is{ } L&&float.IsFinite(L)?L:0f;
        this.Target[Right]=Trace&&RightGround is{ } R&&float.IsFinite(R)?R:0f;
        var Step=this.InterpolationSpeed>0f?Math.Clamp((float.IsFinite(DeltaSeconds)?Math.Max(DeltaSeconds,0f):0f)*this.InterpolationSpeed,0f,1f):1f;
        for(var Side=0;Side<2;Side++){
            this.Current[Side]=this.Initialized?this.Current[Side]+((this.Target[Side]-this.Current[Side])*Step):this.Target[Side];
        }
        this.Initialized=true;
        this.PelvisCurrent=MathF.Min(this.Current[Left],this.Current[Right]);
    }
    /// <summary>True when the smoothed offsets are all zero, so applying them would change nothing.</summary>
    public bool IsNeutral=>MathF.Abs(this.Current[Left])<1e-5f&&MathF.Abs(this.Current[Right])<1e-5f&&MathF.Abs(this.PelvisCurrent)<1e-5f;
    /// <summary>
    /// Moves the pelvis by <see cref="PelvisOffset"/> and solves each leg so its foot sits at the animated foot plus its own offset (model-space up is +Y).
    /// <paramref name="Local"/> is the animated local pose and is edited; <paramref name="Model"/> is scratch.
    /// </summary>
    public void Apply(Span<JointPose>Local,Span<Matrix4x4>Model){
        if(this.IsNeutral){
            return;
        }
        this.Skeleton.ComputeModel(Local,Model);
        var Targets=new Vector3[2];
        var Bends=new Vector3[2];
        for(var Side=0;Side<2;Side++){
            var Ankle=Model[this.Joints[(Side*4)+Foot]].Translation;
            Targets[Side]=Ankle+new Vector3(0f,this.Current[Side],0f);
            var BallJoint=this.Joints[(Side*4)+Ball];
            var Toes=BallJoint>=0?Model[BallJoint].Translation-Ankle:Vector3.UnitZ;
            Toes.Y=0f;
            Bends[Side]=Toes.LengthSquared()>1e-8f?Vector3.Normalize(Toes):Vector3.UnitZ;
        }
        // Pelvis: a model-space shift expressed in the pelvis's parent frame.
        var Parent=this.Skeleton.ParentOf(this.PelvisJoint);
        var ParentModel=Parent<0?this.Skeleton.RootTransform:Model[Parent];
        if(Matrix4x4.Invert(ParentModel,out var Inverse)){
            Local[this.PelvisJoint].Translation+=Vector3.TransformNormal(new Vector3(0f,this.PelvisCurrent,0f),Inverse);
        }
        for(var Side=0;Side<2;Side++){
            TwoBoneIk.Solve(this.Skeleton,Local,Model,this.Joints[(Side*4)+Thigh],this.Joints[(Side*4)+Calf],this.Joints[(Side*4)+Foot],Targets[Side],Bends[Side]);
        }
    }
    private static int Find(Skeleton Skeleton,string Name){
        var Joint=Skeleton.IndexOf(Name??string.Empty);
        if(Joint<0){
            throw new ArgumentException($"The skeleton has no joint named '{Name}'.");
        }
        return Joint;
    }
    private static int FindOptional(Skeleton Skeleton,string Name)=>string.IsNullOrEmpty(Name)?-1:Skeleton.IndexOf(Name);
}
