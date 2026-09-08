using CSharpGameEngine.Lighting;
namespace CSharpGameEngine.Contracts.Tests;
public sealed class LightingValueTests {
    [Fact]
    public void Quantities_RejectInvalidNumbersAndKeepUnits(){
        Assert.Equal(800f,new LuminousFlux(800f).Lumens);
        Assert.Equal(100f,new Illuminance(100f).Lux);
        Assert.Equal(20f,new Luminance(20f).Nits);
        Assert.Equal(3f,new LuminousIntensity(3f).Candela);
        foreach(var Value in new[]{-1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity}){
            Assert.Throws<ArgumentOutOfRangeException>(()=>new LuminousFlux(Value));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new Illuminance(Value));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new Luminance(Value));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new LuminousIntensity(Value));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new ShadowBias(Value));
        }
        Assert.True(default(LuminousFlux).IsValid);
        Assert.True(default(ShadowBias).IsValid);
    }
    [Fact]
    public void Shapes_ValidateTheWholeValueIncludingDefault(){
        Assert.False(default(LightRange).IsValid);
        Assert.False(default(RectangleSize).IsValid);
        Assert.False(default(SpotConeAngles).IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new LightRange(0f));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new RectangleSize(1f,0f));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new SpotConeAngles(46f,45f));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new SpotConeAngles(0f,45f));
        Assert.True(new SpotConeAngles(180f,180f).IsValid);
        Assert.Equal(2f,new RectangleSize(2f,3f).WidthMeters);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new LinearLightColor(1f,float.NaN,0f));
    }
    [Fact]
    public void FluxConversions_PreservePointSpotAndRectangleFlux(){
        var Flux=new LuminousFlux(800f);
        var Cone=new SpotConeAngles(30f,45f);
        var Size=new RectangleSize(2f,3f);
        Assert.Equal(800f,LightPhotometry.PointFlux(LightPhotometry.PointIntensity(Flux)).Lumens,3);
        Assert.Equal(800f,LightPhotometry.SpotFlux(LightPhotometry.SpotIntensity(Flux,Cone),Cone).Lumens,3);
        Assert.Equal(800f,LightPhotometry.RectangleFlux(LightPhotometry.RectangleLuminance(Flux,Size),Size).Lumens,3);
    }
}
