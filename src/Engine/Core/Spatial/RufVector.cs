namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// 意味上のRight／Up／Forward座標。ゲーム内で意味を持つ方向／位置を表し、物理的なX／Y／Z軸の対応付けには依存しない。
/// 有効な<see cref="ICoordinateConvention"/>にかかわらず、<see cref="Right"/>／<see cref="Up"/>／<see cref="Forward"/>とその反対方向は
/// 常に同じ意味を持つ。各プリセットでこれを物理ストレージへどう対応付けるかを知るのは、
/// <see cref="ICoordinateConvention.ToXyz"/>／<see cref="ICoordinateConvention.FromXyz"/>だけである。
/// Blittableなアンマネージ型で、ヒープ割り当ては発生しない。
/// </summary>
public readonly struct RufVector(float R,float U,float F):IEquatable<RufVector>{
    public float R { get;}=R;
    public float U { get;}=U;
    public float F { get;}=F;

    public static readonly RufVector Zero=new(0f,0f,0f);
    public static readonly RufVector Right=new(1f,0f,0f);
    public static readonly RufVector Left=new(-1f,0f,0f);
    public static readonly RufVector Up=new(0f,1f,0f);
    public static readonly RufVector Down=new(0f,-1f,0f);
    public static readonly RufVector Forward=new(0f,0f,1f);
    public static readonly RufVector Backward=new(0f,0f,-1f);

    public float LengthSquared=>(this.R* this.R)+(this.U* this.U)+(this.F* this.F);

    public float Length=>MathF.Sqrt(this.LengthSquared);

    public static RufVector operator+(RufVector Left,RufVector Right)=>new(Left.R+Right.R,Left.U+Right.U,Left.F+Right.F);

    public static RufVector operator-(RufVector Left,RufVector Right)=>new(Left.R-Right.R,Left.U-Right.U,Left.F-Right.F);

    public static RufVector operator-(RufVector Value)=>new(-Value.R,-Value.U,-Value.F);

    public static RufVector operator*(RufVector Value,float Scalar)=>new(Value.R*Scalar,Value.U*Scalar,Value.F*Scalar);

    public static RufVector operator*(float Scalar,RufVector Value)=>Value*Scalar;

    public static RufVector operator/(RufVector Value,float Scalar)=>new(Value.R/Scalar,Value.U/Scalar,Value.F/Scalar);

    public static float Dot(RufVector Left,RufVector Right)=>(Left.R*Right.R)+(Left.U*Right.U)+(Left.F*Right.F);

    public RufVector Normalize(){
        var Length= this.Length;
        if(Length <= 0f||!float.IsFinite(Length)){
            throw new SpatialConfigurationException($"Cannot normalize a zero-length or non-finite {nameof(RufVector)}: {this}.");
        }

        return this/Length;
    }

    /// <summary>
    /// R/U/F 成分に直接標準の外積公式を適用し、それらを内部の右手系 3 成分として扱う。これは意図的に internal とする。任意のベクトルに適用しても <see cref="ICoordinateConvention"/> が宣言する物理的な左右系を考慮しないため、汎用の public API として公開してはならない (Docs/Specifications/Spatial-RUF.md の「Cross Product仕様」を参照)。外部の物理規約が関与しない、自己整合したローカル基底（例：<see cref="RufQuaternion.LookRotation"/>）を作る場合にのみ使用する。
    /// </summary>
    internal static RufVector RawCross(RufVector Left,RufVector Right)=>new(
        (Left.U*Right.F)-(Left.F*Right.U),
        (Left.F*Right.R)-(Left.R*Right.F),
        (Left.R*Right.U)-(Left.U*Right.R));

    public bool Equals(RufVector Other)=> this.R.Equals(Other.R)&& this.U.Equals(Other.U)&& this.F.Equals(Other.F);

    public override bool Equals(object?Obj)=>Obj is RufVector Other&& this.Equals(Other);

    public override int GetHashCode()=>HashCode.Combine(this.R, this.U, this.F);

    public static bool operator==(RufVector Left,RufVector Right)=>Left.Equals(Right);

    public static bool operator!=(RufVector Left,RufVector Right)=>!Left.Equals(Right);

    public override string ToString()=>FormattableString.Invariant($"Ruf({this.R}, {this.U}, {this.F})");
}
