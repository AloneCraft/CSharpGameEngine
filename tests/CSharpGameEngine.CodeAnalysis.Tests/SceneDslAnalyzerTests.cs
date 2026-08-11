using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// 本リポジトリで初めての<see cref="DiagnosticAnalyzer"/>のテスト(ADR-0040)。
///
/// <para>
/// 検証したいのは<b>配線</b>である: SCENE1xxがアナライザー経由で実際にIDE/ビルドへ届くこと、
/// Generatorは同じ規則を評価しても<b>報告しない</b>こと(二重スクイグルの防止)、そしてECSを使わない
/// プロジェクトでは何も報告しないこと。個々の診断の判定ロジック自体は
/// <c>CSharpGameEngine.SceneModel.Tests</c>が網羅している。
/// </para>
/// </summary>
public sealed class SceneDslAnalyzerTests {
    private const string ValidScene="""
        using CSharpGameEngine.Entities;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() { Name="Root" };
        }
        """;

    private static ImmutableArray<Diagnostic>Analyze(params string[]ExtraSources){
        CSharpCompilation Compilation=GameSystemGeneratorTestHelper.CreateCompilation(
            [.. GameSystemGeneratorTestHelper.SceneDslSources,.. ExtraSources]);
        return RunAnalyzer(Compilation);
    }

    private static ImmutableArray<Diagnostic>RunAnalyzer(CSharpCompilation Compilation){
        CompilationWithAnalyzers WithAnalyzers=Compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new SceneDslAnalyzer()));
        return WithAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private static string[]Ids(ImmutableArray<Diagnostic>Diagnostics)=>
        [.. Diagnostics.Select(static D=>D.Id).OrderBy(static Id=>Id,StringComparer.Ordinal)];

    [Fact]
    public void ValidScene_ProducesNoDiagnostics(){
        Assert.Empty(Analyze(ValidScene));
    }

    [Fact]
    public void InvalidScene_IsReportedThroughTheAnalyzer(){
        ImmutableArray<Diagnostic>Diagnostics=Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public class MainScene:IScene {
                public Node Root=>new() { Name="Root" };
            }
            """);

        Assert.Equal(["SCENE100"],Ids(Diagnostics));
    }

    [Fact]
    public void Generator_DoesNotAlsoReportSceneDslDiagnostics(){
        // SCENE1xxを報告するのはアナライザーだけ。両方が報告すると同じ箇所に二重の赤線が出る。
        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
            [.. GameSystemGeneratorTestHelper.SceneDslSources,
            """
            using CSharpGameEngine.Entities;
            namespace Game;
            public class MainScene:IScene {
                public Node Root=>new() { Name="Root" };
            }
            """]);

        Assert.DoesNotContain(Result.Diagnostics,static D=>D.Id.StartsWith("SCENE1",StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectWithoutTheRuntimeTypes_IsNotAnalyzedAtAll(){
        // CSharpGameEngine.CodeAnalysisは1パッケージに複数機能を同居させるため、参照しただけの
        // 無関係なプロジェクトでもアナライザーは走る。ランタイム型の実在をゲートにしている。
        CSharpCompilation Compilation=CSharpCompilation.Create(
            "Unrelated",
            [CSharpSyntaxTree.ParseText("public class Unrelated { }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(RunAnalyzer(Compilation));
    }

    [Fact]
    public void Scene114_NodeConstructedOutsideASceneDeclaration(){
        ImmutableArray<Diagnostic>Diagnostics=Analyze(ValidScene,"""
            using CSharpGameEngine.Entities;
            namespace Game;
            public static class Rogue {
                public static Node Build()=>new Node { Name="not a scene" };
            }
            """);

        Assert.Equal(["SCENE114"],Ids(Diagnostics));
    }

    [Fact]
    public void Scene114_ReadingRootExecutesTheDslAndIsRejected(){
        ImmutableArray<Diagnostic>Diagnostics=Analyze(ValidScene,"""
            using CSharpGameEngine.Entities;
            namespace Game;
            public static class Rogue {
                public static string?Peek(MainScene Scene)=>Scene.Root.Name;
            }
            """);

        Assert.Equal(["SCENE114"],Ids(Diagnostics));
    }

    [Fact]
    public void Scene114_NestedNodesInsideTheSceneItselfAreAllowed(){
        Assert.Empty(Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Children=[new Node { Name="A",Children=[new Node { Name="A0" }] }],
                };
            }
            """));
    }

    [Fact]
    public void Scene114_ExplicitInterfaceImplementationIsAllowed(){
        Assert.Empty(Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                Node IScene.Root=>new() { Name="Root" };
            }
            """));
    }

    [Fact]
    public void Scene117_IsNotYetReported_BecauseStartupSceneStillTargetsTheLegacyForm(){
        // [StartupScene]をIScene実装へ再ターゲットするのはPhase 5。それまで
        // [StartupScene]+[SceneEntity](タプルプロパティ形式)は現役の正しい仕組みであり、
        // ここで報告すると既存の正しいコードを壊す(UserEcsProject/Scenes/MainScene.csで実測)。
        ImmutableArray<Diagnostic>Diagnostics=Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            [StartupScene(Order=0)]
            public partial class NotAScene {
            }
            """);

        Assert.Empty(Diagnostics);
    }

    [Fact]
    public void Scene118_TwoStartupScenesSharingAnOrder(){
        ImmutableArray<Diagnostic>Diagnostics=Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            [StartupScene(Order=0)]
            public partial class FirstScene:IScene {
                public Node Root=>new() { Name="First" };
            }
            [StartupScene(Order=0)]
            public partial class SecondScene:IScene {
                public Node Root=>new() { Name="Second" };
            }
            """);

        Assert.Equal(["SCENE118"],Ids(Diagnostics));
    }

    [Fact]
    public void StartupScenesWithDistinctOrders_AreAccepted(){
        Assert.Empty(Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            [StartupScene(Order=0)]
            public partial class FirstScene:IScene {
                public Node Root=>new() { Name="First" };
            }
            [StartupScene(Order=1)]
            public partial class SecondScene:IScene {
                public Node Root=>new() { Name="Second" };
            }
            """));
    }

    [Fact]
    public void SupportedDiagnostics_CoversEveryDeclaredSceneDslRule(){
        // SceneDslDiagnostics.Allへの追加漏れは「報告したのにIDEへ出ない」という追跡困難な状態になる。
        var Supported=new SceneDslAnalyzer().SupportedDiagnostics.Select(static D=>D.Id).ToHashSet(StringComparer.Ordinal);

        foreach(var Expected in Enumerable.Range(100,19).Select(static N=> $"SCENE{N}")){
            Assert.Contains(Expected,Supported);
        }
    }
}
