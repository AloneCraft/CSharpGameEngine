namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>ADR-0077 stage 3: <c>in Parent&lt;T&gt;</c> reads a component from the parent entity.</summary>
public sealed class GameSystemParentRelationTests {
    private const string Components="""
        namespace Sample;

        public struct Position { public float X; }
        public struct Health { public int Value; }
        public struct Mana { public int Value; }
        """;

    [Fact]
    public void ChildReadsTheParentsComponent_AndAChildOfADeadParentIsSkipped(){
        var Source="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            internal struct CopyParentHealth : ISystem
            {
                public void Update(ref Position position, in Parent<Health> owner) { position.X = owner.Value.Value; }
            }

            // The parent's archetype exists only because something declares {Health}.
            internal struct HealthShape : ISystem
            {
                public void Spawn(ref Health health) { }
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    State state = new(world) { Enabled = true };
                    Entity parent = world.Create(new Health { Value = 7 });
                    Entity child = world.Create(new Position { X = 0f }, new Parent { Value = parent });
                    Entity orphan = world.Create(new Position { X = 0f }, new Parent { Value = new Entity(999, 1) });
                    GeneratedSystemSet systems = default;
                    GeneratedGameLoop.RunBakeTick(ref state, ref systems);
                    world.TryGetComponent(child, out Position first);
                    world.TryGetComponent(orphan, out Position skipped);
                    if (first.X != 7f) return "FAIL:child " + first.X;
                    if (skipped.X != 0f) return "FAIL:orphan " + skipped.X;
                    world.Destroy(parent);
                    world.TrySetComponent(child, new Position { X = 0f });
                    GeneratedGameLoop.RunBakeTick(ref state, ref systems);
                    world.TryGetComponent(child, out Position afterDeath);
                    if (afterDeath.X != 0f) return "FAIL:dead " + afterDeath.X;
                    return "OK";
                }
            }
            """;

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        Assert.Equal("OK",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario"));
    }

    [Fact]
    public void ParentRelation_DoesNotStampTheChildsParentComponentAsChanged(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(GameSystemGeneratorTestHelper.CommonComponents,
            """
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct Reader : ISystem
            {
                public void Update(ref Position position, in Parent<Health> owner) { }
            }
            """);

        Assert.Empty(Result.Diagnostics);
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        Assert.Contains("var ParentEntity0 = State.World.Archetype0.GetParentRefUnchecked(Index).Value;",Loop);
        Assert.Contains("TryGetComponent(ParentEntity0, out global::Sample.Health ParentValue0)) continue;",Loop);
        Assert.Contains("new global::CSharpGameEngine.Entities.Parent<global::Sample.Health>(ParentEntity0, ParentValue0)",Loop);
        Assert.DoesNotContain("MarkParentChangedUnchecked",Loop);
    }

    [Fact]
    public void ParentRelationPassedByRef_ReportsSysgen036(){
        AssertHasDiagnostic(
            """
            internal struct BadRelation : ISystem
            {
                public void Update(ref Position position, ref Parent<Health> owner) { }
            }
            """,
            "SYSGEN036");
    }

    [Fact]
    public void ParentRelationInASpawn_ReportsSysgen036(){
        AssertHasDiagnostic(
            """
            internal struct SpawnRelation : ISystem
            {
                public void Spawn(ref Position position, in Parent<Health> owner) { }
            }
            """,
            "SYSGEN036");
    }

    [Fact]
    public void ParentRelationOfAComponentTheMethodAlsoTakes_ReportsSysgen037(){
        AssertHasDiagnostic(
            """
            internal struct Conflict : ISystem
            {
                public void Update(ref Health health, in Parent<Health> owner) { }
            }
            """,
            "SYSGEN037");
    }

    [Fact]
    public void TwoParentRelationsOfTheSameComponent_ReportsSysgen037(){
        AssertHasDiagnostic(
            """
            internal struct Twice : ISystem
            {
                public void Update(ref Position position, in Parent<Health> a, in Parent<Health> b) { }
            }
            """,
            "SYSGEN037");
    }

    [Fact]
    public void AMethodWithOnlyAParentRelation_StillNeedsAComponent(){
        AssertHasDiagnostic(
            """
            internal struct OnlyRelation : ISystem
            {
                public void Update(in Parent<Health> owner) { }
            }
            """,
            "SYSGEN008");
    }

    private const string Preamble="""
        using CSharpGameEngine.Entities;

        namespace Sample;

        public struct Position { public float X; }
        public struct Health { public int Value; }

        """;

    private static void AssertHasDiagnostic(string SystemSource,string DiagnosticId){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Preamble+SystemSource);

        Assert.Contains(Result.Diagnostics,D=>D.Id==DiagnosticId);
    }
}
