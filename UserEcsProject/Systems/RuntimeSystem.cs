using System.Numerics;
using CSharpGameEngine.Audio.Abstractions;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Runtime.Windows;

namespace UserEcsProject.Systems;

// ============================================================================
// エンジンが提供するべきSystem。いずれ共有ソースへ移す(=ランタイム側)。
// このプロジェクト固有のStartup Scene宣言は Scenes/MainScene.cs にある。
//
// ここにゲーム固有のリテラルは1つも無い。ウィンドウのタイトル/サイズ、レベルのFBXパス、
// シェーダーのパス、初期カメラはすべてComponentの値として渡ってくる。
//
// Program.csは存在せず、`Main`はSGが生成する(Generated/.../GameSystem.EntryPoint.g.cs)。
// 生成されたMainが作るのはWorld・State・Bootstrapシード1個だけで、ウィンドウもレンダラーも
// レベルも、MainScene([StartupScene]宣言からSGが合成するSystem)とこのファイルのSystemが作る。
//
// 実行順序について(ADR-0033決定事項1 + ADR-0036 + GameSystemLoopEmitterの構造):
//   生成ループは「Archetype順が外側・System順が内側」。そしてArchetypeの出現順は、
//   Systemの並び順に引数の組を登録した順そのもの。したがって
//   「1ファイルにフレーム順で書けば、Archetypeループの順序もフレーム順になる」。
//   このファイル内の9つは相互に順序が意味を持つので、絶対に分割しないこと。
//
//   このファイルはfileスコープのマーカーで[SystemOrder(1)]なので、Scenes/MainScene.csの
//   [StartupScene(Order = 0)]から合成されるSystemより必ず後に走る。
//   ファイル名のアルファベット順には一切依存しない。
//
//   現在の出現順(ADR-0040 Phase 6でPrimitiveMesh用のLevelLoadオーバーロードを追加、既存の番号がずれた):
//     Archetype0  {Bootstrap}                  … MainScene/PrimitiveShowcaseScene 起動時に1回
//     Archetype1  {AppHost}                    … Windowの宣言形状(実体の入らない空ループ)
//     Archetype2  {Transform, PrimitiveMesh}   … LevelLoad(1つ目のUpdateオーバーロード。{AppHost,Level}
//                                                  より必ず先に宣言・登録すること — 詳細はLevelLoad内コメント参照)
//     Archetype3  {AppHost, Level}             … LevelLoad(2つ目のUpdateオーバーロード)
//     Archetype4  {DirectionalLight}           … DirectionalLight
//     Archetype5  {PointLight}                 … PointLight
//     Archetype6  {SpotLight}                  … SpotLight
//     Archetype7  {Transform, Renderable}      … TransformSync
//     Archetype8  {FramePresent}               … Present(必ず最後)
//     Archetype9  {AudioListener}              … AudioListenerSpawn(Archetype形状の宣言のみ、ディスパッチ無し)
//     Archetype10 {AudioSource}                … AudioPlayback
//
//   Archetype.Createは即時追加で、各ループは開始時にCountを読む。よってArchetype0が作った
//   appエンティティはArchetype2のループ開始前に存在し、Archetype2が作ったdirectionalLight/
//   pointLight/spotLight/renderable/present/audioListener/audioSourceエンティティも
//   Archetype3〜9のループ開始前に存在する ― 起動処理はすべてフレーム1で完了する。
//
//   PointLight/SpotLightはStageRenderer側で毎フレーム加算・リセットされる(ADR-0037)。ライトの
//   種類はUnityのように実行時に切り替えるものではなく、DirectionalLight/PointLight/SpotLightを
//   それぞれ固定のComponent型・専用Systemとして分けている(切り替えが要らない代わりに、
//   新しいライト種を足すときはComponent+Systemを素直に1組追加する)。
//
//   AudioListener/AudioSourceは別Entityなので、閉集合Archetypeの制約上、両者を横断参照する
//   距離減衰は実装していない(ADR-0039「既知の制約」参照)。AudioEngineHandleはLevelLoadが
//   StageRendererと同じタイミングで生成し、Level→FramePresentへコピーしてPresentが破棄する
//   (Renderer/Windowと同じ経路)。オーディオバックエンド自体はAudioBackend.cs(このプロジェクトの
//   プラットフォーム依存集中点)経由でのみ参照し、CSharpGameEngine.Audio.XAudio2を直接参照しない。
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
internal struct Window:ISystem {
    public bool Update(ref AppHost App,ref State state){
        if(App.Window is not { } window){
            App.Window=new Win32Window(new Win32WindowOptions {
                Title=App.Title,
                Width=App.Width,
                Height=App.Height,
                Visible=true,
            });

            return true;
        }

        window.PumpEvents();

        if(window.IsClosed){
            state.Enabled=false;
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
internal struct LevelLoad:ISystem {
    // {Transform,PrimitiveMesh}用のこのオーバーロードは、必ず下の{AppHost,Level}用オーバーロードより
    // 前に宣言すること。同一struct内のオーバーロード順序はArchetype登録順に直結し(ADR-0033決定事項1、
    // GameSystemAnalyzer.cs参照)、生成ループはArchetype順が外側なので、先に宣言したオーバーロードの
    // Archetypeは1フレーム内で必ず先に処理される。この2つのオーバーロードは互いに部分集合関係にない
    // 別々のArchetypeを担当するため、宣言順を入れ替えるとコンパイルエラー無しで静かに
    // PendingPrimitivesが空のままStageRenderer.Createへ渡り、Scene DSLで宣言したPrimitiveMeshが
    // 一切描画されなくなる(ADR-0040 Phase 6)。
    private List<(Transform Transform,PrimitiveMesh Mesh)>?PendingPrimitives;
    private bool RosterCaptured;

    public bool Update(ref Transform transform,ref PrimitiveMesh mesh,ref State state){
        if(RosterCaptured){
            // PrimitiveMeshはStageRenderer.Create時に確定する起動専用のロースターの一部であり、
            // このリポジトリの全レンダラーに共通する「オブジェクト集合はCreate時に固定」という制約を
            // そのまま踏襲する(起動後の動的追加は非対応)。
            return true;
        }

        (PendingPrimitives??=[]).Add((transform,mesh));
        return true;
    }

    public bool Update(ref AppHost App,ref Level level,ref State state){
        if(level.Loaded){
            // StageRenderer.SetCameraは「ダブルバッファされた2つのフレームスロットのうち今回の
            // フレームが使う側だけを書く」設計のため、毎フレーム呼ばないと片方のスロットが
            // 起動時カメラのまま固定される(実測されたバグ、Docs/Conformance.md参照)。
            // DirectionalLight/PointLight/SpotLightと同じ「毎フレーム状態を再送する」パターンに合わせる。
            level.Renderer?.SetCamera(level.Camera);
            return true;
        }

        if(App.Window is not { } window){
            return true;
        }

        var GlbPath=Path.Combine(AppContext.BaseDirectory,level.Path);
        StageSceneData Scene=StageSceneData.Load(GlbPath);

        // Scene DSLが宣言した{Transform,PrimitiveMesh}エンティティは、上のオーバーロードが同じ
        // フレーム内で先に処理済みなので、この時点でPendingPrimitivesは確定している。.glbロースターに
        // 連結して1回のStageRenderer.Createへ渡す(2つ目のRendererは作らない — 同一HWNDへの2つの
        // SwapChainは破綻しうる上、Camera/Light/RenderFrame呼び出しを二重配線する必要が生じるため)。
        List<(Transform Transform,PrimitiveMesh Mesh)>Pending=PendingPrimitives??=[];
        (StageMeshCreateInfo[]PrimitiveMeshes,StageMaterialCreateInfo[]PrimitiveMaterials,StageObjectCreateInfo[]PrimitiveObjects)=
            RenderServer.BuildPrimitiveRoster([.. Pending.Select(P=>P.Mesh.Type)],Scene.Meshes.Length,Scene.Materials.Length);

        StageRenderer Renderer=StageRenderer.Create(
            window.Handle,
            window.ClientWidth,
            window.ClientHeight,
            new GraphicsDeviceOptions(),
            Path.Combine(AppContext.BaseDirectory,level.ShaderPath),
            [.. Scene.Meshes,.. PrimitiveMeshes],
            [.. Scene.Materials,.. PrimitiveMaterials],
            [.. Scene.Objects,.. PrimitiveObjects]);

        window.Resized+=(Width,Height)=>Renderer.Resize(Width,Height);
        Renderer.SetCamera(level.Camera);

        level.Renderer=Renderer;
        level.Loaded=true;

        // StageRendererと同じタイミングでAudioEngineを生成する。level.Listener/Sourceも
        // Sun/PointLight/SpotLightと同じ「値渡しで置いてあったものをここで実Entity化する」パターン。
        AudioEngineHandle AudioEngine=GameAudio.CreateEngine();
        level.AudioEngine=AudioEngine;

        state.World.Create(level.Listener);

        UserEcsProject.AudioSource Source=level.Source;
        AudioVoiceHandle Voice=GameAudio.CreateVoice(AudioEngine,sampleRate:44100,channels:1);
        Voice=GameAudio.SubmitBuffer(Voice,ProceduralAudioClip.GenerateSineWave(440f,1f,44100),Source.Loop);
        GameAudio.SetVolume(Voice,Source.Volume);
        GameAudio.Start(Voice);
        Source.Voice=Voice;
        state.World.Create(Source);

        // level.Sun/PointLight/SpotLightはまだ存在しないレンダラーを指せないので値渡しのまま
        // 置いてあった。ここで初めてレンダラーが確定するため、コピーしてRendererを埋めてから
        // Entityにする(Renderable/FramePresentと同じ「作成時にコピーする」パターン)。
        UserEcsProject.DirectionalLight Sun=level.Sun;
        Sun.Renderer=Renderer;
        state.World.Create(Sun);

        UserEcsProject.PointLight pointLight=level.PointLight;
        pointLight.Renderer=Renderer;
        state.World.Create(pointLight);

        UserEcsProject.SpotLight spotLight=level.SpotLight;
        spotLight.Renderer=Renderer;
        state.World.Create(spotLight);

        for(var objectIndex=0;objectIndex<Scene.Objects.Length;objectIndex++){
            Matrix4x4.Decompose(
                Scene.ObjectTransforms[objectIndex],
                out Vector3 scale,
                out Quaternion rotation,
                out Vector3 translation);

            state.World.Create(
                new Transform {
                    PositionX=translation.X,
                    PositionY=translation.Y,
                    PositionZ=translation.Z,
                    RotationX=rotation.X,
                    RotationY=rotation.Y,
                    RotationZ=rotation.Z,
                    RotationW=rotation.W,
                    ScaleX=scale.X,
                    ScaleY=scale.Y,
                    ScaleZ=scale.Z,
                },
                new Renderable { Renderer=Renderer,ObjectIndex=objectIndex });
        }

        // 元の{Transform,PrimitiveMesh}エンティティ自体は変更・破棄しない(何も後から読まないため
        // 無害)。Sun/PointLight/SpotLightと同じ「値渡しで置いてあったものをここで実Entity化する」
        // パターンで、.glbオブジェクトに続くObjectIndexを持つ新しい{Transform,Renderable}エンティティを
        // 別途作る(MoveArchetypeは使わない — 使ってもObjectIndex確定前に全プリミティブを列挙すると
        // いう同じ問題を移すだけで解決しない)。
        for(var Index=0;Index<Pending.Count;Index++){
            state.World.Create(Pending[Index].Transform,new Renderable { Renderer=Renderer,ObjectIndex=Scene.Objects.Length+Index });
        }

        RosterCaptured=true;
        PendingPrimitives=null;

        state.World.Create(new FramePresent { Window=window,Renderer=Renderer,AudioEngine=AudioEngine });

        return true;
    }
}

/// <summary>
/// ディレクショナルライト(「太陽」)を毎フレーム<see cref="StageRenderer"/>へ書き込む。<c>DirectionTowardLight*</c>は
/// 光源へ向かうベクトルで、<c>StageRenderer</c>側で正規化されるため呼び出し側での正規化は不要。
/// </summary>
internal struct DirectionalLight:ISystem {
    public bool Update(ref UserEcsProject.DirectionalLight Light,ref State state){
        if(Light.Renderer is not { } Renderer){
            return true;
        }

        Renderer.SetDirectionalLight(
            new Vector3(Light.DirectionTowardLightX,Light.DirectionTowardLightY,Light.DirectionTowardLightZ),
            new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),
            Light.Intensity);

        return true;
    }
}

/// <summary>点光源を毎フレーム<see cref="StageRenderer"/>へ加算する(<see cref="StageRenderer.AddPointLight"/>参照)。</summary>
internal struct PointLight:ISystem {
    public bool Update(ref UserEcsProject.PointLight Light,ref State state){
        if(Light.Renderer is not { } Renderer){
            return true;
        }

        Renderer.AddPointLight(
            new Vector3(Light.PositionX,Light.PositionY,Light.PositionZ),
            new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),
            Light.Intensity,
            Light.Range);

        return true;
    }
}

/// <summary>スポットライトを毎フレーム<see cref="StageRenderer"/>へ加算する(<see cref="StageRenderer.AddSpotLight"/>参照)。</summary>
internal struct SpotLight:ISystem {
    public bool Update(ref UserEcsProject.SpotLight Light,ref State state){
        if(Light.Renderer is not { } Renderer){
            return true;
        }

        Renderer.AddSpotLight(
            new Vector3(Light.PositionX,Light.PositionY,Light.PositionZ),
            new Vector3(Light.DirectionX,Light.DirectionY,Light.DirectionZ),
            new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),
            Light.Intensity,
            Light.Range,
            Light.InnerConeAngleRadians,
            Light.OuterConeAngleRadians);

        return true;
    }
}

/// <summary>ECSのTransformを、そのEntityが指すStageRendererオブジェクトのワールド行列へ同期する。</summary>
internal struct TransformSync:ISystem {
    public bool Update(ref Transform transform,ref Renderable renderable,ref State state){
        if(renderable.Renderer is not { } Renderer){
            return true;
        }

        Quaternion Rotation=new(transform.RotationX,transform.RotationY,transform.RotationZ,transform.RotationW);
        Matrix4x4 world=Matrix4x4.CreateScale(transform.ScaleX,transform.ScaleY,transform.ScaleZ)
            *Matrix4x4.CreateFromQuaternion(Rotation)
            *Matrix4x4.CreateTranslation(transform.PositionX,transform.PositionY,transform.PositionZ);

        Renderer.SetObjectTransform(renderable.ObjectIndex,world);
        return true;
    }
}

/// <summary>
/// {AudioListener}のArchetype形状だけを宣言する(ADR-0033のSpawn用途: どのUpdateとも一致しない
/// 構成を成立させる)。距離減衰は未実装のため、現時点ではディスパッチ対象を持たない
/// (ADR-0039「既知の制約」参照)。
/// </summary>
internal struct AudioListenerSpawn:ISystem {
    public void Spawn(ref AudioListener listener){
    }
}

/// <summary>
/// 毎フレーム<see cref="AudioSource.Volume"/>を<see cref="AudioSource.Voice"/>へ同期し、終了時に
/// 破棄する(DirectionalLight等と同じ「毎フレーム同期・終了時破棄」パターン)。<see cref="Present"/>の
/// <c>GameAudio.DestroyEngine</c>より前に実行される必要がある(宣言順=実行順。エンジン破棄後に
/// Voiceへアクセスすると解放済みポインタを触ってAccessViolationになる)。
/// </summary>
internal struct AudioPlayback:ISystem {
    public bool Update(ref AudioSource source,ref State state){
        if(source.Voice is not { } Voice){
            return true;
        }

        if(!state.Enabled){
            GameAudio.Stop(Voice);
            GameAudio.DestroyVoice(Voice);
            return true;
        }

        GameAudio.SetVolume(Voice,source.Volume);
        return true;
    }
}

/// <summary>
/// フレーム末尾。描画を1枚出し、ループが終わると決まったフレームではエンジンオブジェクトを破棄する
/// (Program.csの<c>using</c>が担っていた後始末。生成されたMainはDisposeすべき対象を知らない)。
/// <see cref="AudioPlayback"/>より後に実行される必要がある(Voice破棄前にEngineを破棄しない)。
/// </summary>
internal struct Present:ISystem {
    public bool Update(ref FramePresent present,ref State state){
        if(present.Window is not { } window||present.Renderer is not { } Renderer){
            return true;
        }

        if(!state.Enabled){
            Renderer.Dispose();
            window.Dispose();

            if(present.AudioEngine is { } AudioEngine){
                GameAudio.DestroyEngine(AudioEngine);
            }

            return true;
        }

        if(window.IsMinimized){
            return true;
        }

        Renderer.RenderFrame();
        return true;
    }
}
