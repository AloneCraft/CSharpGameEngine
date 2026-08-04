namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// Semantic Right/Up/Forward coordinate - game-meaningful direction/position, independent of any
/// physical X/Y/Z axis mapping. <see cref="Right"/>/<see cref="Up"/>/<see cref="Forward"/> (and
/// their opposites) always mean the same thing regardless of which <see cref="ICoordinateConvention"/>
/// is active; only <see cref="ICoordinateConvention.ToXyz"/>/<see cref="ICoordinateConvention.FromXyz"/>
/// know how a given preset maps this to physical storage. Blittable, unmanaged, no heap allocation.
/// </summary>
public readonly struct RufVector : IEquatable<RufVector>
{
    public float R { get; }
    public float U { get; }
    public float F { get; }

    public RufVector(float r, float u, float f)
    {
        R = r;
        U = u;
        F = f;
    }

    public static readonly RufVector Zero = new(0f, 0f, 0f);
    public static readonly RufVector Right = new(1f, 0f, 0f);
    public static readonly RufVector Left = new(-1f, 0f, 0f);
    public static readonly RufVector Up = new(0f, 1f, 0f);
    public static readonly RufVector Down = new(0f, -1f, 0f);
    public static readonly RufVector Forward = new(0f, 0f, 1f);
    public static readonly RufVector Backward = new(0f, 0f, -1f);

    public float LengthSquared => (R * R) + (U * U) + (F * F);

    public float Length => MathF.Sqrt(LengthSquared);

    public static RufVector operator +(RufVector left, RufVector right) => new(left.R + right.R, left.U + right.U, left.F + right.F);

    public static RufVector operator -(RufVector left, RufVector right) => new(left.R - right.R, left.U - right.U, left.F - right.F);

    public static RufVector operator -(RufVector value) => new(-value.R, -value.U, -value.F);

    public static RufVector operator *(RufVector value, float scalar) => new(value.R * scalar, value.U * scalar, value.F * scalar);

    public static RufVector operator *(float scalar, RufVector value) => value * scalar;

    public static RufVector operator /(RufVector value, float scalar) => new(value.R / scalar, value.U / scalar, value.F / scalar);

    public static float Dot(RufVector left, RufVector right) => (left.R * right.R) + (left.U * right.U) + (left.F * right.F);

    public RufVector Normalize()
    {
        float length = Length;
        if (length <= 0f || !float.IsFinite(length))
        {
            throw new SpatialConfigurationException($"Cannot normalize a zero-length or non-finite {nameof(RufVector)}: {this}.");
        }

        return this / length;
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
    internal static RufVector RawCross(RufVector left, RufVector right) => new(
        (left.U * right.F) - (left.F * right.U),
        (left.F * right.R) - (left.R * right.F),
        (left.R * right.U) - (left.U * right.R));

    public bool Equals(RufVector other) => R.Equals(other.R) && U.Equals(other.U) && F.Equals(other.F);

    public override bool Equals(object? obj) => obj is RufVector other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(R, U, F);

    public static bool operator ==(RufVector left, RufVector right) => left.Equals(right);

    public static bool operator !=(RufVector left, RufVector right) => !left.Equals(right);

    public override string ToString() => FormattableString.Invariant($"Ruf({R}, {U}, {F})");
}
