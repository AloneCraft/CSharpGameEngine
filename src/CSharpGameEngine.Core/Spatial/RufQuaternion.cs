using System.Numerics;

namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// Rotation of <see cref="RufVector"/> values. Axis-angle/composition/LookRotation are defined
/// directly on the (R, U, F) component triple, treated as an internal right-handed basis for the
/// purposes of quaternion math - deliberately independent of any <see cref="ICoordinateConvention"/>'s
/// declared Handedness (see Docs/Specifications/Spatial-RUF.md "Quaternion仕様" for why rotation
/// math and coordinate Handedness are kept separate: Handedness only affects how a vector's R/U/F
/// numbers map to physical X/Y/Z, not how one RUF-space rotation composes with another).
/// Positive <see cref="FromAxisAngle"/> rotation follows the right-hand rule around the given
/// axis, evaluated on the R/U/F triple.
/// </summary>
public readonly struct RufQuaternion : IEquatable<RufQuaternion>
{
    public float R { get; }
    public float U { get; }
    public float F { get; }
    public float W { get; }

    public RufQuaternion(float r, float u, float f, float w)
    {
        R = r;
        U = u;
        F = f;
        W = w;
    }

    public static readonly RufQuaternion Identity = new(0f, 0f, 0f, 1f);

    public static RufQuaternion FromAxisAngle(RufVector axis, float angleRadians)
    {
        RufVector normalizedAxis = axis.Normalize();
        Quaternion q = Quaternion.CreateFromAxisAngle(new Vector3(normalizedAxis.R, normalizedAxis.U, normalizedAxis.F), angleRadians);
        return FromSystemQuaternion(q);
    }

    /// <summary>
    /// Composes rotations for parent/child hierarchies: <c>parent * child</c> applies
    /// <paramref name="child"/>'s rotation first (in the parent's local frame), then
    /// <paramref name="parent"/>'s - i.e. <c>RotateVector(parent * child, v) ==
    /// RotateVector(parent, RotateVector(child, v))</c> (verified by
    /// <c>RufQuaternionTests.Multiply_AppliesChildThenParent</c>).
    /// </summary>
    public static RufQuaternion operator *(RufQuaternion parent, RufQuaternion child)
        => FromSystemQuaternion(parent.ToSystemQuaternion() * child.ToSystemQuaternion());

    public static RufVector RotateVector(in RufQuaternion rotation, in RufVector vector)
    {
        Vector3 rotated = Vector3.Transform(new Vector3(vector.R, vector.U, vector.F), rotation.ToSystemQuaternion());
        return new RufVector(rotated.X, rotated.Y, rotated.Z);
    }

    /// <summary>
    /// Builds a rotation whose local Forward maps to <paramref name="forward"/> and whose local Up
    /// is as close as possible to <paramref name="up"/> (Gram-Schmidt orthogonalized). If
    /// <paramref name="forward"/> and <paramref name="up"/> are parallel or near-parallel (or
    /// <paramref name="up"/> is zero/non-finite), falls back to a deterministic reference Up
    /// (<see cref="RufVector.Up"/>, or <see cref="RufVector.Right"/> if <paramref name="forward"/>
    /// itself is parallel to <see cref="RufVector.Up"/>) - always the same result for the same
    /// input, never NaN, but not guaranteed continuous across the fallback boundary. Throws
    /// <see cref="SpatialConfigurationException"/> if <paramref name="forward"/> is zero-length or
    /// non-finite.
    /// </summary>
    public static RufQuaternion LookRotation(RufVector forward, RufVector up)
    {
        if (forward.LengthSquared <= 0f || !float.IsFinite(forward.LengthSquared))
        {
            throw new SpatialConfigurationException("LookRotation requires a non-zero, finite forward vector.");
        }

        RufVector normalizedForward = forward.Normalize();
        RufVector referenceUp = ResolveReferenceUp(normalizedForward, up);

        RufVector right = RufVector.RawCross(referenceUp, normalizedForward).Normalize();
        RufVector orthogonalUp = RufVector.RawCross(normalizedForward, right).Normalize();

        Matrix4x4 basis = new(
            right.R, right.U, right.F, 0f,
            orthogonalUp.R, orthogonalUp.U, orthogonalUp.F, 0f,
            normalizedForward.R, normalizedForward.U, normalizedForward.F, 0f,
            0f, 0f, 0f, 1f);

        return FromSystemQuaternion(Quaternion.CreateFromRotationMatrix(basis));
    }

    private static RufVector ResolveReferenceUp(RufVector normalizedForward, RufVector up)
    {
        const float parallelThreshold = 0.9999f;

        bool upIsUsable = up.LengthSquared > 0f && float.IsFinite(up.LengthSquared)
            && MathF.Abs(RufVector.Dot(normalizedForward, up.Normalize())) <= parallelThreshold;

        if (upIsUsable)
        {
            return up.Normalize();
        }

        bool forwardParallelToCanonicalUp = MathF.Abs(RufVector.Dot(normalizedForward, RufVector.Up)) > parallelThreshold;
        return forwardParallelToCanonicalUp ? RufVector.Right : RufVector.Up;
    }

    private Quaternion ToSystemQuaternion() => new(R, U, F, W);

    private static RufQuaternion FromSystemQuaternion(Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    public bool Equals(RufQuaternion other) => R.Equals(other.R) && U.Equals(other.U) && F.Equals(other.F) && W.Equals(other.W);

    public override bool Equals(object? obj) => obj is RufQuaternion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(R, U, F, W);

    public static bool operator ==(RufQuaternion left, RufQuaternion right) => left.Equals(right);

    public static bool operator !=(RufQuaternion left, RufQuaternion right) => !left.Equals(right);

    public override string ToString() => FormattableString.Invariant($"RufQuaternion({R}, {U}, {F}, {W})");
}
