using System.Reflection;
using UserEcsProject;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class PhysicalLightComponentTests {
    [Fact]
    public void DirectionalLight_Default_UsesLuxAndShadowSettings(){
        DirectionalLight Light=new();

        Assert.Equal(100_000f,Light.IlluminanceLux);
        Assert.Equal(1f,Light.ColorR);
        Assert.Equal(1f,Light.ColorG);
        Assert.Equal(1f,Light.ColorB);
        Assert.True(Light.CastsShadows);
        Assert.Equal(0,Light.ShadowPriority);
        Assert.Equal(0.005f,Light.ShadowBiasMeters);
        Assert.DoesNotContain("Intensity",GetPublicFieldNames<DirectionalLight>());
        Assert.DoesNotContain("DirectionTowardLightX",GetPublicFieldNames<DirectionalLight>());
    }

    [Fact]
    public void PointLight_Default_UsesLumensAndTransformOwnsPosition(){
        PointLight Light=new();

        Assert.Equal(800f,Light.LuminousFluxLumens);
        Assert.Equal(10f,Light.RangeMeters);
        Assert.True(Light.CastsShadows);
        Assert.DoesNotContain("PositionX",GetPublicFieldNames<PointLight>());
        Assert.DoesNotContain("Intensity",GetPublicFieldNames<PointLight>());
    }

    [Fact]
    public void SpotLight_Default_UsesFullConeAnglesInDegreesAndTransformOwnsPose(){
        SpotLight Light=new();

        Assert.Equal(800f,Light.LuminousFluxLumens);
        Assert.Equal(30f,Light.InnerConeFullAngleDegrees);
        Assert.Equal(45f,Light.OuterConeFullAngleDegrees);
        Assert.Equal(10f,Light.RangeMeters);
        Assert.DoesNotContain("PositionX",GetPublicFieldNames<SpotLight>());
        Assert.DoesNotContain("DirectionX",GetPublicFieldNames<SpotLight>());
        Assert.DoesNotContain("InnerConeAngleRadians",GetPublicFieldNames<SpotLight>());
    }

    [Fact]
    public void AreaLight_Default_UsesNitsAndRectangleDimensions(){
        AreaLight Light=new();

        Assert.Equal(100f,Light.LuminanceNits);
        Assert.Equal(1f,Light.WidthMeters);
        Assert.Equal(1f,Light.HeightMeters);
        Assert.Equal(10f,Light.RangeMeters);
        Assert.True(Light.CastsShadows);
        Assert.DoesNotContain("PositionX",GetPublicFieldNames<AreaLight>());
        Assert.DoesNotContain("DirectionX",GetPublicFieldNames<AreaLight>());
    }

    private static IReadOnlyList<string>GetPublicFieldNames<T>()where T:struct =>
        typeof(T).GetFields(BindingFlags.Instance|BindingFlags.Public).Select(static Field=>Field.Name).ToArray();
}
