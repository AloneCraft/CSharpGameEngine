using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;

namespace UserEcsProject.Scenes;

// ============================================================================
// このプロジェクト固有のStartup Scene宣言。ランタイムへは移さない。
//
// 以前はSystems/GameSystems.csに手書きのGameStartup : ISystemとして書かれていた
// (Bootstrap.Done判定 + state.World.Create(...)を自分で書く必要があった)。[StartupScene]/
// [SceneEntity]により、ユーザーはComponent初期値だけを宣言すればよくなった。GameSystemGeneratorが
// これを「Order = 0、Query {Bootstrap}」の合成Systemへ変換し、既存のSystem実行順序モデルへ
// そのまま合流させる(詳細はStartupSceneSystemEmitterのXMLコメント参照)。
//
// 実行順序: [StartupScene(Order = 0)]、Systems/RuntimeSystem.csのファイル全体には
// [SystemOrder(1)](fileスコープのマーカー経由、ADR-0036)。このSceneが撒く{AppHost, Level}を
// Systems/RuntimeSystem.csのWindow/LevelLoadが読むため、必ず先に走る必要がある。
// ============================================================================

/// <summary>
/// このゲームの起動時設定。生成されたエントリポイントが撒いた<see cref="Bootstrap"/>を消費する合成
/// Systemへ、GameSystemGeneratorがこの宣言を変換する。「どんなウィンドウで」「どのレベルを」
/// 「どのシェーダーで」「どのカメラから」「どんな太陽光・点光源・スポットライトで」見るのかを宣言する。
/// 以前<c>Program.cs</c>の先頭にあった定数が、そのままここのComponent初期値になっている。
/// </summary>
[StartupScene(Order=0)]
internal static partial class MainScene {
    private const string ProductDisplayName="C# Game Engine";

    /// <summary>
    /// 唯一のStartup Entity: ウィンドウ設定(<see cref="AppHost"/>)とレベル設定(<see cref="Level"/>)を
    /// 1組のComponentとして持つ。<see cref="Level.Renderer"/>等の遅延生成フィールドはdefault(null)の
    /// まま置かれ、Systems/RuntimeSystem.csのWindow/LevelLoadが実際のウィンドウ・レンダラー・
    /// ライトEntityを組み立てる(責務分担はADR-0033のまま変更していない)。
    /// </summary>
    [SceneEntity]
    internal static(AppHost App,Level Level)Application=>(
        App:new AppHost {
            Title= $"{ProductDisplayName} - ECS FBX Rendering PoC",
            Width=1280,
            Height=720,
        },

        Level:new Level {
            Path=Path.Combine("Assets","Primitives","Box.fbx"),
            ShaderPath=Path.Combine("Shaders","Stage.hlsl"),
            Camera=Camera.CreateDefault(Vector3.Zero),

            Sun=new UserEcsProject.DirectionalLight {
                DirectionTowardLightX=0.4f,
                DirectionTowardLightY=0.7f,
                DirectionTowardLightZ=0.5f,
                ColorR=1f,
                ColorG=0.95f,
                ColorB=0.9f,
                Intensity=1.0f,
            },

            PointLight=new UserEcsProject.PointLight {
                PositionX=1.5f,
                PositionY=1.0f,
                PositionZ=-1.5f,
                ColorR=0.2f,
                ColorG=0.5f,
                ColorB=1.0f,
                Intensity=6f,
                Range=6f,
            },

            SpotLight=new UserEcsProject.SpotLight {
                PositionX=-1.5f,
                PositionY=2.0f,
                PositionZ=-1.5f,
                DirectionX=0.4f,
                DirectionY=-0.6f,
                DirectionZ=0.4f,
                ColorR=1.0f,
                ColorG=0.3f,
                ColorB=0.1f,
                Intensity=10f,
                Range=8f,
                InnerConeAngleRadians=12f*MathF.PI/180f,
                OuterConeAngleRadians=22f*MathF.PI/180f,
            },

            Listener=new UserEcsProject.AudioListener {
                PositionX=0f,
                PositionY=0f,
                PositionZ=0f,
            },

            Source=new UserEcsProject.AudioSource {
                PositionX=0f,
                PositionY=0f,
                PositionZ=-1f,
                Volume=0.3f,
                Loop=true,
            },
        });
}
