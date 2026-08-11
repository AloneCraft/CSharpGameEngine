using System.Numerics;

namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// The one <see cref="ITransformConvention"/> implementation shipped this milestone: Scale then
/// Rotation then Translation composition, row-vector convention (matches the
/// <see cref="System.Numerics.Matrix4x4"/> usage already established elsewhere in this repo, e.g.
/// <c>UserGameProject/Program.cs</c>'s <c>Matrix4x4.CreateFromQuaternion(rotation) *
/// Matrix4x4.CreateTranslation(position)</c>). Parent/child combination is <c>local * parentWorld</c>
/// (child-then-parent, consistent with the same row-vector convention). Independent of which
/// <see cref="ICoordinateConvention"/> preset is active - Transform Convention and Coordinate
/// Convention are orthogonal axes (see <see cref="ITransformConvention"/>).
/// </summary>
public readonly struct StandardTransformConvention : ITransformConvention
{
    public static Matrix4x4 Compose(in RufVector Position, in RufQuaternion Rotation, in RufVector Scale)
    {
        Matrix4x4 ScaleMatrix = Matrix4x4.CreateScale(Scale.R, Scale.U, Scale.F);
        Matrix4x4 RotationMatrix = Matrix4x4.CreateFromQuaternion(new Quaternion(Rotation.R, Rotation.U, Rotation.F, Rotation.W));
        Matrix4x4 TranslationMatrix = Matrix4x4.CreateTranslation(Position.R, Position.U, Position.F);
        return ScaleMatrix * RotationMatrix * TranslationMatrix;
    }

    public static Matrix4x4 Combine(in Matrix4x4 ParentWorld, in Matrix4x4 Local) => Local * ParentWorld;

    public static RufVector TransformPoint(in Matrix4x4 Transform, in RufVector Point)
    {
        Vector3 Transformed = Vector3.Transform(new Vector3(Point.R, Point.U, Point.F), Transform);
        return new RufVector(Transformed.X, Transformed.Y, Transformed.Z);
    }

    /// <summary>Applies the linear (rotation + scale) part only - no translation.</summary>
    public static RufVector TransformDirection(in Matrix4x4 Transform, in RufVector Direction)
    {
        Vector3 Transformed = Vector3.TransformNormal(new Vector3(Direction.R, Direction.U, Direction.F), Transform);
        return new RufVector(Transformed.X, Transformed.Y, Transformed.Z);
    }

    /// <summary>
    /// Applies the inverse-transpose of the linear part, so non-uniform scale does not tilt the
    /// normal off the surface (the standard graphics-correct normal transform - deliberately not
    /// the same code path as <see cref="TransformDirection"/>, see
    /// Docs/Specifications/Spatial-RUF.md "コード品質": Point/Direction/Normal must not share one
    /// transform function).
    /// </summary>
    public static RufVector TransformNormal(in Matrix4x4 Transform, in RufVector Normal)
    {
        if (!Matrix4x4.Invert(Transform, out Matrix4x4 Inverse))
        {
            throw new SpatialConfigurationException($"TransformNormal requires an invertible matrix, but {Transform} is singular.");
        }

        Matrix4x4 InverseTranspose = Matrix4x4.Transpose(Inverse);
        Vector3 Transformed = Vector3.TransformNormal(new Vector3(Normal.R, Normal.U, Normal.F), InverseTranspose);
        if (Transformed == Vector3.Zero)
        {
            return RufVector.Zero;
        }

        Vector3 Normalized = Vector3.Normalize(Transformed);
        return new RufVector(Normalized.X, Normalized.Y, Normalized.Z);
    }
}
