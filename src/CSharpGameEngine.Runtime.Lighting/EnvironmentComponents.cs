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
/// <summary>Unreal's Global Illumination Method (ADR-0078/M0111).</summary>
public enum GlobalIlluminationMethod {
    /// <summary>No Lumen: the probe volume (<see cref="GlobalIlluminationVolume"/>) and the sky light light the scene, as before M0111.</summary>
    None,
    /// <summary>The Lumen-like dynamic GI (surface cache, radiance cache, screen probes), where the adapter's scalability allows it.</summary>
    Lumen,
}
/// <summary>Unreal's Reflection Method (ADR-0078/M0111).</summary>
public enum ReflectionMethod {
    /// <summary>No Lumen reflections: the sky light's specular, as before M0111.</summary>
    None,
    /// <summary>The Lumen-like reflections (screen traces, then the distance field and the surface cache), where the scalability allows them.</summary>
    Lumen,
}
/// <summary>
/// Scene-wide (unbound) post-process settings: exposure, tone mapping, bloom, and (ADR-0078/M0111) Unreal's Lumen settings with Unreal's defaults,
/// which the four UE 5.8 templates use unchanged (tools/UnrealExport/probe_lumen.py). Distances are metres (Unreal's centimetres / 100). Lumen needs a
/// <see cref="GlobalDistanceField"/> in the scene and an adapter whose scalability is High or Epic (the default on a GPU; WARP keeps the probe volume).
/// </summary>
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
    public GlobalIlluminationMethod GlobalIlluminationMethod=GlobalIlluminationMethod.Lumen;
    public ReflectionMethod ReflectionMethod=ReflectionMethod.Lumen;
    /// <summary>Scales how many surface cache texels are lit each frame (Unreal's Lumen Scene Lighting Quality, 0.25-4).</summary>
    public float LumenSceneLightingQuality=1f;
    /// <summary>Scales the surface cache's texels per metre (Unreal's Lumen Scene Detail, 0.25-4).</summary>
    public float LumenSceneDetail=1f;
    /// <summary>Objects farther than this get no surface cache cards (Unreal's Lumen Scene View Distance, 20000 cm).</summary>
    public float LumenSceneViewDistanceMeters=200f;
    /// <summary>Scales the surface cache's lighting budget per frame (Unreal's Lumen Scene Lighting Update Speed, 0.5-4).</summary>
    public float LumenSceneLightingUpdateSpeed=1f;
    /// <summary>Screen probes every 16 pixels at Epic divided by this (Unreal's Final Gather Quality, 0.25-4).</summary>
    public float LumenFinalGatherQuality=1f;
    /// <summary>Scales the radiance cache's probes per frame and the final gather's new-sample weight (Unreal's Final Gather Lighting Update Speed).</summary>
    public float LumenFinalGatherLightingUpdateSpeed=1f;
    /// <summary>Whether the final gather traces the screen before the distance field (Unreal's Final Gather Screen Traces).</summary>
    public bool LumenFinalGatherScreenTraces=true;
    /// <summary>The longest ray (Unreal's Max Trace Distance, 20000 cm).</summary>
    public float LumenMaxTraceDistanceMeters=200f;
    /// <summary>Multiplies the surface cache's albedo, at most to 1 (Unreal's Diffuse Color Boost, 1-4).</summary>
    public float LumenDiffuseColorBoost=1f;
    /// <summary>The final gather's irradiance is at least this fraction of the open sky's (Unreal's Skylight Leaking, 0-1; an art knob against black interiors).</summary>
    public float LumenSkylightLeaking=0f;
    /// <summary>Reflections at full resolution at Epic when at least 0.75, else every 2 x 2 pixels (Unreal's Lumen Reflection Quality).</summary>
    public float LumenReflectionQuality=1f;
    /// <summary>Rougher surfaces reflect the final gather's probes instead of tracing (Unreal's Max Roughness To Trace Reflections, 0.4).</summary>
    public float LumenMaxRoughnessToTraceReflections=0.4f;
    /// <summary>Whether the reflections trace the screen before the distance field (Unreal's Lumen Reflections Screen Traces).</summary>
    public bool LumenReflectionsScreenTraces=true;
    /// <summary>Scales the surface cache's resolution (Unreal's Surface Cache Resolution, 0.5-1).</summary>
    public float LumenSurfaceCacheResolution=1f;
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
/// <summary>
/// Volumetric clouds (ADR-0078/M0110): Unreal's VolumetricCloud component together with the values of its default cloud material
/// (<c>m_SimpleVolumetricCloud_Inst</c>), read from the UE 5.8 templates (all four levels use the same ones). A shell of the planet between
/// <see cref="LayerBottomAltitudeKm"/> and its top is ray marched toward the sun after Hillaire, lit by the sun through the atmosphere and by the
/// sky light, and drawn over the sky. The cloud shapes are the engine's own noise (Unreal's come from its cloud textures); the wind moves them.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,65)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/VolumetricCloud.svg")]
public struct VolumetricCloud() {
    /// <summary>Altitude of the bottom of the cloud layer above the ground (Unreal's Layer Bottom Altitude).</summary>
    public float LayerBottomAltitudeKm=5f;
    /// <summary>Thickness of the cloud layer (Unreal's Layer Height).</summary>
    public float LayerHeightKm=10f;
    /// <summary>Clouds whose layer starts farther than this along a view ray are not traced (Unreal's Tracing Start Max Distance).</summary>
    public float TracingStartMaxDistanceKm=350f;
    /// <summary>The longest stretch of a view ray traced inside the layer (Unreal's Tracing Max Distance).</summary>
    public float TracingMaxDistanceKm=50f;
    /// <summary>Extinction of the densest cloud per metre (the material's Cloud_GlobalDensity).</summary>
    public float Density=0.008f;
    /// <summary>Shifts how much of the sky is covered: 0 is about half, negative is less (the material's Cloud_GlobalCoverage).</summary>
    public float Coverage=-0.2f;
    /// <summary>Size of the cloud layout pattern (the material's Layout_CloudGlobalScale).</summary>
    public float LayoutScaleKm=256f;
    /// <summary>Single-scattering albedo (the material's Cloud_AlbedoColor).</summary>
    public float AlbedoR=0.98f;
    public float AlbedoG=0.98f;
    public float AlbedoB=0.98f;
    /// <summary>The dual-lobe Henyey-Greenstein phase: the two eccentricities and the blend toward the second (the material's Phase_Controls).</summary>
    public float PhaseG=0.8f;
    public float PhaseG2=0.166667f;
    public float PhaseBlend=0.575f;
    /// <summary>Hillaire's multiple-scattering approximation per octave: contribution, occlusion and eccentricity factors (the material's Multiscatter_Controls).</summary>
    public float MultiScatteringContribution=0.666667f;
    public float MultiScatteringOcclusion=0.25f;
    public float MultiScatteringEccentricity=0.18f;
    /// <summary>How much the sky light is dimmed at the bottom of the layer (Unreal's Sky Light Cloud Bottom Occlusion).</summary>
    public float SkyLightCloudBottomOcclusion=0.5f;
    /// <summary>Tracing stops once the view ray's transmittance falls below this (Unreal's Stop Tracing Transmittance Threshold).</summary>
    public float StopTracingTransmittanceThreshold=0.005f;
    /// <summary>Wind speed moving the clouds. Unreal animates its default clouds through Layout_WindControls; this speed is the engine's choice.</summary>
    public float WindSpeedMetersPerSecond=10f;
    /// <summary>The direction the wind blows toward, clockwise from +F seen from above (+R at 90).</summary>
    [Angle(AngleUnit.Degrees)]
    public float WindDirectionDegrees=45f;
}
/// <summary>
/// The sky sphere (ADR-0078/M0110): Unreal's <c>SM_SkySphere</c> drawn with <c>M_SimpleSkyDome</c>, an unlit "Is Sky" material whose colour is
/// the SkyAtmosphere's view luminance plus its sun disk. The engine draws exactly that sky (with the clouds) on every pixel that is not covered
/// by geometry closer than the sphere: geometry beyond <see cref="RadiusMeters"/> from the Entity's position is hidden by the sky, as Unreal's
/// dome hides it. Without a <see cref="SkyAtmosphere"/> it has no effect. Rotation and scale of the Transform are ignored.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,66)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/SkySphere.svg")]
public struct SkySphere() {
    /// <summary>Radius of the sphere. Unreal's SM_SkySphere is 40.96 m and the templates scale it 400 times: 16.384 km.</summary>
    public float RadiusMeters=16384f;
}
/// <summary>
/// The global distance field (ADR-0078/M0110): clipmaps of the scene's signed distance around the camera, made every frame from the mesh distance
/// fields the build writes for each glTF, for the Lumen-like lighting (M0111) to trace. Unreal makes it when the project's "Generate Mesh Distance
/// Fields" is on, as its templates have it for Lumen; here a level with this component gets it. The values are the engine's choice for WARP (Unreal's
/// first clipmap reaches 25 m): <see cref="ClipmapCount"/> levels of <see cref="Resolution"/>^3 voxels, the finest of <see cref="VoxelSizeMeters"/>.
/// It changes nothing on screen by itself.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,67)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/GlobalDistanceField.svg")]
public struct GlobalDistanceField() {
    /// <summary>Voxel size of the finest clipmap; each next one doubles it.</summary>
    public float VoxelSizeMeters=0.5f;
    /// <summary>Clipmaps (1-4).</summary>
    public int ClipmapCount=4;
    /// <summary>Voxels per side of each clipmap (16-128, a multiple of 8).</summary>
    public int Resolution=64;
}
