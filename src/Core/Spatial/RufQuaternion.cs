using System.Numerics;

namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// <see cref="RufVector"/> 値の回転。Axis-angle、合成、LookRotation は (R, U, F) の成分 3 つに直接定義し、クォータニオン計算の目的上、内部の右手系基底として扱う。どの <see cref="ICoordinateConvention"/> が宣言する左右系からも意図的に独立させる (回転の計算と座標系の左右系を分離する理由は Docs/Specifications/Spatial-RUF.md の「Quaternion仕様」を参照。左右系が影響するのは、ベクトルの R/U/F の値を物理 X/Y/Z へ対応付ける方法だけであり、RUF 空間内で回転を合成する方法には影響しない)。
/// <see cref="FromAxisAngle"/> の正の回転は、R/U/F の成分 3 つ上で評価したとき、指定軸周りの右手系の法則に従う。
/// </summary>
public readonly struct RufQuaternion(float R,float U,float F,float W):IEquatable<RufQuaternion>{
    public float R { get;}=R;
    public float U { get;}=U;
    public float F { get;}=F;
    public float W { get;}=W;

    public static readonly RufQuaternion Identity=new(0f,0f,0f,1f);

    public static RufQuaternion FromAxisAngle(RufVector Axis,float AngleRadians){
        RufVector NormalizedAxis=Axis.Normalize();
        Quaternion Q=Quaternion.CreateFromAxisAngle(new Vector3(NormalizedAxis.R,NormalizedAxis.U,NormalizedAxis.F),AngleRadians);
        return FromSystemQuaternion(Q);
    }

    /// <summary>
    /// 親子階層の回転を合成する。<c>parent * child</c>では、最初に親のローカル座標系で<paramref name="Child"/>の回転を適用し、
    /// 次に<paramref name="Parent"/>の回転を適用する。つまり<c>RotateVector(parent * child, v) ==
    /// RotateVector(parent, RotateVector(child, v))</c>（<c>RufQuaternionTests.Multiply_AppliesChildThenParent</c>で検証済み）。
    /// </summary>
    public static RufQuaternion operator*(RufQuaternion Parent,RufQuaternion Child)
        =>FromSystemQuaternion(Parent.ToSystemQuaternion()*Child.ToSystemQuaternion());

    public static RufVector RotateVector(in RufQuaternion Rotation,in RufVector Vector){
        Vector3 Rotated=Vector3.Transform(new Vector3(Vector.R,Vector.U,Vector.F),Rotation.ToSystemQuaternion());
        return new RufVector(Rotated.X,Rotated.Y,Rotated.Z);
    }

    /// <summary>
    /// ローカルForwardが<paramref name="Forward"/>を向き、ローカルUpが可能な限り<paramref name="Up"/>に近づく回転を構築する
    ///（グラム・シュミット直交化）。<paramref name="Forward"/>と<paramref name="Up"/>が平行またはほぼ平行、または
    /// <paramref name="Up"/>がゼロ／有限値でない場合は、決定的な基準Upを使う（<see cref="RufVector.Up"/>、または
    /// <paramref name="Forward"/>自体が<see cref="RufVector.Up"/>と平行なら<see cref="RufVector.Right"/>）。
    /// 同じ入力には常に同じ結果を返し、NaNにはならない。ただしフォールバック境界をまたいで連続する保証はない。
    /// <paramref name="Forward"/>の長さがゼロまたは有限値でない場合は、<see cref="SpatialConfigurationException"/>をスローする。
    /// </summary>
    public static RufQuaternion LookRotation(RufVector Forward,RufVector Up){
        if(Forward.LengthSquared<=0f||!float.IsFinite(Forward.LengthSquared)){
            throw new SpatialConfigurationException("LookRotation requires a non-zero, finite forward vector.");
        }

        RufVector NormalizedForward=Forward.Normalize();
        RufVector ReferenceUp=ResolveReferenceUp(NormalizedForward,Up);

        RufVector Right=RufVector.RawCross(ReferenceUp,NormalizedForward).Normalize();
        RufVector OrthogonalUp=RufVector.RawCross(NormalizedForward,Right).Normalize();

        Matrix4x4 Basis=new(
            Right.R,Right.U,Right.F,0f,
            OrthogonalUp.R,OrthogonalUp.U,OrthogonalUp.F,0f,
            NormalizedForward.R,NormalizedForward.U,NormalizedForward.F,0f,
            0f,0f,0f,1f);

        return FromSystemQuaternion(Quaternion.CreateFromRotationMatrix(Basis));
    }

    private static RufVector ResolveReferenceUp(RufVector NormalizedForward,RufVector Up){
        const float ParallelThreshold=0.9999f;

        var UpIsUsable=Up.LengthSquared>0f&&float.IsFinite(Up.LengthSquared)
            &&MathF.Abs(RufVector.Dot(NormalizedForward,Up.Normalize()))<=ParallelThreshold;

        if(UpIsUsable){
            return Up.Normalize();
        }

        var ForwardParallelToCanonicalUp=MathF.Abs(RufVector.Dot(NormalizedForward,RufVector.Up))>ParallelThreshold;
        return ForwardParallelToCanonicalUp?RufVector.Right:RufVector.Up;
    }

    private Quaternion ToSystemQuaternion()=>new(this.R, this.U, this.F, this.W);

    private static RufQuaternion FromSystemQuaternion(Quaternion Q)=>new(Q.X,Q.Y,Q.Z,Q.W);

    public bool Equals(RufQuaternion Other)=> this.R.Equals(Other.R)&& this.U.Equals(Other.U)&& this.F.Equals(Other.F)&& this.W.Equals(Other.W);

    public override bool Equals(object?Obj)=>Obj is RufQuaternion Other&& this.Equals(Other);

    public override int GetHashCode()=>HashCode.Combine(this.R, this.U, this.F, this.W);

    public static bool operator==(RufQuaternion Left,RufQuaternion Right)=>Left.Equals(Right);

    public static bool operator!=(RufQuaternion Left,RufQuaternion Right)=>!Left.Equals(Right);

    public override string ToString()=>FormattableString.Invariant($"RufQuaternion({this.R}, {this.U}, {this.F}, {this.W})");
}
