namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// Unit conversion, kept as a separate step from axis conversion (see
/// Docs/Specifications/Spatial-RUF.md "Units": "External axis conversion → Unit conversion →
/// Semantic RUF normalization"). This engine's runtime units are meters (distance), radians
/// (angle APIs), seconds (time), and dimensionless scale; these helpers only exist for the
/// Authoring/Import boundary, where a source tool's centimeters or degrees must be converted
/// before becoming RUF-space runtime data.
/// </summary>
public static class UnitConversion
{
    private const float MetersPerCentimeter = 0.01f;

    public static float MetersFromCentimeters(float Centimeters) => Centimeters * MetersPerCentimeter;

    public static float CentimetersFromMeters(float Meters) => Meters / MetersPerCentimeter;

    public static float RadiansFromDegrees(float Degrees) => Degrees * (MathF.PI / 180f);

    public static float DegreesFromRadians(float Radians) => Radians * (180f / MathF.PI);
}
