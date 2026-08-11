using System.Reflection;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class GameSystemGeneratorEmissionTests
{
    private const string TwoSystems = """
        using CSharpGameEngine.Entities;
        using Sample;

        namespace Game;

        internal struct FirstSystem : ISystem
        {
            public bool Update(ref Position position, ref State state) => true;
        }

        internal struct SecondSystem : ISystem
        {
            public bool Update(ref Position position, ref Velocity velocity, ref State state) => true;
        }
        """;

    [Fact]
    public void ArchetypesAreNamedByDiscoveryOrder_NotByComponentIndexConcatenation()
    {
        GeneratorDriverRunResult Result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        Assert.Empty(Result.Diagnostics);

        string Text = GameSystemGeneratorTestHelper.GeneratedText(Result, "GameSystem.Types.g.cs");

        // ADR-0033: Archetype名は出現順の連番。Component集合を連結した名前(Archetype0001…)は、
        // 読めない上にComponentを1つ足すだけで全部の名前が変わってしまうため採用していない。
        Assert.Contains("public enum Archetype\n{\n    Archetype0,\n    Archetype1,\n}", Text);
        Assert.Contains("public sealed class Archetype0 : IArchetype", Text);
        Assert.Contains("public sealed class Archetype1 : IArchetype", Text);
    }

    [Fact]
    public void CreateFactory_ReturnsTheCreatedEntity()
    {
        GeneratorDriverRunResult Result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        string Text = GameSystemGeneratorTestHelper.GeneratedText(Result, "GameSystem.Types.g.cs");

        Assert.Contains("public static Entity Create(ref World World,", Text);
        Assert.Contains("        return Entity;", Text);
    }

    [Fact]
    public void WorldGetsCreateOverloads_SoCallersNeverNameAnArchetype()
    {
        GeneratorDriverRunResult Result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        string Text = GameSystemGeneratorTestHelper.GeneratedText(Result, "GameSystem.World.g.cs");

        Assert.Contains("public Entity Create(global::Sample.Position Position)", Text);
        Assert.Contains("public Entity Create(global::Sample.Position Position, global::Sample.Velocity Velocity)", Text);
        Assert.Contains("public bool MoveArchetype(Entity Entity, global::Sample.Position Position, ref State State)", Text);
        Assert.Contains("public void PlaybackMoveArchetypes(ref State State)", Text);
    }

    [Fact]
    public void DispatchOrderFollowsSourceDeclarationOrder()
    {
        // FirstSystem/SecondSystem は宣言順どおりに呼ばれなければならない(ADR-0033決定事項1:
        // Component引数が全てrefになった結果、Write→Readの自動順序決定は原理的に不可能)。
        GeneratorDriverRunResult Result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        string Text = GameSystemGeneratorTestHelper.GeneratedText(Result, "GameSystem.Loop.g.cs");

        int First = Text.IndexOf("firstSystem.Update(", StringComparison.Ordinal);
        int Second = Text.IndexOf("secondSystem.Update(", StringComparison.Ordinal);

        Assert.True(First >= 0 && Second >= 0, "both systems must be dispatched");
        Assert.True(First < Second, "declaration order must be preserved");
    }

    [Fact]
    public void SubsetDispatch_RunsASmallerOverloadAgainstASupersetArchetype()
    {
        GeneratorDriverRunResult Result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        string Text = GameSystemGeneratorTestHelper.GeneratedText(Result, "GameSystem.Loop.g.cs");

        // Archetype1 は {Position, Velocity}。{Position} だけを要求する FirstSystem もここに一致する。
        int Archetype1Loop = Text.IndexOf("State.World.Archetype1.Count", StringComparison.Ordinal);
        string Archetype1Body = Text[Archetype1Loop..];

        Assert.Contains("firstSystem.Update(ref State.World.Archetype1.GetPositionRefUnchecked(Index), ref State)", Archetype1Body);
        Assert.Contains("secondSystem.Update(ref State.World.Archetype1.GetPositionRefUnchecked(Index), ref State.World.Archetype1.GetVelocityRefUnchecked(Index), ref State)", Archetype1Body);
    }

    [Fact]
    public void SpawnMethod_RegistersAnArchetypeButIsNeverDispatched()
    {
        GeneratorDriverRunResult Result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents,
            """
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct OnlySystem : ISystem
            {
                public bool Update(ref Position position, ref State state) => true;
            }

            internal struct HealthSpawn : ISystem
            {
                public void Spawn(ref Position position, ref Health health) { }
            }
            """);

        Assert.Empty(Result.Diagnostics);

        string Types = GameSystemGeneratorTestHelper.GeneratedText(Result, "GameSystem.Types.g.cs");
        string Loop = GameSystemGeneratorTestHelper.GeneratedText(Result, "GameSystem.Loop.g.cs");

        // Spawnが宣言した {Position, Health} のArchetypeとCreateは生成される。
        Assert.Contains("global::Sample.Health Health", Types);

        // しかしSpawn自体はディスパッチされない。
        Assert.DoesNotContain("healthSpawn", Loop);
        Assert.DoesNotContain(".Spawn(", Loop);
    }

    [Fact]
    public void ContextParameters_AreThreadedThroughRunFrame()
    {
        GeneratorDriverRunResult Result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents,
            """
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct ContextSystem : ISystem
            {
                public bool Update(ref Position position, in FrameContext frame, Log log, ref State state) => true;
            }
            """);

        Assert.Empty(Result.Diagnostics);

        string Text = GameSystemGeneratorTestHelper.GeneratedText(Result, "GameSystem.Loop.g.cs");

        // 構造体Contextは`in`、参照型Contextは値渡し(属性は一切使わず、ref-kindだけで判別される)。
        Assert.Contains("in global::Sample.FrameContext frameContext", Text);
        Assert.Contains("global::Sample.Log log", Text);
        Assert.Contains("contextSystem.Update(ref State.World.Archetype0.GetPositionRefUnchecked(Index), in frameContext, log, ref State)", Text);
    }

    [Fact]
    public void WorldSnapshot_IsEmittedForTheOutOfProcessPreviewBridge()
    {
        GeneratorDriverRunResult Result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        string Text = GameSystemGeneratorTestHelper.GeneratedText(Result, "GameSystem.Snapshot.g.cs");

        Assert.Contains("public static class WorldSnapshot", Text);
        Assert.Contains("public static ArchetypeSnapshotEntry[] Capture(in World World)", Text);
        Assert.Contains("ComponentValues = new object[]", Text);
    }

    [Fact]
    public void PromotedSample_Compiles()
    {
        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        Assert.NotNull(assembly.GetType("CSharpGameEngine.Generated.GeneratedGameLoop"));
        Assert.NotNull(assembly.GetType("CSharpGameEngine.Entities.WorldSnapshot"));
    }

    [Fact]
    public void GeneratorIsDeterministic_AcrossTwoIndependentRuns()
    {
        GeneratorDriverRunResult First = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);
        GeneratorDriverRunResult Second = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        string[] FirstTexts = [.. First.Results[0].GeneratedSources.OrderBy(S => S.HintName, StringComparer.Ordinal).Select(S => S.HintName + "\n" + S.SourceText)];
        string[] SecondTexts = [.. Second.Results[0].GeneratedSources.OrderBy(S => S.HintName, StringComparer.Ordinal).Select(S => S.HintName + "\n" + S.SourceText)];

        Assert.Equal(FirstTexts, SecondTexts);
    }

    [Fact]
    public void SystemWithDiagnostics_IsExcluded_ButValidSystemsStillGenerate()
    {
        GeneratorDriverRunResult Result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents,
            """
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct ValidSystem : ISystem
            {
                public bool Update(ref Position position, ref State state) => true;
            }

            internal struct BrokenSystem : ISystem
            {
                public bool Update(ref Velocity velocity) => true;
            }
            """);

        Assert.Contains(Result.Diagnostics, D => D.Id == "SYSGEN013");

        string Loop = GameSystemGeneratorTestHelper.GeneratedText(Result, "GameSystem.Loop.g.cs");

        Assert.Contains("validSystem", Loop);
        Assert.DoesNotContain("brokenSystem", Loop);
    }
}

