using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class SceneDefinitionGeneratorDiagnosticsTests
{
    [Fact]
    public void NonStaticOrPartialClass_ReportsScene001()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public class NotStaticOrPartialScene
            {
            }
            """;

        AssertHasDiagnostic(source, "SCENE001");
    }

    [Fact]
    public void NonPrivateNodeProperty_ReportsScene002()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class BadNodePropertyScene
            {
                public static partial SceneNodeSymbol Root { get; }

                public static void Define(SceneBuilder Scene)
                {
                }
            }
            """;

        AssertHasDiagnostic(source, "SCENE002");
    }

    [Fact]
    public void CreateArgumentAndLambdaParameterNameMismatch_ReportsScene003()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class MismatchedNameScene
            {
                private static partial SceneNodeSymbol Root { get; }

                public static void Define(SceneBuilder Scene)
                {
                    Scene.Create(Root, NotRoot =>
                    {
                    });
                }
            }
            """;

        AssertHasDiagnostic(source, "SCENE003");
    }

    [Fact]
    public void DeclaredNodeNeverCreated_ReportsScene004()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class UnusedNodeScene
            {
                private static partial SceneNodeSymbol Root { get; }

                private static partial SceneNodeSymbol Orphan { get; }

                public static void Define(SceneBuilder Scene)
                {
                    Scene.Create(Root, Root =>
                    {
                    });
                }
            }
            """;

        AssertHasDiagnostic(source, "SCENE004");
    }

    [Fact]
    public void SameNodeCreatedTwice_ReportsScene005()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class DuplicateCreationScene
            {
                private static partial SceneNodeSymbol Root { get; }

                private static partial SceneNodeSymbol Child { get; }

                public static void Define(SceneBuilder Scene)
                {
                    Scene.Create(Root, Root =>
                    {
                        Root.Create(Child, Child => { });
                        Root.Create(Child, Child => { });
                    });
                }
            }
            """;

        AssertHasDiagnostic(source, "SCENE005");
    }

    [Fact]
    public void MultipleTopLevelCreateCalls_ReportsScene006()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class MultipleRootsScene
            {
                private static partial SceneNodeSymbol RootA { get; }

                private static partial SceneNodeSymbol RootB { get; }

                public static void Define(SceneBuilder Scene)
                {
                    Scene.Create(RootA, RootA => { });
                    Scene.Create(RootB, RootB => { });
                }
            }
            """;

        AssertHasDiagnostic(source, "SCENE006");
    }

    [Fact]
    public void NoTopLevelCreateCalls_ReportsScene006()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class EmptyDefineScene
            {
                public static void Define(SceneBuilder Scene)
                {
                }
            }
            """;

        AssertHasDiagnostic(source, "SCENE006");
    }

    [Fact]
    public void LoopInsideDefine_ReportsScene007()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class LoopInDefineScene
            {
                private static partial SceneNodeSymbol Root { get; }

                public static void Define(SceneBuilder Scene)
                {
                    for (int i = 0; i < 1; i++)
                    {
                    }

                    Scene.Create(Root, Root => { });
                }
            }
            """;

        AssertHasDiagnostic(source, "SCENE007");
    }

    [Fact]
    public void SetArgumentIsNotObjectCreation_ReportsScene008()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class BadSetArgumentScene
            {
                private static partial SceneNodeSymbol Root { get; }

                public static void Define(SceneBuilder Scene)
                {
                    Scene.Create(Root, Root =>
                    {
                        Root.Set(default(Position));
                    });
                }
            }
            """;

        AssertHasDiagnostic(source, "SCENE008");
    }

    [Fact]
    public void NonRootNodeWithEightComponents_ExceedsArchetypeLimit_ReportsScene009()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class TooManyComponentsScene
            {
                private static partial SceneNodeSymbol Root { get; }

                private static partial SceneNodeSymbol Overloaded { get; }

                public static void Define(SceneBuilder Scene)
                {
                    Scene.Create(Root, Root =>
                    {
                        Root.Create(Overloaded, Overloaded =>
                        {
                            Overloaded.Set(new C1());
                            Overloaded.Set(new C2());
                            Overloaded.Set(new C3());
                            Overloaded.Set(new C4());
                            Overloaded.Set(new C5());
                            Overloaded.Set(new C6());
                            Overloaded.Set(new C7());
                            Overloaded.Set(new C8());
                        });
                    });
                }
            }

            public struct C1 { public int V; }
            public struct C2 { public int V; }
            public struct C3 { public int V; }
            public struct C4 { public int V; }
            public struct C5 { public int V; }
            public struct C6 { public int V; }
            public struct C7 { public int V; }
            public struct C8 { public int V; }
            """;

        AssertHasDiagnostic(source, "SCENE009");
    }

    [Fact]
    public void WellFormedScene_ProducesNoDiagnostics()
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
            """;

        GeneratorDriverRunResult result = SceneDefinitionGeneratorTestHelper.RunGenerator(source);

        Assert.Empty(result.Diagnostics);
    }

    private static void AssertHasDiagnostic(string source, string diagnosticId)
    {
        GeneratorDriverRunResult result = SceneDefinitionGeneratorTestHelper.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == diagnosticId);
    }
}
