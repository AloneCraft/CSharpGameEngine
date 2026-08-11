using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Unreal;

/// <summary>
/// Unreal-compatible physical mapping: R=+Y, U=+Z, F=+X, LeftHanded (see
/// Docs/Specifications/Spatial-RUF.md "Unreal互換"). <see cref="ToXyz"/>/<see cref="FromXyz"/> are a
/// fixed component permutation - no branching, no allocation.
/// </summary>
public readonly struct UnrealCoordinateConvention:ICoordinateConvention {
    public static CoordinateHandedness Handedness=>CoordinateHandedness.LeftHanded;

    public static XyzVector ToXyz(in RufVector Value)=>new(Value.F,Value.R,Value.U);

    public static RufVector FromXyz(in XyzVector Value)=>new(Value.Y,Value.Z,Value.X);

    public static XyzVector RightXyz=>new(0f,1f,0f);

    public static XyzVector UpXyz=>new(0f,0f,1f);

    public static XyzVector ForwardXyz=>new(1f,0f,0f);
}
