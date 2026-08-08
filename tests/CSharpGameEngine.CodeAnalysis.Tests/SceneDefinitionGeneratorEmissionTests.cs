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

        Assert.Contains("public static global::CSharpGameEngine.Entities.Entity CreateRuntime(global::CSharpGameEngine.Entities.World world)", text);
        Assert.Contains("global::CSharpGameEngine.Entities.Entity Root = world.CreateEntity(world.CreateArchetype<global::CSharpGameEngine.Entities.SceneNodeTag>());", text);
        Assert.Contains("global::CSharpGameEngine.Entities.Entity MainCamera = world.CreateEntity(world.CreateArchetype<global::Sample.Camera, global::CSharpGameEngine.Entities.Parent>());", text);
        Assert.Contains("world.GetRef<global::Sample.Camera>(MainCamera) = new Camera { FieldOfView = 60f };", text);
        Assert.Contains("world.GetRef<global::CSharpGameEngine.Entities.Parent>(MainCamera) = new global::CSharpGameEngine.Entities.Parent { Value = Root };", text);
        Assert.Contains("return Root;", text);

        // Rootは空ノード(Set呼び出しなし)なのでSceneNodeTagのみ持ち、GetRef<SceneNodeTag>行は生成されない
        // (デフォルト値以外に設定すべきフィールドが無いため)。
        Assert.DoesNotContain("GetRef<global::CSharpGameEngine.Entities.SceneNodeTag>", text);
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
        Assert.Equal(2, firstTexts.Length);
    }
}
