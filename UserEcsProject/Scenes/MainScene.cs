using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;

namespace UserEcsProject.Scenes;

// ============================================================================
// このプロジェクト固有のStartup Scene宣言。ランタイムへは移さない。
//
// C#がScene用の宣言DSLである(ADR-0040)。このRootプロパティの本体は<b>実行されない</b> —
// RoslynがObjectCreationExpressionとして読み、Source Generatorが
// {Namespace}.MainScene.Scene.g.cs へ ArchetypeNN.Create の直接呼び出しを生成する。
// [StartupScene]により、生成された合成System(Query {Bootstrap})が起動時に一度だけ
// MainScene.Instantiate(ref State.World) を呼ぶ。
//
// 実行順序: [StartupScene(Order = 0)]、Systems/RuntimeSystem.csのファイル全体には
// [SystemOrder(1)](fileスコープのマーカー経由、ADR-0036)。このSceneが撒く{AppHost, Level}を
// Systems/RuntimeSystem.csのWindow/LevelLoadが読むため、必ず先に走る必要がある。
//
// Scene DSLは静的に意味を確定できる式しか許可しない。Path.Combineは定数畳み込みできないため
// 文字列リテラルにしてある(BCLのメソッドには[SceneFactory]を付けられない)。
// Camera.CreateFirstPersonは[SceneFactory]を明示的に付与したstaticファクトリなので呼べる。
// ============================================================================

/// <summary>
/// このゲームの起動時設定。「どんなウィンドウで」「どのレベルを」「どのシェーダーで」
/// 「どのカメラから」「どんな太陽光・点光源・スポットライトで」見るのかを宣言する。
/// 以前<c>Program.cs</c>の先頭にあった定数が、そのままここのComponent初期値になっている。
/// </summary>
[StartupScene(Order=0)]
internal partial class MainScene:IScene {
    private const string ProductDisplayName="C# Game Engine";

    /// <summary>
    /// 唯一のStartup Entity: ウィンドウ設定(<see cref="AppHost"/>)とレベル設定(<see cref="Level"/>)を
    /// 1ノードのComponent構成として持つ。<see cref="Level.Renderer"/>等の遅延生成フィールドは宣言せず
    /// default(null)のまま置かれ、Systems/RuntimeSystem.csのWindow/LevelLoadが実際のウィンドウ・
    /// レンダラー・ライトEntityを組み立てる(責務分担はADR-0033のまま変更していない)。
    /// </summary>
    public Node Root=>new(){
        Name="Application",

        Components=[
            new AppHost {
                Title=ProductDisplayName+" - FirstPerson Stage",
                Width=1280,
                Height=720,
            },

            new Level {
                Path="Assets/Stage/Lvl_FirstPerson/Lvl_FirstPerson.glb",
                ShaderPath="Shaders/Stage.hlsl",
                Camera=Camera.CreateFirstPerson(new Vector3(0f,4.62f,0f)),

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
            },
        ],
    };
}
