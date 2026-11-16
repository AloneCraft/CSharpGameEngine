using System.Numerics;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// このノードに配置する、スキニングされた glTF（ADR-0066）。ファイルにはスキンを正確に1つ含める。スキニング対象プリミティブはスケルトンの姿勢に従い、それ以外のプリミティブは glTF での
/// 配置を保つ。アニメーターがなければメッシュはバインド姿勢のまま。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,2)]
public struct SkeletalMesh {
    public string AssetPath;
}
/// <summary><see cref="SkeletalMesh"/> のスケルトンと最新のスキニングパレット。</summary>
public struct SkeletalMeshRuntime:IRuntimeComponent<SkeletalMesh> {
    public Skeleton?Skeleton;
    /// <summary>関節ごとの逆バインド行列 × 関節モデル行列（行ベクトル）。アニメーターが姿勢を書き込むまでは単位行列。</summary>
    public Matrix4x4[]?Palette;
    public Matrix4x4[]?Model;
    public string?Error;
}
/// <summary>
/// <see cref="SkeletalMesh"/> ノードで使う、Unreal ThirdPerson の非武装移動グラフ（<see cref="LocomotionStateMachine"/>、
/// ABP_Unarmed）。ゲーム側は入力（<see cref="Speed"/>、<see cref="Direction"/>、<see cref="ShouldMove"/>、
/// <see cref="Falling"/>、<see cref="VerticalSpeed"/>）を毎フレーム書き込む。Blueprint の BlueprintUpdateAnimation と
/// 同様に、エンジンの <c>LocomotionObserver</c> がキャラクターの移動から設定することもできる。速度はブレンドスペースのサンプル点（m/s）。<see cref="Walk"/>
///  と <see cref="Jog"/> は前進クリップ。各モードの他の7方向は省略可能で、方向別クリップがなければ前進クリップを使う。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,3)]
public struct Locomotion() {
    public string Idle="";
    public string Walk="";
    public string WalkForwardRight="";
    public string WalkRight="";
    public string WalkBackwardRight="";
    public string WalkBackward="";
    public string WalkBackwardLeft="";
    public string WalkLeft="";
    public string WalkForwardLeft="";
    public string Jog="";
    public string JogForwardRight="";
    public string JogRight="";
    public string JogBackwardRight="";
    public string JogBackward="";
    public string JogBackwardLeft="";
    public string JogLeft="";
    public string JogForwardLeft="";
    public string Jump="";
    public string Fall="";
    public string Land="";
    public float WalkSpeed=3f;
    public float JogSpeed=6f;
    /// <summary>ブレンドスペースで目標重みに近づく速度（1秒あたり、UE の TargetWeightInterpolationSpeedPerSec）。</summary>
    public float BlendWeightSpeed=LocomotionStateMachine.WeightInterpolationSpeed;
    /// <summary>このフレームの水平移動速度（m/s）。</summary>
    public float Speed=0f;
    /// <summary>ボディの前方から速度方向までの符号付き角度（度、−180～180）。Right クリップが進む側（+F を向いているときの +R 側）を正とする。</summary>
    public float Direction=0f;
    /// <summary>移動して押されている状態。地上速度が0.01 cm/sを超え、キャラクターに加速度がある。</summary>
    public bool ShouldMove=false;
    public bool Falling=false;
    /// <summary>このフレームの上昇速度（m/s）。上昇中は正。</summary>
    public float VerticalSpeed=0f;
    /// <summary><paramref name="Direction"/> 方向の歩行クリップ（<see cref="LocomotionDirection"/>）。指定がなければ前進クリップ。</summary>
    public readonly string WalkClip(int Direction)=>Direction switch {
        LocomotionDirection.ForwardRight=>Pick(this.WalkForwardRight,this.Walk),
        LocomotionDirection.Right=>Pick(this.WalkRight,this.Walk),
        LocomotionDirection.BackwardRight=>Pick(this.WalkBackwardRight,this.Walk),
        LocomotionDirection.Backward=>Pick(this.WalkBackward,this.Walk),
        LocomotionDirection.BackwardLeft=>Pick(this.WalkBackwardLeft,this.Walk),
        LocomotionDirection.Left=>Pick(this.WalkLeft,this.Walk),
        LocomotionDirection.ForwardLeft=>Pick(this.WalkForwardLeft,this.Walk),
        _=>this.Walk??string.Empty,
    };
    /// <summary><paramref name="Direction"/> 方向のジョギングクリップ。指定がなければ前進クリップ。</summary>
    public readonly string JogClip(int Direction)=>Direction switch {
        LocomotionDirection.ForwardRight=>Pick(this.JogForwardRight,this.Jog),
        LocomotionDirection.Right=>Pick(this.JogRight,this.Jog),
        LocomotionDirection.BackwardRight=>Pick(this.JogBackwardRight,this.Jog),
        LocomotionDirection.Backward=>Pick(this.JogBackward,this.Jog),
        LocomotionDirection.BackwardLeft=>Pick(this.JogBackwardLeft,this.Jog),
        LocomotionDirection.Left=>Pick(this.JogLeft,this.Jog),
        LocomotionDirection.ForwardLeft=>Pick(this.JogForwardLeft,this.Jog),
        _=>this.Jog??string.Empty,
    };
    private static string Pick(string?Named,string?Forward)=>string.IsNullOrEmpty(Named)?Forward??string.Empty:Named;
}
public struct LocomotionRuntime:IRuntimeComponent<Locomotion> {
    public LocomotionStateMachine?Machine;
    public JointPose[]?Pose;
    public string?Error;
}
