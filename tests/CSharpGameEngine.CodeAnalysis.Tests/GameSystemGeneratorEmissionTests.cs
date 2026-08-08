using System.Reflection;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class GameSystemGeneratorEmissionTests
{
    [Fact]
    public void SimpleMoveMethod_GeneratesExpectedExecutorShape()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class MovementSystems
            {
                [Update]
                public static void Move(ref Position position, in Velocity velocity)
                {
                }
            }
            """;

        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        GeneratedSourceResult generated = Assert.Single(result.Results[0].GeneratedSources, s => s.HintName == "Sample.MovementSystems.Move.Executor.g.cs");
        string text = generated.SourceText.ToString();

        Assert.Contains("internal static class MovementSystems_Move_Executor", text);
        Assert.Contains("global::CSharpGameEngine.Entities.ComponentType<global::Sample.Position>.Id", text);
        Assert.Contains("global::CSharpGameEngine.Entities.ComponentType<global::Sample.Velocity>.Id", text);
        Assert.Contains("global::System.Span<global::Sample.Position> position = archetype.GetSpan<global::Sample.Position>(", text);
        Assert.Contains("global::System.ReadOnlySpan<global::Sample.Velocity> velocity = archetype.GetReadOnlySpan<global::Sample.Velocity>(", text);
        Assert.Contains("global::Sample.MovementSystems.Move(", text);
        Assert.Contains("ref position[row]", text);
        Assert.Contains("in velocity[row]", text);
    }

    [Fact]
    public void GeneratedExecutor_NeverUsesReflectionOrDictionaryLookups()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class MovementSystems
            {
                [Update]
                public static void Move(ref Position position, in Velocity velocity)
                {
                }
            }
            """;

        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(source);
        GeneratedSourceResult generated = Assert.Single(result.Results[0].GeneratedSources, s => s.HintName == "Sample.MovementSystems.Move.Executor.g.cs");
        string text = generated.SourceText.ToString();

        Assert.DoesNotContain("typeof(", text);
        Assert.DoesNotContain("Has<", text);
        Assert.DoesNotContain(".Contains(", text);
        Assert.DoesNotContain("Dictionary", text);
        Assert.DoesNotContain("GetType()", text);
    }

    [Fact]
    public void TagComponent_IsHoistedOutsideMatchLoop_AndExcludedFromAccessArray()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class MovingPropSystems
            {
                [Update]
                public static void Spin(ref Position position, in MovableTag tag)
                {
                }
            }
            """;

        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(source);
        GeneratedSourceResult generated = Assert.Single(result.Results[0].GeneratedSources, s => s.HintName == "Sample.MovingPropSystems.Spin.Executor.g.cs");
        string text = generated.SourceText.ToString();

        Assert.Contains("global::Sample.MovableTag tag = default;", text);

        int hoistIndex = text.IndexOf("global::Sample.MovableTag tag = default;", StringComparison.Ordinal);
        int loopIndex = text.IndexOf("for (int matchIndex = 0;", StringComparison.Ordinal);
        Assert.True(hoistIndex >= 0 && loopIndex >= 0 && hoistIndex < loopIndex, "Tag default must be hoisted before the match loop.");

        int accessStart = text.IndexOf("access: new int[]", StringComparison.Ordinal);
        int accessEnd = text.IndexOf(");", accessStart, StringComparison.Ordinal);
        string accessBlock = text[accessStart..accessEnd];

        Assert.Contains("Position", accessBlock);
        Assert.DoesNotContain("MovableTag", accessBlock);
    }

    [Fact]
    public void WithoutAttribute_AddsExcludedTypeToNoneArray()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class ActiveOnlySystems
            {
                [Update]
                [Without(typeof(DisabledTag))]
                public static void Move(ref Position position)
                {
                }
            }
            """;

        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(source);
        GeneratedSourceResult generated = Assert.Single(result.Results[0].GeneratedSources, s => s.HintName == "Sample.ActiveOnlySystems.Move.Executor.g.cs");
        string text = generated.SourceText.ToString();

        Assert.Contains("none: new int[]", text);
        Assert.Contains("global::CSharpGameEngine.Entities.ComponentType<global::Sample.DisabledTag>.Id,", text);
    }

    [Fact]
    public void TwoDifferentGameSystemClasses_WithSameMethodName_CompileTogetherWithoutCollision()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class MovementSystems
            {
                [Update]
                public static void Move(ref Position position)
                {
                }
            }

            [GameSystem]
            public static class PhysicsSystems
            {
                [Update]
                public static void Move(ref Velocity velocity)
                {
                }
            }
            """;

        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Sample.MovementSystems.Move.Executor.g.cs");
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Sample.PhysicsSystems.Move.Executor.g.cs");

        string movementText = result.Results[0].GeneratedSources.Single(s => s.HintName == "Sample.MovementSystems.Move.Executor.g.cs").SourceText.ToString();
        string physicsText = result.Results[0].GeneratedSources.Single(s => s.HintName == "Sample.PhysicsSystems.Move.Executor.g.cs").SourceText.ToString();

        Assert.Contains("internal static class MovementSystems_Move_Executor", movementText);
        Assert.Contains("internal static class PhysicsSystems_Move_Executor", physicsText);

        // テキストの形だけでなく、実際にEmitして「同一名前空間に2つのExecutor型」が本当に
        // コンパイル可能(型名が衝突しない)ことを証明する - これが本命の回帰テスト。
        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(source);
        Assert.NotNull(assembly.GetType("Sample.MovementSystems_Move_Executor"));
        Assert.NotNull(assembly.GetType("Sample.PhysicsSystems_Move_Executor"));
    }

    [Fact]
    public void GeneratedGameLoop_CallsWriterBeforeReader_EvenWhenReaderIsDeclaredFirst()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class ConsumerFirstDeclared
            {
                [Update]
                public static void ReadHealthWritePosition(ref Position position, in Health health)
                {
                }
            }

            [GameSystem]
            public static class ProducerDeclaredSecond
            {
                [Update]
                public static void WriteHealth(ref Health health)
                {
                }
            }
            """;

        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        GeneratedSourceResult loop = Assert.Single(result.Results[0].GeneratedSources, s => s.HintName == "GeneratedGameLoop.g.cs");
        string text = loop.SourceText.ToString();

        int producerIndex = text.IndexOf("ProducerDeclaredSecond_WriteHealth_Executor.Execute(", StringComparison.Ordinal);
        int consumerIndex = text.IndexOf("ConsumerFirstDeclared_ReadHealthWritePosition_Executor.Execute(", StringComparison.Ordinal);

        Assert.True(producerIndex >= 0, "producer call not found");
        Assert.True(consumerIndex >= 0, "consumer call not found");
        Assert.True(producerIndex < consumerIndex, "Write(Health) must execute before Read(Health) despite ConsumerFirstDeclared sorting first in baseline (declaration/name) order");

        Assert.Contains("world.Commands.Playback(world);", text);
    }

    [Fact]
    public void GeneratorIsDeterministic_AcrossTwoIndependentRuns()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class MovementSystems
            {
                [Update]
                public static void Move(ref Position position, in Velocity velocity)
                {
                }
            }
            """;

        GeneratorDriverRunResult first = GameSystemGeneratorTestHelper.RunGenerator(source);
        GeneratorDriverRunResult second = GameSystemGeneratorTestHelper.RunGenerator(source);

        string[] firstTexts = [.. first.Results[0].GeneratedSources.OrderBy(s => s.HintName, StringComparer.Ordinal).Select(s => s.HintName + "\n" + s.SourceText)];
        string[] secondTexts = [.. second.Results[0].GeneratedSources.OrderBy(s => s.HintName, StringComparer.Ordinal).Select(s => s.HintName + "\n" + s.SourceText)];

        Assert.Equal(firstTexts, secondTexts);
    }
}
