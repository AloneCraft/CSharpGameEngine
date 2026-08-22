namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// Semantic Right/Up/Forward coordinate - game-meaningful direction/position, independent of any
/// physical X/Y/Z axis mapping. <see cref="Right"/>/<see cref="Up"/>/<see cref="Forward"/> (and
/// their opposites) always mean the same thing regardless of which <see cref="ICoordinateConvention"/>
/// is active; only <see cref="ICoordinateConvention.ToXyz"/>/<see cref="ICoordinateConvention.FromXyz"/>
/// know how a given preset maps this to physical storage. Blittable, unmanaged, no heap allocation.
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
    /// Standard cross-product formula applied directly to the R/U/F components, treating them as
    /// an internal right-handed triple. This is intentionally internal: applied to arbitrary
    /// vectors it does NOT know about any <see cref="ICoordinateConvention"/>'s physical
    /// Handedness, so it must never be exposed as a general-purpose public API (see
    /// Docs/Specifications/Spatial-RUF.md "Cross Product仕様"). It exists only to build
    /// self-consistent local bases (e.g. <see cref="RufQuaternion.LookRotation"/>) where no
    /// external physical convention is involved.
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
