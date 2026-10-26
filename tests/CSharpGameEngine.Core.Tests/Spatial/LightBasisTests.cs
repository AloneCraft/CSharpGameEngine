using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md の「Light Tests」。Forward は光が進む方向（光子の進行方向）であり、DirectionToLight は常にその反対方向。
/// </summary>
public class LightBasisTests {
    [Fact]
    public void DirectionalLight_ForwardIsTravelDirection_DirectionToLightIsOpposite(){
        DirectionalLightBasis Light=new(new RufVector(0f,-1f,1f).Normalize());

        AssertOpposite(Light.Forward,Light.DirectionToLight);
    }

    [Fact]
    public void SpotLight_ForwardIsConeDirection_DirectionToLightIsOpposite(){
        SpotLightBasis Spot=new(new RufVector(1f,-1f,0f).Normalize());

        AssertOpposite(Spot.Forward,Spot.DirectionToLight);
    }

    [Fact]
    public void DirectionalLight_Forward_IsNormalized(){
        DirectionalLightBasis Light=new(new RufVector(0f,0f,5f));

        Assert.Equal(1f,Light.Forward.Length,precision:5);
    }

    private static void AssertOpposite(RufVector Forward,RufVector DirectionToLight){
        Assert.Equal(-Forward.R,DirectionToLight.R,precision:5);
        Assert.Equal(-Forward.U,DirectionToLight.U,precision:5);
        Assert.Equal(-Forward.F,DirectionToLight.F,precision:5);
    }
}
