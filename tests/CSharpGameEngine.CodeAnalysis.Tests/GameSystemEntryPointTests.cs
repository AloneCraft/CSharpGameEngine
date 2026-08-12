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
                state.World.Create(new Position { X = 1f });
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
        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
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
        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents,StartupAndTickSystems);

        Assert.Empty(Result.Diagnostics);
        AssertNoEntryPoint(Result);
    }

    [Fact]
    public void EntryPointIsNotGenerated_WhenTheUserWroteTheirOwnMain(){
        // UserGameProjectの回帰。手書きProgram.csがある限りSGは黙る(CS0017を起こさない)。
        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
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
        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CreateApplicationCompilation(
                GameSystemGeneratorTestHelper.CommonComponents));

        AssertNoEntryPoint(Result);
    }

    [Fact]
    public void ContextParameter_ReportsSysgen021_AndSuppressesTheEntryPoint(){
        // Contextは`Run`の引数になるため、生成されたMainには渡す値を作る手段が無い。
        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
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

        Diagnostic Diagnostic=Assert.Single(Result.Diagnostics,D=>D.Id=="SYSGEN021");

        Assert.Contains("Game.ContextSystem",Diagnostic.GetMessage());
        Assert.Contains("Sample.Log",Diagnostic.GetMessage());
        AssertNoEntryPoint(Result);
    }

    [Fact]
    public void BootstrapArchetypeIsNotRegistered_WhenNoEntryPointIsGenerated(){
        // 生成しないコンパイルにまでBootstrapのArchetypeが増えると、Archetype番号がずれるうえに
        // 誰も入らない空ループが出る。注入はエントリポイントを出すと決めた場合だけに限定している。
        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
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
        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
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
        CSharpCompilation Compilation=GameSystemGeneratorTestHelper.CreateApplicationCompilation(
            GameSystemGeneratorTestHelper.CommonComponents,
            """
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal static class Trace
            {
                internal static int BootstrapTicks;
                internal static int Spawned;
                internal static int Frames;

                internal static string Result() => Spawned + ":" + Frames + ":" + BootstrapTicks;
            }

            internal struct StartupSystem : ISystem
            {
                public bool Update(ref Bootstrap bootstrap, ref State state)
                {
                    Trace.BootstrapTicks++;

                    // 安全網: TickSystemが一度も走らなくてもテストがハングしないようにする。
                    if (Trace.BootstrapTicks > 100) state.Enabled = false;

                    if (bootstrap.Done) return true;
                    bootstrap.Done = true;

                    state.World.Create(new Position { X = 1f });
                    Trace.Spawned++;
                    return true;
                }
            }

            internal struct TickSystem : ISystem
            {
                public bool Update(ref Position position, ref State state)
                {
                    Trace.Frames++;
                    if (Trace.Frames >= 3) state.Enabled = false;
                    return true;
                }
            }
            """);

        Assembly Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Compilation);

        GameSystemGeneratorTestHelper.Invoke(Assembly,"CSharpGameEngine.Generated.GeneratedEntryPoint","Main");

        // Spawned=1: Bootstrapは一度きり。Frames=3: 停止条件どおり。BootstrapTicks=3: Bootstrapの
        // Archetypeループは毎フレーム回り続ける(閉集合方式にDestroyは無い)。
        // Frames=3がBootstrapTicksと一致すること自体が「Archetype0(Bootstrap)が作ったEntityは
        // 同じフレームのうちにArchetype1(Position)のループで処理される」ことの証明でもある。
        Assert.Equal("1:3:3",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.Trace","Result"));
    }

    private static void AssertNoEntryPoint(GeneratorDriverRunResult Result)=>
        Assert.DoesNotContain(EntryPointHintName,Result.Results[0].GeneratedSources.Select(S=>S.HintName));
}
