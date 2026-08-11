using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class SceneDefinitionGeneratorEmissionTests
{
    [Fact]
    public void SimpleScene_GeneratesExpectedSceneNodesShape()
    {
        string Source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class MainScene
            {
                private static partial SceneNodeSymbol Root { get; }

                private static partial SceneNodeSymbol MainCamera { get; }

                public static void Define(SceneBuilder Scene)
                {
                    Scene.Create(Root, Root =>
                    {
                        Root.Create(MainCamera, MainCamera =>
                        {
                            MainCamera.Set(new Camera { FieldOfView = 60f });
                        });
                    });
                }
            }
            """;

        GeneratorDriverRunResult Result = SceneDefinitionGeneratorTestHelper.RunGenerator(Source);

        Assert.Empty(Result.Diagnostics);
        GeneratedSourceResult generated = Assert.Single(Result.Results[0].GeneratedSources, S => S.HintName == "Sample.MainScene.SceneNodes.g.cs");
        string Text = generated.SourceText.ToString();

        Assert.Contains("partial class MainScene", Text);
        Assert.Contains("private static partial global::CSharpGameEngine.Entities.SceneNodeSymbol Root", Text);
        Assert.Contains("private static partial global::CSharpGameEngine.Entities.SceneNodeSymbol MainCamera", Text);
        Assert.Contains("nameof(Root)", Text);
        Assert.Contains("nameof(MainCamera)", Text);
        Assert.Contains("new global::CSharpGameEngine.Entities.SceneNodeId(0x", Text);
    }

    [Fact]
    public void SimpleScene_GeneratesExpectedSceneRuntimeShape()
    {
        string Source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class MainScene
            {
                private static partial SceneNodeSymbol Root { get; }

                private static partial SceneNodeSymbol MainCamera { get; }

                public static void Define(SceneBuilder Scene)
                {
                    Scene.Create(Root, Root =>
                    {
                        Root.Create(MainCamera, MainCamera =>
                        {
                            MainCamera.Set(new Camera { FieldOfView = 60f });
                        });
                    });
                }
            }
            """;

        GeneratorDriverRunResult Result = SceneDefinitionGeneratorTestHelper.RunGenerator(Source);

        Assert.Empty(Result.Diagnostics);
        GeneratedSourceResult generated = Assert.Single(Result.Results[0].GeneratedSources, S => S.HintName == "Sample.MainScene.SceneRuntime.g.cs");
        string Text = generated.SourceText.ToString();

        // ADR-0033: Worldがstructになったため`ref World`を取る。ノードごとの生成は、旧実装の
        // 「CreateEntity + Componentごとの後付けGetRef代入」から、生成されたArchetypeのCreate
        // ファクトリ1回の呼び出しへ簡潔化された。
        Assert.Contains("public static global::CSharpGameEngine.Entities.Entity CreateRuntime(ref global::CSharpGameEngine.Entities.World world)", Text);
        Assert.Contains("Entity Root = global::CSharpGameEngine.Entities.Archetype", Text);
        Assert.Contains(".Create(ref world, default);", Text);
        Assert.Contains("new Camera { FieldOfView = 60f }", Text);
        Assert.Contains("new global::CSharpGameEngine.Entities.Parent { Value = Root }", Text);
        Assert.Contains("return Root;", Text);

        // 後付けの代入パスは無くなった。
        Assert.DoesNotContain("GetRef<", Text);
        Assert.DoesNotContain("CreateArchetype<", Text);
    }

    [Fact]
    public void GeneratorIsDeterministic_AcrossTwoIndependentRuns()
    {
        string Source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


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
                            MainCamera.Set(new Camera { FieldOfView = 60f });
                        });

                        Root.Create(Player, Player =>
                        {
                            Player.Set(new PlayerTag());
                            Player.Set(new Health { Value = 100f });
                        });
                    });
                }
            }
            """;

        GeneratorDriverRunResult First = SceneDefinitionGeneratorTestHelper.RunGenerator(Source);
        GeneratorDriverRunResult Second = SceneDefinitionGeneratorTestHelper.RunGenerator(Source);

        string[] FirstTexts = [.. First.Results[0].GeneratedSources.OrderBy(S => S.HintName, StringComparer.Ordinal).Select(S => S.HintName + "\n" + S.SourceText)];
        string[] SecondTexts = [.. Second.Results[0].GeneratedSources.OrderBy(S => S.HintName, StringComparer.Ordinal).Select(S => S.HintName + "\n" + S.SourceText)];

        Assert.Equal(FirstTexts, SecondTexts);

        // ADR-0033でScene生成はGameSystemGeneratorへ統合されたため、Sceneクラスごとの2ファイルに加えて
        // コンパイル全体で共有されるArchetype/World/Snapshot/ゲームループも同じ実行で出力される。
        string[] HintNames = [.. First.Results[0].GeneratedSources.Select(S => S.HintName).OrderBy(N => N, StringComparer.Ordinal)];

        Assert.Equal(
            [
                "GameSystem.Loop.g.cs",
                "GameSystem.Snapshot.g.cs",
                "GameSystem.Types.g.cs",
                "GameSystem.World.g.cs",
                "Sample.MainScene.SceneNodes.g.cs",
                "Sample.MainScene.SceneRuntime.g.cs",
            ],
            HintNames);
    }
}
