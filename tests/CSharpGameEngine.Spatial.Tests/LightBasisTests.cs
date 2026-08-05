using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md "Light Tests": Forward is the direction
/// light travels (photon direction), DirectionToLight is always its opposite.
/// </summary>
public class LightBasisTests
{
    [Fact]
    public void DirectionalLight_ForwardIsTravelDirection_DirectionToLightIsOpposite()
    {
        DirectionalLightBasis light = new(new RufVector(0f, -1f, 1f).Normalize());

        AssertOpposite(light.Forward, light.DirectionToLight);
    }

    [Fact]
    public void SpotLight_ForwardIsConeDirection_DirectionToLightIsOpposite()
    {
        SpotLightBasis spot = new(new RufVector(1f, -1f, 0f).Normalize());

        AssertOpposite(spot.Forward, spot.DirectionToLight);
    }

    [Fact]
    public void DirectionalLight_Forward_IsNormalized()
    {
        DirectionalLightBasis light = new(new RufVector(0f, 0f, 5f));

        Assert.Equal(1f, light.Forward.Length, precision: 5);
    }

    private static void AssertOpposite(RufVector forward, RufVector directionToLight)
    {
        Assert.Equal(-forward.R, directionToLight.R, precision: 5);
        Assert.Equal(-forward.U, directionToLight.U, precision: 5);
        Assert.Equal(-forward.F, directionToLight.F, precision: 5);
    }
}
