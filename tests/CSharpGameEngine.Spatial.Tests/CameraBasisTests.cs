using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>Docs/RUF-Semantic-Coordinate-Specification-Prompt.md "Camera Tests".</summary>
public class CameraBasisTests
{
    [Fact]
    public void Forward_IsNormalized()
    {
        CameraBasis camera = new(new RufVector(3f, 0f, 0f), RufVector.Up);

        Assert.Equal(1f, camera.Forward.Length, precision: 5);
        Assert.Equal(RufVector.Right.R, camera.Forward.R, precision: 5);
    }

    [Fact]
    public void Forward_IsTheViewDirection()
    {
        RufVector lookDirection = new RufVector(1f, 1f, 1f).Normalize();
        CameraBasis camera = new(lookDirection, RufVector.Up);

        Assert.Equal(lookDirection.R, camera.Forward.R, precision: 5);
        Assert.Equal(lookDirection.U, camera.Forward.U, precision: 5);
        Assert.Equal(lookDirection.F, camera.Forward.F, precision: 5);
    }
}
