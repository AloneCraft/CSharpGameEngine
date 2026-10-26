using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// 一人称視点の腕のアニメーション（ABP_FP_Copy とコントロールリグ CtrlRig_FPWarp、ADR-0078/M0107）。ボディのメッシュの子となる
/// <see cref="SkeletalMesh"/> ノードに配置する。毎フレーム、ボディの最終姿勢をコピーしてから（UE の CopyPoseFromMesh における親へのアタッチと同じ処理）、腕
/// がカメラのすぐ前に来るよう変形する。リグに従い、背骨（<see cref="SpineStart"/> から <see cref="SpineEnd"/>）は FABRIK で
/// <see cref="BodyOffset"/> の方向へ半分の重みで引き、胸部がカメラを遮らないようにする。頭はコントロール <c>Ctrl_Head</c> の位置に置く。各上腕はアニメーション時
/// の向きを維持したまま肩コントロール（<c>Ctrl_Shoulder_L</c> / <c>_R</c>）の位置へ移動し、前腕と手は剛体的に追従させる。3つのコントロールはリグから測定した基準姿勢（
/// M0107）の頭と上腕に正確に一致するため、スケルトンの基準姿勢をコントロール位置の代わりに使う。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,5)]
public struct FirstPersonPose() {
    public string SpineStart="spine_01";
    public string SpineEnd="spine_04";
    public string Head="head";
    public string UpperArmLeft="upperarm_l";
    public string UpperArmRight="upperarm_r";
    /// <summary><c>BodyOffset</c> コントロール。メッシュのモデル空間で背骨の末端を引く位置（m）。UE の値は (0, -3.586, 109.51) cm。</summary>
    public LogicalVector3 BodyOffset=new(0f,1.0951f,0.03586f);
    /// <summary>FABRIK ノードの Weight。計算した背骨の姿勢でアニメーション姿勢を置き換える割合。</summary>
    public float SpineWeight=0.5f;
    /// <summary>FABRIKノードのMaxIterations（最大反復回数）。</summary>
    public int Iterations=10;
    /// <summary>FABRIK ノードの Precision（m）。UE の値は1 cm。</summary>
    public float Precision=0.01f;
}
public struct FirstPersonPoseRuntime:IRuntimeComponent<FirstPersonPose> {
    public FirstPersonRig?Rig;
    public JointPose[]?Pose;
    public Matrix4x4[]?Scratch;
    public string?Error;
}
/// <summary>1つのスケルトンに対する <see cref="FirstPersonPose"/> の計算結果。</summary>
public sealed class FirstPersonRig {
    private readonly Skeleton Skeleton;
    private readonly int[]Chain;
    private readonly int Head;
    private readonly int[]Arms;
    private readonly Matrix4x4[]RestModel;
    private readonly Vector3 BodyOffset;
    private readonly float SpineWeight;
    private readonly int Iterations;
    private readonly float Precision;
    private readonly bool[]Moved;
    public FirstPersonRig(Skeleton Skeleton,in FirstPersonPose Settings){
        ArgumentNullException.ThrowIfNull(Skeleton);
        this.Skeleton=Skeleton;
        var Start=Find(Skeleton,Settings.SpineStart);
        var End=Find(Skeleton,Settings.SpineEnd);
        // 背骨は、末端の関節から開始関節まで親をたどった連鎖。
        List<int>Joints=[End];
        for(var Joint=End;Joint!=Start;){
            Joint=Skeleton.ParentOf(Joint);
            if(Joint<0){
                throw new ArgumentException($"'{Settings.SpineStart}' is not an ancestor of '{Settings.SpineEnd}'.");
            }
            Joints.Add(Joint);
        }
        Joints.Reverse();
        this.Chain=[.. Joints];
        this.Head=Find(Skeleton,Settings.Head);
        this.Arms=[Find(Skeleton,Settings.UpperArmLeft),Find(Skeleton,Settings.UpperArmRight)];
        this.RestModel=new Matrix4x4[Skeleton.JointCount];
        Skeleton.ComputeModel(Skeleton.Rest,this.RestModel);
        this.BodyOffset=Settings.BodyOffset.TryToSingle(out var Offset)?Offset:Vector3.Zero;
        this.SpineWeight=float.IsFinite(Settings.SpineWeight)?Math.Clamp(Settings.SpineWeight,0f,1f):0f;
        this.Iterations=Math.Max(Settings.Iterations,0);
        this.Precision=float.IsFinite(Settings.Precision)?Math.Max(Settings.Precision,1e-6f):0.01f;
        this.Moved=new bool[Skeleton.JointCount];
    }
    /// <summary>
    /// コピーしたボディの最終ローカル姿勢である <paramref name="Local"/> を変形する。まず背骨をボディオフセットへ向け、その後、頭と上腕をコントロール位置へ合わせる。
    /// <paramref name="Scratch"/> には <see cref="Skeleton.JointCount"/> 個以上の行列を格納できる領域が必要。
    /// </summary>
    public void Apply(Span<JointPose>Local,Span<Matrix4x4>Scratch){
        var Count=this.Skeleton.JointCount;
        if(Scratch.Length<Count||Local.Length<Count){
            throw new ArgumentException($"The skeleton has {Count} joints.");
        }
        var Model=Scratch[..Count];
        this.Skeleton.ComputeModel(Local,Model);
        Array.Clear(this.Moved);
        this.PullSpine(Model);
        // Ctrl_Head：頭部全体の変換。頭より下の関節も一緒に移動する。
        this.Place(Model,this.Head,this.RestModel[this.Head]);
        // Ctrl_Shoulder_L / _R：平行移動のみ。上腕の向きは維持し、前腕と手は一緒に移動する。
        foreach(var Arm in this.Arms){
            var Placed=Model[Arm];
            Placed.Translation=this.RestModel[Arm].Translation;
            this.Place(Model,Arm,Placed);
        }
        this.WriteBack(Local,Model);
    }
    /// <summary>最初の関節を固定して背骨に沿って FABRIK を実行し、その後、各骨を次の関節の新しい位置へ向ける。骨より下の関節も追従する。</summary>
    private void PullSpine(Span<Matrix4x4>Model){
        if(this.SpineWeight<=0f||this.Chain.Length<2){
            return;
        }
        var Count=this.Chain.Length;
        var Original=new Vector3[Count];
        var Solved=new Vector3[Count];
        var Lengths=new float[Count-1];
        for(var Index=0;Index<Count;Index++){
            Original[Index]=Model[this.Chain[Index]].Translation;
            Solved[Index]=Original[Index];
        }
        var Reach=0f;
        for(var Index=0;Index<Count-1;Index++){
            Lengths[Index]=Vector3.Distance(Original[Index],Original[Index+1]);
            Reach+=Lengths[Index];
        }
        var Root=Original[0];
        var Target=this.BodyOffset;
        if(Vector3.Distance(Root,Target)>=Reach){
            // 目標に届かない場合は、関節の連鎖を目標へ向けてまっすぐ伸ばす。
            var Direction=Vector3.Normalize(Target-Root);
            for(var Index=1;Index<Count;Index++){
                Solved[Index]=Solved[Index-1]+(Direction*Lengths[Index-1]);
            }
        }
        else {
            for(var Iteration=0;Iteration<this.Iterations&&Vector3.Distance(Solved[Count-1],Target)>this.Precision;Iteration++){
                Solved[Count-1]=Target;
                for(var Index=Count-2;Index>=0;Index--){
                    Solved[Index]=Solved[Index+1]+(Vector3.Normalize(Solved[Index]-Solved[Index+1])*Lengths[Index]);
                }
                Solved[0]=Root;
                for(var Index=1;Index<Count;Index++){
                    Solved[Index]=Solved[Index-1]+(Vector3.Normalize(Solved[Index]-Solved[Index-1])*Lengths[Index-1]);
                }
            }
        }
        for(var Index=1;Index<Count;Index++){
            Solved[Index]=Vector3.Lerp(Original[Index],Solved[Index],this.SpineWeight);
        }
        // 各骨を自身の位置を中心に回転させ、次の関節の新しい位置へ向ける。下位の階層も追従するため、次の骨はすでに正しい位置にある。
        for(var Index=0;Index<Count-1;Index++){
            var Pivot=Model[this.Chain[Index]].Translation;
            var Current=Model[this.Chain[Index+1]].Translation-Pivot;
            var Wanted=Solved[Index+1]-Solved[Index];
            if(Current.LengthSquared()<1e-10f||Wanted.LengthSquared()<1e-10f){
                continue;
            }
            var Turn=Matrix4x4.CreateTranslation(-Pivot)*TwoBoneIk.FromTo(Vector3.Normalize(Current),Vector3.Normalize(Wanted))*Matrix4x4.CreateTranslation(Pivot);
            this.Transform(Model,this.Chain[Index],Turn,IncludeRoot:true);
        }
    }
    /// <summary><paramref name="Joint"/> のモデル行列を <paramref name="Placed"/> に置き換え、UE の bPropagateToChildren と同じく、その下の階層すべてを同じように移動する。</summary>
    private void Place(Span<Matrix4x4>Model,int Joint,Matrix4x4 Placed){
        if(!Matrix4x4.Invert(Model[Joint],out var Inverse)){
            return;
        }
        this.Transform(Model,Joint,Inverse*Placed,IncludeRoot:true);
    }
    /// <summary><paramref name="Joint"/> とその下の全関節に、モデル空間変換（行ベクトル規約。関節の行列の後に適用）を行う。</summary>
    private void Transform(Span<Matrix4x4>Model,int Joint,Matrix4x4 Change,bool IncludeRoot){
        foreach(var Candidate in this.Skeleton.ParentsFirstOrder){
            if((Candidate==Joint&&IncludeRoot)||(Candidate!=Joint&&this.IsDescendant(Candidate,Joint))){
                Model[Candidate]=Model[Candidate]*Change;
                this.Moved[Candidate]=true;
            }
        }
    }
    private bool IsDescendant(int Joint,int Ancestor){
        for(var Parent=this.Skeleton.ParentOf(Joint);Parent>=0;Parent=this.Skeleton.ParentOf(Parent)){
            if(Parent==Ancestor){
                return true;
            }
        }
        return false;
    }
    /// <summary>変更後のモデル行列をローカルの平行移動と回転に戻す（スケールはアニメーション値のまま）。</summary>
    private void WriteBack(Span<JointPose>Local,ReadOnlySpan<Matrix4x4>Model){
        foreach(var Joint in this.Skeleton.ParentsFirstOrder){
            if(!this.Moved[Joint]){
                continue;
            }
            var Parent=this.Skeleton.ParentOf(Joint);
            var ParentModel=Parent<0?this.Skeleton.RootTransform:Model[Parent];
            if(!Matrix4x4.Invert(ParentModel,out var ParentInverse)){
                continue;
            }
            var Relative=Model[Joint]*ParentInverse;
            Local[Joint].Translation=Relative.Translation;
            Local[Joint].Rotation=TwoBoneIk.ToQuaternion(TwoBoneIk.RotationOf(Relative));
        }
    }
    private static int Find(Skeleton Skeleton,string Name){
        var Joint=Skeleton.IndexOf(Name??string.Empty);
        if(Joint<0){
            throw new ArgumentException($"The skeleton has no joint named '{Name}'.");
        }
        return Joint;
    }
}
