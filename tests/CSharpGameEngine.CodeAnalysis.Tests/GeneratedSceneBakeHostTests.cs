namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class GeneratedSceneBakeHostTests {
    private const string BakeContracts="""
        namespace CSharpGameEngine.Runtime.Windows {
            public sealed class EngineHost { }
        }
        namespace CSharpGameEngine.Runtime.Physics {
            public sealed class PhysicsCacheStreamWriter { }
        }
        namespace CSharpGameEngine.Runtime.Simulation {
            public readonly struct SimulationCacheIdentity { }
        }
        """;

    private const string SceneAndSystem="""
        using CSharpGameEngine.Entities;
        using Sample;
        using CSharpGameEngine.Runtime.Windows;

        namespace Game;

        internal partial class BakeScene : IScene {
            public Node Root => new() {
                Name = "Bake",
                Components = [new Position()],
            };
        }

        internal struct PhysicsSystem : ISystem {
            public bool Update(ref Position Position, EngineHost Host, ref State State) => true;
        }
        """;

    [Fact]
    public void EmitsCompileTimeSceneDispatchAndHeadlessTickOnlyWhenContractsExist(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents,BakeContracts,SceneAndSystem);

        Assert.Empty(Result.Diagnostics);
        string Host=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.SceneBakeHost.g.cs");
        Assert.Contains("public static class GeneratedSceneBakeHost",Host);
        Assert.Contains("case \"Game.BakeScene\": State.World.InstantiateScene<global::Game.BakeScene>()",Host);
        Assert.Contains("GeneratedGameLoop.RunBakeTick(ref State, ref Systems, EngineHost);",Host);
        Assert.Contains("new global::CSharpGameEngine.Runtime.Physics.PhysicsCacheStreamWriter",Host);
        Assert.Contains("EngineHost.WritePhysicsCacheFrame(Writer, Tick)",Host);
        Assert.Contains("GeneratedGameLoop.Shutdown",Host);
        Assert.DoesNotContain("Reflection",Host,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Activator",Host,StringComparison.Ordinal);

        string Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");
        int Start=Loop.IndexOf("internal static void RunBakeTick",StringComparison.Ordinal);
        int End=Loop.IndexOf("internal static void RunFrame",Start,StringComparison.Ordinal);
        string BakeTick=Loop[Start..End];
        Assert.Contains("RunSimulationTick",BakeTick);
        Assert.DoesNotContain("SystemPhase.FrameInput",BakeTick);
        Assert.DoesNotContain("SystemPhase.Presentation",BakeTick);
    }

    [Fact]
    public void DoesNotEmitHostWhenPhysicsBakeContractsAreAbsent(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents,SceneAndSystem);

        Assert.DoesNotContain(
            Result.Results[0].GeneratedSources,
            static Source=>Source.HintName=="GameSystem.SceneBakeHost.g.cs");
    }
}
