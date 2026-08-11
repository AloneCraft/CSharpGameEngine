namespace CSharpGameEngine.SceneAuthoring.Cli.Tests;

public class SceneDefinitionSourceParserTests
{
    [Fact]
    public void ParseNodeNamesInDeclarationOrder_MatchesSceneAuthoringDemoShape()
    {
        string Source = """
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

        IReadOnlyList<string> Names = SceneDefinitionSourceParser.ParseNodeNamesInDeclarationOrder(Source);

        Assert.Equal(["Root", "MainCamera", "Player"], Names);
    }

    [Fact]
    public void ParseNodeNamesInDeclarationOrder_NoSceneDefinitionClass_ReturnsEmpty()
    {
        string Source = "public class NotAScene { }";

        IReadOnlyList<string> Names = SceneDefinitionSourceParser.ParseNodeNamesInDeclarationOrder(Source);

        Assert.Empty(Names);
    }

    [Fact]
    public void ParseNodeNamesInDeclarationOrder_QualifiedAttributeForm_IsRecognized()
    {
        string Source = """
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

        IReadOnlyList<string> Names = SceneDefinitionSourceParser.ParseNodeNamesInDeclarationOrder(Source);

        Assert.Equal(["Root"], Names);
    }

    [Fact]
    public void ParseNodeNamesInDeclarationOrder_DeeplyNestedTree_IsDepthFirst()
    {
        string Source = """
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

        IReadOnlyList<string> Names = SceneDefinitionSourceParser.ParseNodeNamesInDeclarationOrder(Source);

        Assert.Equal(["Root", "Branch", "Leaf"], Names);
    }
}
