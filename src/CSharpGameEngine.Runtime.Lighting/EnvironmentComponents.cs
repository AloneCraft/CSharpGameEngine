using CSharpGameEngine.Rendering;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Lighting;
/// <summary>ディレクショナルシャドウのバックエンド方針。各ライトが要求する影は引き続きLightShadowが所有する。</summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,70)]
public struct DirectionalShadowRendering() {
    public CSharpGameEngine.Rendering.DirectionalShadowMode Mode=CSharpGameEngine.Rendering.DirectionalShadowMode.Cascaded;
    public int PageBudget=1024;
    public bool Compare=false;
    public bool SoftShadows=false;
    public float SourceAngularDiameterDegrees=0.5357f;
    public float MaximumSoftShadowRadiusMeters=2f;
}
/// <summary>
/// 物理ベースの空大気（ADR-0069）。単位と既定値はUnreal EngineのSkyAtmosphereコンポーネントに合わせる
///（キロメートル、1キロメートルあたりの係数、地球プリセット）ため、インポート値の変換は不要。
/// 最も明るいディレクショナルライトを太陽として扱う。その照度が大気圏上端の値となり、レンダラーは大気の透過率を掛けて減衰させる。
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
    /// <summary>太陽円盤の全角直径（度数。UnrealのDirectionalLight Source Angle）。</summary>
    [Angle(AngleUnit.Degrees)]
    public float SunDiskAngleDegrees=0.5357f;
}
/// <summary><see cref="SkyAtmosphere"/>から取り込んだスカイライト。<see cref="Intensity"/>を取り込んだ空の放射輝度に掛ける。</summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,61)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/SkyLight.svg")]
public struct SkyLight() {
    public float Intensity=1f;
    public float LowerHemisphereR=0f;
    public float LowerHemisphereG=0f;
    public float LowerHemisphereB=0f;
}
/// <summary>露出モデル。Legacyではレンダラー本来の2^-EV100スケールを維持する。</summary>
public enum ExposureMethod {
    Legacy,
    /// <summary><see cref="PostProcessVolume.MinEv100"/>で固定する、物理カメラの露出。</summary>
    Manual,
    /// <summary><see cref="PostProcessVolume.MinEv100"/>から<see cref="PostProcessVolume.MaxEv100"/>の間を測光する、Unrealのヒストグラム自動露出。</summary>
    AutoHistogram,
}
/// <summary>出力変換。Legacyでは従来の線形ACES近似を維持する。</summary>
public enum ToneMapperKind {
    Legacy,
    /// <summary>Unreal Engineのフィルム調カーブと、それに続くsRGB伝達関数。</summary>
    UnrealFilmic,
}
/// <summary>Unrealのグローバルイルミネーション方式（ADR-0078/M0111）。</summary>
public enum GlobalIlluminationMethod {
    /// <summary>Lumenを使わない方式。M0111以前と同様、プローブボリューム（<see cref="GlobalIlluminationVolume"/>）とスカイライトでシーンを照らす。</summary>
    None,
    /// <summary>Lumen風の動的GI（サーフェスキャッシュ、放射輝度キャッシュ、スクリーンプローブ）。アダプターのスケーラビリティが許す環境で使用する。</summary>
    Lumen,
}
/// <summary>Unrealの反射方式（ADR-0078/M0111）。</summary>
public enum ReflectionMethod {
    /// <summary>Lumen反射を使わない。M0111以前と同様、スカイライトの鏡面反射を使う。</summary>
    None,
    /// <summary>Lumen風の反射（スクリーントレース後、距離場とサーフェスキャッシュを使用）。スケーラビリティが許す環境で有効にする。</summary>
    Lumen,
}
/// <summary>
/// シーン全体に適用する、ボリューム範囲を持たないポストプロセス設定。露出、トーンマッピング、ブルーム、およびUnrealの既定値を使うLumen設定
///（ADR-0078/M0111）を含む。Unreal 5.8の4つのテンプレートでも変更せずに使われている（tools/UnrealExport/probe_lumen.py）。
/// 距離の単位はメートル（Unrealのセンチメートルを100で割った値）。Lumenの使用には、シーン内の<see cref="GlobalDistanceField"/>と、
/// スケーラビリティがHighまたはEpicのアダプターが必要（GPUでは既定値。WARPではプローブボリュームを維持する）。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,62)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/PostProcessVolume.svg")]
public struct PostProcessVolume() {
    public ExposureMethod ExposureMethod=ExposureMethod.Manual;
    public float MinEv100=0f;
    public float MaxEv100=0f;
    /// <summary>明るいシーンへ向かうヒストグラム適応速度（毎秒、Unrealの既定値は3）。</summary>
    public float AutoExposureSpeedUp=3f;
    /// <summary>暗いシーンへ向かうヒストグラム適応速度（毎秒、Unrealの既定値は1）。</summary>
    public float AutoExposureSpeedDown=1f;
    /// <summary>段数で指定する露出補正（UnrealのExposure Compensation／AutoExposureBias）。</summary>
    public float ExposureCompensation=1f;
    public ToneMapperKind ToneMapper=ToneMapperKind.UnrealFilmic;
    public float BloomIntensity=0.675f;
    /// <summary>間接光に対するスクリーン空間アンビエントオクルージョンの強度。0でオフ、1はUnrealの既定値0.5。</summary>
    public float AmbientOcclusionIntensity=0.5f;
    /// <summary>ワールド空間でのアンビエントオクルージョン半径（m、Unrealでは200 cm）。</summary>
    public float AmbientOcclusionRadiusMeters=2f;
    public GlobalIlluminationMethod GlobalIlluminationMethod=GlobalIlluminationMethod.Lumen;
    public ReflectionMethod ReflectionMethod=ReflectionMethod.Lumen;
    /// <summary>フレームごとに照明を計算するサーフェスキャッシュのテクセル数を倍率指定（UnrealのLumen Scene Lighting Quality、0.25～4）。</summary>
    public float LumenSceneLightingQuality=1f;
    /// <summary>サーフェスキャッシュの1メートルあたりのテクセル数を倍率指定（UnrealのLumen Scene Detail、0.25～4）。</summary>
    public float LumenSceneDetail=1f;
    /// <summary>この距離より遠いオブジェクトにはサーフェスキャッシュのカードを作らない（UnrealのLumen Scene View Distance、20000 cm）。</summary>
    public float LumenSceneViewDistanceMeters=200f;
    /// <summary>サーフェスキャッシュのフレームごとの照明計算予算を倍率指定（UnrealのLumen Scene Lighting Update Speed、0.5～4）。</summary>
    public float LumenSceneLightingUpdateSpeed=1f;
    /// <summary>Epicでは16ピクセル間隔で配置するスクリーンプローブを、この値で割った間隔にする（UnrealのFinal Gather Quality、0.25～4）。</summary>
    public float LumenFinalGatherQuality=1f;
    /// <summary>フレームごとの放射輝度キャッシュのプローブ数と、Final Gatherの新しいサンプルの重みを倍率指定（UnrealのFinal Gather Lighting Update Speed）。</summary>
    public float LumenFinalGatherLightingUpdateSpeed=1f;
    /// <summary>Final Gatherが距離場より先にスクリーンをトレースするかどうか（UnrealのFinal Gather Screen Traces）。</summary>
    public bool LumenFinalGatherScreenTraces=true;
    /// <summary>レイの最長距離（UnrealのMax Trace Distance、20000 cm）。</summary>
    public float LumenMaxTraceDistanceMeters=200f;
    /// <summary>サーフェスキャッシュのアルベドに倍率を掛ける。上限は1（UnrealのDiffuse Color Boost、1～4）。</summary>
    public float LumenDiffuseColorBoost=1f;
    /// <summary>Final Gatherの照度が、開放空の照度に対して保つ最小割合（UnrealのSkylight Leaking、0～1。暗い室内を抑えるためのアート調整値）。</summary>
    public float LumenSkylightLeaking=0f;
    /// <summary>Epicでは0.75以上ならフル解像度で反射を描画し、それ未満なら2 x 2ピクセルごとに描画する（UnrealのLumen Reflection Quality）。</summary>
    public float LumenReflectionQuality=1f;
    /// <summary>これより粗い面はレイトレースせず、Final Gatherのプローブを反射する（UnrealのMax Roughness To Trace Reflections、0.4）。</summary>
    public float LumenMaxRoughnessToTraceReflections=0.4f;
    /// <summary>反射で距離場より先にスクリーンをトレースするかどうか（UnrealのLumen Reflections Screen Traces）。</summary>
    public bool LumenReflectionsScreenTraces=true;
    /// <summary>サーフェスキャッシュの解像度を倍率指定（UnrealのSurface Cache Resolution、0.5～1）。</summary>
    public float LumenSurfaceCacheResolution=1f;
}
/// <summary>
/// Unreal Engineの指数型高さフォグ。<see cref="FogDensity"/>と<see cref="FogHeightFalloff"/>はUnrealで設定した値を保ち、
/// レンダラー側で1メートルあたりの値（×0.1）へ変換する。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,63)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/ExponentialHeightFog.svg")]
public struct ExponentialHeightFog() {
    public float FogDensity=0.02f;
    public float FogHeightFalloff=0.2f;
    /// <summary>フォグの基準面のワールド高さ（m）。</summary>
    public float FogHeight=0f;
    public float FogMaxOpacity=1f;
    public float InscatteringLuminanceR=0f;
    public float InscatteringLuminanceG=0f;
    public float InscatteringLuminanceB=0f;
    public float SkyAtmosphereAmbientContributionScale=1f;
}
/// <summary>
/// 動的な拡散グローバルイルミネーションのプローブボリューム（ADR-0069）。EntityのTransform位置を中心とする軸平行ボックスで、回転とスケールは無視する。
/// ボックス内に規則的な格子状でプローブを配置し、照明を反映したStageパイプラインでラウンドロビン方式に再撮影する。
/// そのため数フレームかけて、間接光が太陽、空、移動オブジェクトに追従する。ボックス外の面にはスカイライトを使う。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,64)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/GlobalIlluminationVolume.svg")]
public struct GlobalIlluminationVolume() {
    public float HalfExtentX=16f;
    public float HalfExtentY=4f;
    public float HalfExtentZ=16f;
    /// <summary>プローブ間の目標距離（m）。格子はボックス全体にちょうど収まる。</summary>
    public float ProbeSpacingMeters=2.5f;
    /// <summary>毎フレーム再撮影するプローブ数（1～64）。</summary>
    public int ProbesPerFrame=32;
    /// <summary>プローブ更新時に前回の結果を残す重み（0なら置き換え）。</summary>
    public float Hysteresis=0.5f;
    public float Intensity=1f;
}
/// <summary>
/// ボリュームクラウド（ADR-0078/M0110）。UnrealのVolumetricCloudコンポーネントと既定クラウドマテリアル
///（<c>m_SimpleVolumetricCloud_Inst</c>）の値を使う（UE 5.8の4つのテンプレートすべてで共通）。
/// 惑星の<see cref="LayerBottomAltitudeKm"/>から雲層上端までの殻を、Hillaire方式で太陽方向へレイマーチし、大気を通した太陽光とスカイライトで照らして空に重ねる。
/// 雲の形状にはエンジン独自のノイズを使う（Unrealでは雲テクスチャを使う）。風で雲を移動させる。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,65)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/VolumetricCloud.svg")]
public struct VolumetricCloud() {
    /// <summary>地面から雲層下端までの高度（UnrealのLayer Bottom Altitude）。</summary>
    public float LayerBottomAltitudeKm=5f;
    /// <summary>雲層の厚み（UnrealのLayer Height）。</summary>
    public float LayerHeightKm=10f;
    /// <summary>視線レイに沿ってこの距離より遠くから始まる雲層はトレースしない（UnrealのTracing Start Max Distance）。</summary>
    public float TracingStartMaxDistanceKm=350f;
    /// <summary>視線レイが雲層内でトレースされる最長距離（UnrealのTracing Max Distance）。</summary>
    public float TracingMaxDistanceKm=50f;
    /// <summary>最も密度の高い雲の1メートルあたりの消散係数（マテリアルのCloud_GlobalDensity）。</summary>
    public float Density=0.008f;
    /// <summary>空のどれだけを覆うかを調整する。0で約半分、負の値でそれより少なくなる（マテリアルのCloud_GlobalCoverage）。</summary>
    public float Coverage=-0.2f;
    /// <summary>雲の配置パターンの大きさ（マテリアルのLayout_CloudGlobalScale）。</summary>
    public float LayoutScaleKm=256f;
    /// <summary>単一散乱アルベド（マテリアルのCloud_AlbedoColor）。</summary>
    public float AlbedoR=0.98f;
    public float AlbedoG=0.98f;
    public float AlbedoB=0.98f;
    /// <summary>二重ローブのHenyey-Greenstein位相関数。2つの異方性と、2つ目へ混ぜる割合を表す（マテリアルのPhase_Controls）。</summary>
    public float PhaseG=0.8f;
    public float PhaseG2=0.166667f;
    public float PhaseBlend=0.575f;
    /// <summary>Hillaireの多重散乱近似のオクターブごとの寄与、遮蔽、異方性係数（マテリアルのMultiscatter_Controls）。</summary>
    public float MultiScatteringContribution=0.666667f;
    public float MultiScatteringOcclusion=0.25f;
    public float MultiScatteringEccentricity=0.18f;
    /// <summary>雲層の下端でスカイライトをどれだけ減光するか（UnrealのSky Light Cloud Bottom Occlusion）。</summary>
    public float SkyLightCloudBottomOcclusion=0.5f;
    /// <summary>視線レイの透過率がこの値を下回るとトレースを終了する（UnrealのStop Tracing Transmittance Threshold）。</summary>
    public float StopTracingTransmittanceThreshold=0.005f;
    /// <summary>雲を動かす風速。UnrealはLayout_WindControlsで既定の雲をアニメーションさせるが、この速度はエンジン独自の設定。</summary>
    public float WindSpeedMetersPerSecond=10f;
    /// <summary>上から見て+Fを基準に時計回りで表した風向（90度は+R）。</summary>
    [Angle(AngleUnit.Degrees)]
    public float WindDirectionDegrees=45f;
}
/// <summary>
/// スカイスフィア（ADR-0078/M0110）。Unrealの<c>SM_SkySphere</c>を<c>M_SimpleSkyDome</c>で描画する。
/// 「Is Sky」が有効なUnlitマテリアルで、SkyAtmosphereの視線輝度と太陽円盤を色として使う。
/// エンジンも雲とともに同じ空を描き、スフィアより手前のジオメトリに覆われていない全ピクセルを埋める。
/// つまり、Entity位置から<see cref="RadiusMeters"/>より遠いジオメトリは空に隠れる（Unrealのドームと同じ）。
/// <see cref="SkyAtmosphere"/>がない場合は何も起こらない。Transformの回転とスケールは無視する。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,66)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/SkySphere.svg")]
public struct SkySphere() {
    /// <summary>スフィアの半径。UnrealのSM_SkySphereは40.96 mで、テンプレートでは400倍に拡大し、16.384 km。</summary>
    public float RadiusMeters=16384f;
}
/// <summary>
/// グローバル距離場（ADR-0078/M0110）。カメラ周囲のシーンの符号付き距離をクリップマップに記録し、毎フレーム更新する。
/// Lumen風の照明（M0111）がレイトレースに使うため、各glTF用にビルドが作成したメッシュ距離場から生成する。
/// Unrealではプロジェクトの「Generate Mesh Distance Fields」が有効な場合に作られる（Lumen用としてテンプレートで有効）。
/// ここではこのコンポーネントを持つレベルに生成する。値はWARP用のエンジン独自設定（Unrealの最初のクリップマップは25 mまで）：
/// <see cref="ClipmapCount"/>レベルにわたり、それぞれ<see cref="Resolution"/>^3ボクセルを配置し、最小レベルのサイズは<see cref="VoxelSizeMeters"/>。
/// これ単体では画面表示に変化を与えない。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,67)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/GlobalDistanceField.svg")]
public struct GlobalDistanceField() {
    /// <summary>最小クリップマップのボクセルサイズ。次のレベルでは2倍ずつ大きくなる。</summary>
    public float VoxelSizeMeters=0.5f;
    /// <summary>クリップマップの数（1～4）。</summary>
    public int ClipmapCount=4;
    /// <summary>各クリップマップの1辺あたりのボクセル数（16～128、8の倍数）。</summary>
    public int Resolution=64;
}
