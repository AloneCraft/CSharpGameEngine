using CSharpGameEngine.Rendering.Pipeline;
using System.Numerics;
namespace CSharpGameEngine.Rendering;
/// <summary>
/// 物理ベースの大気係数（ADR-0069）。距離はキロメートル、散乱係数はキロメートルあたりで表し、Unreal Engine の SkyAtmosphere コンポーネントに合わせる。インポートした値
/// の単位変換は不要。既定値は Unreal の地球プリセット。
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
/// <summary>大気から取り込んだスカイライト。<see cref="Intensity"/> を取り込んだ放射輝度に乗算する。</summary>
public readonly record struct SkyLightSettings(float Intensity,Vector3 LowerHemisphereRadiance){
    public bool IsValid=>float.IsFinite(this.Intensity)&&this.Intensity>=0f&&SkyAtmosphereSettings.IsFiniteNonNegative(this.LowerHemisphereRadiance);
}
public enum ExposureMode {
    /// <summary>Scale = 2^-EV100（レンダラーの従来の動作）。</summary>
    Legacy,
    /// <summary>Scale = 2^Compensation / (1.2 * 2^EV100)。Unreal Engine が使う物理カメラモデル。</summary>
    PhysicalCamera,
    /// <summary>
    /// Unreal のヒストグラム自動露出。シーンから測定した EV100 における物理カメラの露出を使う（GPU 上で測定する <c>LuminanceHistogram.hlsl</c>）。値は [
    /// <see cref="ExposureSettings.Ev100"/>、<see cref="ExposureSettings.MaxEv100"/>] に制限する。
    /// </summary>
    AutoHistogram,
}
public enum ToneMapper {
    /// <summary>リニア UNORM ターゲット向けの Narkowicz ACES 近似（レンダラーの従来の動作）。</summary>
    LegacyAces,
    /// <summary>AP1 で適用する Unreal Engine のフィルム調カーブと、その後の sRGB 変換関数。</summary>
    UnrealFilmicSrgb,
}
/// <summary>
/// 露出モデル。<see cref="Ev100"/> は <see cref="ExposureMode.PhysicalCamera"/> の固定 EV100 であり、
/// <see cref="ExposureMode.AutoHistogram"/> の最小値でもある。<see cref="MaxEv100"/>、<see cref="SpeedUp"/>、
/// <see cref="SpeedDown"/>（明るい／暗いシーンへ適応する毎秒の速度）は、ヒストグラム方式でのみ使う。Unreal の既定値はそれぞれ3と1。
/// </summary>
public readonly record struct ExposureSettings(ExposureMode Mode,float Ev100,float Compensation,float MaxEv100=0f,float SpeedUp=3f,float SpeedDown=1f){
    public static ExposureSettings Legacy=>new(ExposureMode.Legacy,0f,0f);
    public bool IsValid=>float.IsFinite(this.Ev100)&&float.IsFinite(this.Compensation)
        &&(this.Mode!=ExposureMode.AutoHistogram
            ||(float.IsFinite(this.MaxEv100)&&this.MaxEv100>=this.Ev100
                &&float.IsFinite(this.SpeedUp)&&this.SpeedUp>0f&&float.IsFinite(this.SpeedDown)&&this.SpeedDown>0f));
    /// <summary>トーンマッピング前にシーン輝度へ適用する線形乗数。ヒストグラム方式は最小 EV100 から開始する。</summary>
    public float Scale(float CameraEv100)=>this.Mode==ExposureMode.Legacy
        ?PhysicalLightUnits.Ev100ToExposureScale(CameraEv100)
        :this.PhysicalScale(this.Ev100);
    /// <summary>露出補正の係数：2^Compensation / (1.2 * 2^EV100)。</summary>
    public float PhysicalScale(float MeteredEv100)=>MathF.Pow(2f,this.Compensation)/(1.2f*MathF.Pow(2f,MeteredEv100));
}
/// <summary>Unreal Engine 方式の指数高さフォグ。レイに沿ったフォグ量 Density * 2^(-Falloff * (height - FogHeight)) を解析式で積分する。各値はメートル単位で表す。</summary>
public readonly record struct PhysicalHeightFogSettings(
    float DensityPerMeter,float HeightFalloffPerMeter,float FogHeightMeters,
    Vector3 InscatteringLuminance,float SkyAtmosphereContributionScale,float MaxOpacity){
    public bool IsValid=>float.IsFinite(this.DensityPerMeter)&&this.DensityPerMeter>=0f
        &&float.IsFinite(this.HeightFalloffPerMeter)&&this.HeightFalloffPerMeter>=0f
        &&float.IsFinite(this.FogHeightMeters)&&SkyAtmosphereSettings.IsFiniteNonNegative(this.InscatteringLuminance)
        &&float.IsFinite(this.SkyAtmosphereContributionScale)&&this.SkyAtmosphereContributionScale>=0f
        &&float.IsFinite(this.MaxOpacity)&&this.MaxOpacity>=0f&&this.MaxOpacity<=1f;
    /// <summary>カメラ位置での密度。解析式による線積分の定数項。</summary>
    public float RayOriginTerms(float CameraAltitudeMeters)=>
        this.DensityPerMeter*MathF.Pow(2f,-this.HeightFalloffPerMeter*(CameraAltitudeMeters-this.FogHeightMeters));
}
/// <summary>
/// シーン全体の環境ライティング（ADR-0069）。メンバーが null の場合は、レンダラーの従来経路を使う。大気がなければクリア色を背景にし、指向性ライトを減衰させない。スカイライトがなければ一様な
/// 環境光だけを使う。物理フォグがなければ従来のフォグを使う。
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
    LumenSettings?Lumen=null,
    CSharpGameEngine.Rendering.DirectionalShadowSettings?DirectionalShadows=null,
    PixelColor?Background=null){
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
        &&(this.DirectionalShadows is null||this.DirectionalShadows.Value.IsValid)
        &&this.Exposure.IsValid&&float.IsFinite(this.BloomIntensity)&&this.BloomIntensity>=0f
        &&float.IsFinite(this.SunAngularRadiusRadians)&&this.SunAngularRadiusRadians>0f&&this.SunAngularRadiusRadians<0.5f;
}
/// <summary>
/// Unreal の単位系によるボリューム雲（ADR-0078/M0110）。雲層とレイ追跡距離はキロメートル、消散係数はメートルあたり。マテリアルの被覆率、アルベド、位相関数、複数回散乱の設定を保持する
/// 。<see cref="CameraPositionKm"/> は惑星上のカメラ位置（ワールド R と F 軸、キロメートル）、<see cref="WindOffsetKm"/> は風が雲を移動させ
/// た距離を表す。どちらもカメラから見える雲を動かし、毎フレーム更新する。大気が必要であり、<see cref="EnvironmentLightingSettings.Atmosphere"/> がなけ
/// れば描画しない。
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
    /// <summary>Unreal 5.8 の既定マテリアルを使った VolumetricCloud の値。UE 5.8 テンプレートの設定に合わせる。</summary>
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
/// スカイスフィア（ADR-0078/M0110、Unreal の SM_SkySphere と M_SimpleSkyDome）。<see cref="RadiusMeters"/> よりも
/// <see cref="Center"/> から遠い面を持つ各ピクセル（カメラ原点とともに移動するレンダリング座標系）と、形状で覆われないピクセルに空を描画する。
/// </summary>
public readonly record struct SkySphereSettings(Vector3 Center,float RadiusMeters){
    public bool IsValid=>float.IsFinite(this.Center.X)&&float.IsFinite(this.Center.Y)&&float.IsFinite(this.Center.Z)
        &&float.IsFinite(this.RadiusMeters)&&this.RadiusMeters>0f;
}
/// <summary>
/// グローバル距離場（ADR-0078/M0110）。カメラの周囲に <see cref="Resolution"/>^3 ボクセルのクリップマップを <see cref="ClipmapCount"/>
///  個配置する。最も細かい段のボクセルサイズは <see cref="VoxelSizeMeters"/> で、次の段以降は順に2倍粗くなる。オブジェクトのメッシュ距離場から作成し、ブリック単位で更新す
/// る。ボクセルはワールドに固定する。<see cref="OriginVoxel"/> はレンダリング座標原点が属する最細段のワールドボクセル、<see cref="OriginRemainder"/>
///  はボクセルの角から原点までの距離を表し、カメラ移動時に距離場がずれて見えるのを防ぐ。ワールド座標で描画する呼び出し側は、どちらもゼロにする。
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
/// <summary>64ビット整数3つで表す、ワールドボクセル格子上のボクセル座標。</summary>
public readonly record struct LongVector3(long X,long Y,long Z);
/// <summary>
/// Unreal の PostProcessVolume 用語で表した、Lumen 相当のシーン動的グローバルイルミネーションと反射（ADR-0078/M0111）。距離はメートル（Unreal の
/// センチメートルを100で割った値）、品質と速度のスケールは Unreal に合わせる（1が既定値）。<see cref="GlobalIllumination"/> と
/// <see cref="Reflections"/> は Unreal の Global Illumination Method と Reflection Method を Lumen に設定した状態に
/// 相当する。レンダラーのスケーラビリティ（<see cref="RenderScalability"/>）が Lumen を許可し、環境にレイ追跡用グローバル距離場がある場合のみ使用する。それ以外では、
/// 従来どおりプローブボリューム（DDGI）とスカイライトでシーンを照らす。事前焼き付けは行わず、サーフェスキャッシュ、放射輝度キャッシュ、画面プローブを毎フレーム再計算する。
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
    /// <summary>Unreal 5.8 の PostProcessVolume 既定値。4つすべてのテンプレートで使う（tools/UnrealExport/probe_lumen.py）。</summary>
    public static LumenSettings Unreal=>new(true,true,1f,1f,200f,1f,1f,1f,true,200f,1f,0f,1f,0.4f,true,1f);
    public bool IsValid=>
        InRange(this.SceneLightingQuality,0.25f,4f)&&InRange(this.SceneDetail,0.25f,4f)
        &&InRange(this.SceneViewDistanceMeters,1f,100000f)&&InRange(this.SceneLightingUpdateSpeed,0.5f,4f)
        &&InRange(this.FinalGatherQuality,0.25f,4f)&&InRange(this.FinalGatherLightingUpdateSpeed,0.5f,4f)
        &&InRange(this.MaxTraceDistanceMeters,1f,100000f)&&InRange(this.DiffuseColorBoost,1f,4f)&&InRange(this.SkylightLeaking,0f,1f)
        &&InRange(this.ReflectionQuality,0.25f,4f)&&InRange(this.MaxRoughnessToTraceReflections,0f,1f)&&InRange(this.SurfaceCacheResolution,0.5f,1f);
    private static bool InRange(float Value,float Minimum,float Maximum)=>float.IsFinite(Value)&&Value>=Minimum&&Value<=Maximum;
}
/// <summary>Unreal のスケーラビリティレベル（sg.GlobalIlluminationQuality / sg.ReflectionQuality、0～3）。</summary>
public enum ScalabilityLevel {
    Low,
    Medium,
    High,
    Epic,
}
/// <summary>
/// 動的ライティングのアダプター側品質設定（ADR-0078/M0111）。Unreal の sg.GlobalIlluminationQuality と sg.ReflectionQuality に対応
/// する。Unreal の BaseScalability.ini と同様、Lumen GI には High または Epic が必要（High は32ピクセルごと、Epic は16ピクセルごとに画面
/// プローブを追跡）。Lumen 反射には High（半解像度）または Epic（全解像度）が必要。それ未満では、M0110 以前のフレームと同じくプローブボリューム（DDGI）とスカイライトの鏡面反射を
/// 使う（Unreal の Medium は、プローブボリュームに相当する Irradiance Field の Final Gather を使う）。ハードウェアアダプターの既定値は High、CPU で
/// 描画する WARP の既定値は Medium。Epic（Unreal のテンプレート設定）は明示的に選択する。WARP では断続的にデバイスを失い、ビット単位の再現性もなかったため（M0111）、
/// ハードウェア GPU ではまだ実行していない。
/// </summary>
public readonly record struct RenderScalability(ScalabilityLevel GlobalIllumination,ScalabilityLevel Reflections){
    public static RenderScalability Epic=>new(ScalabilityLevel.Epic,ScalabilityLevel.Epic);
    public static RenderScalability High=>new(ScalabilityLevel.High,ScalabilityLevel.High);
    public static RenderScalability Medium=>new(ScalabilityLevel.Medium,ScalabilityLevel.Medium);
    public static RenderScalability DefaultFor(bool IsWarp)=>IsWarp?Medium:High;
    public bool AllowsLumenGlobalIllumination=>this.GlobalIllumination>=ScalabilityLevel.High;
    /// <summary>Lumen の反射には Lumen GI も必要（粗い反射ローブとレイの失敗時に GI のプローブを参照する）。</summary>
    public bool AllowsLumenReflections=>this.AllowsLumenGlobalIllumination&&this.Reflections>=ScalabilityLevel.High;
}
/// <summary>デバッグ用の Unreal の r.Lumen.Visualize（ADR-0078/M0111）。Lumen 実行中に、ライティング済みシーンの代わりに表示する内容。</summary>
public enum LumenVisualization {
    None,
    /// <summary>白い面に対する Final Gather の拡散照度。</summary>
    Diffuse,
    /// <summary>反射された放射輝度。</summary>
    Reflections,
    /// <summary>サーフェスキャッシュに記録された各ピクセル位置の面の放射輝度。カードに捉えられていない場所は黒。</summary>
    SurfaceCache,
}
/// <summary>
/// CGE_SCALABILITY_GI と CGE_SCALABILITY_REFLECTIONS（low、medium、high、epic）から
/// <see cref="RenderScalability"/> の上書きを読み込む。また、CGE_LUMEN_VISUALIZE（none、diffuse、reflections、
/// surfacecache）からデバッグ表示を読み込む。
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
    /// <summary>アダプターの既定値。それぞれのレベルには対応する変数名で上書きでき、反射の値だけが指定された場合は GI のレベルに合わせる。</summary>
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
/// スクリーンスペースで評価する間接光のアンビエントオクルージョン（ADR-0069、Unreal Engine の用語）。<see cref="Intensity"/> は遮蔽なし（0）から完全な遮蔽（
/// 1）までを補間する。<see cref="RadiusMeters"/> はワールド空間でのサンプリング半径。
/// </summary>
public readonly record struct AmbientOcclusionSettings(float Intensity,float RadiusMeters){
    public bool IsValid=>float.IsFinite(this.Intensity)&&this.Intensity>=0f&&this.Intensity<=1f
        &&float.IsFinite(this.RadiusMeters)&&this.RadiusMeters>0f;
}
/// <summary>
/// 動的拡散グローバルイルミネーションのプローブボリューム（ADR-0069）。<see cref="Center"/> は当該フレームのレンダラーのカメラ相対座標で表すため、オブジェクトの変換と同じよう
/// にカメラ原点とともに移動する。他のメンバーはグリッド配置を表す。ボックス内に規則的なグリッドを設け、各軸に最低2個のプローブを配置する。毎フレーム、
/// <see cref="ProbesPerFrame"/> 個をラウンドロビン方式で再取得する。
/// </summary>
public readonly record struct GlobalIlluminationSettings(
    Vector3 Center,
    Vector3 HalfExtent,
    float ProbeSpacingMeters,
    int ProbesPerFrame,
    float Hysteresis,
    float Intensity){
    /// <summary>固定サイズの照度アトラスと距離アトラスのプローブ容量。</summary>
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
    /// <summary>各軸上のプローブ数。ボックスの辺を間隔で割って1を加えた値とし、最低2個。</summary>
    public (int X,int Y,int Z) ProbeCounts=>(AxisCount(this.HalfExtent.X,this.ProbeSpacingMeters),AxisCount(this.HalfExtent.Y,this.ProbeSpacingMeters),AxisCount(this.HalfExtent.Z,this.ProbeSpacingMeters));
    public int ProbeCount=>(int)Math.Min(ProbeCountFor(this.HalfExtent,this.ProbeSpacingMeters),int.MaxValue);
    /// <summary>各軸で隣接するプローブ間の実際の距離。グリッドがボックス全体に正確に広がるようにする。</summary>
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
/// <summary>大気の透過率を計算する CPU 参照実装。レンダラーはこれを使って指向性ライトを減衰させ、テストでは GPU の透過率 LUT の正解値として使う。</summary>
public static class AtmosphereTransmittance {
    private const int SampleCount=64;
    /// <summary>地表から <paramref name="AltitudeMeters"/> の高度にある点から、<paramref name="DirectionTowardLight"/> 方向への透過率（+Y が上向き）。</summary>
    public static Vector3 Evaluate(in SkyAtmosphereSettings Atmosphere,float AltitudeMeters,Vector3 DirectionTowardLight){
        if(!Atmosphere.IsValid){
            throw new ArgumentException("Atmosphere settings must be finite and physically valid.",nameof(Atmosphere));
        }
        var Direction=Vector3.Normalize(DirectionTowardLight);
        if(!float.IsFinite(Direction.X)||!float.IsFinite(AltitudeMeters)){
            throw new ArgumentException("The direction and altitude must be finite.");
        }
        var Origin=new Vector3(0f,Atmosphere.BottomRadiusKm+MathF.Max(AltitudeMeters,0f)*0.001f,0f);
        // 惑星に遮られるのは、ローカル地平線より下へ向かうレイだけ。原点が地面上にあると、ちょうど
        // 上向きのすべての方向でも距離ゼロの交差が報告されてしまう。
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
