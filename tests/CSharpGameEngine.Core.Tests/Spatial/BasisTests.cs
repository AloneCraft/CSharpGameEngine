using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Blender;
using CSharpGameEngine.Spatial.Fbx;
using CSharpGameEngine.Spatial.Gltf;
using CSharpGameEngine.Spatial.Unity;
using CSharpGameEngine.Spatial.Unreal;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md の「Basis Tests」。プリセットごとに Right、Up、Forward が仕様の
/// Golden Values と一致して単位長になり、互いに直交することを確認する。Left、Down、Backward が反対方向になる条件も、物理 XYZ 空間で検証する。
/// </summary>
public class BasisTests {
    [Fact]
    public void Unity_MatchesGoldenValues()
        =>AssertBasis<UnityCoordinateConvention>(
            new XyzVector(1f,0f,0f),new XyzVector(0f,1f,0f),new XyzVector(0f,0f,1f),CoordinateHandedness.LeftHanded);

    [Fact]
    public void Unreal_MatchesGoldenValues()
        =>AssertBasis<UnrealCoordinateConvention>(
            new XyzVector(0f,1f,0f),new XyzVector(0f,0f,1f),new XyzVector(1f,0f,0f),CoordinateHandedness.LeftHanded);

    [Fact]
    public void Blender_MatchesGoldenValues()
        =>AssertBasis<BlenderWorldConvention>(
            new XyzVector(1f,0f,0f),new XyzVector(0f,0f,1f),new XyzVector(0f,1f,0f),CoordinateHandedness.RightHanded);

    [Fact]
    public void Gltf_MatchesGoldenValues()
        =>AssertBasis<GltfCoordinateConvention>(
            new XyzVector(1f,0f,0f),new XyzVector(0f,1f,0f),new XyzVector(0f,0f,-1f),CoordinateHandedness.RightHanded);

    [Fact]
    public void Fbx_MatchesGoldenValues()
        =>AssertBasis<FbxCoordinateConvention>(
            new XyzVector(1f,0f,0f),new XyzVector(0f,1f,0f),new XyzVector(0f,0f,-1f),CoordinateHandedness.RightHanded);

    private static void AssertBasis<TConvention>(XyzVector ExpectedRight,XyzVector ExpectedUp,XyzVector ExpectedForward,CoordinateHandedness ExpectedHandedness)
        where TConvention:struct,ICoordinateConvention {
        Assert.Equal(ExpectedRight,TConvention.RightXyz);
        Assert.Equal(ExpectedUp,TConvention.UpXyz);
        Assert.Equal(ExpectedForward,TConvention.ForwardXyz);
        Assert.Equal(ExpectedHandedness,TConvention.Handedness);

        Assert.Equal(1f,TConvention.RightXyz.Length,precision:5);
        Assert.Equal(1f,TConvention.UpXyz.Length,precision:5);
        Assert.Equal(1f,TConvention.ForwardXyz.Length,precision:5);

        Assert.Equal(0f,XyzVector.Dot(TConvention.RightXyz,TConvention.UpXyz),precision:5);
        Assert.Equal(0f,XyzVector.Dot(TConvention.UpXyz,TConvention.ForwardXyz),precision:5);
        Assert.Equal(0f,XyzVector.Dot(TConvention.ForwardXyz,TConvention.RightXyz),precision:5);

        CoordinateBasisValidator.Validate<TConvention>();

        Assert.Equal(TConvention.RightXyz,TConvention.ToXyz(RufVector.Right));
        Assert.Equal(TConvention.UpXyz,TConvention.ToXyz(RufVector.Up));
        Assert.Equal(TConvention.ForwardXyz,TConvention.ToXyz(RufVector.Forward));
        Assert.Equal(-TConvention.RightXyz,TConvention.ToXyz(RufVector.Left));
        Assert.Equal(-TConvention.UpXyz,TConvention.ToXyz(RufVector.Down));
        Assert.Equal(-TConvention.ForwardXyz,TConvention.ToXyz(RufVector.Backward));
    }
}
