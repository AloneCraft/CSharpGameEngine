using CSharpGameEngine.Rendering.Pipeline;
using System.Numerics;
namespace CSharpGameEngine.Rendering;
/// <summary>
/// Physically based atmosphere coefficients (ADR-0069). Distances are kilometres and scattering
/// coefficients are per kilometre, matching Unreal Engine's SkyAtmosphere component so imported
/// values need no unit conversion. The defaults are Unreal's Earth preset.
/// </summary>
public readonly record struct SkyAtmosphereSettings(
    Vector3 RayleighScatteringPerKm,
    float RayleighScaleHeightKm,
    Vector3 MieScatteringPerKm,
    Vector3 MieAbsorptionPerKm,
    float MieScaleHeightKm,
    float MieAnisotropy,
    Vector3 OzoneAbsorptionPerKm,
    float OzoneTipAltitudeKm,
    float OzoneWidthKm,
    float BottomRadiusKm,
    float AtmosphereHeightKm,
    Vector3 GroundAlbedo,
    float MultiScatteringFactor){
    public static SkyAtmosphereSettings Earth=>new(
        new Vector3(0.175287f,0.409607f,1f)*0.0331f,8f,
        new Vector3(1f,1f,1f)*0.003996f,new Vector3(1f,1f,1f)*0.000444f,1.2f,0.8f,
        new Vector3(0.345561f,1f,0.045189f)*0.001881f,25f,30f,
        6360f,60f,new Vector3(0.401978f,0.401978f,0.401978f),1f);
    public float TopRadiusKm=>this.BottomRadiusKm+this.AtmosphereHeightKm;
    public bool IsValid=>
        IsFiniteNonNegative(this.RayleighScatteringPerKm)&&this.RayleighScaleHeightKm>0f
        &&IsFiniteNonNegative(this.MieScatteringPerKm)&&IsFiniteNonNegative(this.MieAbsorptionPerKm)
        &&this.MieScaleHeightKm>0f&&this.MieAnisotropy>-1f&&this.MieAnisotropy<1f
        &&IsFiniteNonNegative(this.OzoneAbsorptionPerKm)&&this.OzoneWidthKm>0f
        &&this.BottomRadiusKm>0f&&this.AtmosphereHeightKm>0f&&float.IsFinite(this.BottomRadiusKm+this.AtmosphereHeightKm)
        &&IsFiniteNonNegative(this.GroundAlbedo)&&float.IsFinite(this.MultiScatteringFactor)&&this.MultiScatteringFactor>=0f;
    internal static bool IsFiniteNonNegative(Vector3 Value)=>
        float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z)&&Value.X>=0f&&Value.Y>=0f&&Value.Z>=0f;
}
/// <summary>A sky light captured from the atmosphere. <see cref="Intensity"/> multiplies the captured radiance.</summary>
public readonly record struct SkyLightSettings(float Intensity,Vector3 LowerHemisphereRadiance){
    public bool IsValid=>float.IsFinite(this.Intensity)&&this.Intensity>=0f&&SkyAtmosphereSettings.IsFiniteNonNegative(this.LowerHemisphereRadiance);
}
public enum ExposureMode {
    /// <summary>Scale = 2^-EV100 (the renderer's original behaviour).</summary>
    Legacy,
    /// <summary>Scale = 2^Compensation / (1.2 * 2^EV100), the physical camera model used by Unreal Engine.</summary>
    PhysicalCamera,
    /// <summary>
    /// Unreal's histogram auto exposure: the physical camera at an EV100 metered from the scene
    /// (<c>LuminanceHistogram.hlsl</c>, metered on the GPU), clamped to [<see cref="ExposureSettings.Ev100"/>, <see cref="ExposureSettings.MaxEv100"/>].
    /// </summary>
    AutoHistogram,
}
public enum ToneMapper {
    /// <summary>Narkowicz ACES fit written to a linear UNORM target (the renderer's original behaviour).</summary>
    LegacyAces,
    /// <summary>Unreal Engine's filmic curve in AP1 followed by the sRGB transfer function.</summary>
    UnrealFilmicSrgb,
}
/// <summary>
/// Exposure model. <see cref="Ev100"/> is the fixed EV100 of <see cref="ExposureMode.PhysicalCamera"/>
/// and the minimum of <see cref="ExposureMode.AutoHistogram"/>; <see cref="MaxEv100"/>,
/// <see cref="SpeedUp"/> and <see cref="SpeedDown"/> (adaptation rates per second toward a
/// brighter / darker scene) apply only to the histogram mode. Unreal's defaults are 3 and 1.
/// </summary>
public readonly record struct ExposureSettings(ExposureMode Mode,float Ev100,float Compensation,float MaxEv100=0f,float SpeedUp=3f,float SpeedDown=1f){
    public static ExposureSettings Legacy=>new(ExposureMode.Legacy,0f,0f);
    public bool IsValid=>float.IsFinite(this.Ev100)&&float.IsFinite(this.Compensation)
        &&(this.Mode!=ExposureMode.AutoHistogram
            ||(float.IsFinite(this.MaxEv100)&&this.MaxEv100>=this.Ev100
                &&float.IsFinite(this.SpeedUp)&&this.SpeedUp>0f&&float.IsFinite(this.SpeedDown)&&this.SpeedDown>0f));
    /// <summary>Linear multiplier applied to scene luminance before tone mapping; the histogram mode starts at its minimum EV100.</summary>
    public float Scale(float CameraEv100)=>this.Mode==ExposureMode.Legacy
        ?PhysicalLightUnits.Ev100ToExposureScale(CameraEv100)
        :this.PhysicalScale(this.Ev100);
    /// <summary>2^Compensation / (1.2 * 2^EV100).</summary>
    public float PhysicalScale(float MeteredEv100)=>MathF.Pow(2f,this.Compensation)/(1.2f*MathF.Pow(2f,MeteredEv100));
}
/// <summary>
/// Exponential height fog in the Unreal Engine formulation: fog amount along a ray is
/// Density * 2^(-Falloff * (height - FogHeight)) integrated in closed form. Values are per metre.
/// </summary>
public readonly record struct PhysicalHeightFogSettings(
    float DensityPerMeter,float HeightFalloffPerMeter,float FogHeightMeters,
    Vector3 InscatteringLuminance,float SkyAtmosphereContributionScale,float MaxOpacity){
    public bool IsValid=>float.IsFinite(this.DensityPerMeter)&&this.DensityPerMeter>=0f
        &&float.IsFinite(this.HeightFalloffPerMeter)&&this.HeightFalloffPerMeter>=0f
        &&float.IsFinite(this.FogHeightMeters)&&SkyAtmosphereSettings.IsFiniteNonNegative(this.InscatteringLuminance)
        &&float.IsFinite(this.SkyAtmosphereContributionScale)&&this.SkyAtmosphereContributionScale>=0f
        &&float.IsFinite(this.MaxOpacity)&&this.MaxOpacity>=0f&&this.MaxOpacity<=1f;
    /// <summary>Density at the camera, the constant factor of the closed-form line integral.</summary>
    public float RayOriginTerms(float CameraAltitudeMeters)=>
        this.DensityPerMeter*MathF.Pow(2f,-this.HeightFalloffPerMeter*(CameraAltitudeMeters-this.FogHeightMeters));
}
/// <summary>
/// Scene-wide environment lighting (ADR-0069). Null members keep the renderer's original path:
/// no atmosphere means the clear colour background and unattenuated directional lights; no sky
/// light means only the flat ambient term; no physical fog means the legacy fog.
/// </summary>
public readonly record struct EnvironmentLightingSettings(
    SkyAtmosphereSettings?Atmosphere,
    SkyLightSettings?SkyLight,
    PhysicalHeightFogSettings?Fog,
    ExposureSettings Exposure,
    ToneMapper ToneMapper,
    float BloomIntensity,
    float SunAngularRadiusRadians,
    GlobalIlluminationSettings?GlobalIllumination=null,
    AmbientOcclusionSettings?AmbientOcclusion=null,
    VolumetricCloudSettings?Clouds=null,
    SkySphereSettings?SkySphere=null,
    GlobalDistanceFieldSettings?DistanceField=null,
    LumenSettings?Lumen=null){
    public static EnvironmentLightingSettings Legacy=>new(null,null,null,ExposureSettings.Legacy,ToneMapper.LegacyAces,1.8f,0.00465f);
    public bool IsValid=>(this.Atmosphere is null||this.Atmosphere.Value.IsValid)
        &&(this.SkyLight is null||this.SkyLight.Value.IsValid)
        &&(this.Fog is null||this.Fog.Value.IsValid)
        &&(this.GlobalIllumination is null||this.GlobalIllumination.Value.IsValid)
        &&(this.AmbientOcclusion is null||this.AmbientOcclusion.Value.IsValid)
        &&(this.Clouds is null||this.Clouds.Value.IsValid)
        &&(this.SkySphere is null||this.SkySphere.Value.IsValid)
        &&(this.DistanceField is null||this.DistanceField.Value.IsValid)
        &&(this.Lumen is null||this.Lumen.Value.IsValid)
        &&this.Exposure.IsValid&&float.IsFinite(this.BloomIntensity)&&this.BloomIntensity>=0f
        &&float.IsFinite(this.SunAngularRadiusRadians)&&this.SunAngularRadiusRadians>0f&&this.SunAngularRadiusRadians<0.5f;
}
/// <summary>
/// Volumetric clouds (ADR-0078/M0110) in Unreal's units: kilometres for the layer and the tracing distances, extinction per metre, the
/// material's coverage, albedo, phase and multiple-scattering controls. <see cref="CameraPositionKm"/> is where the camera is over the planet
/// (world R and F, kilometres) and <see cref="WindOffsetKm"/> how far the wind has carried the clouds; both move the clouds the camera sees and
/// are written every frame. The clouds need the atmosphere: without <see cref="EnvironmentLightingSettings.Atmosphere"/> they are not drawn.
/// </summary>
public readonly record struct VolumetricCloudSettings(
    float LayerBottomAltitudeKm,
    float LayerHeightKm,
    float TracingStartMaxDistanceKm,
    float TracingMaxDistanceKm,
    float DensityPerMeter,
    float Coverage,
    float LayoutScaleKm,
    Vector3 Albedo,
    float PhaseG,
    float PhaseG2,
    float PhaseBlend,
    float MultiScatteringContribution,
    float MultiScatteringOcclusion,
    float MultiScatteringEccentricity,
    float SkyLightBottomOcclusion,
    float StopTracingTransmittanceThreshold,
    Vector2 CameraPositionKm,
    Vector2 WindOffsetKm){
    /// <summary>The values of Unreal's VolumetricCloud with its default material, as the UE 5.8 templates use them.</summary>
    public static VolumetricCloudSettings Unreal=>new(5f,10f,350f,50f,0.008f,-0.2f,256f,new Vector3(0.98f),0.8f,0.166667f,0.575f,0.666667f,0.25f,0.18f,0.5f,0.005f,Vector2.Zero,Vector2.Zero);
    public bool IsValid=>
        float.IsFinite(this.LayerBottomAltitudeKm)&&this.LayerBottomAltitudeKm>=0f&&this.LayerBottomAltitudeKm<=1000f
        &&float.IsFinite(this.LayerHeightKm)&&this.LayerHeightKm>0f&&this.LayerHeightKm<=1000f
        &&float.IsFinite(this.TracingStartMaxDistanceKm)&&this.TracingStartMaxDistanceKm>0f
        &&float.IsFinite(this.TracingMaxDistanceKm)&&this.TracingMaxDistanceKm>0f
        &&float.IsFinite(this.DensityPerMeter)&&this.DensityPerMeter>=0f&&this.DensityPerMeter<=10f
        &&float.IsFinite(this.Coverage)&&this.Coverage>=-1f&&this.Coverage<=1f
        &&float.IsFinite(this.LayoutScaleKm)&&this.LayoutScaleKm>0f
        &&SkyAtmosphereSettings.IsFiniteNonNegative(this.Albedo)&&this.Albedo.X<=1f&&this.Albedo.Y<=1f&&this.Albedo.Z<=1f
        &&this.PhaseG>-1f&&this.PhaseG<1f&&this.PhaseG2>-1f&&this.PhaseG2<1f
        &&float.IsFinite(this.PhaseBlend)&&this.PhaseBlend>=0f&&this.PhaseBlend<=1f
        &&float.IsFinite(this.MultiScatteringContribution)&&this.MultiScatteringContribution>=0f&&this.MultiScatteringContribution<=1f
        &&float.IsFinite(this.MultiScatteringOcclusion)&&this.MultiScatteringOcclusion>=0f&&this.MultiScatteringOcclusion<=1f
        &&float.IsFinite(this.MultiScatteringEccentricity)&&this.MultiScatteringEccentricity>=0f&&this.MultiScatteringEccentricity<=1f
        &&float.IsFinite(this.SkyLightBottomOcclusion)&&this.SkyLightBottomOcclusion>=0f&&this.SkyLightBottomOcclusion<=1f
        &&float.IsFinite(this.StopTracingTransmittanceThreshold)&&this.StopTracingTransmittanceThreshold>=0f&&this.StopTracingTransmittanceThreshold<1f
        &&float.IsFinite(this.CameraPositionKm.X)&&float.IsFinite(this.CameraPositionKm.Y)
        &&float.IsFinite(this.WindOffsetKm.X)&&float.IsFinite(this.WindOffsetKm.Y);
}
/// <summary>
/// The sky sphere (ADR-0078/M0110, Unreal's SM_SkySphere with M_SimpleSkyDome): the sky is drawn on every pixel whose surface is farther than
/// <see cref="RadiusMeters"/> from <see cref="Center"/> (render space, so it moves with the camera origin like every object) as well as on the
/// pixels no geometry covers.
/// </summary>
public readonly record struct SkySphereSettings(Vector3 Center,float RadiusMeters){
    public bool IsValid=>float.IsFinite(this.Center.X)&&float.IsFinite(this.Center.Y)&&float.IsFinite(this.Center.Z)
        &&float.IsFinite(this.RadiusMeters)&&this.RadiusMeters>0f;
}
/// <summary>
/// The global distance field (ADR-0078/M0110): <see cref="ClipmapCount"/> clipmaps of <see cref="Resolution"/>^3 voxels around the camera, the finest
/// with voxels of <see cref="VoxelSizeMeters"/> and each next one twice as coarse, made from the objects' mesh distance fields and kept up to date a
/// brick at a time. The voxels are fixed to the world: <see cref="OriginVoxel"/> is the world voxel (of the finest level) the render-space origin is in,
/// and <see cref="OriginRemainder"/> how far the origin is past that voxel's corner, so the field does not swim when the camera moves. A caller that
/// renders in world coordinates leaves both at zero.
/// </summary>
public readonly record struct GlobalDistanceFieldSettings(float VoxelSizeMeters,int ClipmapCount,int Resolution,LongVector3 OriginVoxel,Vector3 OriginRemainder){
    public const int MaximumClipmapCount=4;
    public static GlobalDistanceFieldSettings Default=>new(0.5f,4,64,default,Vector3.Zero);
    public bool IsValid=>
        float.IsFinite(this.VoxelSizeMeters)&&this.VoxelSizeMeters>=0.01f&&this.VoxelSizeMeters<=100f
        &&this.ClipmapCount is>=1 and<=MaximumClipmapCount
        &&this.Resolution is>=16 and<=128&&this.Resolution%8==0
        &&float.IsFinite(this.OriginRemainder.X)&&float.IsFinite(this.OriginRemainder.Y)&&float.IsFinite(this.OriginRemainder.Z)
        &&this.OriginRemainder.X>=0f&&this.OriginRemainder.Y>=0f&&this.OriginRemainder.Z>=0f
        &&this.OriginRemainder.X<=this.VoxelSizeMeters&&this.OriginRemainder.Y<=this.VoxelSizeMeters&&this.OriginRemainder.Z<=this.VoxelSizeMeters
        &&Math.Abs(this.OriginVoxel.X)<(1L<<52)&&Math.Abs(this.OriginVoxel.Y)<(1L<<52)&&Math.Abs(this.OriginVoxel.Z)<(1L<<52);
}
/// <summary>Three 64-bit integers: a voxel's coordinates on the world's voxel grid.</summary>
public readonly record struct LongVector3(long X,long Y,long Z);
/// <summary>
/// The scene's Lumen-like dynamic global illumination and reflections (ADR-0078/M0111) in Unreal's PostProcessVolume terms. Distances are metres
/// (Unreal's centimetres / 100); the qualities and speeds keep Unreal's scales (1 is the default). <see cref="GlobalIllumination"/> and
/// <see cref="Reflections"/> are Unreal's Global Illumination Method and Reflection Method set to Lumen. The renderer uses these only when its
/// scalability (<see cref="RenderScalability"/>) allows Lumen and the environment has a global distance field to trace; otherwise the probe volume
/// (DDGI) and the sky light keep lighting the scene as before. Nothing is baked: the surface cache, the radiance cache and the screen probes are
/// recomputed every frame.
/// </summary>
public readonly record struct LumenSettings(
    bool GlobalIllumination,
    bool Reflections,
    float SceneLightingQuality,
    float SceneDetail,
    float SceneViewDistanceMeters,
    float SceneLightingUpdateSpeed,
    float FinalGatherQuality,
    float FinalGatherLightingUpdateSpeed,
    bool FinalGatherScreenTraces,
    float MaxTraceDistanceMeters,
    float DiffuseColorBoost,
    float SkylightLeaking,
    float ReflectionQuality,
    float MaxRoughnessToTraceReflections,
    bool ReflectionScreenTraces,
    float SurfaceCacheResolution){
    /// <summary>Unreal 5.8's PostProcessVolume defaults, which all four templates use (tools/UnrealExport/probe_lumen.py).</summary>
    public static LumenSettings Unreal=>new(true,true,1f,1f,200f,1f,1f,1f,true,200f,1f,0f,1f,0.4f,true,1f);
    public bool IsValid=>
        InRange(this.SceneLightingQuality,0.25f,4f)&&InRange(this.SceneDetail,0.25f,4f)
        &&InRange(this.SceneViewDistanceMeters,1f,100000f)&&InRange(this.SceneLightingUpdateSpeed,0.5f,4f)
        &&InRange(this.FinalGatherQuality,0.25f,4f)&&InRange(this.FinalGatherLightingUpdateSpeed,0.5f,4f)
        &&InRange(this.MaxTraceDistanceMeters,1f,100000f)&&InRange(this.DiffuseColorBoost,1f,4f)&&InRange(this.SkylightLeaking,0f,1f)
        &&InRange(this.ReflectionQuality,0.25f,4f)&&InRange(this.MaxRoughnessToTraceReflections,0f,1f)&&InRange(this.SurfaceCacheResolution,0.5f,1f);
    private static bool InRange(float Value,float Minimum,float Maximum)=>float.IsFinite(Value)&&Value>=Minimum&&Value<=Maximum;
}
/// <summary>Unreal's scalability levels (sg.GlobalIlluminationQuality / sg.ReflectionQuality 0-3).</summary>
public enum ScalabilityLevel {
    Low,
    Medium,
    High,
    Epic,
}
/// <summary>
/// The adapter-level quality of the dynamic lighting (ADR-0078/M0111), Unreal's sg.GlobalIlluminationQuality and sg.ReflectionQuality. As in
/// Unreal's BaseScalability.ini, Lumen's GI needs High or Epic (High traces a screen probe every 32 pixels, Epic every 16) and Lumen's reflections
/// need High (half resolution) or Epic (full resolution). Below that the renderer keeps the probe volume (DDGI) and the sky light's specular, the
/// frames of M0110 and earlier (Unreal's Medium uses an irradiance-field final gather, the counterpart of the probe volume). The default is High on
/// a hardware adapter and Medium on WARP, which draws on the CPU. Epic (Unreal's templates) is opt-in: on WARP it intermittently lost the device
/// and was not bit-reproducible (M0111), and it has not been run on a hardware GPU yet.
/// </summary>
public readonly record struct RenderScalability(ScalabilityLevel GlobalIllumination,ScalabilityLevel Reflections){
    public static RenderScalability Epic=>new(ScalabilityLevel.Epic,ScalabilityLevel.Epic);
    public static RenderScalability High=>new(ScalabilityLevel.High,ScalabilityLevel.High);
    public static RenderScalability Medium=>new(ScalabilityLevel.Medium,ScalabilityLevel.Medium);
    public static RenderScalability DefaultFor(bool IsWarp)=>IsWarp?Medium:High;
    public bool AllowsLumenGlobalIllumination=>this.GlobalIllumination>=ScalabilityLevel.High;
    /// <summary>Lumen's reflections also need Lumen's GI (their rough lobes and misses read its probes).</summary>
    public bool AllowsLumenReflections=>this.AllowsLumenGlobalIllumination&&this.Reflections>=ScalabilityLevel.High;
}
/// <summary>Unreal's r.Lumen.Visualize for debugging (ADR-0078/M0111): what the frame shows instead of the lit scene while Lumen runs.</summary>
public enum LumenVisualization {
    None,
    /// <summary>The final gather's diffuse irradiance on a white surface.</summary>
    Diffuse,
    /// <summary>The reflected radiance.</summary>
    Reflections,
    /// <summary>The surface cache's radiance where each pixel's surface is (black where no card saw it).</summary>
    SurfaceCache,
}
/// <summary>
/// Reads <see cref="RenderScalability"/> overrides from CGE_SCALABILITY_GI and CGE_SCALABILITY_REFLECTIONS (low, medium, high, epic), and the debugging
/// view from CGE_LUMEN_VISUALIZE (none, diffuse, reflections, surfacecache).
/// </summary>
public static class RenderScalabilityResolver {
    public const string GlobalIlluminationVariable="CGE_SCALABILITY_GI";
    public const string ReflectionsVariable="CGE_SCALABILITY_REFLECTIONS";
    public const string VisualizeVariable="CGE_LUMEN_VISUALIZE";
    public static LumenVisualization VisualizationFromEnvironment()=>(Environment.GetEnvironmentVariable(VisualizeVariable)??string.Empty).Trim().ToLowerInvariant() switch {
        ""or"none"=>LumenVisualization.None,
        "diffuse"=>LumenVisualization.Diffuse,
        "reflections"=>LumenVisualization.Reflections,
        "surfacecache"=>LumenVisualization.SurfaceCache,
        _=>throw new InvalidOperationException($"{VisualizeVariable} must be none, diffuse, reflections or surfacecache."),
    };
    /// <summary>The adapter's default, with each level a variable names replacing it; the reflections follow the GI level when only that is named.</summary>
    public static RenderScalability FromEnvironment(bool IsWarp)=>Resolve(IsWarp,Environment.GetEnvironmentVariable(GlobalIlluminationVariable),Environment.GetEnvironmentVariable(ReflectionsVariable));
    public static RenderScalability Resolve(bool IsWarp,string?GlobalIllumination,string?Reflections){
        var Default=RenderScalability.DefaultFor(IsWarp);
        var Gi=Parse(GlobalIllumination,GlobalIlluminationVariable)??Default.GlobalIllumination;
        var Reflection=Parse(Reflections,ReflectionsVariable)??(string.IsNullOrWhiteSpace(GlobalIllumination)?Default.Reflections:Gi);
        return new RenderScalability(Gi,Reflection);
    }
    private static ScalabilityLevel?Parse(string?Text,string Variable){
        if(string.IsNullOrWhiteSpace(Text)){
            return null;
        }
        return Text.Trim().ToLowerInvariant() switch {
            "low" or "0"=>ScalabilityLevel.Low,
            "medium" or "1"=>ScalabilityLevel.Medium,
            "high" or "2"=>ScalabilityLevel.High,
            "epic" or "3"=>ScalabilityLevel.Epic,
            _=>throw new InvalidOperationException($"{Variable} must be low, medium, high or epic."),
        };
    }
}
/// <summary>
/// Screen-space ambient occlusion of indirect light (ADR-0069), in Unreal Engine's terms:
/// <see cref="Intensity"/> blends between no occlusion (0) and the full obscurance (1) and
/// <see cref="RadiusMeters"/> is the world-space sampling radius.
/// </summary>
public readonly record struct AmbientOcclusionSettings(float Intensity,float RadiusMeters){
    public bool IsValid=>float.IsFinite(this.Intensity)&&this.Intensity>=0f&&this.Intensity<=1f
        &&float.IsFinite(this.RadiusMeters)&&this.RadiusMeters>0f;
}
/// <summary>
/// Dynamic diffuse global illumination probe volume (ADR-0069). <see cref="Center"/> is in the
/// renderer's camera-relative space of the current frame, so it moves with the camera origin
/// exactly like every object transform; the other members describe the grid layout. Probes sit on
/// a regular grid spanning the box, at least two per axis, and the renderer re-captures
/// <see cref="ProbesPerFrame"/> of them each frame (round robin).
/// </summary>
public readonly record struct GlobalIlluminationSettings(
    Vector3 Center,
    Vector3 HalfExtent,
    float ProbeSpacingMeters,
    int ProbesPerFrame,
    float Hysteresis,
    float Intensity){
    /// <summary>Probe capacity of the fixed-size irradiance and distance atlases.</summary>
    public const int MaxProbeCount=4096;
    public const int MaxProbesPerFrame=64;
    public bool IsValid=>
        SkyAtmosphereSettings.IsFiniteNonNegative(this.HalfExtent)&&this.HalfExtent.X>0f&&this.HalfExtent.Y>0f&&this.HalfExtent.Z>0f
        &&float.IsFinite(this.Center.X)&&float.IsFinite(this.Center.Y)&&float.IsFinite(this.Center.Z)
        &&float.IsFinite(this.ProbeSpacingMeters)&&this.ProbeSpacingMeters>0f
        &&this.ProbesPerFrame is>0 and<=MaxProbesPerFrame
        &&float.IsFinite(this.Hysteresis)&&this.Hysteresis>=0f&&this.Hysteresis<1f
        &&float.IsFinite(this.Intensity)&&this.Intensity>=0f
        &&ProbeCountFor(this.HalfExtent,this.ProbeSpacingMeters)<=MaxProbeCount;
    /// <summary>Probes along each axis: the box edge divided by the spacing, plus one, and at least two.</summary>
    public (int X,int Y,int Z) ProbeCounts=>(AxisCount(this.HalfExtent.X,this.ProbeSpacingMeters),AxisCount(this.HalfExtent.Y,this.ProbeSpacingMeters),AxisCount(this.HalfExtent.Z,this.ProbeSpacingMeters));
    public int ProbeCount=>(int)Math.Min(ProbeCountFor(this.HalfExtent,this.ProbeSpacingMeters),int.MaxValue);
    /// <summary>Actual distance between neighbouring probes per axis; the grid spans the box exactly.</summary>
    public Vector3 GridSpacing{
        get {
            var Counts=this.ProbeCounts;
            return this.HalfExtent*2f/new Vector3(Counts.X-1,Counts.Y-1,Counts.Z-1);
        }
    }
    public Vector3 GridOrigin=>this.Center-this.HalfExtent;
    public Vector3 ProbePosition(int Index){
        var Counts=this.ProbeCounts;
        var X=Index%Counts.X;
        var Y=Index/Counts.X%Counts.Y;
        var Z=Index/(Counts.X*Counts.Y);
        return this.GridOrigin+new Vector3(X,Y,Z)*this.GridSpacing;
    }
    private static int AxisCount(float HalfExtent,float Spacing){
        var Count=MathF.Floor(2f*HalfExtent/Spacing)+1f;
        return float.IsFinite(Count)?(int)Math.Clamp(Count,2f,MaxProbeCount):MaxProbeCount+1;
    }
    private static long ProbeCountFor(Vector3 HalfExtent,float Spacing)=>
        (long)AxisCount(HalfExtent.X,Spacing)*AxisCount(HalfExtent.Y,Spacing)*AxisCount(HalfExtent.Z,Spacing);
}
/// <summary>
/// CPU reference for the atmosphere's transmittance. The renderer attenuates directional lights
/// with it and tests use it as the oracle for the GPU transmittance LUT.
/// </summary>
public static class AtmosphereTransmittance {
    private const int SampleCount=64;
    /// <summary>Transmittance from a point at <paramref name="AltitudeMeters"/> above the ground toward <paramref name="DirectionTowardLight"/> (+Y up).</summary>
    public static Vector3 Evaluate(in SkyAtmosphereSettings Atmosphere,float AltitudeMeters,Vector3 DirectionTowardLight){
        if(!Atmosphere.IsValid){
            throw new ArgumentException("Atmosphere settings must be finite and physically valid.",nameof(Atmosphere));
        }
        var Direction=Vector3.Normalize(DirectionTowardLight);
        if(!float.IsFinite(Direction.X)||!float.IsFinite(AltitudeMeters)){
            throw new ArgumentException("The direction and altitude must be finite.");
        }
        var Origin=new Vector3(0f,Atmosphere.BottomRadiusKm+MathF.Max(AltitudeMeters,0f)*0.001f,0f);
        // Only a ray heading below the local horizon can be blocked by the planet; an origin exactly
        // on the ground would otherwise report a zero-distance hit for every upward direction.
        if(Vector3.Dot(Origin,Direction)<0f&&IntersectNearest(Origin,Direction,Atmosphere.BottomRadiusKm)>0f){
            return Vector3.Zero;
        }
        var Distance=IntersectNearest(Origin,Direction,Atmosphere.TopRadiusKm);
        if(Distance<=0f){
            return Vector3.One;
        }
        var Step=Distance/SampleCount;
        var OpticalDepth=Vector3.Zero;
        for(var Index=0;Index<SampleCount;Index++){
            var Point=Origin+Direction*((Index+0.5f)*Step);
            OpticalDepth+=Extinction(Atmosphere,Point)*Step;
        }
        return new Vector3(MathF.Exp(-OpticalDepth.X),MathF.Exp(-OpticalDepth.Y),MathF.Exp(-OpticalDepth.Z));
    }
    internal static Vector3 Extinction(in SkyAtmosphereSettings Atmosphere,Vector3 Point){
        var Height=MathF.Max(Point.Length()-Atmosphere.BottomRadiusKm,0f);
        var Rayleigh=MathF.Exp(-Height/Atmosphere.RayleighScaleHeightKm);
        var Mie=MathF.Exp(-Height/Atmosphere.MieScaleHeightKm);
        var Ozone=MathF.Max(0f,1f-MathF.Abs(Height-Atmosphere.OzoneTipAltitudeKm)/(Atmosphere.OzoneWidthKm*0.5f));
        return Atmosphere.RayleighScatteringPerKm*Rayleigh+(Atmosphere.MieScatteringPerKm+Atmosphere.MieAbsorptionPerKm)*Mie+Atmosphere.OzoneAbsorptionPerKm*Ozone;
    }
    private static float IntersectNearest(Vector3 Origin,Vector3 Direction,float Radius){
        var B=Vector3.Dot(Origin,Direction);
        var C=Vector3.Dot(Origin,Origin)-Radius*Radius;
        var Discriminant=B*B-C;
        if(Discriminant<0f){
            return -1f;
        }
        var Root=MathF.Sqrt(Discriminant);
        var T0=-B-Root;
        var T1=-B+Root;
        if(T0<0f&&T1<0f){
            return -1f;
        }
        return T0<0f?MathF.Max(0f,T1):MathF.Max(0f,T0);
    }
}
