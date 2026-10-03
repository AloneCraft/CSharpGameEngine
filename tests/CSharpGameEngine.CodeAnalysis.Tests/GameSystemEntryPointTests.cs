using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// <c>GameSystem.EntryPoint.g.cs</c>の出力条件と、生成された<c>Main</c>を実際に実行した結果を検証する。
/// この生成物だけは「出してはいけないコンパイルで出すとCS0017(エントリポイントの重複)でビルドを
/// 壊す」ため、出す条件よりも<b>出さない条件</b>のテストが本体になる。
/// </summary>
public sealed class GameSystemEntryPointTests {
    private const string EntryPointHintName="GameSystem.EntryPoint.g.cs";

    private const string StartupAndTickSystems="""
        using CSharpGameEngine.Entities;
        using Sample;

        namespace Game;

        internal struct StartupSystem : ISystem
        {
            public bool Update(ref Bootstrap bootstrap, ref State state)
            {
                if (bootstrap.Done) return true;
                bootstrap.Done = true;
                state.World.CreateDeferred(new Position { X = 1f });
                return true;
            }
        }

        internal struct TickSystem : ISystem
        {
            public bool Update(ref Position position, ref State state)
            {
                state.Enabled = false;
                return true;
            }
        }
        """;

    [Fact]
    public void EntryPointIsGenerated_ForAnApplicationWithoutItsOwnMain(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CreateApplicationCompilation(
                                                               GameSystemGeneratorTestHelper.CommonComponents,StartupAndTickSystems));

        Assert.Empty(Result.Diagnostics);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,EntryPointHintName);

        Assert.Contains("private static void Main()",Text);
        Assert.Contains("World.Create(new global::CSharpGameEngine.Entities.Bootstrap());",Text);
        Assert.Contains("GeneratedGameLoop.Run(ref State);",Text);
    }

    [Fact]
    public void EntryPointIsNotGenerated_ForALibrary(){
        // テストプロジェクトや共有ソースのスモークビルドがこれに当たる。
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,StartupAndTickSystems);

        Assert.Empty(Result.Diagnostics);
        AssertNoEntryPoint(Result);
    }

    [Fact]
    public void EntryPointIsNotGenerated_WhenTheUserWroteTheirOwnMain(){
        // 手書きProgram.csを持つ従来型プロジェクトの回帰。SGは黙る(CS0017を起こさない)。
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CreateApplicationCompilation(
                                                               GameSystemGeneratorTestHelper.CommonComponents,
                                                               StartupAndTickSystems,
                                                               """
                                                               namespace Game;

                                                               internal static class Program
                                                               {
                                                                   private static void Main() { }
                                                               }
                                                               """));

        Assert.Empty(Result.Diagnostics);
        AssertNoEntryPoint(Result);
    }

    [Fact]
    public void EntryPointIsNotGenerated_ForAnApplicationWithoutAnySystem(){
        // Systemが無ければループも無い。Mainが要るなら手書きするしかなく、CS5001がそのまま正しい。
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CreateApplicationCompilation(
                                                               GameSystemGeneratorTestHelper.CommonComponents));

        AssertNoEntryPoint(Result);
    }

    [Fact]
    public void ContextParameter_ReportsSysgen021_AndSuppressesTheEntryPoint(){
        // Contextは`Run`の引数になるため、生成されたMainには渡す値を作る手段が無い。
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CreateApplicationCompilation(
                                                               GameSystemGeneratorTestHelper.CommonComponents,
                                                               """
                                                               using CSharpGameEngine.Entities;
                                                               using Sample;

                                                               namespace Game;

                                                               internal struct ContextSystem : ISystem
                                                               {
                                                                   public bool Update(ref Position position, Log log, ref State state) => true;
                                                               }
                                                               """));

        var Diagnostic=Assert.Single(Result.Diagnostics,D=>D.Id=="SYSGEN021");

        Assert.Contains("Game.ContextSystem",Diagnostic.GetMessage());
        Assert.Contains("Sample.Log",Diagnostic.GetMessage());
        AssertNoEntryPoint(Result);
    }

    [Fact]
    public void EngineHostContext_IsOwnedAndPassedByTheGeneratedEntryPoint(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CreateApplicationCompilation(
                GameSystemGeneratorTestHelper.CommonComponents,
                """
                using CSharpGameEngine.Entities;
                using Sample;
                using CSharpGameEngine.Runtime.Windows;

                namespace CSharpGameEngine.Runtime.Windows
                {
                    public sealed class EngineHost : System.IDisposable
                    {
                        private CSharpGameEngine.Entities.World world = new();
                        public ref CSharpGameEngine.Entities.World World => ref world;
                        public CSharpGameEngine.Runtime.Simulation.FixedTickScheduler FixedTicks { get; } = new();
                        public CSharpGameEngine.Entities.State CreateState() => new(world);
                        public (long ElapsedCounter, long CounterFrequency) FrameTime(long Measured, long Frequency) => (Measured, Frequency);
                        public void Dispose() { }
                    }
                }

                namespace Game
                {
                    internal struct HostedSystem : ISystem
                    {
                        public bool Update(ref Position position, EngineHost host, ref State state)
                        {
                            state.Enabled = false;
                            return true;
                        }
                    }
                }
                """));

        Assert.Empty(Result.Diagnostics);
        string Text=GameSystemGeneratorTestHelper.GeneratedText(Result,EntryPointHintName);
        Assert.Contains("using var EngineHost = new global::CSharpGameEngine.Runtime.Windows.EngineHost();",Text);
        Assert.Contains("ref var World = ref EngineHost.World;",Text);
        Assert.Contains("var State = EngineHost.CreateState();",Text);
        Assert.Contains("GeneratedGameLoop.Run(ref State, EngineHost);",Text);
        // The loop takes each frame's time from the host, so an automation run can fix it (RuntimeAutomation).
        string Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");
        Assert.Contains("var (FrameCounter, FrameFrequency) = EngineHost.FrameTime(ElapsedCounter, global::System.Diagnostics.Stopwatch.Frequency);",Loop);
        Assert.Contains("RunFrame(ref State, ref Systems, ref Scheduler, FrameCounter, FrameFrequency, EngineHost);",Loop);
    }

    [Fact]
    public void BootstrapArchetypeIsNotRegistered_WhenNoEntryPointIsGenerated(){
        // 生成しないコンパイルにまでBootstrapのArchetypeが増えると、Archetype番号がずれるうえに
        // 誰も入らない空ループが出る。注入はエントリポイントを出すと決めた場合だけに限定している。
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,
                                                              """
                                                              using CSharpGameEngine.Entities;
                                                              using Sample;

                                                              namespace Game;

                                                              internal struct TickSystem : ISystem
                                                              {
                                                                  public bool Update(ref Position position, ref State state) => true;
                                                              }
                                                              """);

        var Types=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Types.g.cs");

        Assert.DoesNotContain("Bootstrap",Types);
    }

    [Fact]
    public void BootstrapArchetypeExists_EvenIfNoSystemDeclaresIt(){
        // 生成されたMainは必ず`World.Create(new Bootstrap())`を呼ぶので、Bootstrapを受け取るSystemが
        // 1つも無くてもオーバーロードが存在しなければならない。
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CreateApplicationCompilation(
                                                               GameSystemGeneratorTestHelper.CommonComponents,
                                                               """
                                                               using CSharpGameEngine.Entities;
                                                               using Sample;

                                                               namespace Game;

                                                               internal struct TickSystem : ISystem
                                                               {
                                                                   public bool Update(ref Position position, ref State state) => true;
                                                               }
                                                               """));

        var World=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.World.g.cs");

        Assert.Contains("public Entity Create(global::CSharpGameEngine.Entities.Bootstrap Bootstrap)",World);
        Assert.Contains(EntryPointHintName,Result.Results[0].GeneratedSources.Select(S=>S.HintName));
    }

    /// <summary>
    /// 生成された<c>Main</c>を実際に実行する。Program.csを1行も書かずに
    /// 「シードEntity → Systemが世界を組み立てる → State.Enabledでループが終わる」が
    /// 成立することの証明で、この機能の中核契約にあたる。
    /// </summary>
    [Fact]
    public void GeneratedMain_SeedsBootstrap_RunsFrames_AndStopsWhenStateIsDisabled(){
        var Compilation=GameSystemGeneratorTestHelper.CreateApplicationCompilation(
            GameSystemGeneratorTestHelper.CommonComponents,
            """
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal static class Trace
            {
                internal static int BootstrapTicks;
                internal static int Spawned;
                internal static int Shutdowns;

                internal static string Result() => Spawned + ":" + BootstrapTicks + ":" + Shutdowns;
            }

            internal struct StartupSystem : ISystem
            {
                public bool Update(ref Bootstrap bootstrap, ref State state)
                {
                    Trace.BootstrapTicks++;

                    // Runはhost frameを止めずに回るため、FrameInput側で3 host frame後に終了する。
                    if (Trace.BootstrapTicks >= 3) state.Enabled = false;

                    if (bootstrap.Done) return true;
                    bootstrap.Done = true;

                    state.World.CreateDeferred(new Position { X = 1f });
                    Trace.Spawned++;
                    return true;
                }
            }

            internal struct TickSystem : ISystem
            {
                public bool Update(ref Position position, ref State state) => true;
            }

            internal struct ShutdownSystem : IWorldSystem
            {
                public void Shutdown(ref State state)
                {
                    Trace.Shutdowns++;
                }
            }
            """);

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Compilation);

        GameSystemGeneratorTestHelper.Invoke(Assembly,"CSharpGameEngine.Generated.GeneratedEntryPoint","Main");

        // Spawned=1: Bootstrapは一度きり。BootstrapTicks=3: 3 host frameで停止する。
        // 実時間FixedSimulationを停止条件に混ぜると、全体テストの並列負荷によってhost frame数が変わる。
        // Fixed tickの進行はFixedTickSchedulerの決定論的テストで独立に検証する。
        var Result=(string)GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.Trace","Result")!;
        var Parts=Result.Split(':').Select(int.Parse).ToArray();
        Assert.Equal(1,Parts[0]);
        Assert.Equal(3,Parts[1]);
        Assert.Equal(1,Parts[2]);
    }

    private static void AssertNoEntryPoint(GeneratorDriverRunResult Result)=>
        Assert.DoesNotContain(EntryPointHintName,Result.Results[0].GeneratedSources.Select(S=>S.HintName));
}
