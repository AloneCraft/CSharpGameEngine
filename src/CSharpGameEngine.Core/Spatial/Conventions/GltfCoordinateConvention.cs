using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Gltf;

/// <summary>
/// The glTF 2.0 file format's own baked coordinate convention: right-handed, +Y up, meters.
/// <see cref="ForwardXyz"/> is -Z because glTF cameras and <c>KHR_lights_punctual</c> lights face
/// their node's local -Z by specification default (see
/// Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md). This is deliberately not one of the
/// Unity/Unreal/Blender authoring-tool presets (ADR-0025) - it represents the raw file format's own
/// axes, used only at the glTF Import boundary (<c>CSharpGameEngine.Import.Gltf</c>) to convert
/// into this engine's fixed <see cref="CSharpGameEngine.Spatial.Unity.UnityCoordinateConvention"/>
/// (ADR-0026), never exposed as a selectable <c>SpatialPreset</c>.
/// </summary>
public readonly struct GltfCoordinateConvention:ICoordinateConvention {
    public static CoordinateHandedness Handedness=>CoordinateHandedness.RightHanded;

    public static XyzVector ToXyz(in RufVector Value)=>new(Value.R,Value.U,-Value.F);

    public static RufVector FromXyz(in XyzVector Value)=>new(Value.X,Value.Y,-Value.Z);

    public static XyzVector RightXyz=>new(1f,0f,0f);

    public static XyzVector UpXyz=>new(0f,1f,0f);

    public static XyzVector ForwardXyz=>new(0f,0f,-1f);
}
