using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class StartupSceneGeneratorDiagnosticsTests
{
    private const string Preamble = """
        using CSharpGameEngine.Entities;

        namespace Sample;

        public struct Position { public float X; }
        public struct Velocity { public float X; }

        // GameSystemGeneratorはISystem型が実在しないと何も出力しない(ADR-0033のゲート)。
        // StartupScene単体の診断テストでもこのダミーが必要。
        internal struct KeepGeneratorAlive : ISystem
        {
            public bool Update(ref Position position, ref State state) => true;
        }

        """;

    [Fact]
    public void NonStaticClass_ReportsStartup001()
    {
        AssertHasDiagnostic(
            """
            [StartupScene(Order = 0)]
            internal partial class MainScene
            {
                [SceneEntity]
                internal static Position Origin => new Position();
            }
            """,
            "STARTUP001");
    }

    [Fact]
    public void NonPartialClass_ReportsStartup001()
    {
        AssertHasDiagnostic(
            """
            [StartupScene(Order = 0)]
            internal static class MainScene
            {
                [SceneEntity]
                internal static Position Origin => new Position();
            }
            """,
            "STARTUP001");
    }

    [Fact]
    public void GenericClass_ReportsStartup002()
    {
        AssertHasDiagnostic(
            """
            [StartupScene(Order = 0)]
            internal static partial class MainScene<T>
            {
                [SceneEntity]
                internal static Position Origin => new Position();
            }
            """,
            "STARTUP002");
    }

    [Fact]
    public void NonStaticProperty_ReportsStartup003()
    {
        AssertHasDiagnostic(
            """
            [StartupScene(Order = 0)]
            internal partial class MainScene
            {
                [SceneEntity]
                internal Position Origin => new Position();
            }
            """,
            "STARTUP003");
    }

    [Fact]
    public void Indexer_ReportsStartup004()
    {
        // インデクサーはインスタンスメンバーにしかなれない(static class内には書けない)ため、
        // このケースはSTARTUP001(非static)と同時に発生する。AssertHasDiagnosticは含有だけを見る。
        AssertHasDiagnostic(
            """
            [StartupScene(Order = 0)]
            internal partial class MainScene
            {
                [SceneEntity]
                internal Position this[int index] => new Position();
            }
            """,
            "STARTUP004");
    }

    [Fact]
    public void SetOnlyProperty_ReportsStartup005()
    {
        AssertHasDiagnostic(
            """
            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static Position Origin { set { } }
            }
            """,
            "STARTUP005");
    }

    [Fact]
    public void RefReturningProperty_ReportsStartup006()
    {
        AssertHasDiagnostic(
            """
            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                private static Position _origin;

                [SceneEntity]
                internal static ref Position Origin => ref _origin;
            }
            """,
            "STARTUP006");
    }

    [Fact]
    public void NonStructReturnType_ReportsStartup007()
    {
        AssertHasDiagnostic(
            """
            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static string Origin => "not-a-component";
            }
            """,
            "STARTUP007");
    }

    [Fact]
    public void NullableStructReturnType_ReportsStartup007()
    {
        AssertHasDiagnostic(
            """
            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static Position? Origin => null;
            }
            """,
            "STARTUP007");
    }

    [Fact]
    public void ClassComponentInTuple_ReportsStartup008()
    {
        AssertHasDiagnostic(
            """
            public sealed class NotAComponent { }

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static (Position Pos, NotAComponent Bad) Origin => (new Position(), new NotAComponent());
            }
            """,
            "STARTUP008");
    }

    [Fact]
    public void DuplicateComponentTypeInTuple_ReportsStartup009()
    {
        AssertHasDiagnostic(
            """
            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static (Position First, Position Second) Origin => (new Position(), new Position());
            }
            """,
            "STARTUP009");
    }

    [Fact]
    public void UnnamedTupleElement_ReportsStartup010()
    {
        AssertHasDiagnostic(
            """
            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static (Position, Velocity) Origin => (new Position(), new Velocity());
            }
            """,
            "STARTUP010");
    }

    [Fact]
    public void ValidSingleComponentEntity_ReportsNothing()
    {
        GeneratorDriverRunResult Result = GameSystemGeneratorTestHelper.RunGenerator(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources, Preamble + """
            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static Position Origin => new Position();
            }
            """]);

        Assert.Empty(Result.Diagnostics);
    }

    [Fact]
    public void ValidTupleEntity_ReportsNothing()
    {
        GeneratorDriverRunResult Result = GameSystemGeneratorTestHelper.RunGenerator(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources, Preamble + """
            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static (Position Pos, Velocity Vel) Application => (new Position(), new Velocity());
            }
            """]);

        Assert.Empty(Result.Diagnostics);
    }

    private static void AssertHasDiagnostic(string SceneSource, string DiagnosticId)
    {
        GeneratorDriverRunResult Result = GameSystemGeneratorTestHelper.RunGenerator(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources, Preamble + SceneSource]);

        Assert.Contains(Result.Diagnostics, D => D.Id == DiagnosticId);
    }
}
