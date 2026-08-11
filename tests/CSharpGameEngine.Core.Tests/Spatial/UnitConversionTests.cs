using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/Specifications/Spatial-RUF.md "Units": Runtime units are meters/radians/seconds;
/// <see cref="UnitConversion"/> exists only for the Authoring/Import boundary.
/// </summary>
public class UnitConversionTests {
    [Fact]
    public void MetersFromCentimeters_Converts()
        =>Assert.Equal(2.5f,UnitConversion.MetersFromCentimeters(250f),precision:4);

    [Fact]
    public void CentimetersFromMeters_Converts()
        =>Assert.Equal(250f,UnitConversion.CentimetersFromMeters(2.5f),precision:4);

    [Fact]
    public void MetersCentimeters_RoundTrip(){
        var Original=173.4f;
        var RoundTripped=UnitConversion.CentimetersFromMeters(UnitConversion.MetersFromCentimeters(Original));

        Assert.Equal(Original,RoundTripped,precision:3);
    }

    [Fact]
    public void RadiansFromDegrees_Converts()
        =>Assert.Equal(MathF.PI,UnitConversion.RadiansFromDegrees(180f),precision:4);

    [Fact]
    public void DegreesFromRadians_Converts()
        =>Assert.Equal(180f,UnitConversion.DegreesFromRadians(MathF.PI),precision:4);

    [Fact]
    public void DegreesRadians_RoundTrip(){
        var Original=57.3f;
        var RoundTripped=UnitConversion.DegreesFromRadians(UnitConversion.RadiansFromDegrees(Original));

        Assert.Equal(Original,RoundTripped,precision:3);
    }
}
