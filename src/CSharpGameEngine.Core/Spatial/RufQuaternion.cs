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

    public static RufQuaternion FromAxisAngle(RufVector Axis, float AngleRadians)
    {
        RufVector NormalizedAxis = Axis.Normalize();
        Quaternion Q = Quaternion.CreateFromAxisAngle(new Vector3(NormalizedAxis.R, NormalizedAxis.U, NormalizedAxis.F), AngleRadians);
        return FromSystemQuaternion(Q);
    }

    /// <summary>
    /// Composes rotations for parent/child hierarchies: <c>parent * child</c> applies
    /// <paramref name="Child"/>'s rotation first (in the parent's local frame), then
    /// <paramref name="Parent"/>'s - i.e. <c>RotateVector(parent * child, v) ==
    /// RotateVector(parent, RotateVector(child, v))</c> (verified by
    /// <c>RufQuaternionTests.Multiply_AppliesChildThenParent</c>).
    /// </summary>
    public static RufQuaternion operator *(RufQuaternion Parent, RufQuaternion Child)
        => FromSystemQuaternion(Parent.ToSystemQuaternion() * Child.ToSystemQuaternion());

    public static RufVector RotateVector(in RufQuaternion Rotation, in RufVector vector)
    {
        Vector3 Rotated = Vector3.Transform(new Vector3(vector.R, vector.U, vector.F), Rotation.ToSystemQuaternion());
        return new RufVector(Rotated.X, Rotated.Y, Rotated.Z);
    }

    /// <summary>
    /// Builds a rotation whose local Forward maps to <paramref name="Forward"/> and whose local Up
    /// is as close as possible to <paramref name="Up"/> (Gram-Schmidt orthogonalized). If
    /// <paramref name="Forward"/> and <paramref name="Up"/> are parallel or near-parallel (or
    /// <paramref name="Up"/> is zero/non-finite), falls back to a deterministic reference Up
    /// (<see cref="RufVector.Up"/>, or <see cref="RufVector.Right"/> if <paramref name="Forward"/>
    /// itself is parallel to <see cref="RufVector.Up"/>) - always the same result for the same
    /// input, never NaN, but not guaranteed continuous across the fallback boundary. Throws
    /// <see cref="SpatialConfigurationException"/> if <paramref name="Forward"/> is zero-length or
    /// non-finite.
    /// </summary>
    public static RufQuaternion LookRotation(RufVector Forward, RufVector Up)
    {
        if (Forward.LengthSquared <= 0f || !float.IsFinite(Forward.LengthSquared))
        {
            throw new SpatialConfigurationException("LookRotation requires a non-zero, finite forward vector.");
        }

        RufVector NormalizedForward = Forward.Normalize();
        RufVector ReferenceUp = ResolveReferenceUp(NormalizedForward, Up);

        RufVector Right = RufVector.RawCross(ReferenceUp, NormalizedForward).Normalize();
        RufVector OrthogonalUp = RufVector.RawCross(NormalizedForward, Right).Normalize();

        Matrix4x4 Basis = new(
            Right.R, Right.U, Right.F, 0f,
            OrthogonalUp.R, OrthogonalUp.U, OrthogonalUp.F, 0f,
            NormalizedForward.R, NormalizedForward.U, NormalizedForward.F, 0f,
            0f, 0f, 0f, 1f);

        return FromSystemQuaternion(Quaternion.CreateFromRotationMatrix(Basis));
    }

    private static RufVector ResolveReferenceUp(RufVector NormalizedForward, RufVector Up)
    {
        const float ParallelThreshold = 0.9999f;

        bool UpIsUsable = Up.LengthSquared > 0f && float.IsFinite(Up.LengthSquared)
            && MathF.Abs(RufVector.Dot(NormalizedForward, Up.Normalize())) <= ParallelThreshold;

        if (UpIsUsable)
        {
            return Up.Normalize();
        }

        bool ForwardParallelToCanonicalUp = MathF.Abs(RufVector.Dot(NormalizedForward, RufVector.Up)) > ParallelThreshold;
        return ForwardParallelToCanonicalUp ? RufVector.Right : RufVector.Up;
    }

    private Quaternion ToSystemQuaternion() => new(R, U, F, W);

    private static RufQuaternion FromSystemQuaternion(Quaternion Q) => new(Q.X, Q.Y, Q.Z, Q.W);

    public bool Equals(RufQuaternion Other) => R.Equals(Other.R) && U.Equals(Other.U) && F.Equals(Other.F) && W.Equals(Other.W);

    public override bool Equals(object? Obj) => Obj is RufQuaternion Other && Equals(Other);

    public override int GetHashCode() => HashCode.Combine(R, U, F, W);

    public static bool operator ==(RufQuaternion Left, RufQuaternion Right) => Left.Equals(Right);

    public static bool operator !=(RufQuaternion Left, RufQuaternion Right) => !Left.Equals(Right);

    public override string ToString() => FormattableString.Invariant($"RufQuaternion({R}, {U}, {F}, {W})");
}
