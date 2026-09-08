using System.Reflection;
using CSharpGameEngine.Runtime.Lighting;
namespace CSharpGameEngine.Runtime.Windows.Tests;
public sealed class PhysicalLightComponentTests {
    [Fact]
    public void Defaults_PreservePhysicalBrightnessAndGeometry(){
        Assert.Equal(100_000f,new DirectionalLight().Illuminance.Lux);
        Assert.Equal(800f,new PointLight().Flux.Lumens);
        Assert.Equal(10f,new PointLight().Range.Meters);
        Assert.Equal(30f,new SpotLight().Cone.InnerFullAngleDegrees);
        Assert.Equal(45f,new SpotLight().Cone.OuterFullAngleDegrees);
        Assert.Equal(100f,new RectAreaLight().Luminance.Nits);
        Assert.Equal(1f,new RectAreaLight().Size.WidthMeters);
        Assert.Equal(0.005f,new LightShadow().Bias.Meters);
    }
    [Fact]
    public void Components_ExposeOnlyApplicableProperties(){
        Assert.DoesNotContain("Cone",Fields<PointLight>());
        Assert.DoesNotContain("Range",Fields<DirectionalLight>());
        Assert.DoesNotContain("Size",Fields<SpotLight>());
        foreach(var Type in new[]{typeof(PointLight),typeof(SpotLight),typeof(DirectionalLight),typeof(RectAreaLight)}){
            var Names=Type.GetFields(BindingFlags.Public|BindingFlags.Instance).Select(Field=>Field.Name).ToArray();
            Assert.DoesNotContain("Type",Names);
            Assert.DoesNotContain("Intensity",Names);
            Assert.DoesNotContain("CastsShadows",Names);
            Assert.DoesNotContain("ShadowPriority",Names);
            Assert.DoesNotContain("Position",Names);
        }
    }
    private static string[]Fields<T>()=>typeof(T).GetFields(BindingFlags.Public|BindingFlags.Instance).Select(Field=>Field.Name).ToArray();
}
