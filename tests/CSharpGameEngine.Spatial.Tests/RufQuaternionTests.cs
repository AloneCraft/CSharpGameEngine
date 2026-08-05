using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md "Quaternion仕様". Preset-independent:
/// <see cref="RufQuaternion"/> rotates the R/U/F triple directly, treated as a self-consistent
/// right-handed basis regardless of which <see cref="ICoordinateConvention"/> is active.
/// </summary>
public class RufQuaternionTests
{
    [Fact]
    public void Identity_DoesNotRotateVector()
    {
        RufVector rotated = RufQuaternion.RotateVector(RufQuaternion.Identity, RufVector.Forward);

        Assert.Equal(RufVector.Forward.R, rotated.R, precision: 5);
        Assert.Equal(RufVector.Forward.U, rotated.U, precision: 5);
        Assert.Equal(RufVector.Forward.F, rotated.F, precision: 5);
    }

    [Fact]
    public void FromAxisAngle_RotatesAroundAxisByRightHandRule()
    {
        RufQuaternion rotation = RufQuaternion.FromAxisAngle(RufVector.Forward, MathF.PI / 2f);

        RufVector rotated = RufQuaternion.RotateVector(rotation, RufVector.Right);

        // Right-hand rule around F: R rotates toward U (matches Quaternion.CreateFromAxisAngle applied
        // directly to the (R, U, F) triple, treated as an internal right-handed basis).
        Assert.Equal(0f, rotated.R, precision: 4);
        Assert.Equal(1f, rotated.U, precision: 4);
        Assert.Equal(0f, rotated.F, precision: 4);
    }

    [Fact]
    public void Multiply_AppliesChildThenParent()
    {
        RufQuaternion parent = RufQuaternion.FromAxisAngle(RufVector.Up, MathF.PI / 2f);
        RufQuaternion child = RufQuaternion.FromAxisAngle(RufVector.Right, MathF.PI / 4f);
        RufVector v = new(0.3f, 0.6f, 0.8f);

        RufVector expected = RufQuaternion.RotateVector(parent, RufQuaternion.RotateVector(child, v));
        RufVector actual = RufQuaternion.RotateVector(parent * child, v);

        Assert.Equal(expected.R, actual.R, precision: 4);
        Assert.Equal(expected.U, actual.U, precision: 4);
        Assert.Equal(expected.F, actual.F, precision: 4);
    }

    [Fact]
    public void LookRotation_ForwardMapsToRequestedDirection()
    {
        RufVector forward = new RufVector(1f, 1f, 1f).Normalize();
        RufQuaternion rotation = RufQuaternion.LookRotation(forward, RufVector.Up);

        RufVector rotatedForward = RufQuaternion.RotateVector(rotation, RufVector.Forward);

        Assert.Equal(forward.R, rotatedForward.R, precision: 4);
        Assert.Equal(forward.U, rotatedForward.U, precision: 4);
        Assert.Equal(forward.F, rotatedForward.F, precision: 4);
    }

    [Fact]
    public void LookRotation_ForwardParallelToUp_FallsBackDeterministically()
    {
        RufQuaternion first = RufQuaternion.LookRotation(RufVector.Up, RufVector.Up);
        RufQuaternion second = RufQuaternion.LookRotation(RufVector.Up, RufVector.Up);

        Assert.Equal(first.R, second.R, precision: 5);
        Assert.Equal(first.U, second.U, precision: 5);
        Assert.Equal(first.F, second.F, precision: 5);
        Assert.Equal(first.W, second.W, precision: 5);

        Assert.True(float.IsFinite(first.R) && float.IsFinite(first.U) && float.IsFinite(first.F) && float.IsFinite(first.W));
    }

    [Fact]
    public void LookRotation_ForwardParallelToCanonicalUp_FallsBackToRightWithoutNaN()
    {
        RufQuaternion rotation = RufQuaternion.LookRotation(RufVector.Up, RufVector.Zero);

        Assert.True(float.IsFinite(rotation.R) && float.IsFinite(rotation.U) && float.IsFinite(rotation.F) && float.IsFinite(rotation.W));
    }

    [Fact]
    public void LookRotation_ZeroForward_ThrowsSpatialConfigurationException()
        => Assert.Throws<SpatialConfigurationException>(() => RufQuaternion.LookRotation(RufVector.Zero, RufVector.Up));
}
