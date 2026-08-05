using CSharpGameEngine.Spatial.Abstractions;
#if SPATIAL_PRESET_UNITY
using CSharpGameEngine.Spatial.Unity;
#elif SPATIAL_PRESET_UNREAL
using CSharpGameEngine.Spatial.Unreal;
#elif SPATIAL_PRESET_BLENDER
using CSharpGameEngine.Spatial.Blender;
#else
#error SpatialPreset must be set to exactly one of Unity, Unreal, Blender (see SpatialPresetSelection.props) before compiling this file.
#endif

namespace SpatialPresetDemo;

/// <summary>
/// The non-generic, single-preset facade Docs/Specifications/Spatial-RUF.md "型ベース呼び出し" asks
/// every consuming project to write once it has picked exactly one <see cref="ICoordinateConvention"/>
/// (here via SpatialPresetSelection.props' <c>SPATIAL_PRESET_*</c> constants) - application code below
/// this point never spells out <see cref="Spatial{TConvention}"/> itself. Exactly one <c>#if</c> arm is
/// compiled in, so there is no Runtime Preset dispatch anywhere in this type.
/// </summary>
public static class ProjectSpatial
{
    public static RufVector Forward => RufVector.Forward;

#if SPATIAL_PRESET_UNITY
    public const string PresetName = "Unity";

    public static XyzVector ToXyz(in RufVector value) => Spatial<UnityCoordinateConvention>.ToXyz(value);

    public static RufVector FromXyz(in XyzVector value) => Spatial<UnityCoordinateConvention>.FromXyz(value);
#elif SPATIAL_PRESET_UNREAL
    public const string PresetName = "Unreal";

    public static XyzVector ToXyz(in RufVector value) => Spatial<UnrealCoordinateConvention>.ToXyz(value);

    public static RufVector FromXyz(in XyzVector value) => Spatial<UnrealCoordinateConvention>.FromXyz(value);
#elif SPATIAL_PRESET_BLENDER
    public const string PresetName = "Blender";

    public static XyzVector ToXyz(in RufVector value) => Spatial<BlenderWorldConvention>.ToXyz(value);

    public static RufVector FromXyz(in XyzVector value) => Spatial<BlenderWorldConvention>.FromXyz(value);
#endif
}
