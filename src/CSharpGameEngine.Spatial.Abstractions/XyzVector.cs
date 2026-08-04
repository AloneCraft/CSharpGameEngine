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

    public static XyzVector operator +(XyzVector left, XyzVector right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static XyzVector operator -(XyzVector left, XyzVector right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static XyzVector operator -(XyzVector value) => new(-value.X, -value.Y, -value.Z);

    public static XyzVector operator *(XyzVector value, float scalar) => new(value.X * scalar, value.Y * scalar, value.Z * scalar);

    public static XyzVector operator *(float scalar, XyzVector value) => value * scalar;

    public static XyzVector operator /(XyzVector value, float scalar) => new(value.X / scalar, value.Y / scalar, value.Z / scalar);

    public static float Dot(XyzVector left, XyzVector right) => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    /// <summary>
    /// Standard right-hand-rule cross product formula applied directly to the X/Y/Z components.
    /// This is the one unambiguous physical cross product in this codebase - see
    /// <see cref="Spatial{TConvention}.Cross"/> for the RUF-space, convention-aware counterpart.
    /// </summary>
    public static XyzVector Cross(XyzVector left, XyzVector right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

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
    public static explicit operator Vector3(XyzVector value) => new(value.X, value.Y, value.Z);

    public static explicit operator XyzVector(Vector3 value) => new(value.X, value.Y, value.Z);

    public bool Equals(XyzVector other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override bool Equals(object? obj) => obj is XyzVector other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public static bool operator ==(XyzVector left, XyzVector right) => left.Equals(right);

    public static bool operator !=(XyzVector left, XyzVector right) => !left.Equals(right);

    public override string ToString() => FormattableString.Invariant($"Xyz({X}, {Y}, {Z})");
}
