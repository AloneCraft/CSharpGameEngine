using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// C#側のシーン境界（ADR-0070）。シーンは<c>Scenes</c>配下のJSON文書である。そのためC#のシーンクラスや<c>[StartupScene]</c>がコードに残っている場合、
/// シーンが黙って消えるのではなくSCENE206を報告する。<c>Node</c>ツリーを構築／読み取るコードは引き続きSCENE114、
/// startupOrderが同じJSONシーンは後の文書の値の位置でSCENE118となる。
/// </summary>
public sealed class SceneDslAnalyzerTests {
    private const string CSharpScene="""
        using CSharpGameEngine.Entities;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() { Name="Root" };
        }
        """;

    private static ImmutableArray<Diagnostic>Analyze(params string[]ExtraSources){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation(
                                                                        [.. GameSystemGeneratorTestHelper.SceneDslSources,.. ExtraSources]);
        return RunAnalyzers(Compilation,[]);
    }

    private static ImmutableArray<Diagnostic>RunAnalyzers(CSharpCompilation Compilation,ImmutableArray<AdditionalText>AdditionalFiles){
        var WithAnalyzers=Compilation.WithAnalyzers(
                                                    ImmutableArray.Create<DiagnosticAnalyzer>(new SceneDslAnalyzer(),new SceneJsonDslAnalyzer()),
                                                    SceneProjectOptions.Analyzer(AdditionalFiles));
        return WithAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private static string[]Ids(ImmutableArray<Diagnostic>Diagnostics)=>
        [.. Diagnostics.Select(static D=>D.Id).OrderBy(static Id=>Id,StringComparer.Ordinal)];

    private static AdditionalText JsonScene(string Path,string ClassName,int?StartupOrder)=>new InMemoryAdditionalText(Path,
        "{\n"
        +"  \"schemaVersion\": 1,\n"
        +"  \"namespace\": \"Game\",\n"
        +"  \"className\": \""+ClassName+"\",\n"
        +(StartupOrder is { }Order?"  \"startupOrder\": "+Order+",\n":string.Empty)
        +"  \"root\": { \"id\": \""+Guid.NewGuid().ToString("D")+"\", \"components\": [], \"children\": [] }\n"
        +"}\n");

    [Fact]
    public void CSharpSceneClass_IsReportedAtItsNameAsNotBuilt(){
        var Diagnostic=Assert.Single(Analyze(CSharpScene));

        Assert.Equal("SCENE206",Diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error,Diagnostic.Severity);
        Assert.Contains("Game.MainScene",Diagnostic.GetMessage(),StringComparison.Ordinal);
        Assert.Contains("CSharpGameEngine.SceneJson.Cli convert",Diagnostic.GetMessage(),StringComparison.Ordinal);
        Assert.Equal("MainScene",Diagnostic.Location.SourceTree!.GetText().ToString(Diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void StartupSceneAttributeWithoutAScene_IsAlsoNotBuilt(){
        Assert.Equal(["SCENE206"],Ids(Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            [StartupScene(Order=0)]
            public partial class NotAScene {
            }
            """)));
    }

    [Fact]
    public void Generator_DoesNotBuildACSharpSceneTheConverterCouldNotRead(){
        // コンバーターが拒否したシーン（ここではpartialでないもの）はC#のまま残す。ジェネレーターは出力してはならない。
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              [.. GameSystemGeneratorTestHelper.SceneDslSources,
                                                                  """
                                                                  using CSharpGameEngine.Entities;
                                                                  namespace Game;
                                                                  public class MainScene:IScene {
                                                                      public Node Root=>new() { Name="Root" };
                                                                  }
                                                                  """]);

        Assert.DoesNotContain(Result.Results[0].GeneratedSources,static Source=>Source.HintName.Contains("MainScene",StringComparison.Ordinal));
        Assert.DoesNotContain(Result.Diagnostics,static D=>D.Id.StartsWith("SCENE",StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectWithoutTheRuntimeTypes_IsNotAnalyzedAtAll(){
        // CSharpGameEngine.CodeAnalysisは1パッケージに複数機能を同居させるため、参照しただけの
        // 無関係なプロジェクトでもアナライザーは走る。ランタイム型の実在をゲートにしている。
        var Compilation=CSharpCompilation.Create(
                                                 "Unrelated",
                                                 [CSharpSyntaxTree.ParseText("public class Unrelated { }")],
                                                 [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                                                 new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(RunAnalyzers(Compilation,[JsonScene(SceneProjectOptions.ScenesDirectory+"A.json","A",0),JsonScene(SceneProjectOptions.ScenesDirectory+"B.json","B",0)]));
    }

    [Fact]
    public void Scene114_NodeConstructedOutsideASceneDeclaration(){
        var Diagnostics=Analyze("""
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
        var Diagnostics=Analyze(CSharpScene,"""
                                           using CSharpGameEngine.Entities;
                                           namespace Game;
                                           public static class Rogue {
                                               public static string?Peek(MainScene Scene)=>Scene.Root.Name;
                                           }
                                           """);

        Assert.Equal(["SCENE114","SCENE206"],Ids(Diagnostics));
    }

    [Fact]
    public void JsonStartupScenesSharingAnOrder_AreScene118AtTheLaterDocumentsValue(){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources]);
        var Diagnostics=RunAnalyzers(Compilation,[
            JsonScene(SceneProjectOptions.ScenesDirectory+"B.json","SecondScene",0),
            JsonScene(SceneProjectOptions.ScenesDirectory+"A.json","FirstScene",0),
            JsonScene(SceneProjectOptions.ScenesDirectory+"C.json","ThirdScene",1),
            JsonScene(SceneProjectOptions.ScenesDirectory+"D.json","Level",null),
        ]);

        var Diagnostic=Assert.Single(Diagnostics);
        Assert.Equal("SCENE118",Diagnostic.Id);
        Assert.Equal(SceneProjectOptions.ScenesDirectory+"B.json",Diagnostic.Location.GetLineSpan().Path);
        Assert.Equal(4,Diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Contains("Game.FirstScene",Diagnostic.GetMessage(),StringComparison.Ordinal);
        Assert.Contains("Game.SecondScene",Diagnostic.GetMessage(),StringComparison.Ordinal);
    }

    [Fact]
    public void SupportedDiagnostics_AreExactlyWhatEachAnalyzerReports(){
        // 報告したIDがSupportedDiagnosticsにないと、IDEには届かない。
        Assert.Equal(["SCENE114","SCENE206"],new SceneDslAnalyzer().SupportedDiagnostics.Select(static D=>D.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["SCENE118","SCENE201","SCENE202","SCENE203","SCENE204","SCENE205","SCENE207"],new SceneJsonDslAnalyzer().SupportedDiagnostics.Select(static D=>D.Id).Order(StringComparer.Ordinal));
    }
}
