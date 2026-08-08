using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class GameSystemGeneratorDiagnosticsTests
{
    [Fact]
    public void NonStaticGameSystemClass_ReportsSysgen001()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public class NonStaticSystem
            {
                [Update]
                public static void Move(ref Position position) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN001");
    }

    [Fact]
    public void GenericGameSystemClass_ReportsSysgen002()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class GenericSystem<T>
            {
                [Update]
                public static void Move(ref Position position) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN002");
    }

    [Fact]
    public void NestedGameSystemClass_ReportsSysgen003()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            public static class Container
            {
                [GameSystem]
                public static class NestedSystem
                {
                    [Update]
                    public static void Move(ref Position position) { }
                }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN003");
    }

    [Fact]
    public void NonStaticUpdateMethod_ReportsSysgen004()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public class InstanceMethodSystem
            {
                [Update]
                public void Move(ref Position position) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN004");
    }

    [Fact]
    public void NonVoidUpdateMethod_ReportsSysgen005()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class NonVoidReturnSystem
            {
                [Update]
                public static int Move(ref Position position) { return 0; }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN005");
    }

    [Fact]
    public void GenericUpdateMethod_ReportsSysgen006()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class GenericMethodSystem
            {
                [Update]
                public static void Move<T>(ref Position position) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN006");
    }

    [Fact]
    public void PrivateUpdateMethod_ReportsSysgen007()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class PrivateMethodSystem
            {
                [Update]
                private static void Move(ref Position position) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN007");
    }

    [Fact]
    public void UpdateMethodWithoutComponentParameter_ReportsSysgen008()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class NoComponentParamSystem
            {
                [Update]
                public static void Move() { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN008");
    }

    [Fact]
    public void ByValueComponentParameter_ReportsSysgen009()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class ByValueComponentSystem
            {
                [Update]
                public static void Move(Position position) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN009");
    }

    [Fact]
    public void ReferenceTypeComponentParameter_ReportsSysgen010()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class BadComponentTypeSystem
            {
                [Update]
                public static void Move(ref string name) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN010");
    }

    [Fact]
    public void DuplicateComponentType_ReportsSysgen011()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class DuplicateComponentSystem
            {
                [Update]
                public static void Move(ref Position position, ref Position position2) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN011");
    }

    [Fact]
    public void TagComponentPassedByRef_ReportsSysgen012()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class TagRefSystem
            {
                [Update]
                public static void Move(ref MovableTag tag) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN012");
    }

    [Fact]
    public void ContextStructParameterPassedByRef_ReportsSysgen013()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class BadContextRefSystem
            {
                [Update]
                public static void Move(ref Position position, [Context] ref Velocity velocity) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN013");
    }

    [Fact]
    public void CurrentEntityParameterPassedByRef_ReportsSysgen014()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class BadCurrentEntitySystem
            {
                [Update]
                public static void Move(ref Position position, [CurrentEntity] ref Entity entity) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN014");
    }

    [Fact]
    public void CommandsParameterPassedByIn_ReportsSysgen015()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class BadCommandsSystem
            {
                [Update]
                public static void Move(ref Position position, [Commands] in EntityCommandBuffer commands) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN015");
    }

    [Fact]
    public void ParameterCombiningContextAndCurrentEntity_ReportsSysgen016()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class MultiRoleSystem
            {
                [Update]
                public static void Move(ref Position position, [Context, CurrentEntity] Velocity extra) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN016");
    }

    [Fact]
    public void DuplicateCurrentEntityParameter_ReportsSysgen017()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class DuplicateCurrentEntitySystem
            {
                [Update]
                public static void Move(ref Position position, [CurrentEntity] Entity e1, [CurrentEntity] Entity e2) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN017");
    }

    [Fact]
    public void DuplicateUpdateMethodName_ReportsSysgen018()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class DuplicateMethodNameSystem
            {
                [Update]
                public static void Move(ref Position position) { }

                [Update]
                public static void Move(ref Velocity velocity) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN018");
    }

    [Fact]
    public void MutualWriteReadDependency_ReportsSysgen019_ButStillEmitsGeneratedGameLoop()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class CircularSystemA
            {
                [Update]
                public static void WriteHealthReadPosition(ref Health health, in Position position) { }
            }

            [GameSystem]
            public static class CircularSystemB
            {
                [Update]
                public static void WritePositionReadHealth(ref Position position, in Health health) { }
            }
            """;

        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "SYSGEN019");
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "GeneratedGameLoop.g.cs");
    }

    [Fact]
    public void WithoutArgumentNotAStruct_ReportsSysgen020()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class BadWithoutSystem
            {
                [Update]
                [Without(typeof(string))]
                public static void Move(ref Position position) { }
            }
            """;

        AssertHasDiagnostic(source, "SYSGEN020");
    }

    [Fact]
    public void ValidSystem_ProducesNoDiagnosticsAndGeneratesExecutor()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class MovementSystems
            {
                [Update]
                public static void Move(ref Position position, in Velocity velocity)
                {
                    position.X += velocity.X;
                }
            }
            """;

        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName.Contains("Move.Executor.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void SystemWithDiagnostics_DoesNotGenerateExecutorSource()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class NoComponentParamSystem
            {
                [Update]
                public static void Move() { }
            }
            """;

        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(result.Results[0].GeneratedSources, s => s.HintName.Contains("NoComponentParamSystem", StringComparison.Ordinal));
    }

    private static void AssertHasDiagnostic(string source, string diagnosticId)
    {
        GeneratorDriverRunResult result = GameSystemGeneratorTestHelper.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == diagnosticId);
    }
}
