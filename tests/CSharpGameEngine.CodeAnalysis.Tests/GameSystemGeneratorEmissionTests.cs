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
        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        Assert.Empty(result.Diagnostics);

        string text = GameSystemGeneratorTestHelper.GeneratedText(result, "GameSystem.Types.g.cs");

        // ADR-0033: Archetype名は出現順の連番。Component集合を連結した名前(Archetype0001…)は、
        // 読めない上にComponentを1つ足すだけで全部の名前が変わってしまうため採用していない。
        Assert.Contains("public enum Archetype\n{\n    Archetype0,\n    Archetype1,\n}", text);
        Assert.Contains("public struct Archetype0 : IArchetype", text);
        Assert.Contains("public struct Archetype1 : IArchetype", text);
    }

    [Fact]
    public void CreateFactory_ReturnsTheCreatedEntity()
    {
        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        string text = GameSystemGeneratorTestHelper.GeneratedText(result, "GameSystem.Types.g.cs");

        Assert.Contains("public static Entity Create(ref World World,", text);
        Assert.Contains("        return Entity;", text);
    }

    [Fact]
    public void WorldGetsCreateOverloads_SoCallersNeverNameAnArchetype()
    {
        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        string text = GameSystemGeneratorTestHelper.GeneratedText(result, "GameSystem.World.g.cs");

        Assert.Contains("public Entity Create(global::Sample.Position Position)", text);
        Assert.Contains("public Entity Create(global::Sample.Position Position, global::Sample.Velocity Velocity)", text);
        Assert.Contains("public bool MoveArchetype(Entity Entity, global::Sample.Position Position, ref State State)", text);
        Assert.Contains("public void PlaybackMoveArchetypes(ref State State)", text);
    }

    [Fact]
    public void DispatchOrderFollowsSourceDeclarationOrder()
    {
        // FirstSystem/SecondSystem は宣言順どおりに呼ばれなければならない(ADR-0033決定事項1:
        // Component引数が全てrefになった結果、Write→Readの自動順序決定は原理的に不可能)。
        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        string text = GameSystemGeneratorTestHelper.GeneratedText(result, "GameSystem.Loop.g.cs");

        int first = text.IndexOf("firstSystem.Update(", StringComparison.Ordinal);
        int second = text.IndexOf("secondSystem.Update(", StringComparison.Ordinal);

        Assert.True(first >= 0 && second >= 0, "both systems must be dispatched");
        Assert.True(first < second, "declaration order must be preserved");
    }

    [Fact]
    public void SubsetDispatch_RunsASmallerOverloadAgainstASupersetArchetype()
    {
        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        string text = GameSystemGeneratorTestHelper.GeneratedText(result, "GameSystem.Loop.g.cs");

        // Archetype1 は {Position, Velocity}。{Position} だけを要求する FirstSystem もここに一致する。
        int archetype1Loop = text.IndexOf("ListArchetype1.Count", StringComparison.Ordinal);
        string archetype1Body = text[archetype1Loop..];

        Assert.Contains("firstSystem.Update(ref Archetype.Position, ref State)", archetype1Body);
        Assert.Contains("secondSystem.Update(ref Archetype.Position, ref Archetype.Velocity, ref State)", archetype1Body);
    }

    [Fact]
    public void SpawnMethod_RegistersAnArchetypeButIsNeverDispatched()
    {
        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(
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

        Assert.Empty(result.Diagnostics);

        string types = GameSystemGeneratorTestHelper.GeneratedText(result, "GameSystem.Types.g.cs");
        string loop = GameSystemGeneratorTestHelper.GeneratedText(result, "GameSystem.Loop.g.cs");

        // Spawnが宣言した {Position, Health} のArchetypeとCreateは生成される。
        Assert.Contains("global::Sample.Health Health", types);

        // しかしSpawn自体はディスパッチされない。
        Assert.DoesNotContain("healthSpawn", loop);
        Assert.DoesNotContain(".Spawn(", loop);
    }

    [Fact]
    public void ContextParameters_AreThreadedThroughRunFrame()
    {
        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(
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

        Assert.Empty(result.Diagnostics);

        string text = GameSystemGeneratorTestHelper.GeneratedText(result, "GameSystem.Loop.g.cs");

        // 構造体Contextは`in`、参照型Contextは値渡し(属性は一切使わず、ref-kindだけで判別される)。
        Assert.Contains("in global::Sample.FrameContext frameContext", text);
        Assert.Contains("global::Sample.Log log", text);
        Assert.Contains("contextSystem.Update(ref Archetype.Position, in frameContext, log, ref State)", text);
    }

    [Fact]
    public void WorldSnapshot_IsEmittedForTheOutOfProcessPreviewBridge()
    {
        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        string text = GameSystemGeneratorTestHelper.GeneratedText(result, "GameSystem.Snapshot.g.cs");

        Assert.Contains("public static class WorldSnapshot", text);
        Assert.Contains("public static ArchetypeSnapshotEntry[] Capture(in World World)", text);
        Assert.Contains("ComponentValues = new object[]", text);
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
        GeneratorDriverRunResult first = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);
        GeneratorDriverRunResult second = GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents, TwoSystems);

        string[] firstTexts = [.. first.Results[0].GeneratedSources.OrderBy(s => s.HintName, StringComparer.Ordinal).Select(s => s.HintName + "\n" + s.SourceText)];
        string[] secondTexts = [.. second.Results[0].GeneratedSources.OrderBy(s => s.HintName, StringComparer.Ordinal).Select(s => s.HintName + "\n" + s.SourceText)];

        Assert.Equal(firstTexts, secondTexts);
    }

    [Fact]
    public void SystemWithDiagnostics_IsExcluded_ButValidSystemsStillGenerate()
    {
        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(
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

        Assert.Contains(result.Diagnostics, d => d.Id == "SYSGEN013");

        string loop = GameSystemGeneratorTestHelper.GeneratedText(result, "GameSystem.Loop.g.cs");

        Assert.Contains("validSystem", loop);
        Assert.DoesNotContain("brokenSystem", loop);
    }
}
