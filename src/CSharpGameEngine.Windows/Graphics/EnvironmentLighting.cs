using System.Numerics;
namespace CSharpGameEngine.Graphics.D3D12;
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
}
public enum ToneMapper {
    /// <summary>Narkowicz ACES fit written to a linear UNORM target (the renderer's original behaviour).</summary>
    LegacyAces,
    /// <summary>Unreal Engine's filmic curve in AP1 followed by the sRGB transfer function.</summary>
    UnrealFilmicSrgb,
}
public readonly record struct ExposureSettings(ExposureMode Mode,float Ev100,float Compensation){
    public static ExposureSettings Legacy=>new(ExposureMode.Legacy,0f,0f);
    public bool IsValid=>float.IsFinite(this.Ev100)&&float.IsFinite(this.Compensation);
    /// <summary>Linear multiplier applied to scene luminance before tone mapping.</summary>
    public float Scale(float CameraEv100)=>this.Mode==ExposureMode.PhysicalCamera
        ?MathF.Pow(2f,this.Compensation)/(1.2f*MathF.Pow(2f,this.Ev100))
        :PhysicalLightUnits.Ev100ToExposureScale(CameraEv100);
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
    GlobalIlluminationSettings?GlobalIllumination=null){
    public static EnvironmentLightingSettings Legacy=>new(null,null,null,ExposureSettings.Legacy,ToneMapper.LegacyAces,1.8f,0.00465f);
    public bool IsValid=>(this.Atmosphere is null||this.Atmosphere.Value.IsValid)
        &&(this.SkyLight is null||this.SkyLight.Value.IsValid)
        &&(this.Fog is null||this.Fog.Value.IsValid)
        &&(this.GlobalIllumination is null||this.GlobalIllumination.Value.IsValid)
        &&this.Exposure.IsValid&&float.IsFinite(this.BloomIntensity)&&this.BloomIntensity>=0f
        &&float.IsFinite(this.SunAngularRadiusRadians)&&this.SunAngularRadiusRadians>0f&&this.SunAngularRadiusRadians<0.5f;
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
