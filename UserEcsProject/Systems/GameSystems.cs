using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;

namespace UserEcsProject.Systems;

// ============================================================================
// このプロジェクト固有のSystem。ランタイムへは移さない。
//
// Systems/RuntimeSystem.cs が「エンジンが提供するべきSystem(いずれ共有ソースへ移すもの)」で、
// こちらは「そのエンジンを使う1本のゲームが書くSystem」。両者を分けておくと、移設のときに
// ファイルごと動かすだけで済み、ランタイム側にゲームのリテラルが残っていないことも一目で分かる。
//
// 実行順序: このSystem自身に[SystemOrder(0)]、Systems/RuntimeSystem.csのファイル全体にはf     
// [SystemOrder(1)](fileスコープのマーカー経由、ADR-0036)。ファイル名のアルファベット順
// (たまたまG<Rだった)に頼らず、このSystemが先に走ることを明示的に宣言している。仮に順序が    
// 入れ替わっても壊れはせず起動が数フレーム遅れるだけだが(Runtime側のSystemはいずれも
// 「まだ無い」状態をnullで判定して素通りするため)、意図を偶然に委ねないために宣言する。    
// [SystemOrder]はISystem実装struct自身に直接付けてもよいし(このファイルの例)、fileスコープの
// マーカークラスに付けてファイル全体の既定値にしてもよい(Systems/RuntimeSystem.csの例)。 
// ============================================================================

/// <summary>
/// このゲームの起動時設定。生成されたエントリポイントが撒いた<see cref="Bootstrap"/>を受け取り、
/// 「どんなウィンドウで」「どのレベルを」「どのシェーダーで」「どのカメラから」「どんな太陽光・
/// 点光源・スポットライトで」見るのかを宣言する。以前<c>Program.cs</c>の先頭にあった定数が、
/// そのままここのComponent初期値になっている。
/// </summary>
[SystemOrder(0)]
internal struct GameStartupSystem : ISystem
{
    private const string ProductDisplayName = "C# Game Engine";

    public bool Update(ref Bootstrap bootstrap, ref State state)
    {
        if (bootstrap.Done)
        {
            return true;
        }

        bootstrap.Done = true;

        state.World.Create(
            new AppHost
            {
                Title = $"{ProductDisplayName} - ECS FBX Rendering PoC",
                Width = 1280,
                Height = 720,
            },
            new Level
            {
                Path = Path.Combine("Assets", "Primitives", "Box.fbx"),
                ShaderPath = Path.Combine("Shaders", "Stage.hlsl"),
                Camera = Camera.CreateDefault(Vector3.Zero),
                Sun = new DirectionalLight
                {
                    DirectionTowardLightX = 0.4f,
                    DirectionTowardLightY = 0.7f,
                    DirectionTowardLightZ = 0.5f,
                    ColorR = 1f,
                    ColorG = 0.95f,
                    ColorB = 0.9f,
                    Intensity = 1.0f,
                },
                PointLight = new PointLight
                {
                    PositionX = 1.5f,
                    PositionY = 1.0f,
                    PositionZ = -1.5f,
                    ColorR = 0.2f,
                    ColorG = 0.5f,
                    ColorB = 1.0f,
                    Intensity = 6f,
                    Range = 6f,
                },
                SpotLight = new SpotLight
                {
                    PositionX = -1.5f,
                    PositionY = 2.0f,
                    PositionZ = -1.5f,
                    DirectionX = 0.4f,
                    DirectionY = -0.6f,
                    DirectionZ = 0.4f,
                    ColorR = 1.0f,
                    ColorG = 0.3f,
                    ColorB = 0.1f,
                    Intensity = 10f,
                    Range = 8f,
                    InnerConeAngleRadians = 12f * MathF.PI / 180f,
                    OuterConeAngleRadians = 22f * MathF.PI / 180f,
                },
            });

        return true;
    }
}
