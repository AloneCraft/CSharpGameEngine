using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>ADR-0079：[EngineService]を付けたクラスはサービスとなる。生成ループが一度だけ生成して共有し、破棄する。</summary>
public sealed class GameSystemEngineServiceTests {
    private static readonly string Attribute=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"TestAssets","Core","EngineServiceAttribute.cs"));

    private const string Host="""
        namespace CSharpGameEngine.Runtime.Windows;

        public sealed class EngineHost { }
        """;

    private const string Services="""
        using CSharpGameEngine.Authoring;

        namespace Game;

        [EngineService]
        public sealed class Score : System.IDisposable
        {
            public static int Made;
            public static int Disposed;
            public int Total;
            public Score() { Made++; }
            public void Dispose() { Disposed++; }
        }

        [EngineService]
        public sealed class Tracker
        {
            public Tracker(CSharpGameEngine.Runtime.Windows.EngineHost host) { }
        }

        [EngineService]
        public sealed class Unmakeable
        {
            public Unmakeable(int seed) { }
        }
        """;

    private const string Adders="""
        using CSharpGameEngine.Entities;
        using Sample;

        namespace Game;

        internal struct AddX : ISystem
        {
            public void Update(in Position position, Score score) { score.Total += (int)position.X; }
        }

        internal struct AddOne : ISystem
        {
            public void Update(in Position position, Score score) { score.Total += 1; }
        }
        """;

    private static GeneratorDriverRunResult Run(params string[]Sources)=>
        GameSystemGeneratorTestHelper.RunGenerator([GameSystemGeneratorTestHelper.CommonComponents,Attribute,Host,Services,.. Sources]);

    [Fact]
    public void AParameterlessService_IsAFieldOfTheSystemSetMadeOnFirstUse_AndNeedsNoHost(){
        var Result=Run(Adders);

        Assert.Empty(Result.Diagnostics);
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        Assert.Contains("internal global::Game.Score? Service0;",Loop);
        Assert.Contains("Systems.AddX.Update(in State.World.Archetype0.GetPositionRefUnchecked(Index), (Systems.Service0 ??= new global::Game.Score()))",Loop);
        Assert.Contains("Systems.AddOne.Update(in State.World.Archetype0.GetPositionRefUnchecked(Index), (Systems.Service0 ??= new global::Game.Score()))",Loop);
        Assert.DoesNotContain("EngineHost EngineHost",Loop);
    }

    [Fact]
    public void AServiceThatTakesTheHost_IsMadeWithIt(){
        var Result=Run("""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct Track : ISystem
            {
                public void Update(ref Position position, Tracker tracker) { }
            }
            """);

        Assert.Empty(Result.Diagnostics);
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        Assert.Contains("(Systems.Service0 ??= new global::Game.Tracker(EngineHost))",Loop);
        Assert.Contains("global::CSharpGameEngine.Runtime.Windows.EngineHost EngineHost",Loop);
    }

    [Fact]
    public void AServiceWithoutAUsableConstructor_ReportsSysgen043(){
        var Result=Run("""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct Use : ISystem
            {
                public void Update(ref Position position, Unmakeable unmakeable) { }
            }
            """);

        Assert.Contains(Result.Diagnostics,Diagnostic=>Diagnostic.Id=="SYSGEN043");
    }

    [Fact]
    public void TwoSystemsShareOneService_AndShutdownDisposesIt(){
        var Source="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    State state = new(world) { Enabled = true };
                    world.Create(new Position { X = 2f });
                    world.Create(new Position { X = 3f });
                    GeneratedSystemSet systems = default;
                    GeneratedGameLoop.RunBakeTick(ref state, ref systems);
                    GeneratedGameLoop.RunBakeTick(ref state, ref systems);
                    if (Score.Made != 1) return "FAIL:made " + Score.Made;
                    // Two entities: AddX adds 2 + 3 and AddOne adds 1 + 1, on each of two ticks, into the same object.
                    if (systems.Service0!.Total != 14) return "FAIL:total " + systems.Service0.Total;
                    GeneratedGameLoop.Shutdown(ref state, ref systems);
                    if (Score.Disposed != 1) return "FAIL:disposed " + Score.Disposed;
                    if (systems.Service0 is not null) return "FAIL:kept";
                    return "OK";
                }
            }
            """;

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(GameSystemGeneratorTestHelper.CommonComponents,Attribute,Host,Services,Adders,Source);

        Assert.Equal("OK",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario"));
    }
}
