using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>Docs/RUF-Semantic-Coordinate-Specification-Prompt.md "Camera Tests".</summary>
public class CameraBasisTests {
    [Fact]
    public void Forward_IsNormalized(){
        CameraBasis Camera=new(new RufVector(3f,0f,0f),RufVector.Up);

        Assert.Equal(1f,Camera.Forward.Length,precision:5);
        Assert.Equal(RufVector.Right.R,Camera.Forward.R,precision:5);
    }

    [Fact]
    public void Forward_IsTheViewDirection(){
        var LookDirection=new RufVector(1f,1f,1f).Normalize();
        CameraBasis Camera=new(LookDirection,RufVector.Up);

        Assert.Equal(LookDirection.R,Camera.Forward.R,precision:5);
        Assert.Equal(LookDirection.U,Camera.Forward.U,precision:5);
        Assert.Equal(LookDirection.F,Camera.Forward.F,precision:5);
    }
}
