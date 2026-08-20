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
    public void NonBoolUpdateMethod_ReportsSysgen006(){
        AssertHasDiagnostic(
            """
            internal struct VoidUpdateSystem : ISystem
            {
                public void Update(ref Position position, ref State state) { }
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
    public void UpdateWithoutTrailingState_ReportsSysgen013(){
        AssertHasDiagnostic(
            """
            internal struct NoStateSystem : ISystem
            {
                public bool Update(ref Position position) => true;
            }
            """,
            "SYSGEN013");
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
                public bool Update(ref Position position, in Frame a, in Frame b, ref State state) => true;
            }

            public readonly struct Frame { }
            """,
            "SYSGEN019");
    }

    [Fact]
    public void ComponentTypesSharingASimpleName_ReportsSysgen020(){
        AssertHasDiagnostic(
            """
            internal struct CollidingSystem : ISystem
            {
                public bool Update(ref Position a, ref Other.Position b, ref State state) => true;
            }

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
