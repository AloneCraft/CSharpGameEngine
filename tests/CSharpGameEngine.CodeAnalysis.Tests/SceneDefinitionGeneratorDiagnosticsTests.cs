using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class SceneDefinitionGeneratorDiagnosticsTests {
    [Fact]
    public void NonStaticOrPartialClass_ReportsScene001(){
        var Source=SceneDefinitionGeneratorTestHelper.CommonComponents+"""


            [SceneDefinition]
            public class NotStaticOrPartialScene
            {
            }
            """;

        AssertHasDiagnostic(Source,"SCENE001");
    }

    [Fact]
    public void NonPrivateNodeProperty_ReportsScene002(){
        var Source=SceneDefinitionGeneratorTestHelper.CommonComponents+"""


            [SceneDefinition]
            public static partial class BadNodePropertyScene
            {
                public static partial SceneNodeSymbol Root { get; }

                public static void Define(SceneBuilder Scene)
                {
                }
            }
            """;

        AssertHasDiagnostic(Source,"SCENE002");
    }

    [Fact]
    public void CreateArgumentAndLambdaParameterNameMismatch_ReportsScene003(){
        var Source=SceneDefinitionGeneratorTestHelper.CommonComponents+"""


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

        AssertHasDiagnostic(Source,"SCENE003");
    }

    [Fact]
    public void DeclaredNodeNeverCreated_ReportsScene004(){
        var Source=SceneDefinitionGeneratorTestHelper.CommonComponents+"""


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

        AssertHasDiagnostic(Source,"SCENE004");
    }

    [Fact]
    public void SameNodeCreatedTwice_ReportsScene005(){
        var Source=SceneDefinitionGeneratorTestHelper.CommonComponents+"""


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

        AssertHasDiagnostic(Source,"SCENE005");
    }

    [Fact]
    public void MultipleTopLevelCreateCalls_ReportsScene006(){
        var Source=SceneDefinitionGeneratorTestHelper.CommonComponents+"""


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

        AssertHasDiagnostic(Source,"SCENE006");
    }

    [Fact]
    public void NoTopLevelCreateCalls_ReportsScene006(){
        var Source=SceneDefinitionGeneratorTestHelper.CommonComponents+"""


            [SceneDefinition]
            public static partial class EmptyDefineScene
            {
                public static void Define(SceneBuilder Scene)
                {
                }
            }
            """;

        AssertHasDiagnostic(Source,"SCENE006");
    }

    [Fact]
    public void LoopInsideDefine_ReportsScene007(){
        var Source=SceneDefinitionGeneratorTestHelper.CommonComponents+"""


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

        AssertHasDiagnostic(Source,"SCENE007");
    }

    [Fact]
    public void SetArgumentIsNotObjectCreation_ReportsScene008(){
        var Source=SceneDefinitionGeneratorTestHelper.CommonComponents+"""


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

        AssertHasDiagnostic(Source,"SCENE008");
    }

    /// <summary>
    /// 旧SCENE009(1ノードのComponent数が8を超えてはならない)はADR-0033で削除した。この規則は
    /// 旧<c>World.CreateArchetype&lt;T1..T8&gt;</c>のジェネリック型引数の上限に由来するもので、
    /// 閉集合Archetype方式では組み合わせごとに専用のstructを生成するため上限自体が存在しない。
    /// ここでは「9個でも診断が出ずに生成できる」ことを回帰テストとして確認する。
    /// </summary>
    [Fact]
    public void NodeWithManyComponents_IsAllowed_BecauseThereIsNoGenericArityLimitAnymore(){
        var Source=SceneDefinitionGeneratorTestHelper.CommonComponents+"""


            [SceneDefinition]
            public static partial class ManyComponentsScene
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
                            Overloaded.Set(new C9());
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
            public struct C9 { public int V; }
            """;

        GeneratorDriverRunResult Result=SceneDefinitionGeneratorTestHelper.RunGenerator(Source);

        Assert.Empty(Result.Diagnostics);
    }

    [Fact]
    public void WellFormedScene_ProducesNoDiagnostics(){
        var Source=SceneDefinitionGeneratorTestHelper.CommonComponents+"""


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

        GeneratorDriverRunResult Result=SceneDefinitionGeneratorTestHelper.RunGenerator(Source);

        Assert.Empty(Result.Diagnostics);
    }

    private static void AssertHasDiagnostic(string Source,string DiagnosticId){
        GeneratorDriverRunResult Result=SceneDefinitionGeneratorTestHelper.RunGenerator(Source);

        Assert.Contains(Result.Diagnostics,D=>D.Id==DiagnosticId);
    }
}
