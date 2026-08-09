namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// Declared handedness of an <see cref="ICoordinateConvention"/>. This is authoritative metadata
/// about the target external tool's own convention (matching how that tool documents itself),
/// not a value derived from the RUF/XYZ axis mapping in isolation - see
/// Docs/Specifications/Spatial-RUF.md "Handedness の定義" for the verified relationship between
/// this value and <see cref="Spatial{TConvention}.Cross"/>.
/// </summary>
public enum CoordinateHandedness
{
    LeftHanded,
    RightHanded,
}
