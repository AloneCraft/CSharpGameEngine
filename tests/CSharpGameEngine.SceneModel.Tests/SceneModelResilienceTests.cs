namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// <b>不完全な構文でクラッシュしない</b>ことの保証(ADR-0040 F-6)。VSIXはユーザーが1文字打つたびに
/// <see cref="SceneDeclarationAnalyzer"/>を呼ぶため、ここが破れるとVSに例外ダイアログが出る。
///
/// <para>
/// 併せて「入力途中に赤線を重ねない」ことも検証する。型が未解決の箇所はコンパイラが既に別の診断を
/// 出しているので、Scene DSL側は黙る(R2)。
/// </para>
/// </summary>
public sealed class SceneModelResilienceTests {
    private const string CompleteScene="""
        using CSharpGameEngine.Entities;
        using Sample;
        using System.Numerics;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() {
                Name="Root",
                Components=[new Transform { Position=new Vector3(1f,2f,3f) }],
                Children=[new Node { Name="Child",Components=[new DirectionalLight { Intensity=3f }] }],
            };
        }
        """;

    /// <summary>
    /// 完成したSceneソースを末尾から削っていき、<b>どの長さでも例外が出ない</b>ことを確認する。
    /// 「閉じ括弧が足りない」「識別子の途中」「文字列リテラルが閉じていない」といった入力途中の状態を
    /// 踏むための素朴なfuzzである。
    ///
    /// <para>
    /// 1文字刻みだと1テストで数百回コンパイルすることになり、スイート全体が1分を超える。3文字刻みでも
    /// 構文的な境界(括弧・引用符・識別子の途中)はすべて踏むため、刻み幅で品質は落ちない。
    /// </para>
    /// </summary>
    [Fact]
    public void TruncatedSourceAtEveryLength_NeverThrows(){
        for(var Length=CompleteScene.Length;Length>=0;Length-=3){
            var Truncated=CompleteScene.Substring(0,Length);
            Exception?Failure=Record.Exception(()=>AnalyzeIfSceneFound(Truncated));
            Assert.True(Failure is null, $"Analyzing a {Length}-character prefix threw {Failure?.GetType().Name}: {Failure?.Message}");
        }
    }

    [Theory]
    [InlineData("public Node Root=>new() {")]
    [InlineData("public Node Root=>new() { Components=[")]
    [InlineData("public Node Root=>new() { Components=[new ")]
    [InlineData("public Node Root=>new() { Children=[new Node { ")]
    [InlineData("public Node Root=>new() { Name=")]
    public void IncompleteRootExpression_NeverThrows(string PartialBody){
        var Source= $$"""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                {{PartialBody}}
            }
            """;

        Exception?Failure=Record.Exception(()=>AnalyzeIfSceneFound(Source));
        Assert.Null(Failure);
    }

    [Fact]
    public void UnresolvedComponentType_ProducesNoSceneDiagnostic(){
        // ユーザーが型名を打っている途中。コンパイラがCS0246を出すので、Scene DSLは黙る。
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new NotYetTyped()] };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
    }

    [Fact]
    public void UnresolvedMemberValue_ProducesNoSceneDiagnostic(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new DirectionalLight { Intensity=NotYetDefined }] };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
    }

    [Fact]
    public void DeeplyNestedChildren_AreCutOffInsteadOfOverflowingTheStack(){
        var Depth=SceneDslWellKnownNames.MaxRecursionDepth+20;
        var Opening=string.Concat(Enumerable.Repeat("new Node { Children=[",Depth));
        var Closing=string.Concat(Enumerable.Repeat("] }",Depth));
        var Source= $$"""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>{{Opening}}new Node()  {{Closing}};
            }
            """;

        SceneModel Model=SceneAnalysisTestHelper.Analyze(Source);

        SceneAnalysisTestHelper.AssertContainsDiagnostic(Model,"SCENE110");
    }

    [Fact]
    public void EmptySource_FindsNoSceneAndDoesNotThrow(){
        Exception?Failure=Record.Exception(()=>AnalyzeIfSceneFound(string.Empty));
        Assert.Null(Failure);
    }

    /// <summary>
    /// 切り詰めたソースでは<c>IScene</c>実装が見つからないこともある。そのケースは「解析対象なし」であって
    /// 失敗ではないので、見つかったときだけ解析する。
    /// </summary>
    private static void AnalyzeIfSceneFound(string Source){
        Microsoft.CodeAnalysis.CSharp.CSharpCompilation Compilation=
            SceneAnalysisTestHelper.CreateCompilation(SceneAnalysisTestHelper.CommonComponents,Source);
        Microsoft.CodeAnalysis.INamedTypeSymbol?SceneClass=SceneAnalysisTestHelper.FindSceneClass(Compilation,SceneClassName:null);

        if(SceneClass is not null){
            SceneDeclarationAnalyzer.Analyze(SceneClass,Compilation,CancellationToken.None);
        }
    }
}
