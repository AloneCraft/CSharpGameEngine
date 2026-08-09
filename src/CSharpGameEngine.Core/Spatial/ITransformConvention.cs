using System.Numerics;

namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// Scale/Rotation/Translation composition, parent/child combination, and Point/Direction/Normal
/// application - kept as a separate contract from <see cref="ICoordinateConvention"/> on purpose
/// (see Docs/Specifications/Spatial-RUF.md "Transformとの分離"): whether RUF's R/U/F map to
/// physical +X/+Y/+Z is a different question from row-vs-column vectors, matrix multiplication
/// order, and how a parent world matrix combines with a local one. <see cref="Matrix4x4"/> here is
/// pure 16-float storage; the actual row/column-vector and multiplication-order convention lives
/// entirely inside a given implementation's method bodies, not in the type itself.
/// </summary>
public interface ITransformConvention
{
    static abstract Matrix4x4 Compose(in RufVector position, in RufQuaternion rotation, in RufVector scale);

    static abstract Matrix4x4 Combine(in Matrix4x4 parentWorld, in Matrix4x4 local);

    static abstract RufVector TransformPoint(in Matrix4x4 transform, in RufVector point);

    static abstract RufVector TransformDirection(in Matrix4x4 transform, in RufVector direction);

    static abstract RufVector TransformNormal(in Matrix4x4 transform, in RufVector normal);
}
