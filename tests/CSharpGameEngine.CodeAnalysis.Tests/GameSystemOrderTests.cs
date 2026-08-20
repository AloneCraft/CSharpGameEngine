using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// ADR-0036: <c>[SystemOrder]</c>によるソースコード間の実行順序指定。ADR-0033はファイルをまたぐSystemの
/// 順序を<c>SyntaxTree.FilePath</c>のOrdinal順(=絶対パス依存)にフォールバックしており、
/// <c>UserEcsProject</c>の<c>GameSystems.cs</c>/<c>RuntimeSystem.cs</c>分割で「たまたま`G`が`R`より
/// 前だったから意図どおりの順序になった」という偶然依存が生じていた。この属性は2通りの付け方ができる:
/// <c>ISystem</c>実装struct自身に直接(そのSystem1つだけの順序)、または<c>file</c>スコープの
/// マーカークラスに(ファイル内で自分の順序を持たない全Systemの既定値)。このテストは、ファイル名の
/// アルファベット順にわざと**逆らう**配置で、実行順が<c>[SystemOrder]</c>の値どおりになることを証明する。
/// </summary>
public sealed class GameSystemOrderTests {
    private const string StartupSource="""
        using CSharpGameEngine.Entities;
        using Sample;

        namespace Game;

        internal static class Trace
        {
            internal static readonly List<string> Log = [];
            internal static string Result() => string.Join(",", Log);
        }

        [SystemOrder(-1)]
        internal struct StartupSystem : ISystem
        {
            public bool Update(ref Bootstrap bootstrap, ref State state)
            {
                if (bootstrap.Done) return true;
                bootstrap.Done = true;
                state.World.Create(new Position { X = 0f });
                return true;
            }
        }
        """;

    private const string ZFirstSource="""
        using CSharpGameEngine.Entities;
        using Sample;

        namespace Game;

        [SystemOrder(0)]
        file sealed class Order
        {
        }

        internal struct ZSystem : ISystem
        {
            public bool Update(ref Position position, ref State state)
            {
                Trace.Log.Add("Z");
                return true;
            }
        }
        """;

    private const string AFirstSource="""
        using CSharpGameEngine.Entities;
        using Sample;

        namespace Game;

        [SystemOrder(1)]
        internal struct ASystem : ISystem
        {
            public bool Update(ref Position position, ref State state)
            {
                Trace.Log.Add("A");
                state.Enabled = false;
                return true;
            }
        }
        """;

    /// <summary>
    /// ファイル名だけを見ると"AFirst.cs" &lt; "Startup.cs" &lt; "ZFirst.cs"のアルファベット順になるが、
    /// 実行順は<c>[SystemOrder]</c>どおり Startup(-1) → ZFirst(0、fileマーカー経由) → AFirst(1、
    /// System自身に直接指定) にならなければならない。これを生成された<c>Main</c>を実際に実行して証明する。
    /// </summary>
    [Fact]
    public void CrossSourceExecutionOrder_FollowsSystemOrderValue_NotAlphabeticalFileName(){
        var Compilation=GameSystemGeneratorTestHelper.CreateApplicationCompilation(
        [
            ((string?)"AFirst.cs",AFirstSource),
            ("Startup.cs",StartupSource),
            ("ZFirst.cs",ZFirstSource),
            (null,GameSystemGeneratorTestHelper.CommonComponents),
        ]);

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Compilation);

        GameSystemGeneratorTestHelper.Invoke(Assembly,"CSharpGameEngine.Generated.GeneratedEntryPoint","Main");

        Assert.Equal("Z,A",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.Trace","Result"));
    }

    /// <summary>
    /// 属性を1つも使わない従来どおりの構成では、ファイルパスのOrdinal順というADR-0033既存の
    /// タイブレークが変わらず効くことの回帰確認(後方互換)。
    /// </summary>
    [Fact]
    public void FilesWithoutSystemOrder_StillTieBreakByFilePathOrdinal(){
        const string FirstSource="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct FirstSystem : ISystem
            {
                public bool Update(ref Position position, ref State state)
                {
                    Trace.Log.Add("First");
                    return true;
                }
            }
            """;

        const string SecondSourceWithTraceAndStartup="""
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
                public bool Update(ref Bootstrap bootstrap, ref State state)
                {
                    if (bootstrap.Done) return true;
                    bootstrap.Done = true;
                    state.World.Create(new Position { X = 0f });
                    return true;
                }
            }

            internal struct SecondSystem : ISystem
            {
                public bool Update(ref Position position, ref State state)
                {
                    Trace.Log.Add("Second");
                    state.Enabled = false;
                    return true;
                }
            }
            """;

        var Compilation=GameSystemGeneratorTestHelper.CreateApplicationCompilation(
        [
            ((string?)"AFirst.cs",FirstSource),
            ("BSecond.cs",SecondSourceWithTraceAndStartup),
            (null,GameSystemGeneratorTestHelper.CommonComponents),
        ]);

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Compilation);

        GameSystemGeneratorTestHelper.Invoke(Assembly,"CSharpGameEngine.Generated.GeneratedEntryPoint","Main");

        Assert.Equal("First,Second",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.Trace","Result"));
    }

    /// <summary>
    /// Systemに直接付けた明示値は、そのファイルのマーカー既定値より優先される。
    /// </summary>
    [Fact]
    public void SystemOrderOnTheSystemItself_OverridesTheFileMarkerDefault(){
        // fileスコープの型は「そのSyntaxTreeのパスがコンパイル全体で一意」であることをコンパイラ自身が
        // 要求する(CS9068)。ヘルパーの既定ソース群(Runtime/Bootstrap/SystemOrderAttribute等)は
        // すべてパス無し(空文字列)を共有しているため、file型を含むソースだけは必ずパス付きの
        // オーバーロードで実在するファイル名を与える。
        var Compilation=GameSystemGeneratorTestHelper.CreateApplicationCompilation(
        [
            ((string?)"Combined.cs","""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal static class Trace
            {
                internal static readonly List<string> Log = [];
                internal static string Result() => string.Join(",", Log);
            }

            [SystemOrder(0)]
            file sealed class Order
            {
            }

            internal struct StartupSystem : ISystem
            {
                public bool Update(ref Bootstrap bootstrap, ref State state)
                {
                    if (bootstrap.Done) return true;
                    bootstrap.Done = true;
                    state.World.Create(new Position { X = 0f });
                    return true;
                }
            }

            // ファイル既定値は0だが、このSystemだけ-1を直接指定して先に走らせる。
            [SystemOrder(-1)]
            internal struct OverridingSystem : ISystem
            {
                public bool Update(ref Position position, ref State state)
                {
                    Trace.Log.Add("Override");
                    return true;
                }
            }

            internal struct DefaultOrderSystem : ISystem
            {
                public bool Update(ref Position position, ref State state)
                {
                    Trace.Log.Add("Default");
                    state.Enabled = false;
                    return true;
                }
            }
            """),
            (null,GameSystemGeneratorTestHelper.CommonComponents),
        ]);

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Compilation);

        GameSystemGeneratorTestHelper.Invoke(Assembly,"CSharpGameEngine.Generated.GeneratedEntryPoint","Main");

        Assert.Equal("Override,Default",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.Trace","Result"));
    }

    [Fact]
    public void SystemOrderMarkerOnNonFileScopedClass_ReportsSysgen022(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,
                                                              """
                                                              using CSharpGameEngine.Entities;

                                                              namespace Game;

                                                              [SystemOrder(0)]
                                                              internal sealed class NotFileScoped
                                                              {
                                                              }
                                                              """);

        Assert.Contains(Result.Diagnostics,D=>D.Id=="SYSGEN022");
    }

    [Fact]
    public void TwoSystemOrderMarkersInOneFile_ReportsSysgen023(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,
                                                              """
                                                              using CSharpGameEngine.Entities;

                                                              namespace Game;

                                                              [SystemOrder(0)]
                                                              file sealed class FirstMarker
                                                              {
                                                              }

                                                              [SystemOrder(1)]
                                                              file sealed class SecondMarker
                                                              {
                                                              }
                                                              """);

        Assert.Contains(Result.Diagnostics,D=>D.Id=="SYSGEN023");
    }

    [Fact]
    public void TwoFileMarkersWithTheSameExplicitOrder_ReportsSysgen024(){
        const string MarkerSource="""
            using CSharpGameEngine.Entities;

            namespace Game;

            [SystemOrder(0)]
            file sealed class Order
            {
            }
            """;

        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              [
                                                                  ((string?)"First.cs",MarkerSource),
                                                                  ("Second.cs",MarkerSource),
                                                                  (null,GameSystemGeneratorTestHelper.CommonComponents),
                                                              ]);

        Assert.Contains(Result.Diagnostics,D=>D.Id=="SYSGEN024");
    }

    /// <summary>
    /// ファイル既定値と異なり、System単体への明示値が2つの異なるSystemで衝突しても許容する
    /// (「意図的な同着」でありうるため。ADR-0036参照)。
    /// </summary>
    [Fact]
    public void TwoSystemsWithTheSameExplicitOrder_ReportsNothing(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,
                                                              """
                                                              using CSharpGameEngine.Entities;
                                                              using Sample;

                                                              namespace Game;

                                                              [SystemOrder(0)]
                                                              internal struct FirstSystem : ISystem
                                                              {
                                                                  public bool Update(ref Position p, ref State s) => true;
                                                              }

                                                              [SystemOrder(0)]
                                                              internal struct SecondSystem : ISystem
                                                              {
                                                                  public bool Update(ref Position p, ref State s) => true;
                                                              }
                                                              """);

        Assert.Empty(Result.Diagnostics);
    }

    [Fact]
    public void SystemOrderOnStructThatIsNotASystem_ReportsSysgen025(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,
                                                              """
                                                              using CSharpGameEngine.Entities;

                                                              namespace Game;

                                                              [SystemOrder(0)]
                                                              internal struct NotASystem
                                                              {
                                                              }
                                                              """);

        Assert.Contains(Result.Diagnostics,D=>D.Id=="SYSGEN025");
    }

    [Fact]
    public void UnattributedFilesSharingTheImplicitZero_ReportsNothing(){
        // 暗黙の0同士が衝突してもSYSGEN024の対象外(想定内の既定挙動)であることの回帰確認。
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              [
                                                                  ((string?)"First.cs","using CSharpGameEngine.Entities;\nusing Sample;\nnamespace Game;\ninternal struct FirstSystem : ISystem { public bool Update(ref Position p, ref State s) => true; }\n"),
                                                                  ("Second.cs","using CSharpGameEngine.Entities;\nusing Sample;\nnamespace Game;\ninternal struct SecondSystem : ISystem { public bool Update(ref Position p, ref State s) => true; }\n"),
                                                                  (null,GameSystemGeneratorTestHelper.CommonComponents),
                                                              ]);

        Assert.Empty(Result.Diagnostics);
    }
}
