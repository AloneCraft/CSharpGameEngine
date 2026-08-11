using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;

namespace UserEcsProject;

// ============================================================================
// エンジンオブジェクト(Win32Window / StageRenderer)はComponentのフィールドとして持つ。
//
// Componentの要件は「非nullableのstructであること」だけなので、参照型フィールドを持つことは制約に
// 触れない(UserGameProjectの SceneSpawnRequest { LoadedScene? Scene } と同じ既存パターン)。
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
// 移す対象。ゲーム固有のComponentを足すときは、Systems/GameSystems.cs と対になる別ファイルに置くこと。
// ============================================================================

/// <summary>
/// アプリケーション本体。ウィンドウの設定値と、WindowSystemが遅延生成したウィンドウ実体を持つ。
/// </summary>
public struct AppHost
{
    public string Title;
    public int Width;
    public int Height;

    /// <summary>WindowSystemが最初のフレームで生成する。それまではnull。</summary>
    public Win32Window? Window;
}

/// <summary>
/// レベル(= このエンジンではFBXファイル1つ)。LevelLoadがインポートし、その内容から
/// StageRendererを生成して、描画対象のEntityを撒く。
///
/// <para>
/// <b>どの</b>レベルをどのシェーダーでどのカメラから見るかは全部ゲーム側の決定なので、値はすべて
/// <c>GameStartup</c>(Systems/GameSystems.cs)が入れる。LevelLoadはここに入っている
/// ものを読むだけで、リテラルを1つも持たない。
/// </para>
/// </summary>
public struct Level
{
    /// <summary>FBXのパス。アプリのベースディレクトリ(<c>AppContext.BaseDirectory</c>)からの相対。</summary>
    public string Path;

    /// <summary>シェーダーのパス。同じくベースディレクトリからの相対。</summary>
    public string ShaderPath;

    /// <summary>レンダラー生成時に一度だけ適用する初期カメラ。</summary>
    public Camera Camera;

    /// <summary>
    /// 太陽光の初期値。<see cref="DirectionalLight.Renderer"/>はまだ存在しないレンダラーを指せないため
    /// 常に既定値(null)のまま置いておき、LevelLoadSystemがレンダラー生成と同時にここから値をコピーして
    /// 実際のDirectionalLight Entityを作る(Renderer/Renderableと同じ「作成時にコピーする」パターン)。
    /// </summary>
    public DirectionalLight Sun;

    /// <summary>点光源の初期値(<see cref="Sun"/>と同じ「値渡し→LevelLoadSystemがRendererを埋める」パターン)。</summary>
    public PointLight PointLight;

    /// <summary>スポットライトの初期値(<see cref="Sun"/>と同じパターン)。</summary>
    public SpotLight SpotLight;

    public bool Loaded;

    /// <summary>LevelLoadSystemが生成する。StageRendererはメッシュ集合を確定させてからしか作れない。</summary>
    public StageRenderer? Renderer;
}

/// <summary>
/// ECSのEntityが所有するワールド変換。TransformSyncSystemが毎フレームStageRendererの
/// ワールド行列へ変換して書き込む。
/// </summary>
public struct Transform
{
    public float PositionX;
    public float PositionY;
    public float PositionZ;

    public float RotationX;
    public float RotationY;
    public float RotationZ;
    public float RotationW;

    public float ScaleX;
    public float ScaleY;
    public float ScaleZ;
}

/// <summary>このEntityの変換を書き込む先のStageRendererオブジェクト。</summary>
public struct Renderable
{
    public StageRenderer? Renderer;
    public int ObjectIndex;
}

/// <summary>
/// 単一のディレクショナルライト(「太陽」)。DirectionalLightSystemが毎フレーム
/// <see cref="Renderer"/>へ書き込む。<see cref="DirectionTowardLightX"/>等は光源へ向かうベクトルで、
/// 呼び出し側が正規化しておく必要はない(<c>StageRenderer</c>側で正規化される)。
/// </summary>
public struct DirectionalLight
{
    public StageRenderer? Renderer;

    public float DirectionTowardLightX;
    public float DirectionTowardLightY;
    public float DirectionTowardLightZ;

    public float ColorR;
    public float ColorG;
    public float ColorB;

    public float Intensity;
}

/// <summary>
/// 単一の点光源。全方向へ均等に届き、<see cref="Range"/>で滑らかに減衰してゼロになる。
/// ライトの種類はUnityのように実行時に切り替えるものではなく、<see cref="DirectionalLight"/>/
/// <see cref="SpotLight"/>とそれぞれ別のComponent型・別のSystemに固定して分けている(型ごとに
/// Archetypeが分かれるため、切り替え不要でSystemはそのライト種の処理だけ書けばよい)。
/// PointLightSystemが毎フレーム<see cref="Renderer"/>へ書き込む。
/// </summary>
public struct PointLight
{
    public StageRenderer? Renderer;

    public float PositionX;
    public float PositionY;
    public float PositionZ;

    public float ColorR;
    public float ColorG;
    public float ColorB;

    public float Intensity;

    /// <summary>この距離で明るさがちょうどゼロになる。</summary>
    public float Range;
}

/// <summary>
/// 単一のスポットライト(円錐状に絞られた点光源)。<see cref="DirectionX"/>等は円錐が伸びる方向
/// (呼び出し側が正規化しておく必要はない)。<see cref="InnerConeAngleRadians"/>以内は全光量、
/// <see cref="OuterConeAngleRadians"/>を超えるとゼロで、その間は滑らかに減衰する。
/// SpotLightSystemが毎フレーム<see cref="Renderer"/>へ書き込む。
/// </summary>
public struct SpotLight
{
    public StageRenderer? Renderer;

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
}

/// <summary>
/// フレーム末尾のPresentフェーズを表す単独Entity。<b>AppHostとは別のEntityにしている</b>のは
/// 実行順序のため: 生成ループはArchetype順が外側なので、Presentを担うArchetypeがソース上で
/// 最後に出現するようにしないと、描画同期より前にPresentしてしまう(Systems/RuntimeSystem.csの
/// 冒頭コメント参照)。
/// </summary>
public struct FramePresent
{
    public Win32Window? Window;
    public StageRenderer? Renderer;
}
