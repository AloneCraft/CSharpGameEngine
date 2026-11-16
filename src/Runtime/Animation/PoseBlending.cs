using System.Numerics;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// 複数のローカル姿勢を加重ミックスする。平行移動とスケールは平均し、回転は短い方の弧に沿って平均する
///（各回転を最初に追加した回転と同じ半球へ反転してから、合計を正規化する）。ウェイトの合計は1でなくてもよい。
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
    /// <summary><see cref="Begin"/>以降に追加されたウェイトの合計。</summary>
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
    /// <summary>ミックス結果を書き込む。ウェイトがない場合は恒等姿勢（平行移動0、スケール1）になる。</summary>
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
/// 最後の出力姿勢のスナップショットから、変化する目標姿勢へUEのCubicブレンドカーブ（3t² − 2t³）でクロスフェードする。
/// <see cref="Start"/>では最後の姿勢を入力側に保持し、<see cref="Apply"/>でフェードを進めてブレンド姿勢を書き込む。
/// アニメーショングラフの状態機械と同様、<see cref="Start"/>直後の最初の<see cref="Apply"/>でも、そのステップ分だけ既に進む。
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
    /// <summary>カーブ適用後、出力に含まれる目標姿勢の割合（<see cref="Start"/>直後は0）。</summary>
    public float Weight { get; private set; }
    /// <summary>UEのCubicブレンドオプション。時間の割合を0～1に制限してsmoothstepを適用する。</summary>
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
