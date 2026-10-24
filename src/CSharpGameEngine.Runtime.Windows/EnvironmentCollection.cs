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
    internal GlobalIlluminationVolume?GlobalIllumination;
    /// <summary>Camera-relative centre of <see cref="GlobalIllumination"/> for this frame.</summary>
    internal Vector3 GlobalIlluminationCenter;
    internal VolumetricCloud?Clouds;
    internal SkySphere?SkySphere;
    /// <summary>Camera-relative centre of <see cref="SkySphere"/> for this frame.</summary>
    internal Vector3 SkySphereCenter;
    internal GlobalDistanceField?DistanceField;
    /// <summary>The world voxel of the finest clipmap the camera is in and how far past its corner, set by EngineHost before <see cref="Build"/>.</summary>
    internal LongVector3 DistanceFieldOriginVoxel;
    internal Vector3 DistanceFieldOriginRemainder;
    /// <summary>Where the camera is over the world (R and F, metres), set by EngineHost before <see cref="Build"/>: the clouds the camera sees depend on it.</summary>
    internal Vector2 CameraHorizontalMeters;
    /// <summary>Seconds of game time the clouds have been blowing, set by EngineHost before <see cref="Build"/>.</summary>
    internal float CloudSeconds;
    internal bool AmbientSubmitted;
    internal string?InvalidReason;
    internal void Reset(){
        this.Atmosphere=null;
        this.SkyLight=null;
        this.PostProcess=null;
        this.Fog=null;
        this.GlobalIllumination=null;
        this.GlobalIlluminationCenter=Vector3.Zero;
        this.Clouds=null;
        this.SkySphere=null;
        this.SkySphereCenter=Vector3.Zero;
        this.DistanceField=null;
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
        var Exposure=Post switch {
            PostProcessVolume { ExposureMethod:ExposureMethod.Manual } P=>new ExposureSettings(ExposureMode.PhysicalCamera,P.MinEv100,P.ExposureCompensation),
            PostProcessVolume { ExposureMethod:ExposureMethod.AutoHistogram } P=>new ExposureSettings(ExposureMode.AutoHistogram,P.MinEv100,P.ExposureCompensation,P.MaxEv100,P.AutoExposureSpeedUp,P.AutoExposureSpeedDown),
            _=>ExposureSettings.Legacy,
        };
        var ToneMapperValue=Post?.ToneMapper==ToneMapperKind.UnrealFilmic?ToneMapper.UnrealFilmicSrgb:ToneMapper.LegacyAces;
        var Bloom=Post?.BloomIntensity??EnvironmentLightingSettings.Legacy.BloomIntensity;
        var SunRadius=(this.Atmosphere?.SunDiskAngleDegrees??0.5357f)*MathF.PI/360f;
        GlobalIlluminationSettings?GlobalIllumination=this.GlobalIllumination is GlobalIlluminationVolume G
            ?new GlobalIlluminationSettings(this.GlobalIlluminationCenter,new Vector3(G.HalfExtentX,G.HalfExtentY,G.HalfExtentZ),
                G.ProbeSpacingMeters,G.ProbesPerFrame,G.Hysteresis,G.Intensity)
            :null;
        AmbientOcclusionSettings?AmbientOcclusion=Post is PostProcessVolume Occlusion&&Occlusion.AmbientOcclusionIntensity>0f
            ?new AmbientOcclusionSettings(Occlusion.AmbientOcclusionIntensity,Occlusion.AmbientOcclusionRadiusMeters)
            :null;
        if(GlobalIllumination is { IsValid:false }){
            // An oversized or malformed probe volume only disables GI; the sky and exposure stay.
            this.InvalidReason="GlobalIlluminationVolume";
            GlobalIllumination=null;
        }
        var Clouds=this.Clouds is VolumetricCloud C?ToCloudSettings(C,this.CameraHorizontalMeters,this.CloudSeconds):(VolumetricCloudSettings?)null;
        if(Clouds is { IsValid:false }){
            // Like the probe volume, an unusable cloud value only turns the clouds off.
            this.InvalidReason=nameof(VolumetricCloud);
            Clouds=null;
        }
        SkySphereSettings?Sphere=this.SkySphere is SkySphere Dome?new SkySphereSettings(this.SkySphereCenter,Dome.RadiusMeters):null;
        if(Sphere is { IsValid:false }){
            this.InvalidReason=nameof(SkySphere);
            Sphere=null;
        }
        GlobalDistanceFieldSettings?DistanceField=this.DistanceField is GlobalDistanceField Field
            ?new GlobalDistanceFieldSettings(Field.VoxelSizeMeters,Field.ClipmapCount,Field.Resolution,this.DistanceFieldOriginVoxel,this.DistanceFieldOriginRemainder)
            :null;
        if(DistanceField is { IsValid:false }){
            this.InvalidReason=nameof(GlobalDistanceField);
            DistanceField=null;
        }
        return new EnvironmentLightingSettings(Atmosphere,Sky,Fog,Exposure,ToneMapperValue,Bloom,SunRadius,GlobalIllumination,AmbientOcclusion,Clouds,Sphere,DistanceField);
    }
    /// <summary>
    /// The renderer's clouds from the component: the camera's place over the world and the wind's travel since the game started, both in
    /// kilometres. The wind blows toward <see cref="VolumetricCloud.WindDirectionDegrees"/>, clockwise from +F.
    /// </summary>
    internal static VolumetricCloudSettings ToCloudSettings(in VolumetricCloud Cloud,Vector2 CameraHorizontalMeters,float Seconds){
        var Radians=Cloud.WindDirectionDegrees*MathF.PI/180f;
        var Travel=Cloud.WindSpeedMetersPerSecond*Seconds*0.001f;
        return new VolumetricCloudSettings(Cloud.LayerBottomAltitudeKm,Cloud.LayerHeightKm,Cloud.TracingStartMaxDistanceKm,Cloud.TracingMaxDistanceKm,
            Cloud.Density,Cloud.Coverage,Cloud.LayoutScaleKm,new Vector3(Cloud.AlbedoR,Cloud.AlbedoG,Cloud.AlbedoB),Cloud.PhaseG,Cloud.PhaseG2,Cloud.PhaseBlend,
            Cloud.MultiScatteringContribution,Cloud.MultiScatteringOcclusion,Cloud.MultiScatteringEccentricity,Cloud.SkyLightCloudBottomOcclusion,
            Cloud.StopTracingTransmittanceThreshold,CameraHorizontalMeters*0.001f,new Vector2(MathF.Sin(Radians),MathF.Cos(Radians))*Travel);
    }
}
/// <summary>
/// ADR-0078/M0110: where a fixed-point world position falls on a voxel grid: the voxel (floor of position / voxel) and how far past that voxel's corner,
/// computed on the fixed-point value so the global distance field's voxels stay on the world grid however far the camera goes. False when the voxel
/// size is not positive or the voxel index does not fit 64 bits.
/// </summary>
internal static class WorldVoxelGrid {
    internal static bool TryLocate(CSharpGameEngine.Numerics.LogicalScalar Position,float VoxelSizeMeters,out long Voxel,out float Remainder){
        Voxel=0;
        Remainder=0f;
        if(!(VoxelSizeMeters>0f)||!CSharpGameEngine.Numerics.LogicalScalar.TryFromSingle(VoxelSizeMeters,out var Size)||Size.Raw<=0){
            return false;
        }
        var Quotient=Position.Raw/Size.Raw;
        var Rest=Position.Raw%Size.Raw;
        if(Rest<0){
            Rest+=Size.Raw;
            Quotient--;
        }
        if(Quotient>long.MaxValue||Quotient<long.MinValue){
            return false;
        }
        Voxel=(long)Quotient;
        CSharpGameEngine.Numerics.LogicalScalar.FromRaw(Rest).TryToSingle(out Remainder);
        Remainder=Math.Clamp(Remainder,0f,VoxelSizeMeters);
        return true;
    }
}
/// <summary>
/// Game time for the environment's motion (ADR-0078/M0110: the clouds' wind), summed from the frames' elapsed counters. Whole seconds and the
/// remainder are kept as integers, so the time does not lose precision as it grows.
/// </summary>
internal struct EnvironmentClock {
    private long WholeSeconds;
    private long RemainderCounter;
    private long Frequency;
    internal void Advance(long ElapsedCounter,long CounterFrequency){
        if(ElapsedCounter<=0||CounterFrequency<=0){ return; }
        if(CounterFrequency!=this.Frequency){
            this.RemainderCounter=this.Frequency>0?this.RemainderCounter*CounterFrequency/this.Frequency:0;
            this.Frequency=CounterFrequency;
        }
        this.RemainderCounter+=ElapsedCounter;
        this.WholeSeconds+=this.RemainderCounter/CounterFrequency;
        this.RemainderCounter%=CounterFrequency;
    }
    internal readonly float Seconds=>this.Frequency>0?this.WholeSeconds+(float)this.RemainderCounter/this.Frequency:this.WholeSeconds;
}
