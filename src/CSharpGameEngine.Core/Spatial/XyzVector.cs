using System.Numerics;

namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// Physical X/Y/Z storage vector - "where a value sits in the engine's chosen physical axis
/// layout", not a game-semantic direction. See <see cref="RufVector"/> for the semantic
/// counterpart and <see cref="ICoordinateConvention"/> for the boundary between the two.
/// Blittable, unmanaged, no heap allocation.
/// </summary>
public readonly struct XyzVector : IEquatable<XyzVector>
{
    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    public XyzVector(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly XyzVector Zero = new(0f, 0f, 0f);

    public float LengthSquared => (X * X) + (Y * Y) + (Z * Z);

    public float Length => MathF.Sqrt(LengthSquared);

    public static XyzVector operator +(XyzVector Left, XyzVector Right) => new(Left.X + Right.X, Left.Y + Right.Y, Left.Z + Right.Z);

    public static XyzVector operator -(XyzVector Left, XyzVector Right) => new(Left.X - Right.X, Left.Y - Right.Y, Left.Z - Right.Z);

    public static XyzVector operator -(XyzVector Value) => new(-Value.X, -Value.Y, -Value.Z);

    public static XyzVector operator *(XyzVector Value, float Scalar) => new(Value.X * Scalar, Value.Y * Scalar, Value.Z * Scalar);

    public static XyzVector operator *(float Scalar, XyzVector Value) => Value * Scalar;

    public static XyzVector operator /(XyzVector Value, float Scalar) => new(Value.X / Scalar, Value.Y / Scalar, Value.Z / Scalar);

    public static float Dot(XyzVector Left, XyzVector Right) => (Left.X * Right.X) + (Left.Y * Right.Y) + (Left.Z * Right.Z);

    /// <summary>
    /// Standard right-hand-rule cross product formula applied directly to the X/Y/Z components.
    /// This is the one unambiguous physical cross product in this codebase - see
    /// <see cref="Spatial{TConvention}.Cross"/> for the RUF-space, convention-aware counterpart.
    /// </summary>
    public static XyzVector Cross(XyzVector Left, XyzVector Right) => new(
        (Left.Y * Right.Z) - (Left.Z * Right.Y),
        (Left.Z * Right.X) - (Left.X * Right.Z),
        (Left.X * Right.Y) - (Left.Y * Right.X));

    public XyzVector Normalize()
    {
        float length = Length;
        if (length <= 0f || !float.IsFinite(length))
        {
            throw new SpatialConfigurationException($"Cannot normalize a zero-length or non-finite {nameof(XyzVector)}: {this}.");
        }

        return this / length;
    }

    /// <summary>
    /// Explicit (never implicit) conversion to the raw <see cref="Vector3"/> storage type used by
    /// ECS chunks/SIMD/renderer internals - see Docs/Specifications/Spatial-RUF.md "境界の原則".
    /// </summary>
    public static explicit operator Vector3(XyzVector Value) => new(Value.X, Value.Y, Value.Z);

    public static explicit operator XyzVector(Vector3 Value) => new(Value.X, Value.Y, Value.Z);

    public bool Equals(XyzVector Other) => X.Equals(Other.X) && Y.Equals(Other.Y) && Z.Equals(Other.Z);

    public override bool Equals(object? Obj) => Obj is XyzVector Other && Equals(Other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public static bool operator ==(XyzVector Left, XyzVector Right) => Left.Equals(Right);

    public static bool operator !=(XyzVector Left, XyzVector Right) => !Left.Equals(Right);

    public override string ToString() => FormattableString.Invariant($"Xyz({X}, {Y}, {Z})");
}
