using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// ADR-0073：同一フェーズのシステムは、すべての<c>[RunsAfter]</c>／<c>[RunsBefore]</c>指定を満たす順序で実行する。
/// 順序経路がないシステムは完全名順。スタートアップシーンはFrameInputで最初に実行する。
/// 実行テストでは生成された<c>Main</c>を実行し、実際のシステム実行順を読み取る。
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
    /// 名前順ではA、B、C。AはCの後、BはCの前という依存があるため、実行順はB、C、Aとなる。
    /// Aはループを停止するので、早く実行されるはずのシステムが誤って後回しになると、後続システムがトレースから抜ける。
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
    /// 順序を宣言していないシステムは、ファイルパスや宣言位置ではなく完全名順で実行する。
    /// ZSystemはA.cs、ASystemはZ.csに宣言されており、[SystemOrder]時代はファイル順によりZSystemが先だった。
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
    /// スタートアップシーンは他のFrameInputシステムより前に実行する。startupOrder、次に完全名で順序を決める。
    /// [SystemOrder]時代はstartupOrderをシステムの順序値と比較していたため、AInput（0）がstartupOrder 1のスタートアップシーンより前だった。
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
    /// フェーズ順は固定。後続フェーズのシステムの後、または先行フェーズのシステムの前に置こうとするとSYSGEN035。
    /// 逆向きの指定は既に成立しているため、診断は出さない。
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
    /// <summary>[SystemOrder]は廃止済み。使用すると置き換え先を示すコンパイルエラーになる。</summary>
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
