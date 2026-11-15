using System.Numerics;

namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// 物理的なX／Y／Zストレージベクトル。「値がエンジンの選択した物理軸配置のどこに格納されるか」を表し、
/// ゲーム上の意味を持つ方向ではない。意味上の対応型は<see cref="RufVector"/>、両者の境界は<see cref="ICoordinateConvention"/>を参照。
/// Blittableなアンマネージ型で、ヒープ割り当ては発生しない。
/// </summary>
public readonly struct XyzVector(float X,float Y,float Z):IEquatable<XyzVector>{
    public float X { get;}=X;
    public float Y { get;}=Y;
    public float Z { get;}=Z;

    public static readonly XyzVector Zero=new(0f,0f,0f);

    public float LengthSquared=>(this.X* this.X)+(this.Y* this.Y)+(this.Z* this.Z);

    public float Length=>MathF.Sqrt(this.LengthSquared);

    public static XyzVector operator+(XyzVector Left,XyzVector Right)=>new(Left.X+Right.X,Left.Y+Right.Y,Left.Z+Right.Z);

    public static XyzVector operator-(XyzVector Left,XyzVector Right)=>new(Left.X-Right.X,Left.Y-Right.Y,Left.Z-Right.Z);

    public static XyzVector operator-(XyzVector Value)=>new(-Value.X,-Value.Y,-Value.Z);

    public static XyzVector operator*(XyzVector Value,float Scalar)=>new(Value.X*Scalar,Value.Y*Scalar,Value.Z*Scalar);

    public static XyzVector operator*(float Scalar,XyzVector Value)=>Value*Scalar;

    public static XyzVector operator/(XyzVector Value,float Scalar)=>new(Value.X/Scalar,Value.Y/Scalar,Value.Z/Scalar);

    public static float Dot(XyzVector Left,XyzVector Right)=>(Left.X*Right.X)+(Left.Y*Right.Y)+(Left.Z*Right.Z);

    /// <summary>
    /// 標準的な右手系の外積公式をX／Y／Z成分へ直接適用する。このコードベースで曖昧さのない物理外積はこれだけである。
    /// RUF空間で規約を考慮する対応処理については<see cref="Spatial{TConvention}.Cross"/>を参照。
    /// </summary>
    public static XyzVector Cross(XyzVector Left,XyzVector Right)=>new(
        (Left.Y*Right.Z)-(Left.Z*Right.Y),
        (Left.Z*Right.X)-(Left.X*Right.Z),
        (Left.X*Right.Y)-(Left.Y*Right.X));

    public XyzVector Normalize(){
        var Length= this.Length;
        if(Length <= 0f||!float.IsFinite(Length)){
            throw new SpatialConfigurationException($"Cannot normalize a zero-length or non-finite {nameof(XyzVector)}: {this}.");
        }

        return this/Length;
    }

    /// <summary>
    /// ECS チャンク、SIMD、レンダラーの内部で使う生の <see cref="Vector3"/> 格納型への明示的な（暗黙ではない）変換 (Docs/Specifications/Spatial-RUF.md の「境界の原則」を参照)。
    /// </summary>
    public static explicit operator Vector3(XyzVector Value)=>new(Value.X,Value.Y,Value.Z);

    public static explicit operator XyzVector(Vector3 Value)=>new(Value.X,Value.Y,Value.Z);

    public bool Equals(XyzVector Other)=> this.X.Equals(Other.X)&& this.Y.Equals(Other.Y)&& this.Z.Equals(Other.Z);

    public override bool Equals(object?Obj)=>Obj is XyzVector Other&& this.Equals(Other);

    public override int GetHashCode()=>HashCode.Combine(this.X, this.Y, this.Z);

    public static bool operator==(XyzVector Left,XyzVector Right)=>Left.Equals(Right);

    public static bool operator!=(XyzVector Left,XyzVector Right)=>!Left.Equals(Right);

    public override string ToString()=>FormattableString.Invariant($"Xyz({this.X}, {this.Y}, {this.Z})");
}
