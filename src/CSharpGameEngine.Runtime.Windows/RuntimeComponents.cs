using CSharpGameEngine.Runtime.Lighting;
using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;

namespace CSharpGameEngine.Runtime.Windows;

// Components retain portable authoring values, identities, and derived diagnostics only.
// EngineHost is the generated-loop context and exclusively owns process and native resources.

/// <summary>
/// Authoring configuration for the process window. EngineHost owns the runtime window.
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
/// <c>MainScene</c>(Scenes/MainScene.scene.json)の<c>[SceneEntity]</c>宣言が入れる。LevelLoadはここに
/// 入っているものを読むだけで、リテラルを1つも持たない。
/// </para>
/// </summary>
[SceneComponentIcon("Assets/Editor/ComponentIcons/Level.svg")]
public struct Level {
    /// <summary>シェーダーのパス。ゲームのディレクトリ(<see cref="ContentRoot"/>)からの相対。</summary>
    public string ShaderArtifactPath;
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
    public float Volume;
    public float MinimumDistance;
    public float MaximumDistance;
    public bool Loop;


    /// <summary>追加した直後から聞こえる音量。0だと無音で不具合に見える。</summary>
    public AudioSource(){
        this.Volume=1f;
        this.MinimumDistance=1f;
        this.MaximumDistance=100f;
    }
}

/// <summary>
/// ECSのEntityが所有するワールド変換。TransformSyncSystemが毎フレームStageRendererの
/// ワールド行列へ変換して書き込む。
/// </summary>

public struct Renderable:IRuntimeComponent {
    /// <summary>このEntityの変換を書き込む先のStageRendererオブジェクト。</summary>
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

public struct LightShadowRuntime:IRuntimeComponent<LightShadow> {
    public string?InvalidProperty;
}

/// <summary>
/// Runtime-only emissive mesh binding. The source material remains the visual emission authority;
/// this state rebuilds camera-relative direct-light patches whenever its Transform changes.
/// </summary>
public struct EmissiveMeshLighting:IRuntimeComponent {
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

/// <summary>Runtime marker paired with AppHost; it never owns the window or input server.</summary>
public struct AppHostRuntime:IRuntimeComponent<AppHost> {
}

/// <summary>Observable level diagnostics. EngineHost owns the renderer and camera state.</summary>
public struct LevelRuntime:IRuntimeComponent<Level> {
    /// <summary>Last resolved active Camera hierarchy status.</summary>
    public FixedTransformResolveStatus LastCameraWorldResolveStatus;
    /// <summary>Last active Camera fixed-to-float basis status.</summary>
    public FixedTransformStatus LastCameraTransformStatus;
    /// <summary>Whether a valid Camera origin and view have been established.</summary>
    public bool HasLastCameraWorldPose;
    public string?ShaderSemanticHash;
    public bool ShaderArtifactLoaded;
    public int ShaderArtifactDiagnosticCount;
    public bool Loaded;
}

/// <summary>DirectionalLightSyncが毎フレーム書き込む先。LevelLoadがレンダラー生成時に埋める。</summary>
public struct DirectionalLightRuntime:IRuntimeComponent<DirectionalLight> {
    public string?InvalidProperty;
    public Vector3 LastWorldForward;
    public bool HasLastWorldForward;
    public FixedTransformStatus LastTransformStatus;
    public FixedTransformResolveStatus LastWorldResolveStatus;
}

/// <summary>PointLightSyncが毎フレーム書き込む先。</summary>
public struct PointLightRuntime:IRuntimeComponent<PointLight> {
    public string?InvalidProperty;
    public Vector3 LastCameraRelativePosition;
    public bool HasLastCameraRelativePosition;
    public FixedTransformStatus LastTransformStatus;
    public FixedTransformResolveStatus LastWorldResolveStatus;
}

/// <summary>SpotLightSyncが毎フレーム書き込む先。</summary>
public struct SpotLightRuntime:IRuntimeComponent<SpotLight> {
    public string?InvalidProperty;
    public Vector3 LastCameraRelativePosition;
    public Vector3 LastWorldForward;
    public bool HasLastCameraRelativePosition;
    public bool HasLastWorldForward;
    public FixedTransformStatus LastTransformStatus;
    public FixedTransformResolveStatus LastWorldResolveStatus;
}

/// <summary>AreaLightSyncが毎フレーム書き込む先。</summary>
public struct RectAreaLightRuntime:IRuntimeComponent<RectAreaLight> {
    public string?InvalidProperty;
    public Vector3 LastCameraRelativePosition;
    public Vector3 LastWorldForward;
    public Vector3 LastWorldUp;
    public bool HasLastCameraRelativePosition;
    public bool HasLastWorldForward;
    public FixedTransformStatus LastTransformStatus;
    public FixedTransformResolveStatus LastWorldResolveStatus;
}
