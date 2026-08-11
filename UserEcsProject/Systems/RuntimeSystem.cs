using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Import.Fbx;
using CSharpGameEngine.Platform.Windows;

namespace UserEcsProject.Systems;

// ============================================================================
// エンジンが提供するべきSystem。いずれ共有ソースへ移す(=ランタイム側)。
// このプロジェクト固有のSystemは Systems/GameSystems.cs にある。
//
// ここにゲーム固有のリテラルは1つも無い。ウィンドウのタイトル/サイズ、レベルのFBXパス、
// シェーダーのパス、初期カメラはすべてComponentの値として渡ってくる。
//
// Program.csは存在せず、`Main`はSGが生成する(Generated/.../GameSystem.EntryPoint.g.cs)。
// 生成されたMainが作るのはWorld・State・Bootstrapシード1個だけで、ウィンドウもレンダラーも
// レベルも、GameStartupSystemとこのファイルのSystemが作る。
//
// 実行順序について(ADR-0033決定事項1 + ADR-0036 + GameSystemLoopEmitterの構造):
//   生成ループは「Archetype順が外側・System順が内側」。そしてArchetypeの出現順は、
//   Systemの並び順に引数の組を登録した順そのもの。したがって
//   「1ファイルにフレーム順で書けば、Archetypeループの順序もフレーム順になる」。
//   このファイル内の7つは相互に順序が意味を持つので、絶対に分割しないこと。
//
//   このファイルはfileスコープのマーカーで[SystemOrder(1)]なので、Systems/GameSystems.csの
//   GameStartupSystem([SystemOrder(0)]をSystem自身に直接指定)より必ず後に走る。
//   ファイル名のアルファベット順には一切依存しない。
//
//   現在の出現順:
//     Archetype0 {Bootstrap}              … GameStartupSystem(GameSystems.cs) 起動時に1回
//     Archetype1 {AppHost}                … WindowSystemの宣言形状(実体の入らない空ループ)
//     Archetype2 {AppHost, Level}         … WindowSystem → LevelLoadSystem
//     Archetype3 {DirectionalLight}       … DirectionalLightSystem
//     Archetype4 {PointLight}             … PointLightSystem
//     Archetype5 {SpotLight}              … SpotLightSystem
//     Archetype6 {Transform, Renderable}  … TransformSyncSystem
//     Archetype7 {FramePresent}           … PresentSystem(必ず最後)
//
//   Archetype.Createは即時追加で、各ループは開始時にCountを読む。よってArchetype0が作った
//   appエンティティはArchetype2のループ開始前に存在し、Archetype2が作ったdirectionalLight/
//   pointLight/spotLight/renderable/presentエンティティもArchetype3〜7のループ開始前に存在する ―
//   起動処理はすべてフレーム1で完了する。
//
//   PointLight/SpotLightはStageRenderer側で毎フレーム加算・リセットされる(ADR-0037)。ライトの
//   種類はUnityのように実行時に切り替えるものではなく、DirectionalLight/PointLight/SpotLightを
//   それぞれ固定のComponent型・専用Systemとして分けている(切り替えが要らない代わりに、
//   新しいライト種を足すときはComponent+Systemを素直に1組追加する)。
//
// 移設先はCSharpGameEngine.Entitiesではあり得ない(net10.0・ProjectReferenceゼロのプラット
// フォーム非依存ソースで、StageRenderer/Win32Windowを参照した瞬間に単体ビルドとADR-0001の階層が
// 壊れる)。CSharpGameEngine.Runtime.Windowsのような新しい共有ソースプロジェクトが要る。
// ============================================================================

/// <summary>
/// ファイル横断の実行順序を明示するマーカー(ADR-0036)。中身は空でよい。このファイルの4つのSystemは
/// 個別に順序を指定していないので、全員この既定値(1)を継承する。
/// </summary>
[SystemOrder(1)]
file sealed class Order;

/// <summary>
/// フレーム先頭。ウィンドウが無ければ作り、あればOSイベントを流す。閉じられたら
/// <see cref="State.Enabled"/>を落として生成ループ(<c>while (State.Enabled)</c>)を終わらせ、
/// falseを返して同じEntityの後続System(=LevelLoadSystem)をスキップする。
/// </summary>
internal struct WindowSystem : ISystem
{
    public bool Update(ref AppHost app, ref State state)
    {
        if (app.Window is not { } window)
        {
            app.Window = new Win32Window(new Win32WindowOptions
            {
                Title = app.Title,
                Width = app.Width,
                Height = app.Height,
                Visible = true,
            });

            return true;
        }

        window.PumpEvents();

        if (window.IsClosed)
        {
            state.Enabled = false;
            return false;
        }

        return true;
    }
}

/// <summary>
/// レベル(FBX)を読み込み、その内容から<see cref="StageRenderer"/>を作って描画対象Entityを撒く。
/// StageRendererはメッシュ集合を確定させてからしか生成できないため、「ウィンドウができている」
/// かつ「レベルが読めた」という両方が揃うこのタイミングでしか作れない ― Contextでは表現できず、
/// Program.csが残っていた本当の理由がこれだった。
/// </summary>
internal struct LevelLoadSystem : ISystem
{
    public bool Update(ref AppHost app, ref Level level, ref State state)
    {
        if (level.Loaded || app.Window is not { } window)
        {
            return true;
        }

        string fbxPath = Path.Combine(AppContext.BaseDirectory, level.Path);
        FbxImportResult importResult = FbxImporter.Import(fbxPath);

        if (!importResult.Success || importResult.Mesh is not { } mesh)
        {
            throw new InvalidOperationException($"Failed to import '{fbxPath}': {string.Join("; ", importResult.Diagnostics)}");
        }

        StageRenderer renderer = StageRenderer.Create(
            window.Handle,
            window.ClientWidth,
            window.ClientHeight,
            new GraphicsDeviceOptions(),
            Path.Combine(AppContext.BaseDirectory, level.ShaderPath),
            [new MeshCreateInfo
            {
                Positions = mesh.Positions,
                Normals = mesh.Normals,
                Indices = mesh.Indices,
                BaseColorFactor = mesh.BaseColorFactor,
            }]);

        window.Resized += (width, height) => renderer.Resize(width, height);
        renderer.SetCamera(level.Camera);

        level.Renderer = renderer;
        level.Loaded = true;

        // level.Sun/PointLight/SpotLightはまだ存在しないレンダラーを指せないので値渡しのまま
        // 置いてあった。ここで初めてレンダラーが確定するため、コピーしてRendererを埋めてから
        // Entityにする(Renderable/FramePresentと同じ「作成時にコピーする」パターン)。
        DirectionalLight sun = level.Sun;
        sun.Renderer = renderer;
        state.World.Create(sun);

        PointLight pointLight = level.PointLight;
        pointLight.Renderer = renderer;
        state.World.Create(pointLight);

        SpotLight spotLight = level.SpotLight;
        spotLight.Renderer = renderer;
        state.World.Create(spotLight);

        state.World.Create(
            new Transform
            {
                PositionX = 0f,
                PositionY = 0f,
                PositionZ = 0f,
                RotationX = 0f,
                RotationY = 0f,
                RotationZ = 0f,
                RotationW = 1f,
                ScaleX = 1f,
                ScaleY = 1f,
                ScaleZ = 1f,
            },
            new Renderable { Renderer = renderer, ObjectIndex = 0 });

        state.World.Create(new FramePresent { Window = window, Renderer = renderer });

        return true;
    }
}

/// <summary>
/// ディレクショナルライト(「太陽」)を毎フレーム<see cref="StageRenderer"/>へ書き込む。<c>DirectionTowardLight*</c>は
/// 光源へ向かうベクトルで、<c>StageRenderer</c>側で正規化されるため呼び出し側での正規化は不要。
/// </summary>
internal struct DirectionalLightSystem : ISystem
{
    public bool Update(ref DirectionalLight light, ref State state)
    {
        if (light.Renderer is not { } renderer)
        {
            return true;
        }

        renderer.SetDirectionalLight(
            new Vector3(light.DirectionTowardLightX, light.DirectionTowardLightY, light.DirectionTowardLightZ),
            new Vector3(light.ColorR, light.ColorG, light.ColorB),
            light.Intensity);

        return true;
    }
}

/// <summary>点光源を毎フレーム<see cref="StageRenderer"/>へ加算する(<see cref="StageRenderer.AddPointLight"/>参照)。</summary>
internal struct PointLightSystem : ISystem
{
    public bool Update(ref PointLight light, ref State state)
    {
        if (light.Renderer is not { } renderer)
        {
            return true;
        }

        renderer.AddPointLight(
            new Vector3(light.PositionX, light.PositionY, light.PositionZ),
            new Vector3(light.ColorR, light.ColorG, light.ColorB),
            light.Intensity,
            light.Range);

        return true;
    }
}

/// <summary>スポットライトを毎フレーム<see cref="StageRenderer"/>へ加算する(<see cref="StageRenderer.AddSpotLight"/>参照)。</summary>
internal struct SpotLightSystem : ISystem
{
    public bool Update(ref SpotLight light, ref State state)
    {
        if (light.Renderer is not { } renderer)
        {
            return true;
        }

        renderer.AddSpotLight(
            new Vector3(light.PositionX, light.PositionY, light.PositionZ),
            new Vector3(light.DirectionX, light.DirectionY, light.DirectionZ),
            new Vector3(light.ColorR, light.ColorG, light.ColorB),
            light.Intensity,
            light.Range,
            light.InnerConeAngleRadians,
            light.OuterConeAngleRadians);

        return true;
    }
}

/// <summary>ECSのTransformを、そのEntityが指すStageRendererオブジェクトのワールド行列へ同期する。</summary>
internal struct TransformSyncSystem : ISystem
{
    public bool Update(ref Transform transform, ref Renderable renderable, ref State state)
    {
        if (renderable.Renderer is not { } renderer)
        {
            return true;
        }

        Quaternion rotation = new(transform.RotationX, transform.RotationY, transform.RotationZ, transform.RotationW);
        Matrix4x4 world = Matrix4x4.CreateScale(transform.ScaleX, transform.ScaleY, transform.ScaleZ)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(transform.PositionX, transform.PositionY, transform.PositionZ);

        renderer.SetObjectTransform(renderable.ObjectIndex, world);
        return true;
    }
}

/// <summary>
/// フレーム末尾。描画を1枚出し、ループが終わると決まったフレームではエンジンオブジェクトを破棄する
/// (Program.csの<c>using</c>が担っていた後始末。生成されたMainはDisposeすべき対象を知らない)。
/// </summary>
internal struct PresentSystem : ISystem
{
    public bool Update(ref FramePresent present, ref State state)
    {
        if (present.Window is not { } window || present.Renderer is not { } renderer)
        {
            return true;
        }

        if (!state.Enabled)
        {
            renderer.Dispose();
            window.Dispose();
            return true;
        }

        if (window.IsMinimized)
        {
            return true;
        }

        renderer.RenderFrame();
        return true;
    }
}
