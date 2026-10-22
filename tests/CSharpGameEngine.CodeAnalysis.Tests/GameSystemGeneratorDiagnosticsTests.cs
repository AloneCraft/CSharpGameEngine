using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class GameSystemGeneratorDiagnosticsTests {
    private const string Preamble="""
        using CSharpGameEngine.Entities;

        namespace Sample;

        public struct Position { public float X; }
        public struct Velocity { public float X; }

        """;

    [Fact]
    public void NonStructSystem_ReportsSysgen001(){
        AssertHasDiagnostic(
            """
            internal class NonStructSystem : ISystem
            {
                public bool Update(ref Position position, ref State state) => true;
            }
            """,
            "SYSGEN001");
    }

    [Fact]
    public void GenericSystem_ReportsSysgen002(){
        AssertHasDiagnostic(
            """
            internal struct GenericSystem<T> : ISystem
            {
                public bool Update(ref Position position, ref State state) => true;
            }
            """,
            "SYSGEN002");
    }

    [Fact]
    public void NestedSystem_ReportsSysgen003(){
        AssertHasDiagnostic(
            """
            internal static class Container
            {
                internal struct NestedSystem : ISystem
                {
                    public bool Update(ref Position position, ref State state) => true;
                }
            }
            """,
            "SYSGEN003");
    }

    [Fact]
    public void StaticUpdateMethod_ReportsSysgen004(){
        AssertHasDiagnostic(
            """
            internal struct StaticUpdateSystem : ISystem
            {
                public static bool Update(ref Position position, ref State state) => true;
            }
            """,
            "SYSGEN004");
    }

    [Fact]
    public void PrivateUpdateMethod_ReportsSysgen005(){
        AssertHasDiagnostic(
            """
            internal struct PrivateUpdateSystem : ISystem
            {
                private bool Update(ref Position position, ref State state) => true;
            }
            """,
            "SYSGEN005");
    }

    [Fact]
    public void UpdateReturningNeitherVoidNorBool_ReportsSysgen006(){
        // ADR-0072: void is the standard Update and bool is still accepted; any other type is an error.
        AssertHasDiagnostic(
            """
            internal struct IntUpdateSystem : ISystem
            {
                public int Update(ref Position position, ref State state) => 0;
            }
            """,
            "SYSGEN006");
    }

    [Fact]
    public void GenericUpdateMethod_ReportsSysgen007(){
        AssertHasDiagnostic(
            """
            internal struct GenericUpdateSystem : ISystem
            {
                public bool Update<T>(ref Position position, ref State state) => true;
            }
            """,
            "SYSGEN007");
    }

    [Fact]
    public void UpdateWithoutComponentParameter_ReportsSysgen008(){
        AssertHasDiagnostic(
            """
            internal struct NoComponentSystem : ISystem
            {
                public bool Update(ref State state) => true;
            }
            """,
            "SYSGEN008");
    }

    /// <summary>
    /// SYSGEN009は欠番(到達不能): 「refならComponent、ref以外ならContext」という構造的推論のため、
    /// refでないパラメータは壊れたComponentではなく正当なContextになる。<c>out</c>のような不正な
    /// 渡し方はContext側の規則であるSYSGEN018が捕捉する。
    /// </summary>
    [Fact]
    public void OutParameter_ReportsSysgen018_BecauseItIsClassifiedAsAMalformedContext(){
        AssertHasDiagnostic(
            """
            internal struct OutParameterSystem : ISystem
            {
                public bool Update(ref Position position, out Velocity velocity, ref State state) { velocity = default; return true; }
            }
            """,
            "SYSGEN018");
    }

    [Fact]
    public void RefToNonStruct_ReportsSysgen010(){
        AssertHasDiagnostic(
            """
            internal struct RefClassSystem : ISystem
            {
                public bool Update(ref string text, ref State state) => true;
            }
            """,
            "SYSGEN010");
    }

    [Fact]
    public void DuplicateComponentType_ReportsSysgen011(){
        AssertHasDiagnostic(
            """
            internal struct DuplicateComponentSystem : ISystem
            {
                public bool Update(ref Position a, ref Position b, ref State state) => true;
            }
            """,
            "SYSGEN011");
    }

    [Fact]
    public void LeadingEntityPassedByRef_ReportsSysgen012(){
        AssertHasDiagnostic(
            """
            internal struct RefEntitySystem : ISystem
            {
                public bool Update(ref Entity entity, ref Position position, ref State state) => true;
            }
            """,
            "SYSGEN012");
    }

    [Fact]
    public void UpdateWithoutTrailingState_IsAccepted(){
        // ADR-0077: the trailing `ref State` is optional; SYSGEN013 no longer exists.
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Preamble+
            """
            internal struct NoStateSystem : ISystem
            {
                public void Update(ref Position position) { }
            }
            """);

        Assert.Empty(Result.Diagnostics);
        Assert.Contains("NoStateSystem.Update(ref State.World.Archetype0.GetPositionRefUnchecked(Index))",GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs"));
    }

    [Fact]
    public void InStruct_IsAReadOnlyComponentAndDoesNotReportAContext(){
        // ADR-0077: `in T` on a struct reads the component (what `[ReadOnlyComponent] in T` used to say).
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Preamble+
            """
            internal struct ReadSystem : ISystem
            {
                public void Update(in Position position, ref Velocity velocity) { }
            }
            """);

        Assert.Empty(Result.Diagnostics);
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        Assert.Contains("ReadSystem.Update(in State.World.Archetype0.GetPositionRefUnchecked(Index), ref State.World.Archetype0.GetVelocityRefUnchecked(Index))",Loop);
        Assert.DoesNotContain("MarkPositionChangedUnchecked",Loop);
        Assert.Contains("MarkVelocityChangedUnchecked",Loop);
    }

    [Fact]
    public void InAndRefOfTheSameComponent_ReportsSysgen011(){
        AssertHasDiagnostic(
            """
            internal struct ConflictSystem : ISystem
            {
                public void Update(in Position position, ref Position again) { }
            }
            """,
            "SYSGEN011");
    }

    [Fact]
    public void StateUsedAsComponent_ReportsSysgen014(){
        AssertHasDiagnostic(
            """
            internal struct StateAsComponentSystem : ISystem
            {
                public bool Update(ref State extra, ref Position position, ref State state) => true;
            }
            """,
            "SYSGEN014");
    }

    [Fact]
    public void SystemWithNoUpdateOrSpawn_ReportsSysgen015(){
        AssertHasDiagnostic(
            """
            internal struct EmptySystem : ISystem
            {
            }
            """,
            "SYSGEN015");
    }

    [Fact]
    public void SpawnReturningNonVoid_ReportsSysgen016(){
        AssertHasDiagnostic(
            """
            internal struct BadSpawnSystem : ISystem
            {
                public bool Spawn(ref Position position) => true;
            }
            """,
            "SYSGEN016");
    }

    [Fact]
    public void SpawnDeclaringState_ReportsSysgen017(){
        AssertHasDiagnostic(
            """
            internal struct SpawnWithStateSystem : ISystem
            {
                public void Spawn(ref Position position, ref State state) { }
            }
            """,
            "SYSGEN017");
    }

    [Fact]
    public void ContextPassedByRefReadonly_ReportsSysgen018(){
        // 参照型のContextは値渡しでなければならない(`in`は不可)。
        AssertHasDiagnostic(
            """
            internal struct BadContextSystem : ISystem
            {
                public bool Update(ref Position position, in Log log, ref State state) => true;
            }

            public sealed class Log { }
            """,
            "SYSGEN018");
    }

    [Fact]
    public void DuplicateContextType_ReportsSysgen019(){
        AssertHasDiagnostic(
            """
            internal struct DuplicateContextSystem : ISystem
            {
                public bool Update(ref Position position, Frame a, Frame b, ref State state) => true;
            }

            public sealed class Frame { }
            """,
            "SYSGEN019");
    }

    [Fact]
    public void ComponentTypesSharingASimpleName_AreNamedWithTheirNamespaces(){
        // ADR-0079: two plugins may each have a Position; only those two get namespace-qualified generated names.
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Preamble+"""
            internal struct SharingSystem : ISystem
            {
                public bool Update(ref Position a, ref Other.Position b, ref Velocity v, ref State state) => true;
            }

            namespace Other
            {
                public struct Position { public float X; }
            }
            """);

        Assert.Empty(Result.Diagnostics);
        var Types=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Types.g.cs");
        Assert.Contains("Sample_Position = 0,",Types);
        Assert.Contains("Sample_Other_Position = 1,",Types);
        Assert.Contains("Velocity = 2,",Types);
    }

    [Fact]
    public void AQualifiedNameThatIsAnotherTypesSimpleName_ReportsSysgen020(){
        AssertHasDiagnostic(
            """
            internal struct CollidingSystem : ISystem
            {
                public bool Update(ref Position a, ref Other.Position b, ref Sample_Other_Position c, ref State state) => true;
            }

            public struct Sample_Other_Position { public float X; }

            namespace Other
            {
                public struct Position { public float X; }
            }
            """,
            "SYSGEN020");
    }

    [Fact]
    public void ValidSystem_ReportsNothing(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              Preamble+"""
                                                                       internal struct ValidSystem : ISystem
                                                                       {
                                                                           public bool Update(Entity entity, ref Position position, ref Velocity velocity, ref State state) => true;
                                                                       }
                                                                       """);

        Assert.Empty(Result.Diagnostics);
    }

    private static void AssertHasDiagnostic(string SystemSource,string DiagnosticId){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Preamble+SystemSource);

        Assert.Contains(Result.Diagnostics,D=>D.Id==DiagnosticId);
    }
}
