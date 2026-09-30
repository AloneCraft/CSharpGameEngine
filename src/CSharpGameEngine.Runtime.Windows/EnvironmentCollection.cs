using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Runtime.Lighting;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// Per-frame collection of the scene's environment components (ADR-0069). Presentation Systems
/// record what exists; EngineHost converts it to renderer settings once, just before the frame is
/// rendered. Absent components keep the renderer's original path.
/// </summary>
internal sealed class EnvironmentCollection {
    internal SkyAtmosphere?Atmosphere;
    internal SkyLight?SkyLight;
    internal PostProcessVolume?PostProcess;
    internal ExponentialHeightFog?Fog;
    internal bool AmbientSubmitted;
    internal string?InvalidReason;
    internal void Reset(){
        this.Atmosphere=null;
        this.SkyLight=null;
        this.PostProcess=null;
        this.Fog=null;
        this.AmbientSubmitted=false;
    }
    internal EnvironmentLightingSettings Build(){
        SkyAtmosphereSettings?Atmosphere=this.Atmosphere is SkyAtmosphere A
            ?new SkyAtmosphereSettings(
                new Vector3(A.RayleighScatteringR,A.RayleighScatteringG,A.RayleighScatteringB)*A.RayleighScatteringScale,
                A.RayleighExponentialDistributionKm,
                new Vector3(A.MieScatteringR,A.MieScatteringG,A.MieScatteringB)*A.MieScatteringScale,
                new Vector3(A.MieAbsorptionR,A.MieAbsorptionG,A.MieAbsorptionB)*A.MieAbsorptionScale,
                A.MieExponentialDistributionKm,A.MieAnisotropy,
                new Vector3(A.OtherAbsorptionR,A.OtherAbsorptionG,A.OtherAbsorptionB)*A.OtherAbsorptionScale,
                A.OtherTentTipAltitudeKm,A.OtherTentWidthKm,A.BottomRadiusKm,A.AtmosphereHeightKm,
                new Vector3(A.GroundAlbedoR,A.GroundAlbedoG,A.GroundAlbedoB),A.MultiScatteringFactor)
            :null;
        SkyLightSettings?Sky=Atmosphere is not null&&this.SkyLight is SkyLight S
            ?new SkyLightSettings(S.Intensity,new Vector3(S.LowerHemisphereR,S.LowerHemisphereG,S.LowerHemisphereB))
            :null;
        PhysicalHeightFogSettings?Fog=this.Fog is ExponentialHeightFog F
            ?new PhysicalHeightFogSettings(F.FogDensity*0.1f,F.FogHeightFalloff*0.1f,F.FogHeight,
                new Vector3(F.InscatteringLuminanceR,F.InscatteringLuminanceG,F.InscatteringLuminanceB),
                F.SkyAtmosphereAmbientContributionScale,F.FogMaxOpacity)
            :null;
        var Post=this.PostProcess;
        var Exposure=Post is PostProcessVolume P&&P.ExposureMethod!=ExposureMethod.Legacy
            ?new ExposureSettings(ExposureMode.PhysicalCamera,P.MinEv100,P.ExposureCompensation)
            :ExposureSettings.Legacy;
        var ToneMapperValue=Post?.ToneMapper==ToneMapperKind.UnrealFilmic?ToneMapper.UnrealFilmicSrgb:ToneMapper.LegacyAces;
        var Bloom=Post?.BloomIntensity??EnvironmentLightingSettings.Legacy.BloomIntensity;
        var SunRadius=(this.Atmosphere?.SunDiskAngleDegrees??0.5357f)*MathF.PI/360f;
        return new EnvironmentLightingSettings(Atmosphere,Sky,Fog,Exposure,ToneMapperValue,Bloom,SunRadius);
    }
}
