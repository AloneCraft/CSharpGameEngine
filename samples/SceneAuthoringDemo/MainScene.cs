using CSharpGameEngine.Entities;

namespace SceneAuthoringDemo;

// ============================================================================
// USER-WRITTEN REGION
//
// この領域がSceneの正本(samples/SoaSystemsDemo/シーン構築見本.cs のPhase A実装)。
// SceneDefinitionGeneratorが以下を追加生成する(EmitCompilerGeneratedFiles=trueにより
// Generated/ 以下に実ファイルとして書き出されるので、生成結果もそのまま確認できる):
//
//   1. 各 partial SceneNodeSymbol プロパティの実装本体 (*.SceneNodes.g.cs)
//   2. CreateRuntime(World) - Define の構文木から直接構築するRuntime生成コード (*.SceneRuntime.g.cs)
//
// 見本との唯一の意図的な差分: 見本の単一 LocalTransform の代わりに、UserGameProject/Components.cs
// と同じ Position/Rotation/Scale の3コンポーネント構成を再利用する(ユーザー確定済みの設計判断)。
// ============================================================================

[SceneDefinition]
public static partial class MainScene
{
    private static partial SceneNodeSymbol Root { get; }

    private static partial SceneNodeSymbol MainCamera { get; }

    private static partial SceneNodeSymbol Player { get; }

    public static void Define(SceneBuilder Scene)
    {
        Scene.Create(Root, Root =>
        {
            Root.Create(MainCamera, MainCamera =>
            {
                MainCamera.Set(new Position { X = 0f, Y = 2f, Z = -10f });
                MainCamera.Set(new Rotation { X = 0f, Y = 0f, Z = 0f, W = 1f });
                MainCamera.Set(new Scale { X = 1f, Y = 1f, Z = 1f });
                MainCamera.Set(new Camera { FieldOfView = 60f });
            });

            Root.Create(Player, Player =>
            {
                Player.Set(new Position { X = 0f, Y = 0f, Z = 0f });
                Player.Set(new Rotation { X = 0f, Y = 0f, Z = 0f, W = 1f });
                Player.Set(new Scale { X = 1f, Y = 1f, Z = 1f });
                Player.Set(new PlayerTag());
                Player.Set(new Health { Value = 100f });
            });
        });
    }
}
