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
//   GameStartup([SystemOrder(0)]をSystem自身に直接指定)より必ず後に走る。
//   ファイル名のアルファベット順には一切依存しない。
//
//   現在の出現順:
//     Archetype0 {Bootstrap}              … GameStartup(GameSystems.cs) 起動時に1回
//     Archetype1 {AppHost}                … Windowの宣言形状(実体の入らない空ループ)
//     Archetype2 {AppHost, Level}         … Window → LevelLoad
//     Archetype3 {DirectionalLight}       … DirectionalLight
//     Archetype4 {PointLight}             … PointLight
//     Archetype5 {SpotLight}              … SpotLight
//     Archetype6 {Transform, Renderable}  … TransformSync
//     Archetype7 {FramePresent}           … Present(必ず最後)
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
internal struct Window : ISystem
{
    public bool Update(ref AppHost App, ref State state)
    {
        if (App.Window is not { } window)
        {
            App.Window = new Win32Window(new Win32WindowOptions
            {
                Title = App.Title,
                Width = App.Width,
                Height = App.Height,
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
internal struct LevelLoad : ISystem
{
    public bool Update(ref AppHost App, ref Level level, ref State state)
    {
        if (level.Loaded || App.Window is not { } window)
        {
            return true;
        }

        string FbxPath = Path.Combine(AppContext.BaseDirectory, level.Path);
        FbxImportResult ImportResult = FbxImporter.Import(FbxPath);

        if (!ImportResult.Success || ImportResult.Mesh is not { } Mesh)
        {
            throw new InvalidOperationException($"Failed to import '{FbxPath}': {string.Join("; ", ImportResult.Diagnostics)}");
        }

        StageRenderer Renderer = StageRenderer.Create(
            window.Handle,
            window.ClientWidth,
            window.ClientHeight,
            new GraphicsDeviceOptions(),
            Path.Combine(AppContext.BaseDirectory, level.ShaderPath),
            [new MeshCreateInfo
            {
                Positions = Mesh.Positions,
                Normals = Mesh.Normals,
                Indices = Mesh.Indices,
                BaseColorFactor = Mesh.BaseColorFactor,
            }]);

        window.Resized += (Width, Height) => Renderer.Resize(Width, Height);
        Renderer.SetCamera(level.Camera);

        level.Renderer = Renderer;
        level.Loaded = true;

        // level.Sun/PointLight/SpotLightはまだ存在しないレンダラーを指せないので値渡しのまま
        // 置いてあった。ここで初めてレンダラーが確定するため、コピーしてRendererを埋めてから
        // Entityにする(Renderable/FramePresentと同じ「作成時にコピーする」パターン)。
        UserEcsProject.DirectionalLight Sun = level.Sun;
        Sun.Renderer = Renderer;
        state.World.Create(Sun);

        UserEcsProject.PointLight pointLight = level.PointLight;
        pointLight.Renderer = Renderer;
        state.World.Create(pointLight);

        UserEcsProject.SpotLight spotLight = level.SpotLight;
        spotLight.Renderer = Renderer;
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
            new Renderable { Renderer = Renderer, ObjectIndex = 0 });

        state.World.Create(new FramePresent { Window = window, Renderer = Renderer });

        return true;
    }
}

/// <summary>
/// ディレクショナルライト(「太陽」)を毎フレーム<see cref="StageRenderer"/>へ書き込む。<c>DirectionTowardLight*</c>は
/// 光源へ向かうベクトルで、<c>StageRenderer</c>側で正規化されるため呼び出し側での正規化は不要。
/// </summary>
internal struct DirectionalLight : ISystem
{
    public bool Update(ref UserEcsProject.DirectionalLight Light, ref State state)
    {
        if (Light.Renderer is not { } Renderer)
        {
            return true;
        }

        Renderer.SetDirectionalLight(
            new Vector3(Light.DirectionTowardLightX, Light.DirectionTowardLightY, Light.DirectionTowardLightZ),
            new Vector3(Light.ColorR, Light.ColorG, Light.ColorB),
            Light.Intensity);

        return true;
    }
}

/// <summary>点光源を毎フレーム<see cref="StageRenderer"/>へ加算する(<see cref="StageRenderer.AddPointLight"/>参照)。</summary>
internal struct PointLight : ISystem
{
    public bool Update(ref UserEcsProject.PointLight Light, ref State state)
    {
        if (Light.Renderer is not { } Renderer)
        {
            return true;
        }

        Renderer.AddPointLight(
            new Vector3(Light.PositionX, Light.PositionY, Light.PositionZ),
            new Vector3(Light.ColorR, Light.ColorG, Light.ColorB),
            Light.Intensity,
            Light.Range);

        return true;
    }
}

/// <summary>スポットライトを毎フレーム<see cref="StageRenderer"/>へ加算する(<see cref="StageRenderer.AddSpotLight"/>参照)。</summary>
internal struct SpotLight : ISystem
{
    public bool Update(ref UserEcsProject.SpotLight Light, ref State state)
    {
        if (Light.Renderer is not { } Renderer)
        {
            return true;
        }

        Renderer.AddSpotLight(
            new Vector3(Light.PositionX, Light.PositionY, Light.PositionZ),
            new Vector3(Light.DirectionX, Light.DirectionY, Light.DirectionZ),
            new Vector3(Light.ColorR, Light.ColorG, Light.ColorB),
            Light.Intensity,
            Light.Range,
            Light.InnerConeAngleRadians,
            Light.OuterConeAngleRadians);

        return true;
    }
}

/// <summary>ECSのTransformを、そのEntityが指すStageRendererオブジェクトのワールド行列へ同期する。</summary>
internal struct TransformSync : ISystem
{
    public bool Update(ref Transform transform, ref Renderable renderable, ref State state)
    {
        if (renderable.Renderer is not { } Renderer)
        {
            return true;
        }

        Quaternion Rotation = new(transform.RotationX, transform.RotationY, transform.RotationZ, transform.RotationW);
        Matrix4x4 world = Matrix4x4.CreateScale(transform.ScaleX, transform.ScaleY, transform.ScaleZ)
            * Matrix4x4.CreateFromQuaternion(Rotation)
            * Matrix4x4.CreateTranslation(transform.PositionX, transform.PositionY, transform.PositionZ);

        Renderer.SetObjectTransform(renderable.ObjectIndex, world);
        return true;
    }
}

/// <summary>
/// フレーム末尾。描画を1枚出し、ループが終わると決まったフレームではエンジンオブジェクトを破棄する
/// (Program.csの<c>using</c>が担っていた後始末。生成されたMainはDisposeすべき対象を知らない)。
/// </summary>
internal struct Present : ISystem
{
    public bool Update(ref FramePresent present, ref State state)
    {
        if (present.Window is not { } window || present.Renderer is not { } Renderer)
        {
            return true;
        }

        if (!state.Enabled)
        {
            Renderer.Dispose();
            window.Dispose();
            return true;
        }

        if (window.IsMinimized)
        {
            return true;
        }

        Renderer.RenderFrame();
        return true;
    }
}
