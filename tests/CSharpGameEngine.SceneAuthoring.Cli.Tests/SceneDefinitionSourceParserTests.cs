namespace CSharpGameEngine.SceneAuthoring.Cli.Tests;

public class SceneDefinitionSourceParserTests
{
    [Fact]
    public void ParseNodeNamesInDeclarationOrder_MatchesSceneAuthoringDemoShape()
    {
        string source = """
            using CSharpGameEngine.Entities;

            namespace Sample;

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
                        });
                    });
                }
            }
            """;

        IReadOnlyList<string> names = SceneDefinitionSourceParser.ParseNodeNamesInDeclarationOrder(source);

        Assert.Equal(["Root", "MainCamera", "Player"], names);
    }

    [Fact]
    public void ParseNodeNamesInDeclarationOrder_NoSceneDefinitionClass_ReturnsEmpty()
    {
        string source = "public class NotAScene { }";

        IReadOnlyList<string> names = SceneDefinitionSourceParser.ParseNodeNamesInDeclarationOrder(source);

        Assert.Empty(names);
    }

    [Fact]
    public void ParseNodeNamesInDeclarationOrder_QualifiedAttributeForm_IsRecognized()
    {
        string source = """
            [CSharpGameEngine.Entities.SceneDefinition]
            public static partial class QualifiedScene
            {
                private static partial CSharpGameEngine.Entities.SceneNodeSymbol Root { get; }

                public static void Define(CSharpGameEngine.Entities.SceneBuilder Scene)
                {
                    Scene.Create(Root, Root => { });
                }
            }
            """;

        IReadOnlyList<string> names = SceneDefinitionSourceParser.ParseNodeNamesInDeclarationOrder(source);

        Assert.Equal(["Root"], names);
    }

    [Fact]
    public void ParseNodeNamesInDeclarationOrder_DeeplyNestedTree_IsDepthFirst()
    {
        string source = """
            using CSharpGameEngine.Entities;

            namespace Sample;

            [SceneDefinition]
            public static partial class Tree
            {
                private static partial SceneNodeSymbol Root { get; }

                private static partial SceneNodeSymbol Branch { get; }

                private static partial SceneNodeSymbol Leaf { get; }

                public static void Define(SceneBuilder Scene)
                {
                    Scene.Create(Root, Root =>
                    {
                        Root.Create(Branch, Branch =>
                        {
                            Branch.Create(Leaf, Leaf => { });
                        });
                    });
                }
            }
            """;

        IReadOnlyList<string> names = SceneDefinitionSourceParser.ParseNodeNamesInDeclarationOrder(source);

        Assert.Equal(["Root", "Branch", "Leaf"], names);
    }
}
