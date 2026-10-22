using System.Numerics;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// Weighted mix of several local poses: translation and scale average, rotations average along the shorter arcs (each rotation is
/// flipped into the hemisphere of the first one added, then the sum is normalized). Weights need not sum to one.
/// </summary>
public sealed class PoseMixer {
    private readonly Vector3[]Translation;
    private readonly Vector3[]Scale;
    private readonly Vector4[]Rotation;
    private readonly Quaternion[]Reference;
    private float Total;
    private bool HasReference;
    public PoseMixer(int JointCount){
        ArgumentOutOfRangeException.ThrowIfNegative(JointCount);
        this.Translation=new Vector3[JointCount];
        this.Scale=new Vector3[JointCount];
        this.Rotation=new Vector4[JointCount];
        this.Reference=new Quaternion[JointCount];
    }
    public int JointCount=>this.Translation.Length;
    /// <summary>The sum of the weights added since <see cref="Begin"/>.</summary>
    public float TotalWeight=>this.Total;
    public void Begin(){
        Array.Clear(this.Translation);
        Array.Clear(this.Scale);
        Array.Clear(this.Rotation);
        this.Total=0f;
        this.HasReference=false;
    }
    public void Add(ReadOnlySpan<JointPose>Pose,float Weight){
        if(!(Weight>0f)||!float.IsFinite(Weight)){
            return;
        }
        if(Pose.Length<this.JointCount){
            throw new ArgumentException($"The pose needs {this.JointCount} joints.",nameof(Pose));
        }
        for(var Joint=0;Joint<this.JointCount;Joint++){
            var Rotation=Pose[Joint].Rotation;
            if(!this.HasReference){
                this.Reference[Joint]=Rotation;
            }
            else if(Quaternion.Dot(Rotation,this.Reference[Joint])<0f){
                Rotation=Quaternion.Negate(Rotation);
            }
            this.Translation[Joint]+=Pose[Joint].Translation*Weight;
            this.Scale[Joint]+=Pose[Joint].Scale*Weight;
            this.Rotation[Joint]+=new Vector4(Rotation.X,Rotation.Y,Rotation.Z,Rotation.W)*Weight;
        }
        this.HasReference=true;
        this.Total+=Weight;
    }
    /// <summary>Writes the mix. With no weight added the pose is the identity (zero translation, unit scale).</summary>
    public void Resolve(Span<JointPose>Pose){
        if(Pose.Length<this.JointCount){
            throw new ArgumentException($"The pose needs {this.JointCount} joints.",nameof(Pose));
        }
        for(var Joint=0;Joint<this.JointCount;Joint++){
            if(!(this.Total>0f)){
                Pose[Joint]=new JointPose { Rotation=Quaternion.Identity,Scale=Vector3.One };
                continue;
            }
            var Sum=this.Rotation[Joint];
            var Rotation=Sum.LengthSquared()>1e-12f?Quaternion.Normalize(new Quaternion(Sum.X,Sum.Y,Sum.Z,Sum.W)):Quaternion.Identity;
            Pose[Joint]=new JointPose { Translation=this.Translation[Joint]/this.Total,Rotation=Rotation,Scale=this.Scale[Joint]/this.Total };
        }
    }
}
/// <summary>
/// A cross-fade from a snapshot of the last output pose to a changing target pose, with UE's Cubic blend curve (3t² − 2t³).
/// <see cref="Start"/> keeps the last pose as the source; <see cref="Apply"/> advances the fade and writes the blended pose. The first
/// <see cref="Apply"/> after <see cref="Start"/> already advances by its own step, as the state machine of the animation graph does.
/// </summary>
public sealed class PoseFade {
    private readonly JointPose[]Source;
    private readonly JointPose[]Last;
    private float Elapsed;
    private float Duration;
    public PoseFade(ReadOnlySpan<JointPose>Rest){
        this.Source=Rest.ToArray();
        this.Last=Rest.ToArray();
    }
    public bool Active { get; private set; }
    /// <summary>The fraction of the target in the output, after the curve (0 right after <see cref="Start"/>).</summary>
    public float Weight { get; private set; }
    /// <summary>UE's Cubic blend option: smoothstep of the clamped time fraction.</summary>
    public static float Cubic(float Fraction){
        var Clamped=Math.Clamp(float.IsFinite(Fraction)?Fraction:0f,0f,1f);
        return Clamped*Clamped*(3f-(2f*Clamped));
    }
    public void Start(float DurationSeconds){
        this.Last.CopyTo(this.Source,0);
        this.Elapsed=0f;
        this.Duration=float.IsFinite(DurationSeconds)?Math.Max(DurationSeconds,0f):0f;
        this.Active=this.Duration>0f;
        this.Weight=this.Active?0f:1f;
    }
    public void Apply(float DeltaSeconds,ReadOnlySpan<JointPose>Target,Span<JointPose>Output){
        if(this.Active){
            this.Elapsed+=float.IsFinite(DeltaSeconds)?Math.Max(DeltaSeconds,0f):0f;
            var Fraction=Math.Clamp(this.Elapsed/this.Duration,0f,1f);
            this.Weight=Cubic(Fraction);
            JointPose.Blend(this.Source,Target,this.Weight,Output);
            this.Active=Fraction<1f;
        }
        else {
            this.Weight=1f;
            Target[..Output.Length].CopyTo(Output);
        }
        Output.CopyTo(this.Last);
    }
}
