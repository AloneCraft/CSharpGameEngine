using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Lighting;
/// <summary>
/// Physically based sky atmosphere (ADR-0069). Units and defaults follow Unreal Engine's
/// SkyAtmosphere component (kilometres, per-kilometre coefficients, Earth preset) so imported
/// values need no conversion. The brightest directional light is the sun: its illuminance is the
/// top-of-atmosphere value and the renderer attenuates it by the atmosphere's transmittance.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,60)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/SkyAtmosphere.svg")]
public struct SkyAtmosphere() {
    public float BottomRadiusKm=6360f;
    public float AtmosphereHeightKm=60f;
    public float GroundAlbedoR=0.401978f;
    public float GroundAlbedoG=0.401978f;
    public float GroundAlbedoB=0.401978f;
    public float RayleighScatteringR=0.175287f;
    public float RayleighScatteringG=0.409607f;
    public float RayleighScatteringB=1f;
    public float RayleighScatteringScale=0.0331f;
    public float RayleighExponentialDistributionKm=8f;
    public float MieScatteringR=1f;
    public float MieScatteringG=1f;
    public float MieScatteringB=1f;
    public float MieScatteringScale=0.003996f;
    public float MieAbsorptionR=1f;
    public float MieAbsorptionG=1f;
    public float MieAbsorptionB=1f;
    public float MieAbsorptionScale=0.000444f;
    public float MieAnisotropy=0.8f;
    public float MieExponentialDistributionKm=1.2f;
    public float OtherAbsorptionR=0.345561f;
    public float OtherAbsorptionG=1f;
    public float OtherAbsorptionB=0.045189f;
    public float OtherAbsorptionScale=0.001881f;
    public float OtherTentTipAltitudeKm=25f;
    public float OtherTentWidthKm=30f;
    public float MultiScatteringFactor=1f;
    /// <summary>Full angular diameter of the sun disk in degrees (Unreal's DirectionalLight Source Angle).</summary>
    [Angle(AngleUnit.Degrees)]
    public float SunDiskAngleDegrees=0.5357f;
}
/// <summary>Sky light captured from <see cref="SkyAtmosphere"/>. <see cref="Intensity"/> multiplies the captured sky radiance.</summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,61)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/SkyLight.svg")]
public struct SkyLight() {
    public float Intensity=1f;
    public float LowerHemisphereR=0f;
    public float LowerHemisphereG=0f;
    public float LowerHemisphereB=0f;
}
/// <summary>Exposure model. Legacy keeps the renderer's original 2^-EV100 scale.</summary>
public enum ExposureMethod {
    Legacy,
    /// <summary>Fixed physical camera exposure at <see cref="PostProcessVolume.MinEv100"/>.</summary>
    Manual,
    /// <summary>Unreal's histogram auto exposure, metered between <see cref="PostProcessVolume.MinEv100"/> and <see cref="PostProcessVolume.MaxEv100"/>.</summary>
    AutoHistogram,
}
/// <summary>Output transform. Legacy keeps the original linear ACES fit.</summary>
public enum ToneMapperKind {
    Legacy,
    /// <summary>Unreal Engine's filmic curve followed by the sRGB transfer function.</summary>
    UnrealFilmic,
}
/// <summary>Scene-wide (unbound) post-process settings: exposure, tone mapping and bloom.</summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,62)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/PostProcessVolume.svg")]
public struct PostProcessVolume() {
    public ExposureMethod ExposureMethod=ExposureMethod.Manual;
    public float MinEv100=0f;
    public float MaxEv100=0f;
    /// <summary>Histogram adaptation rate per second toward a brighter scene (Unreal's default 3).</summary>
    public float AutoExposureSpeedUp=3f;
    /// <summary>Histogram adaptation rate per second toward a darker scene (Unreal's default 1).</summary>
    public float AutoExposureSpeedDown=1f;
    /// <summary>Exposure compensation in stops (Unreal's Exposure Compensation / AutoExposureBias).</summary>
    public float ExposureCompensation=1f;
    public ToneMapperKind ToneMapper=ToneMapperKind.UnrealFilmic;
    public float BloomIntensity=0.675f;
    /// <summary>Strength of screen-space ambient occlusion on indirect light, 0 (off) to 1 (Unreal's default 0.5).</summary>
    public float AmbientOcclusionIntensity=0.5f;
    /// <summary>World-space ambient occlusion radius in metres (Unreal's 200 cm).</summary>
    public float AmbientOcclusionRadiusMeters=2f;
}
/// <summary>
/// Unreal Engine's exponential height fog. <see cref="FogDensity"/> and <see cref="FogHeightFalloff"/>
/// keep Unreal's authored values; the renderer converts them to per-metre values (x0.1).
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,63)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/ExponentialHeightFog.svg")]
public struct ExponentialHeightFog() {
    public float FogDensity=0.02f;
    public float FogHeightFalloff=0.2f;
    /// <summary>World height of the fog reference plane in metres.</summary>
    public float FogHeight=0f;
    public float FogMaxOpacity=1f;
    public float InscatteringLuminanceR=0f;
    public float InscatteringLuminanceG=0f;
    public float InscatteringLuminanceB=0f;
    public float SkyAtmosphereAmbientContributionScale=1f;
}
/// <summary>
/// Dynamic diffuse global illumination probe volume (ADR-0069): an axis-aligned box centred on the
/// entity's Transform position (rotation and scale are ignored). Probes sit on a regular grid inside
/// the box and are re-captured round robin with the lit Stage pipeline, so indirect light follows
/// the sun, the sky and moving objects over a few frames. Surfaces outside the box keep the sky light.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,64)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/GlobalIlluminationVolume.svg")]
public struct GlobalIlluminationVolume() {
    public float HalfExtentX=16f;
    public float HalfExtentY=4f;
    public float HalfExtentZ=16f;
    /// <summary>Target distance between probes in metres; the grid spans the box exactly.</summary>
    public float ProbeSpacingMeters=2.5f;
    /// <summary>Probes re-captured each frame (1-64).</summary>
    public int ProbesPerFrame=32;
    /// <summary>Weight of the previous result when a probe is refreshed (0 = replace).</summary>
    public float Hysteresis=0.5f;
    public float Intensity=1f;
}
