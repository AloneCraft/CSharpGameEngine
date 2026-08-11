using System.Reflection;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// <c>[StartupScene]</c>/<c>[SceneEntity]</c>から生成されたコードを実際にコンパイル・Emit・ロードして
/// 実行する。「合成Systemが1フレーム目にBootstrapを消費してEntityを1回だけ生成する」
/// 「2フレーム目以降は<c>Bootstrap.Done</c>により再生成しない」という、UserEcsProjectの実挙動と
/// 同じ契約を、生成された実コードで証明する。
/// </summary>
public sealed class StartupSceneGeneratorEndToEndTests
{
    private const string Components = """
        namespace Sample;

        public struct AppHost { public int Width; }
        public struct Level { public string? Path; }
        public struct Position { public float X; }
        """;

    [Fact]
    public void SingleComponentEntity_IsCreatedOnce_AndNotDuplicatedOnSubsequentFrames()
    {
        string Source = """
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static Position Origin => new Position { X = 42f };
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    world.Create(new Bootstrap());
                    State state = new(world) { Enabled = true };

                    MainScene__StartupSystem startup = new();
                    GeneratedGameLoop.RunFrame(ref state, ref startup);

                    if (world.Archetype1.Count != 1) return "FAIL:notCreated=" + world.Archetype1.Count;
                    if (world.Archetype1.GetPositionRefUnchecked(0).X != 42f) return "FAIL:wrongValue";

                    // 2フレーム目: Bootstrap.Doneで一度きりが保証され、二重生成しない。
                    GeneratedGameLoop.RunFrame(ref state, ref startup);

                    if (world.Archetype1.Count != 1) return "FAIL:duplicated=" + world.Archetype1.Count;

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources, Components, Source]);
        object? Result = GameSystemGeneratorTestHelper.Invoke(assembly, "Game.TestEntry", "RunScenario");

        Assert.Equal("OK", Result);
    }

    [Fact]
    public void TupleEntity_CreatesOneEntityWithBothComponentValues()
    {
        string Source = """
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static (AppHost App, Level Level) Application => (
                    new AppHost { Width = 1280 },
                    new Level { Path = "Assets/Box.fbx" });
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    world.Create(new Bootstrap());
                    State state = new(world) { Enabled = true };

                    MainScene__StartupSystem startup = new();
                    GeneratedGameLoop.RunFrame(ref state, ref startup);

                    if (world.Archetype1.Count != 1) return "FAIL:count=" + world.Archetype1.Count;
                    if (world.Archetype1.GetAppHostRefUnchecked(0).Width != 1280) return "FAIL:width";
                    if (world.Archetype1.GetLevelRefUnchecked(0).Path != "Assets/Box.fbx") return "FAIL:path";

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources, Components, Source]);
        object? Result = GameSystemGeneratorTestHelper.Invoke(assembly, "Game.TestEntry", "RunScenario");

        Assert.Equal("OK", Result);
    }

    /// <summary>
    /// UserEcsProjectの実シナリオを縮小再現する: StartupSceneが撒いた{AppHost, Level}を、後続の
    /// 実Systemが同じフレーム内(即時World.Createの連鎖)で読み取れることを検証する。
    /// </summary>
    [Fact]
    public void RealSystemDownstream_ObservesEntityCreatedByStartupScene_WithinTheSameFrame()
    {
        string Source = """
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static (AppHost App, Level Level) Application => (
                    new AppHost { Width = 1280 },
                    new Level { Path = "Assets/Box.fbx" });
            }

            internal struct ObserveWidthSystem : ISystem
            {
                public int LastSeenWidth;

                public bool Update(ref AppHost app, ref Level level, ref State state)
                {
                    this.LastSeenWidth = app.Width;
                    return true;
                }
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    world.Create(new Bootstrap());
                    State state = new(world) { Enabled = true };

                    MainScene__StartupSystem startup = new();
                    ObserveWidthSystem observe = new();
                    GeneratedGameLoop.RunFrame(ref state, ref startup, ref observe);

                    if (observe.LastSeenWidth != 1280) return "FAIL:width=" + observe.LastSeenWidth;

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources, Components, Source]);
        object? Result = GameSystemGeneratorTestHelper.Invoke(assembly, "Game.TestEntry", "RunScenario");

        Assert.Equal("OK", Result);
    }

    /// <summary>Scene propertyの評価回数を副作用カウンターで数える。1フレーム目に1回だけ評価されること。</summary>
    [Fact]
    public void SceneProperty_IsEvaluatedExactlyOnce()
    {
        string Source = """
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                internal static int EvaluationCount;

                [SceneEntity]
                internal static Position Origin
                {
                    get
                    {
                        EvaluationCount++;
                        return new Position { X = 1f };
                    }
                }
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    world.Create(new Bootstrap());
                    State state = new(world) { Enabled = true };

                    MainScene__StartupSystem startup = new();
                    GeneratedGameLoop.RunFrame(ref state, ref startup);
                    GeneratedGameLoop.RunFrame(ref state, ref startup);
                    GeneratedGameLoop.RunFrame(ref state, ref startup);

                    if (MainScene.EvaluationCount != 1) return "FAIL:evaluationCount=" + MainScene.EvaluationCount;

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources, Components, Source]);
        object? Result = GameSystemGeneratorTestHelper.Invoke(assembly, "Game.TestEntry", "RunScenario");

        Assert.Equal("OK", Result);
    }
}
