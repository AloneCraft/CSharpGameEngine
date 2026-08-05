using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Unity;

/// <summary>
/// Unity-compatible physical mapping: R=+X, U=+Y, F=+Z, LeftHanded (see
/// Docs/Specifications/Spatial-RUF.md "Unity互換"). The mapping is an identity permutation of
/// components, so <see cref="ToXyz"/>/<see cref="FromXyz"/> are trivially inlinable and branch-free.
/// </summary>
public readonly struct UnityCoordinateConvention : ICoordinateConvention
{
    public static CoordinateHandedness Handedness => CoordinateHandedness.LeftHanded;

    public static XyzVector ToXyz(in RufVector value) => new(value.R, value.U, value.F);

    public static RufVector FromXyz(in XyzVector value) => new(value.X, value.Y, value.Z);

    public static XyzVector RightXyz => new(1f, 0f, 0f);

    public static XyzVector UpXyz => new(0f, 1f, 0f);

    public static XyzVector ForwardXyz => new(0f, 0f, 1f);
}
