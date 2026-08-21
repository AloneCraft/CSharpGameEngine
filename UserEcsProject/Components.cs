using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Runtime.Windows;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;

namespace UserEcsProject;

// ============================================================================
// エンジンオブジェクト(Win32Window / StageRenderer / AudioEngineHandle)はComponentのフィールドとして持つ。
//
// Componentの要件は「非nullableのstructであること」だけなので、参照型フィールドを持つことは制約に
// 触れない(遅延生成するエンジンオブジェクトをComponentへ保持する既存パターン)。
// これらをContext引数ではなくComponentにしたことが、Program.csを不要にする鍵になっている:
// Contextはループ開始前に構築されて`Run`の全期間で固定されるため、生成されたエントリポイントには
// 渡す値を作る手段が無い。Componentに載せればSystemの中で遅延生成でき、Context引数がゼロになって
// `GeneratedGameLoop.Run(ref State)`というシグネチャが確定する。
//
// 閉集合Archetype方式には「別Entityのコンポーネントを読む」手段が無いため、必要な参照は生成時に
// コピーして持たせる(Renderable.Renderer / FramePresent.*)。これは回避策ではなく、この方式で
// 依存関係を明示する唯一の書き方である。
//
// このファイルのComponentは全部エンジン側の語彙で、Systems/RuntimeSystem.cs と一緒にランタイムへ
// 移す対象。ゲーム固有のComponentを足すときは、Scenes/MainScene.cs と対になる別ファイルに置くこと。
// ============================================================================

/// <summary>
/// アプリケーション本体。ウィンドウの設定値と、WindowSystemが遅延生成したウィンドウ実体を持つ。
/// </summary>
[SceneComponentIcon("Assets/Editor/ComponentIcons/AppHost.svg")]
public struct AppHost {
    public string Title;
    public int Width;
    public int Height;


    /// <summary>パレットから追加した直後でも開けるウィンドウになるための既定値。</summary>
    public AppHost(){
        this.Title="C# Game Engine";
        this.Width=1280;
        this.Height=720;
    }
}

/// <summary>
/// レベル(= このエンジンではFBXファイル1つ)。LevelLoadがインポートし、その内容から
/// StageRendererを生成して、描画対象のEntityを撒く。
///
/// <para>
/// <b>どの</b>レベルをどのシェーダーでどのカメラから見るかは全部ゲーム側の決定なので、値はすべて
/// <c>MainScene</c>(Scenes/MainScene.cs)の<c>[SceneEntity]</c>宣言が入れる。LevelLoadはここに
/// 入っているものを読むだけで、リテラルを1つも持たない。
/// </para>
/// </summary>
[SceneComponentIcon("Assets/Editor/ComponentIcons/Level.svg")]
public struct Level {
    /// <summary>シェーダーのパス。アプリのベースディレクトリ(<c>AppContext.BaseDirectory</c>)からの相対。</summary>
    public string ShaderPath;
}

/// <summary>SkyLightをStageRendererの単純な環境光へ写像した宣言Component。</summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,0)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/AmbientLight.svg")]
public struct AmbientLight {
    public float ColorR;
    public float ColorG;
    public float ColorB;
    public float Intensity;

    /// <summary>追加した直後から実際に照らす既定値(黒・強度0だと何も起きず不具合に見える)。</summary>
    public AmbientLight(){
        this.ColorR=1f;
        this.ColorG=1f;
        this.ColorB=1f;
        this.Intensity=1f;
    }
}

/// <summary>UEのExponentialHeightFogから取り込む単純な指数高さFog。</summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,1)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/HeightFog.svg")]
public struct HeightFog {
    public float Density;
    public float HeightFalloff;
    public float ColorR;
    public float ColorG;
    public float ColorB;

    /// <summary>追加した直後から視認できる薄い灰青のFog。</summary>
    public HeightFog(){
        this.Density=0.02f;
        this.HeightFalloff=0.2f;
        this.ColorR=0.6f;
        this.ColorG=0.65f;
        this.ColorB=0.7f;
    }
}

/// <summary>実行時の初期一人称カメラ位置と向きを宣言する。</summary>
[SceneComponentIcon("Assets/Editor/ComponentIcons/PlayerStart.svg")]
public struct PlayerStart {
    public float YawRadians;
    public float PitchRadians;
}

/// <summary>
/// 単一のオーディオリスナー(「耳」)。現時点では位置を持つだけの器で、<see cref="AudioSource"/>との
/// 距離減衰計算は行わない — 閉集合Archetypeでは別Entityのコンポーネントを横断参照できないため
/// (ADR-0033)、リスナーとソースが別Entityである以上、両者を1つのUpdateで同時に読むSystemは書けない。
/// 距離減衰を実装する場合は、両者を同一Entityにまとめるか、値を毎フレームコピーする別の仕組みが要る
/// (ADR-0039「既知の制約」参照)。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Audio,0)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/AudioListener.svg")]
public struct AudioListener {
    public float PositionX;
    public float PositionY;
    public float PositionZ;
}

/// <summary>
/// 単一のオーディオソース。AudioPlaybackSystemが毎フレーム<see cref="Volume"/>を<see cref="Voice"/>へ
/// 反映し、終了時に破棄する(<see cref="DirectionalLight"/>の<c>Renderer</c>と同じ「作成時にコピー・
/// 毎フレーム同期・終了時破棄」パターン)。<see cref="Voice"/>は<c>struct</c>を包む<c>Nullable&lt;T&gt;</c>
/// であり、<see cref="Win32Window"/>等の参照型null許容フィールドと意味は同じ(未生成はnull)だが、
/// ハンドル自体はネイティブポインタを持たない安全な値型(<c>AudioBackend.cs</c>参照)。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Audio,10)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/AudioSource.svg")]
public struct AudioSource {
    public float PositionX;
    public float PositionY;
    public float PositionZ;

    public float Volume;
    public bool Loop;


    /// <summary>追加した直後から聞こえる音量。0だと無音で不具合に見える。</summary>
    public AudioSource(){
        this.Volume=1f;
    }
}

/// <summary>
/// ECSのEntityが所有するワールド変換。TransformSyncSystemが毎フレームStageRendererの
/// ワールド行列へ変換して書き込む。
/// </summary>

public struct Renderable:IRuntimeComponent {
    /// <summary>このEntityの変換を書き込む先のStageRendererオブジェクト。</summary>
    public StageRenderer?Renderer;
    public int ObjectIndex;
    public Matrix4x4 MeshLocalTransform;
    /// <summary>Hierarchy を持つ作者 Entity。runtime wrapper 自身の Transform は互換用だけに残す。</summary>
    public Entity SourceEntity;
    public Matrix4x4 LastCameraRelativeWorld;
    public bool HasMeshLocalTransform;
    public bool HasSourceEntity;
    public bool HasLastCameraRelativeWorld;
    public FixedTransformStatus LastTransformStatus;
    public FixedTransformResolveStatus LastWorldResolveStatus;
}

/// <summary>
/// Runtime-only emissive mesh binding. The source material remains the visual emission authority;
/// this state rebuilds camera-relative direct-light patches whenever its Transform changes.
/// </summary>
public struct EmissiveMeshLighting:IRuntimeComponent {
    public StageRenderer?Renderer;
    public StageMeshCreateInfo Mesh;
    public Vector3 EmissiveNits;
    /// <summary>Mean linear emissive-texture luminance; zero is a fully black supported texture.</summary>
    public float EmissiveTextureLuminanceScale;
    public Matrix4x4 MeshLocalTransform;
    /// <summary>Hierarchy を持つ作者 Entity。発光パッチもこの fixed world を追従する。</summary>
    public Entity SourceEntity;
    public Matrix4x4 LastCameraRelativeWorld;
    public float RangeMeters;
    public bool CastsShadows;
    public int ShadowPriority;
    public float ShadowBiasMeters;
    public bool HasLastCameraRelativeWorld;
    public bool HasSourceEntity;
    public FixedTransformStatus LastTransformStatus;
    public FixedTransformResolveStatus LastWorldResolveStatus;
}

/// <summary>
/// 無限遠の平行光。向きは同一Entityの<see cref="Transform"/>のローカル+Z（光の進行方向）から得る。
/// <see cref="IlluminanceLux"/>は照度[lux]であり、単位のないIntensityは持たない。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,10)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/DirectionalLight.svg")]
public struct DirectionalLight {
    /// <summary>Linear RGBの発光色。</summary>
    public float ColorR;
    public float ColorG;
    public float ColorB;

    /// <summary>照度[lux]。</summary>
    public float IlluminanceLux;
    public bool CastsShadows;
    public int ShadowPriority;
    /// <summary>シャドウ深度比較の物理バイアス[m]。</summary>
    public float ShadowBiasMeters;

    public DirectionalLight(){
        this.ColorR=1f;
        this.ColorG=1f;
        this.ColorB=1f;
        this.IlluminanceLux=100_000f;
        this.CastsShadows=true;
        this.ShadowPriority=0;
        this.ShadowBiasMeters=0.005f;
    }
}

/// <summary>
/// 全方向へ放射する点光源。位置は同一Entityの<see cref="Transform"/>から得る。
/// <see cref="LuminousFluxLumens"/>は全光束[lm]、<see cref="RangeMeters"/>は打ち切り距離[m]。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,20)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/PointLight.svg")]
public struct PointLight {
    public float ColorR;
    public float ColorG;
    public float ColorB;
    public float LuminousFluxLumens;
    public float RangeMeters;
    public bool CastsShadows;
    public int ShadowPriority;
    public float ShadowBiasMeters;

    public PointLight(){
        this.ColorR=1f;
        this.ColorG=1f;
        this.ColorB=1f;
        this.LuminousFluxLumens=800f;
        this.RangeMeters=10f;
        this.CastsShadows=true;
        this.ShadowPriority=0;
        this.ShadowBiasMeters=0.005f;
    }
}

/// <summary>
/// 円錐状の点光源。位置とローカル+Zの進行方向は同一Entityの<see cref="Transform"/>から得る。
/// 円錐角は半角/radianではなく全角[degree]で保持する。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,30)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/SpotLight.svg")]
public struct SpotLight {
    public float ColorR;
    public float ColorG;
    public float ColorB;
    public float LuminousFluxLumens;
    public float RangeMeters;
    public float InnerConeFullAngleDegrees;
    public float OuterConeFullAngleDegrees;
    public bool CastsShadows;
    public int ShadowPriority;
    public float ShadowBiasMeters;

    public SpotLight(){
        this.ColorR=1f;
        this.ColorG=1f;
        this.ColorB=1f;
        this.LuminousFluxLumens=800f;
        this.RangeMeters=10f;
        this.InnerConeFullAngleDegrees=30f;
        this.OuterConeFullAngleDegrees=45f;
        this.CastsShadows=true;
        this.ShadowPriority=0;
        this.ShadowBiasMeters=0.005f;
    }
}

/// <summary>
/// ローカル+Z側だけが発光する片面矩形Area Light。位置と向きは同一Entityの<see cref="Transform"/>、
/// 物理サイズは<see cref="WidthMeters"/>/<see cref="HeightMeters"/>から得る（Transform Scaleは適用しない）。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,40)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/AreaLight.svg")]
public struct AreaLight {
    public float ColorR;
    public float ColorG;
    public float ColorB;
    /// <summary>輝度[cd/m²]（nit）。</summary>
    public float LuminanceNits;
    public float WidthMeters;
    public float HeightMeters;
    public float RangeMeters;
    public bool CastsShadows;
    public int ShadowPriority;
    public float ShadowBiasMeters;

    public AreaLight(){
        this.ColorR=1f;
        this.ColorG=1f;
        this.ColorB=1f;
        this.LuminanceNits=100f;
        this.WidthMeters=1f;
        this.HeightMeters=1f;
        this.RangeMeters=10f;
        this.CastsShadows=true;
        this.ShadowPriority=0;
        this.ShadowBiasMeters=0.005f;
    }
}

/// <summary>
/// フレーム末尾のPresentフェーズを表す単独Entity。<b>AppHostとは別のEntityにしている</b>のは
/// 実行順序のため: 生成ループはArchetype順が外側なので、Presentを担うArchetypeがソース上で
/// 最後に出現するようにしないと、描画同期より前にPresentしてしまう(Systems/RuntimeSystem.csの
/// 冒頭コメント参照)。
/// </summary>
public struct FramePresent:IRuntimeComponent {
    public Win32Window?Window;
    public StageRenderer?Renderer;

    /// <summary>LevelLoadがコピーする。Presentが終了時にここから破棄する(Renderer/Windowと同じ経路)。</summary>
    public AudioEngineHandle?AudioEngine;
}

// ============================================================================
// 実行時Component(IRuntimeComponent)。作者はSceneへ書かず、随伴先の作者Componentを宣言すれば
// Source Generatorが自動的に付与する(Parent/SceneNodeTagと同じ仕組み)。明示するとSCENE119。
//
// エンジンが遅延生成するハンドル(StageRenderer / Win32Window / AudioEngineHandle)を作者データと
// 同じstructに混ぜると、InspectorやComponentパレットへ「編集可能な値」として並んでしまう。
// 型を分けることで、作者が設定すべき項目だけが編集面に出る。
// ============================================================================

/// <summary>WindowSystemが最初のフレームで生成するウィンドウ実体。それまではnull。</summary>
public struct AppHostRuntime:IRuntimeComponent<AppHost> {
    public Win32Window?Window;
}

/// <summary>LevelLoadが生成する描画・音響の実体。StageRendererはメッシュ集合を確定させてからしか作れない。</summary>
public struct LevelRuntime:IRuntimeComponent<Level> {
    public StageRenderer?Renderer;

    /// <summary>LevelLoadが生成する共有オーディオエンジン。確定前はnull。</summary>
    public AudioEngineHandle?AudioEngine;

    /// <summary>レンダラー生成時に一度だけ適用し、以後LevelLoadが毎ロード上書きする初期カメラ。</summary>
    public RenderCamera RenderCamera;

    /// <summary>LevelLoadが一度だけ走ったことの印。</summary>
    /// <summary>Last resolved active Camera hierarchy status.</summary>
    public FixedTransformResolveStatus LastCameraWorldResolveStatus;
    /// <summary>Last active Camera fixed-to-float basis status.</summary>
    public FixedTransformStatus LastCameraTransformStatus;
    /// <summary>Whether a valid Camera origin and view have been established.</summary>
    public bool HasLastCameraWorldPose;
    public bool Loaded;
}

/// <summary>AmbientLightSyncが毎フレーム書き込む先。</summary>
public struct AmbientLightRuntime:IRuntimeComponent<AmbientLight> {
    public StageRenderer?Renderer;
}

/// <summary>HeightFogSyncが毎フレーム書き込む先。</summary>
public struct HeightFogRuntime:IRuntimeComponent<HeightFog> {
    public StageRenderer?Renderer;
}

/// <summary>DirectionalLightSyncが毎フレーム書き込む先。LevelLoadがレンダラー生成時に埋める。</summary>
public struct DirectionalLightRuntime:IRuntimeComponent<DirectionalLight> {
    public StageRenderer?Renderer;
    public Vector3 LastWorldForward;
    public bool HasLastWorldForward;
    public FixedTransformStatus LastTransformStatus;
    public FixedTransformResolveStatus LastWorldResolveStatus;
}

/// <summary>PointLightSyncが毎フレーム書き込む先。</summary>
public struct PointLightRuntime:IRuntimeComponent<PointLight> {
    public StageRenderer?Renderer;
    public Vector3 LastCameraRelativePosition;
    public bool HasLastCameraRelativePosition;
    public FixedTransformStatus LastTransformStatus;
    public FixedTransformResolveStatus LastWorldResolveStatus;
}

/// <summary>SpotLightSyncが毎フレーム書き込む先。</summary>
public struct SpotLightRuntime:IRuntimeComponent<SpotLight> {
    public StageRenderer?Renderer;
    public Vector3 LastCameraRelativePosition;
    public Vector3 LastWorldForward;
    public bool HasLastCameraRelativePosition;
    public bool HasLastWorldForward;
    public FixedTransformStatus LastTransformStatus;
    public FixedTransformResolveStatus LastWorldResolveStatus;
}

/// <summary>AreaLightSyncが毎フレーム書き込む先。</summary>
public struct AreaLightRuntime:IRuntimeComponent<AreaLight> {
    public StageRenderer?Renderer;
    public Vector3 LastCameraRelativePosition;
    public Vector3 LastWorldForward;
    public bool HasLastCameraRelativePosition;
    public bool HasLastWorldForward;
    public FixedTransformStatus LastTransformStatus;
    public FixedTransformResolveStatus LastWorldResolveStatus;
}

/// <summary>AudioPlaybackが操作する音声。AudioEngine確定後にLevelLoadが埋める。</summary>
public struct AudioSourceRuntime:IRuntimeComponent<AudioSource> {
    public AudioVoiceHandle?Voice;
}
