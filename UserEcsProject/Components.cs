using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
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
}

/// <summary>
/// 単一のディレクショナルライト(「太陽」)。DirectionalLightSystemが毎フレーム
/// <see cref="Renderer"/>へ書き込む。<see cref="DirectionTowardLightX"/>等は光源へ向かうベクトルで、
/// 呼び出し側が正規化しておく必要はない(<c>StageRenderer</c>側で正規化される)。
/// </summary>
[SceneComponentIcon("Assets/Editor/ComponentIcons/DirectionalLight.svg")]
public struct DirectionalLight {

    public float DirectionTowardLightX;
    public float DirectionTowardLightY;
    public float DirectionTowardLightZ;

    public float ColorR;
    public float ColorG;
    public float ColorB;

    public float Intensity;

    /// <summary>真上から白色で照らす既定値。方向が零ベクトルだと正規化できず何も照らさない。</summary>
    public DirectionalLight(){
        this.DirectionTowardLightY=1f;
        this.ColorR=1f;
        this.ColorG=1f;
        this.ColorB=1f;
        this.Intensity=1f;
    }
}

/// <summary>
/// 単一の点光源。全方向へ均等に届き、<see cref="Range"/>で滑らかに減衰してゼロになる。
/// ライトの種類はUnityのように実行時に切り替えるものではなく、<see cref="DirectionalLight"/>/
/// <see cref="SpotLight"/>とそれぞれ別のComponent型・別のSystemに固定して分けている(型ごとに
/// Archetypeが分かれるため、切り替え不要でSystemはそのライト種の処理だけ書けばよい)。
/// PointLightSystemが毎フレーム<see cref="Renderer"/>へ書き込む。
/// </summary>
[SceneComponentIcon("Assets/Editor/ComponentIcons/PointLight.svg")]
public struct PointLight {

    public float PositionX;
    public float PositionY;
    public float PositionZ;

    public float ColorR;
    public float ColorG;
    public float ColorB;

    public float Intensity;

    /// <summary>この距離で明るさがちょうどゼロになる。</summary>
    public float Range;

    /// <summary>追加した直後から周囲を照らす既定値。Rangeが0だと即座に減衰して見えない。</summary>
    public PointLight(){
        this.ColorR=1f;
        this.ColorG=1f;
        this.ColorB=1f;
        this.Intensity=1f;
        this.Range=10f;
    }
}

/// <summary>
/// 単一のスポットライト(円錐状に絞られた点光源)。<see cref="DirectionX"/>等は円錐が伸びる方向
/// (呼び出し側が正規化しておく必要はない)。<see cref="InnerConeAngleRadians"/>以内は全光量、
/// <see cref="OuterConeAngleRadians"/>を超えるとゼロで、その間は滑らかに減衰する。
/// SpotLightSystemが毎フレーム<see cref="Renderer"/>へ書き込む。
/// </summary>
[SceneComponentIcon("Assets/Editor/ComponentIcons/SpotLight.svg")]
public struct SpotLight {
    public float PositionX;
    public float PositionY;
    public float PositionZ;

    public float DirectionX;
    public float DirectionY;
    public float DirectionZ;

    public float ColorR;
    public float ColorG;
    public float ColorB;

    public float Intensity;

    /// <summary>この距離で明るさがちょうどゼロになる。</summary>
    public float Range;

    /// <summary>この半頂角以内は全光量。</summary>
    public float InnerConeAngleRadians;

    /// <summary>この半頂角を超えるとゼロ。</summary>
    public float OuterConeAngleRadians;

    /// <summary>真下へ向く円錐の既定値。方向・Range・円錐角が0だと何も照らさない。</summary>
    public SpotLight(){
        this.DirectionY=-1f;
        this.ColorR=1f;
        this.ColorG=1f;
        this.ColorB=1f;
        this.Intensity=1f;
        this.Range=10f;
        this.InnerConeAngleRadians=0.3f;
        this.OuterConeAngleRadians=0.6f;
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
}

/// <summary>PointLightSyncが毎フレーム書き込む先。</summary>
public struct PointLightRuntime:IRuntimeComponent<PointLight> {
    public StageRenderer?Renderer;
}

/// <summary>SpotLightSyncが毎フレーム書き込む先。</summary>
public struct SpotLightRuntime:IRuntimeComponent<SpotLight> {
    public StageRenderer?Renderer;
}

/// <summary>AudioPlaybackが操作する音声。AudioEngine確定後にLevelLoadが埋める。</summary>
public struct AudioSourceRuntime:IRuntimeComponent<AudioSource> {
    public AudioVoiceHandle?Voice;
}
