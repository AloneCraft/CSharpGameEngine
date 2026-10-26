using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Runtime.Lighting;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// シーンの環境コンポーネントをフレームごとに収集する（ADR-0069）。プレゼンテーションシステムは存在する値を記録し、
/// EngineHostが描画直前に一度だけレンダラー設定へ変換する。コンポーネントがない場合、レンダラーは従来の経路を維持する。
/// </summary>
internal sealed class EnvironmentCollection {
    internal SkyAtmosphere?Atmosphere;
    internal DirectionalShadowRendering?DirectionalShadows;
    internal SkyLight?SkyLight;
    internal PostProcessVolume?PostProcess;
    internal ExponentialHeightFog?Fog;
    internal GlobalIlluminationVolume?GlobalIllumination;
    /// <summary>このフレームの<see cref="GlobalIllumination"/>のカメラ相対中心。</summary>
    internal Vector3 GlobalIlluminationCenter;
    internal VolumetricCloud?Clouds;
    internal SkySphere?SkySphere;
    /// <summary>このフレームの<see cref="SkySphere"/>のカメラ相対中心。</summary>
    internal Vector3 SkySphereCenter;
    internal GlobalDistanceField?DistanceField;
    /// <summary>カメラが位置する最小クリップマップのワールドボクセルと、その角からの距離。EngineHostが<see cref="Build"/>前に設定する。</summary>
    internal LongVector3 DistanceFieldOriginVoxel;
    internal Vector3 DistanceFieldOriginRemainder;
    /// <summary>ワールド上でのカメラ位置（RとF、メートル単位）。EngineHostが<see cref="Build"/>前に設定する。カメラから見える雲はこの位置に依存する。</summary>
    internal Vector2 CameraHorizontalMeters;
    /// <summary>雲が動いたゲーム時間（秒）。EngineHostが<see cref="Build"/>前に設定する。</summary>
    internal float CloudSeconds;
    internal bool AmbientSubmitted;
    internal string?InvalidReason;
    internal void Reset(){
        this.Atmosphere=null;
        this.DirectionalShadows=null;
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
            // プローブボリュームが大きすぎる、または不正な場合はGIだけを無効にする。空と露出は維持する。
            this.InvalidReason="GlobalIlluminationVolume";
            GlobalIllumination=null;
        }
        var Clouds=this.Clouds is VolumetricCloud C?ToCloudSettings(C,this.CameraHorizontalMeters,this.CloudSeconds):(VolumetricCloudSettings?)null;
        if(Clouds is { IsValid:false }){
            // プローブボリュームと同様、雲の値が使用不能でも雲だけを無効にする。
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
        var Lumen=Post is PostProcessVolume Volume?ToLumenSettings(Volume):(LumenSettings?)null;
        if(Lumen is { IsValid:false }){
            // Lumenの値が使用不能な場合はLumenだけを無効にする。プローブボリュームとスカイライトは維持する。
            this.InvalidReason=nameof(LumenSettings);
            Lumen=null;
        }
        var Shadows=this.DirectionalShadows is DirectionalShadowRendering Policy?new CSharpGameEngine.Rendering.DirectionalShadowSettings(Policy.Mode,Policy.PageBudget,Policy.Compare,Policy.SoftShadows,Policy.SourceAngularDiameterDegrees,Policy.MaximumSoftShadowRadiusMeters):(CSharpGameEngine.Rendering.DirectionalShadowSettings?)null;
        return new EnvironmentLightingSettings(Atmosphere,Sky,Fog,Exposure,ToneMapperValue,Bloom,SunRadius,GlobalIllumination,AmbientOcclusion,Clouds,Sphere,DistanceField,Lumen,Shadows);
    }
    /// <summary>
    /// PostProcessVolumeから取得するレンダラーのLumen設定（ADR-0078/M0111）。GIも反射もLumenを使わなければnull。
    /// 反射にはLumenのGIが必要なので、Lumen反射だけを使うボリュームの場合、どちらも有効にしない。
    /// </summary>
    internal static LumenSettings?ToLumenSettings(in PostProcessVolume Volume){
        if(Volume.GlobalIlluminationMethod!=GlobalIlluminationMethod.Lumen){
            return null;
        }
        return new LumenSettings(true,Volume.ReflectionMethod==ReflectionMethod.Lumen,Volume.LumenSceneLightingQuality,Volume.LumenSceneDetail,
            Volume.LumenSceneViewDistanceMeters,Volume.LumenSceneLightingUpdateSpeed,Volume.LumenFinalGatherQuality,Volume.LumenFinalGatherLightingUpdateSpeed,
            Volume.LumenFinalGatherScreenTraces,Volume.LumenMaxTraceDistanceMeters,Volume.LumenDiffuseColorBoost,Volume.LumenSkylightLeaking,Volume.LumenReflectionQuality,
            Volume.LumenMaxRoughnessToTraceReflections,Volume.LumenReflectionsScreenTraces,Volume.LumenSurfaceCacheResolution);
    }
    /// <summary>
    /// コンポーネントから取得するレンダラー用の雲。カメラのワールド上の位置と、ゲーム開始後の風の移動距離を、いずれもキロメートルで表す。
    /// 風は<see cref="VolumetricCloud.WindDirectionDegrees"/>の方向、+Fを基準に時計回りに吹く。
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
/// 固定小数点のワールド位置がボクセル格子のどこにあるか（ADR-0078/M0110）。ボクセル位置（位置／ボクセルサイズのfloor）と、
/// その角からどれだけ進んでいるかを固定小数点値上で計算する。カメラが遠くへ移動しても、グローバル距離場のボクセルはワールド格子に沿う。
/// ボクセルサイズが正でない場合、またはボクセルインデックスが64ビットに収まらない場合はfalse。
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
/// 環境の動き（ADR-0078/M0110では雲の風）に使うゲーム時間。各フレームの経過カウンターを合計する。
/// 整数で秒と余りを保持するため、時間が長くなっても精度を失わない。
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
