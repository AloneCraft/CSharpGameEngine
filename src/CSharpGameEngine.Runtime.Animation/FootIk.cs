using System.Numerics;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// <see cref="SkeletalMesh"/>ノード上で<see cref="Locomotion"/>と組み合わせて使う、マネキンの足IK（UEのCR_Mannequin_FootIK、ADR-0078/M0107）。
/// 毎フレーム、各足の上方からレイを下ろす（メッシュ原点より上の<see cref="TraceTop"/>から下の<see cref="TraceBottom"/>まで、
/// 足首の前方<see cref="TraceForward"/>から開始）。レイが何も検出しなければ0、キャラクターが落下中なら両足とも0とし、地面の高さをメッシュ原点基準の足の目標オフセットにする。
/// オフセットは毎秒<see cref="InterpolationSpeed"/>の速度で目標へ近づけ、骨盤は低い方の足に追従する。その後、骨盤をオフセット分動かし、
/// <see cref="TwoBoneIk"/>で各脚を解いて足をそれぞれのオフセット位置へ戻す。Control Rigでは全身IKノードで解くが、立っているキャラクターの脚にはそれ以上必要ない。
/// ジョイント名はマネキンのものを使う。
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
    /// <summary>メッシュ原点から上方へレイを開始する距離（m）。UEのルートボーンZ + 30 cm。</summary>
    public float TraceTop=0.3f;
    /// <summary>メッシュ原点から下方へレイを終了する距離（m）。UEのルートボーンZ - 50 cm。</summary>
    public float TraceBottom=0.5f;
    /// <summary>足首からつま先方向へレイを進める距離（m）。UEのメッシュ前方へ+5 cm。</summary>
    public float TraceForward=0.05f;
    /// <summary>UEのAlphaInterp速度（両方向）、毎秒。</summary>
    public float InterpolationSpeed=10f;
}
public struct FootIkRuntime:IRuntimeComponent<FootIk> {
    public FootIkPose?Pose;
    public Matrix4x4[]?Scratch;
    public string?Error;
}
/// <summary>1つのスケルトンに対する足IKの状態と計算。地面をトレースするシステムがこれに値を渡す（<see cref="FootIk"/>を参照）。</summary>
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
    /// <summary>地面からの足の平滑化済み垂直オフセット（m）。正の値で足を持ち上げる。</summary>
    public float FootOffset(int Side)=>this.Current[Side];
    public float TargetOffset(int Side)=>this.Target[Side];
    /// <summary>骨盤のオフセット。平滑化済みの両足オフセットのうち低い方。</summary>
    public float PelvisOffset=>this.PelvisCurrent;
    /// <summary>
    /// <paramref name="Side"/>側のレイを開始するモデル空間位置。足首の上方（<see cref="FootIk.TraceTop"/>。モデルの上方向は+Y）かつ少し前方。
    /// スケルトンにボールジョイントがあれば、つま先側へ寄せる。<paramref name="Model"/>はアニメーション姿勢のモデル行列を保持する。
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
    /// 平滑化処理を進める。キャラクターの落下中は<paramref name="Trace"/>がfalseとなり、両方の目標値は0になる。
    /// 地面の高さ（メッシュ原点基準、上向きが正）は、レイが何も検出しなかった場合nullとなり、目標値も0になる。
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
    /// <summary>平滑化済みオフセットがすべて0であり、適用しても変化しない場合はtrue。</summary>
    public bool IsNeutral=>MathF.Abs(this.Current[Left])<1e-5f&&MathF.Abs(this.Current[Right])<1e-5f&&MathF.Abs(this.PelvisCurrent)<1e-5f;
    /// <summary>
    /// 骨盤を<see cref="PelvisOffset"/>分移動し、各脚を解いて、足がアニメーション足の位置に固有のオフセットを加えた場所に来るようにする
    ///（モデル空間の上方向は+Y）。<paramref name="Local"/>はアニメーション済みのローカル姿勢で、直接編集する。<paramref name="Model"/>は作業領域。
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
        // 骨盤：骨盤の親座標系で表したモデル空間の移動量。
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
