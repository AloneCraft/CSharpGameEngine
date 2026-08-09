using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Blender;

/// <summary>
/// Blender world-space exchange mapping: R=+X, U=+Z, F=+Y, RightHanded (see
/// Docs/Specifications/Spatial-RUF.md "Blender交換用"). This covers Blender's native world axes
/// only - Export-specific Forward/Up settings, Mesh local forward, Camera local view direction, and
/// Light local direction are deliberately kept separate (not represented by this single type; see
/// "未確定事項" for why <c>BlenderGltfExportConvention</c>/<c>BlenderFbxExportConvention</c> are not
/// implemented this milestone). <see cref="ToXyz"/>/<see cref="FromXyz"/> are a fixed component
/// permutation - no branching, no allocation.
/// </summary>
public readonly struct BlenderWorldConvention : ICoordinateConvention
{
    public static CoordinateHandedness Handedness => CoordinateHandedness.RightHanded;

    public static XyzVector ToXyz(in RufVector value) => new(value.R, value.F, value.U);

    public static RufVector FromXyz(in XyzVector value) => new(value.X, value.Z, value.Y);

    public static XyzVector RightXyz => new(1f, 0f, 0f);

    public static XyzVector UpXyz => new(0f, 0f, 1f);

    public static XyzVector ForwardXyz => new(0f, 1f, 0f);
}
