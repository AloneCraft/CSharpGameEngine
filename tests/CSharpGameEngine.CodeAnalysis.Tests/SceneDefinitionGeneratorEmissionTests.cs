using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class SceneDefinitionGeneratorEmissionTests
{
    [Fact]
    public void SimpleScene_GeneratesExpectedSceneNodesShape()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


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

        GeneratorDriverRunResult result = SceneDefinitionGeneratorTestHelper.RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        GeneratedSourceResult generated = Assert.Single(result.Results[0].GeneratedSources, s => s.HintName == "Sample.MainScene.SceneNodes.g.cs");
        string text = generated.SourceText.ToString();

        Assert.Contains("partial class MainScene", text);
        Assert.Contains("private static partial global::CSharpGameEngine.Entities.SceneNodeSymbol Root", text);
        Assert.Contains("private static partial global::CSharpGameEngine.Entities.SceneNodeSymbol MainCamera", text);
        Assert.Contains("nameof(Root)", text);
        Assert.Contains("nameof(MainCamera)", text);
        Assert.Contains("new global::CSharpGameEngine.Entities.SceneNodeId(0x", text);
    }

    [Fact]
    public void SimpleScene_GeneratesExpectedSceneRuntimeShape()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


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

        GeneratorDriverRunResult result = SceneDefinitionGeneratorTestHelper.RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        GeneratedSourceResult generated = Assert.Single(result.Results[0].GeneratedSources, s => s.HintName == "Sample.MainScene.SceneRuntime.g.cs");
        string text = generated.SourceText.ToString();

        // ADR-0033: Worldがstructになったため`ref World`を取る。ノードごとの生成は、旧実装の
        // 「CreateEntity + Componentごとの後付けGetRef代入」から、生成されたArchetypeのCreate
        // ファクトリ1回の呼び出しへ簡潔化された。
        Assert.Contains("public static global::CSharpGameEngine.Entities.Entity CreateRuntime(ref global::CSharpGameEngine.Entities.World world)", text);
        Assert.Contains("Entity Root = global::CSharpGameEngine.Entities.Archetype", text);
        Assert.Contains(".Create(ref world, default);", text);
        Assert.Contains("new Camera { FieldOfView = 60f }", text);
        Assert.Contains("new global::CSharpGameEngine.Entities.Parent { Value = Root }", text);
        Assert.Contains("return Root;", text);

        // 後付けの代入パスは無くなった。
        Assert.DoesNotContain("GetRef<", text);
        Assert.DoesNotContain("CreateArchetype<", text);
    }

    [Fact]
    public void GeneratorIsDeterministic_AcrossTwoIndependentRuns()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


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

        GeneratorDriverRunResult first = SceneDefinitionGeneratorTestHelper.RunGenerator(source);
        GeneratorDriverRunResult second = SceneDefinitionGeneratorTestHelper.RunGenerator(source);

        string[] firstTexts = [.. first.Results[0].GeneratedSources.OrderBy(s => s.HintName, StringComparer.Ordinal).Select(s => s.HintName + "\n" + s.SourceText)];
        string[] secondTexts = [.. second.Results[0].GeneratedSources.OrderBy(s => s.HintName, StringComparer.Ordinal).Select(s => s.HintName + "\n" + s.SourceText)];

        Assert.Equal(firstTexts, secondTexts);

        // ADR-0033でScene生成はGameSystemGeneratorへ統合されたため、Sceneクラスごとの2ファイルに加えて
        // コンパイル全体で共有されるArchetype/World/Snapshot/ゲームループも同じ実行で出力される。
        string[] hintNames = [.. first.Results[0].GeneratedSources.Select(s => s.HintName).OrderBy(n => n, StringComparer.Ordinal)];

        Assert.Equal(
            [
                "GameSystem.Loop.g.cs",
                "GameSystem.Snapshot.g.cs",
                "GameSystem.Types.g.cs",
                "GameSystem.World.g.cs",
                "Sample.MainScene.SceneNodes.g.cs",
                "Sample.MainScene.SceneRuntime.g.cs",
            ],
            hintNames);
    }
}
