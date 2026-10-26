using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// 共有 Scene analysis が <b>net472</b>（VSIX が実行される framework）でも、Source Generator が実行される net10.0 と同じ動作をすることを検証します（ADR-0040）。
///
/// <para>
/// analyser を host ごとに再実装せず、ソースとして共有する効果を検証します。以下の assertion は、net10.0 で <c>CSharpGameEngine.SceneModel.Tests</c> が固定している事実と意図的に同じです。言語機能の挙動差、net472 にない BCL API、Roslyn version の不一致などで2つの host が食い違うと、そのうちのどれかが失敗し、editor が生成コードと静かに異なる動作をすることを防ぎます。
/// </para>
/// </summary>
public sealed class SceneAnalysisParityTests {
    private const string CanonicalScene="""
        using CSharpGameEngine.Entities;
        using Sample;
        using System;
        using System.Numerics;
        namespace Game;
        public partial class MainScene:IScene {
            private const string Prefix="C# Game Engine";
            public Node Root=>new() {
                Name="Root",
                Components=[
                    new Transform { Position=new Vector3(1f,2f,3f) },
                    new PrimitiveMesh { Type=PrimitiveType.Cube },
                ],
                Children=[
                    new Node {
                        Name="Sun",
                        // Deliberately spells out pi instead of using MathF: the shared test helper
                        // builds the analysed compilation from the *host process's* assemblies, and
                        // MathF does not exist on net472. That is a property of this fixture, not of
                        // the analyser - in Visual Studio the VSIX analyses the user's project with
                        // the user's own references, where MathF resolves normally. Keeping the
                        // arithmetic identical still pins the folding and G9 formatting behaviour,
                        // which is what this test is actually about.
                        Components=[new SpotLight { Intensity=3f,InnerConeAngleRadians=12f*3.14159274f/180f }],
                    },
                ],
            };
        }
        """;

    private static SceneModel Analyze()=>SceneAnalysisTestHelper.Analyze(CanonicalScene);

    [Fact]
    public void CanonicalSceneAnalysesCleanlyOnNet472(){
        SceneAnalysisTestHelper.AssertNoDiagnostics(Analyze());
    }

    [Fact]
    public void NodePathsMatch(){
        var Model=Analyze();

        Assert.Equal("(root)",Model.Root!.Path.ToDisplayString());
        Assert.Equal("0",Model.Root.Children[0].Path.ToDisplayString());
        Assert.Equal(2,Model.TotalNodeCount);
    }

    [Fact]
    public void ComponentTypesAreFullyQualifiedIdentically(){
        var Model=Analyze();

        Assert.Equal(
            ["global::Sample.Transform","global::Sample.PrimitiveMesh"],
            Model.Root!.Components.Select(static C=>C.TypeFullyQualifiedName));
    }

    [Fact]
    public void EmittedExpressionsAreIdenticalToTheGeneratorHost(){
        // host 間でこのテキストが異なると、Inspector は Source Generator がコンパイルする内容とは
        // 異なるものを編集することになります。
        var Model=Analyze();

        Assert.Equal(
            "new global::Sample.Transform() { Position = new global::System.Numerics.Vector3(1f, 2f, 3f) }",
            Model.Root!.Components[0].EmitExpressionText);
        Assert.Equal(
            "new global::Sample.PrimitiveMesh() { Type = global::Sample.PrimitiveType.Cube }",
            Model.Root.Components[1].EmitExpressionText);
    }

    [Fact]
    public void ConstantFoldingBehavesTheSameOnNet472(){
        // 定数畳み込みは compiler が行いますが、出力時の float formatting は
        // このコードが担い、.NET Framework では "R" の round-trip 結果が異なります。G9 の選択を固定します。
        var Model=Analyze();
        var Angle=Model.Root!.Children[0].Components[0].Members
                       .Single(static M=>M.MemberName=="InnerConeAngleRadians").Value;

        Assert.Equal(SceneValueKind.ConstantExpression,Angle.Kind);
        Assert.Equal("0.209439516f",Angle.EmitExpressionText);
        Assert.Equal("12f*3.14159274f/180f",Angle.SourceExpressionText);
    }

    [Fact]
    public void WhitelistViolationsAreRejectedTheSameWay(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  using Sample;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public Node Root=>new() { Components=[new Label { Text=System.IO.Path.Combine("a","b") }] };
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertContainsDiagnostic(Model,"SCENE111");
    }

    [Fact]
    public void IncompleteSyntaxDoesNotThrowOnNet472(){
        // VSIX はユーザーがキー入力の途中でも analyser を呼び出します。ここで例外をスローすると、
        // Visual Studio 内に例外ダイアログが表示されます。
        for(var Length=CanonicalScene.Length;Length>=0;Length-=7){
            var Truncated=CanonicalScene.Substring(0,Length);
            var Compilation=
                SceneAnalysisTestHelper.CreateCompilation(SceneAnalysisTestHelper.CommonComponents,Truncated);
            var SceneClass=
                SceneAnalysisTestHelper.FindSceneClass(Compilation,SceneClassName:null);

            if(SceneClass is null){
                continue;
            }

            SceneDeclarationAnalyzer.Analyze(SceneClass,Compilation,System.Threading.CancellationToken.None);
        }
    }

    [Fact]
    public void GeneratedScenePartialDoesNotInvalidateLiveAnalysisOnNet472(){
        var Model=SceneAnalysisTestHelper.Analyze([
                                                      SceneAnalysisTestHelper.CommonComponents,
                                                      """
                                                      // <auto-generated/>
                                                      namespace Game;
                                                      public partial class MainScene {
                                                          private MainScene(){
                                                          }
                                                      }
                                                      """,
                                                      """
                                                      using CSharpGameEngine.Entities;
                                                      namespace Game;
                                                      public partial class MainScene:IScene {
                                                          public Node Root=>new() { Name="Root" };
                                                      }
                                                      """,
                                                  ]);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal("Root",Model.Root!.Name);
    }
}
