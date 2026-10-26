namespace CSharpGameEngine.Runtime.Animation;
public enum LocomotionState:byte {
    Locomotion,
    Jump,
    Fall,
    Land,
}
/// <summary>Locomotion状態内にネストされたLocomotion状態機械の状態。Idleまたは歩行／走行のブレンド空間。</summary>
public enum LocomotionMoveState:byte {
    Idle,
    Move,
}
/// <summary>ABP_Unarmedのイベントグラフ（BlueprintUpdateAnimation）が各フレームにアニメーショングラフへ渡す値。速度はm/s、方向は度数。</summary>
/// <param name="GroundSpeed">水平速度の大きさ。</param>
/// <param name="Direction">ボディの前方から水平速度までの符号付き角度（−180～180度）。Rightクリップが移動する側を正とする。</param>
/// <param name="ShouldMove">移動中かつ押されている状態（接地速度が0.01 cm/sを超え、加速度が0ではない）。</param>
/// <param name="IsFalling">移動モードが落下中。</param>
/// <param name="VerticalSpeed">上向き速度。Jump状態へ入るには<see cref="LocomotionStateMachine.JumpVerticalSpeed"/>を超える必要がある。</param>
public readonly record struct LocomotionInput(float GroundSpeed,float Direction,bool ShouldMove,bool IsFalling,float VerticalSpeed);
/// <summary>
/// 1つのスケルトンにバインドされた移動グラフのクリップ。Idle、Jump、Fallループ、Land、および8方向の歩行／ジョギング
///（<see cref="LocomotionDirection"/>でインデックス化）。
/// </summary>
public sealed record LocomotionClips(AnimationClip Idle,AnimationClip[]Walk,AnimationClip[]Jog,AnimationClip Jump,AnimationClip Fall,AnimationClip Land){
    /// <summary>すべての方向に同じクリップを使う。テンプレートが8方向対応する前に持っていた、前方移動専用のグラフ。</summary>
    public static LocomotionClips ForwardOnly(AnimationClip Idle,AnimationClip Walk,AnimationClip Jog,AnimationClip Jump,AnimationClip Fall,AnimationClip Land)=>
        new(Idle,[.. Enumerable.Repeat(Walk,LocomotionDirection.Count)],[.. Enumerable.Repeat(Jog,LocomotionDirection.Count)],Jump,Fall,Land);
}
/// <summary>上から見て正面を起点に時計回りに並べた、BS_Idle_Walk_Runの8方向。歩行／ジョギング用クリップ配列のインデックス。</summary>
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
    /// <summary><paramref name="Degrees"/>に等しいグリッド値を持つ角度の方向（度数。0が前方、正はRightクリップ側）。±180度はどちらも後方。</summary>
    public static int OfAngle(float Degrees)=>(((int)MathF.Round(Degrees/45f))%Count+Count)%Count;
}
/// <summary>
/// Unreal ThirdPersonの素手用アニメーショングラフ（ABP_Unarmed、ADR-0066、ADR-0078/M0107）を状態機械として表す。
/// Main States機械にはLocomotion、Jump、Fall Loop、Landがある。LocomotionまたはLandから地面を離れる際、上向き速度が
/// <see cref="JumpVerticalSpeed"/>を超えていればJumpへ遷移（クロスフェード0.15秒）、そうでなければFall Loopへ遷移（0.4秒）。
/// Jumpのクリップ残り時間が<see cref="JumpToFallSeconds"/>（0.3秒）になるとFall Loopへ移る。JumpまたはFall Loopから着地するとLandへ遷移（0.1秒）。
/// Landのクリップ残り時間が<see cref="LandToLocomotionSeconds"/>（0.4秒）になるか、キャラクターが移動すべき状態になると（0.5秒）Locomotionへ戻る。
/// Locomotion自体も状態機械であり、Idleと歩行／走行ブレンド空間（<see cref="BlendSpace2D"/>。歩行／ジョギング速度での8方向と、速度0のIdle）を持つ。
/// <see cref="LocomotionInput.ShouldMove"/>がtrueならIdleからMoveへ遷移（0.2秒）、falseならIdleへ戻る（0.3秒）。
/// すべてのクロスフェードにはCubicカーブを使い、直前の出力姿勢のスナップショットから開始するため、別の遷移で中断されても画面上の姿勢から続く。
/// ブレンド空間のクリップは正規化された位相を共有し、重み付きクリップ長に応じて進むため、ブレンド中も足並みが保たれる。
/// </summary>
public sealed class LocomotionStateMachine {
    /// <summary>地面を離れる際にJumpではなくFall Loopへ入る上向き速度のしきい値（m/s）。Velocity.Z &gt; 100 cm/s。</summary>
    public const float JumpVerticalSpeed=1f;
    /// <summary>Jumpクリップの開始位置（Jump状態のStartPosition）、秒単位。</summary>
    public const float JumpStartSeconds=0.08f;
    public const float ToJumpBlendSeconds=0.15f;
    public const float ToFallBlendSeconds=0.4f;
    public const float JumpToFallSeconds=0.3f;
    public const float ToLandBlendSeconds=0.1f;
    public const float LandToLocomotionSeconds=0.4f;
    public const float LandToMoveBlendSeconds=0.5f;
    public const float IdleToMoveBlendSeconds=0.2f;
    public const float MoveToIdleBlendSeconds=0.3f;
    /// <summary>ブレンド空間の目標ウェイトを補間する速度（毎秒）。</summary>
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
    /// <summary>Locomotion状態が現在でなくても保持される、その内側の状態。</summary>
    public LocomotionMoveState MoveState { get; private set; }
    /// <summary>現在のメイン状態へ入ってからの経過秒数。</summary>
    public float StateTime { get; private set; }
    /// <summary>ブレンド空間の現在の（平滑化済み）サンプルウェイト。</summary>
    public ReadOnlySpan<float>BlendWeights=>this.BlendSpace.Weights;
    public int BlendSampleCount=>this.BlendSpace.SampleCount;
    /// <summary>ブレンド空間のクリップが共有する正規化済み位相（0～1）。</summary>
    public float BlendPhase=>this.Phase;
    /// <summary><paramref name="DeltaSeconds"/>分だけ進め、ローカル姿勢を書き込む。</summary>
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
                // ブレンド空間プレイヤーは入るたびに初期化する。ウェイトは前回実行時の値ではなく、最初のクエリの値から始める。
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
    /// <summary>共有位相と平滑化済みウェイトを進め、ウェイトに応じてクリップをミックスする。</summary>
    private void EvaluateBlendSpace(float DeltaSeconds,float Direction,float Speed,Span<JointPose>Pose){
        this.BlendSpace.Update(DeltaSeconds,Direction,Speed);
        var Length=this.GatherClipWeights();
        if(Length>0f){
            this.Phase=(this.Phase+(DeltaSeconds/Length))%1f;
        }
        this.MixClips(Pose);
    }
    /// <summary>現在の位相における指定入力のブレンド空間姿勢を、スナップ済み（平滑化なし）のウェイトで返す。状態は進めない。</summary>
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
    /// <summary>平滑化済みのサンプルウェイトをクリップごとに合計する（同じクリップが複数サンプルを占めることもある）。その加重長を返す。</summary>
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
