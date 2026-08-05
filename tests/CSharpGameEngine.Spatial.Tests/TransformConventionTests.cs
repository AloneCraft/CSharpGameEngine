using System.Numerics;
using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md "Transform Tests", against
/// <see cref="StandardTransformConvention"/>. Preset-independent: <see cref="ITransformConvention"/> is
/// orthogonal to <see cref="ICoordinateConvention"/>, so these tests never reference Unity/Unreal/Blender.
/// </summary>
public class TransformConventionTests
{
    [Fact]
    public void Compose_Identity_ProducesIdentityMatrix()
    {
        Matrix4x4 matrix = StandardTransformConvention.Compose(RufVector.Zero, RufQuaternion.Identity, new RufVector(1f, 1f, 1f));

        Assert.Equal(Matrix4x4.Identity, matrix);
    }

    [Fact]
    public void Compose_TranslationOnly_MovesPointByPosition()
    {
        RufVector position = new(5f, -2f, 10f);
        Matrix4x4 matrix = StandardTransformConvention.Compose(position, RufQuaternion.Identity, new RufVector(1f, 1f, 1f));

        RufVector transformed = StandardTransformConvention.TransformPoint(matrix, RufVector.Zero);

        AssertApproximatelyEqual(position, transformed);
    }

    [Fact]
    public void Compose_RotationOnly_RotatesPointAroundOrigin()
    {
        RufQuaternion rotation = RufQuaternion.FromAxisAngle(RufVector.Up, MathF.PI / 2f);
        Matrix4x4 matrix = StandardTransformConvention.Compose(RufVector.Zero, rotation, new RufVector(1f, 1f, 1f));

        RufVector transformed = StandardTransformConvention.TransformPoint(matrix, RufVector.Right);
        RufVector expected = RufQuaternion.RotateVector(rotation, RufVector.Right);

        AssertApproximatelyEqual(expected, transformed);
    }

    [Fact]
    public void Compose_UniformScale_ScalesPointFromOrigin()
    {
        Matrix4x4 matrix = StandardTransformConvention.Compose(RufVector.Zero, RufQuaternion.Identity, new RufVector(2f, 2f, 2f));

        RufVector transformed = StandardTransformConvention.TransformPoint(matrix, RufVector.Right);

        AssertApproximatelyEqual(RufVector.Right * 2f, transformed);
    }

    [Fact]
    public void Compose_NonUniformScale_ScalesEachAxisIndependently()
    {
        RufVector scale = new(2f, 3f, 4f);
        Matrix4x4 matrix = StandardTransformConvention.Compose(RufVector.Zero, RufQuaternion.Identity, scale);

        RufVector transformed = StandardTransformConvention.TransformPoint(matrix, new RufVector(1f, 1f, 1f));

        AssertApproximatelyEqual(scale, transformed);
    }

    [Fact]
    public void Compose_AppliesScaleThenRotationThenTranslation()
    {
        RufVector scale = new(2f, 1f, 1f);
        RufQuaternion rotation = RufQuaternion.FromAxisAngle(RufVector.Up, MathF.PI / 2f);
        RufVector position = new(10f, 0f, 0f);
        Matrix4x4 matrix = StandardTransformConvention.Compose(position, rotation, scale);

        RufVector point = RufVector.Right;
        RufVector scaledPoint = new(point.R * scale.R, point.U * scale.U, point.F * scale.F);
        RufVector expected = RufQuaternion.RotateVector(rotation, scaledPoint) + position;
        RufVector actual = StandardTransformConvention.TransformPoint(matrix, point);

        AssertApproximatelyEqual(expected, actual);
    }

    [Fact]
    public void Combine_ChainsChildThroughParent_SameAsApplyingBothTransformsInOrder()
    {
        Matrix4x4 parentWorld = StandardTransformConvention.Compose(new RufVector(5f, 0f, 0f), RufQuaternion.FromAxisAngle(RufVector.Up, MathF.PI / 2f), new RufVector(1f, 1f, 1f));
        Matrix4x4 local = StandardTransformConvention.Compose(new RufVector(0f, 1f, 0f), RufQuaternion.Identity, new RufVector(2f, 2f, 2f));
        Matrix4x4 combined = StandardTransformConvention.Combine(parentWorld, local);

        RufVector point = new(1f, 0f, 0f);
        RufVector expected = StandardTransformConvention.TransformPoint(parentWorld, StandardTransformConvention.TransformPoint(local, point));
        RufVector actual = StandardTransformConvention.TransformPoint(combined, point);

        AssertApproximatelyEqual(expected, actual);
    }

    [Fact]
    public void TransformDirection_IgnoresTranslation()
    {
        Matrix4x4 matrix = StandardTransformConvention.Compose(new RufVector(100f, 50f, -25f), RufQuaternion.Identity, new RufVector(1f, 1f, 1f));

        RufVector direction = StandardTransformConvention.TransformDirection(matrix, RufVector.Right);

        AssertApproximatelyEqual(RufVector.Right, direction);
    }

    [Fact]
    public void TransformNormal_StaysPerpendicularToSurfaceUnderNonUniformScale()
    {
        Matrix4x4 matrix = StandardTransformConvention.Compose(RufVector.Zero, RufQuaternion.Identity, new RufVector(2f, 1f, 5f));

        RufVector tangentA = StandardTransformConvention.TransformDirection(matrix, RufVector.Right);
        RufVector tangentB = StandardTransformConvention.TransformDirection(matrix, RufVector.Forward);
        RufVector normal = StandardTransformConvention.TransformNormal(matrix, RufVector.Up);

        Assert.Equal(0f, RufVector.Dot(normal, tangentA), precision: 4);
        Assert.Equal(0f, RufVector.Dot(normal, tangentB), precision: 4);
    }

    private static void AssertApproximatelyEqual(RufVector expected, RufVector actual)
    {
        Assert.Equal(expected.R, actual.R, precision: 3);
        Assert.Equal(expected.U, actual.U, precision: 3);
        Assert.Equal(expected.F, actual.F, precision: 3);
    }
}
