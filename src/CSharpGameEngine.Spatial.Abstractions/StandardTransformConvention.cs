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
    public static Matrix4x4 Compose(in RufVector position, in RufQuaternion rotation, in RufVector scale)
    {
        Matrix4x4 scaleMatrix = Matrix4x4.CreateScale(scale.R, scale.U, scale.F);
        Matrix4x4 rotationMatrix = Matrix4x4.CreateFromQuaternion(new Quaternion(rotation.R, rotation.U, rotation.F, rotation.W));
        Matrix4x4 translationMatrix = Matrix4x4.CreateTranslation(position.R, position.U, position.F);
        return scaleMatrix * rotationMatrix * translationMatrix;
    }

    public static Matrix4x4 Combine(in Matrix4x4 parentWorld, in Matrix4x4 local) => local * parentWorld;

    public static RufVector TransformPoint(in Matrix4x4 transform, in RufVector point)
    {
        Vector3 transformed = Vector3.Transform(new Vector3(point.R, point.U, point.F), transform);
        return new RufVector(transformed.X, transformed.Y, transformed.Z);
    }

    /// <summary>Applies the linear (rotation + scale) part only - no translation.</summary>
    public static RufVector TransformDirection(in Matrix4x4 transform, in RufVector direction)
    {
        Vector3 transformed = Vector3.TransformNormal(new Vector3(direction.R, direction.U, direction.F), transform);
        return new RufVector(transformed.X, transformed.Y, transformed.Z);
    }

    /// <summary>
    /// Applies the inverse-transpose of the linear part, so non-uniform scale does not tilt the
    /// normal off the surface (the standard graphics-correct normal transform - deliberately not
    /// the same code path as <see cref="TransformDirection"/>, see
    /// Docs/Specifications/Spatial-RUF.md "コード品質": Point/Direction/Normal must not share one
    /// transform function).
    /// </summary>
    public static RufVector TransformNormal(in Matrix4x4 transform, in RufVector normal)
    {
        if (!Matrix4x4.Invert(transform, out Matrix4x4 inverse))
        {
            throw new SpatialConfigurationException($"TransformNormal requires an invertible matrix, but {transform} is singular.");
        }

        Matrix4x4 inverseTranspose = Matrix4x4.Transpose(inverse);
        Vector3 transformed = Vector3.TransformNormal(new Vector3(normal.R, normal.U, normal.F), inverseTranspose);
        if (transformed == Vector3.Zero)
        {
            return RufVector.Zero;
        }

        Vector3 normalized = Vector3.Normalize(transformed);
        return new RufVector(normalized.X, normalized.Y, normalized.Z);
    }
}
