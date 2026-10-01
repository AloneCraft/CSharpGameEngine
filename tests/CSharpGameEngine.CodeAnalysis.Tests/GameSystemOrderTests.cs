using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// ADR-0073: systems of one phase run in an order that keeps every <c>[RunsAfter]</c> / <c>[RunsBefore]</c>
/// edge, systems with no path between them run by full name, and startup scenes run first in FrameInput. The
/// execution tests run the generated <c>Main</c> and read the order the systems actually ran in.
/// </summary>
public sealed class GameSystemOrderTests {
    private const string TraceAndStartup="""
        using CSharpGameEngine.Entities;
        using Sample;
        namespace Game;
        internal static class Trace
        {
            internal static readonly List<string> Log = [];
            internal static string Result() => string.Join(",", Log);
        }
        internal struct StartupSystem : ISystem
        {
            public void Update(ref Bootstrap bootstrap, ref State state)
            {
                if (bootstrap.Done) return;
                bootstrap.Done = true;
                state.World.CreateDeferred(new Position { X = 0f });
            }
        }
        """;
    private static string RunMain(params (string?Path,string Source)[]Sources){
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(GameSystemGeneratorTestHelper.CreateApplicationCompilation(
            [.. Sources,("Startup.cs",TraceAndStartup),(null,GameSystemGeneratorTestHelper.CommonComponents)]));
        GameSystemGeneratorTestHelper.Invoke(Assembly,"CSharpGameEngine.Generated.GeneratedEntryPoint","Main");
        return (string)GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.Trace","Result")!;
    }
    private static ImmutableArray<Diagnostic>Diagnostics(string Systems)=>
        GameSystemGeneratorTestHelper.RunGenerator(GameSystemGeneratorTestHelper.CommonComponents,
            "using CSharpGameEngine.Entities;\nusing Sample;\nnamespace Game;\n"+Systems).Diagnostics;
    /// <summary>
    /// By name the order would be A, B, C. A runs after C and B before C, so the edges give B, C, A; A stops
    /// the loop, so a system that ran too early would leave the later ones out of the trace.
    /// </summary>
    [Fact]
    public void DeclaredEdges_DecideTheOrderOverTheNames(){
        Assert.Equal("B,C,A",RunMain(("Systems.cs","""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            [RunsAfter(typeof(CSystem))]
            internal struct ASystem : ISystem
            {
                public void Update(ref Position position, ref State state) { Trace.Log.Add("A"); state.Enabled = false; }
            }
            [RunsBefore(typeof(CSystem))]
            internal struct BSystem : ISystem
            {
                public void Update(ref Position position, ref State state) => Trace.Log.Add("B");
            }
            internal struct CSystem : ISystem
            {
                public void Update(ref Position position, ref State state) => Trace.Log.Add("C");
            }
            """)));
    }
    /// <summary>
    /// Systems with no declared order run by full name, not by file path or declaration position: ZSystem is
    /// declared in A.cs and ASystem in Z.cs, which the [SystemOrder] era ran file first.
    /// </summary>
    [Fact]
    public void SystemsWithoutADeclaredOrder_RunByFullName(){
        Assert.Equal("A,Z",RunMain(
            ("A.cs","""
                using CSharpGameEngine.Entities;
                using Sample;
                namespace Game;
                internal struct ZSystem : ISystem
                {
                    public void Update(ref Position position, ref State state) { Trace.Log.Add("Z"); state.Enabled = false; }
                }
                """),
            ("Z.cs","""
                using CSharpGameEngine.Entities;
                using Sample;
                namespace Game;
                internal struct ASystem : ISystem
                {
                    public void Update(ref Position position, ref State state) => Trace.Log.Add("A");
                }
                """)));
    }
    /// <summary>
    /// Startup scenes run before every other FrameInput system, by startupOrder and then full name. The
    /// [SystemOrder] era compared startupOrder with the systems' order values, so AInput (0) ran before a
    /// startup scene with startupOrder 1.
    /// </summary>
    [Fact]
    public void StartupScenes_RunFirstInFrameInput_ByStartupOrderThenName(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator([.. GameSystemGeneratorTestHelper.SceneDslSources,GameSystemGeneratorTestHelper.CommonComponents,"""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            [StartupScene(Order = 1)]
            public partial class BScene : IScene { public Node Root => new() { Name = "B" }; }
            [StartupScene(Order = 1)]
            public partial class AScene : IScene { public Node Root => new() { Name = "A" }; }
            [StartupScene(Order = 0)]
            public partial class CScene : IScene { public Node Root => new() { Name = "C" }; }
            [SystemPhase(SystemPhase.FrameInput)]
            internal struct AInput : ISystem
            {
                public void Update(ref Position position, ref State state) { }
            }
            """]);
        Assert.Empty(Result.Diagnostics);
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");
        int[]Calls=[.. new[]{"CScene__StartupSystem","AScene__StartupSystem","BScene__StartupSystem","AInput"}
            .Select(Name=>Loop.IndexOf($"Systems.{Name}.Update(",StringComparison.Ordinal))];
        Assert.DoesNotContain(-1,Calls);
        Assert.Equal([.. Calls.OrderBy(static Index=>Index)],Calls);
    }
    [Fact]
    public void AnOrderTargetThatIsNotASystem_IsSysgen033(){
        var Diagnostic=Assert.Single(Diagnostics("""
            internal struct NotASystem { }
            [RunsAfter(typeof(NotASystem))]
            internal struct ASystem : ISystem
            {
                public void Update(ref Position position, ref State state) { }
            }
            """));
        Assert.Equal("SYSGEN033",Diagnostic.Id);
        Assert.Contains("Game.ASystem",Diagnostic.GetMessage(),StringComparison.Ordinal);
        Assert.Contains("NotASystem",Diagnostic.GetMessage(),StringComparison.Ordinal);
    }
    [Fact]
    public void ACycleInOnePhase_IsSysgen034(){
        var Diagnostic=Assert.Single(Diagnostics("""
            [RunsAfter(typeof(BSystem))]
            internal struct ASystem : ISystem
            {
                public void Update(ref Position position, ref State state) { }
            }
            [RunsAfter(typeof(ASystem))]
            internal struct BSystem : ISystem
            {
                public void Update(ref Velocity velocity, ref State state) { }
            }
            """));
        Assert.Equal("SYSGEN034",Diagnostic.Id);
        Assert.Contains("Game.ASystem -> Game.BSystem -> Game.ASystem",Diagnostic.GetMessage(),StringComparison.Ordinal);
    }
    /// <summary>
    /// The phase order is fixed: after a later-phase system or before an earlier-phase one is SYSGEN035, and the
    /// opposite declarations are already true and report nothing.
    /// </summary>
    [Fact]
    public void AnOrderAgainstThePhaseOrder_IsSysgen035_AndOneAlongItIsAccepted(){
        const string Phases="""
            [SystemPhase(SystemPhase.FrameInput)]
            internal struct InputSystem : ISystem
            {
                public void Update(ref Position position, ref State state) { }
            }
            [SystemPhase(SystemPhase.Presentation)]
            internal struct DrawSystem : ISystem
            {
                public void Update(ref Velocity velocity, ref State state) { }
            }
            """;
        Assert.Equal(["SYSGEN035","SYSGEN035"],Diagnostics(Phases+"""
            [RunsAfter(typeof(DrawSystem))]
            [RunsBefore(typeof(InputSystem))]
            internal struct FixedSystem : ISystem
            {
                public void Update(ref Position position, ref State state) { }
            }
            """).Select(static Diagnostic=>Diagnostic.Id));
        Assert.Empty(Diagnostics(Phases+"""
            [RunsAfter(typeof(InputSystem))]
            [RunsBefore(typeof(DrawSystem))]
            internal struct FixedSystem : ISystem
            {
                public void Update(ref Position position, ref State state) { }
            }
            """));
    }
    /// <summary>[SystemOrder] is gone; using it is a compile error whose message names the replacement.</summary>
    [Fact]
    public void SystemOrder_IsAnErrorThatNamesTheReplacement(){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation(GameSystemGeneratorTestHelper.CommonComponents,"""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            [SystemOrder(1)]
            internal struct ASystem : ISystem
            {
                public void Update(ref Position position, ref State state) { }
            }
            """);
        var Error=Assert.Single(Compilation.GetDiagnostics(),static Diagnostic=>Diagnostic.Id=="CS0619");
        Assert.Contains("[RunsAfter(typeof(...))]",Error.GetMessage(),StringComparison.Ordinal);
    }
}
