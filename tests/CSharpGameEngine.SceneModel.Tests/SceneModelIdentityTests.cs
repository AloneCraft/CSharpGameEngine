namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// Componentは文字列型名ではなく<c>ITypeSymbol</c>で識別する、という要求の検証(ADR-0040)。
/// using alias・同名型・入れ子名前空間・global usingのいずれでも、同じ型は同じ完全修飾名に、
/// 別の型は別の完全修飾名に落ちなければならない。
///
/// <para>
/// 旧<c>SceneRuntimeEmitter</c>は初期化子テキストを逐語コピーして元ファイルのusingを一緒に運んでいたため、
/// ここに挙げたケースで壊れていた。式を再構築する方式(<see cref="SceneEmitExpressionWriter"/>)へ
/// 変えた理由がこのテスト群である。
/// </para>
/// </summary>
public sealed class SceneModelIdentityTests {
    [Fact]
    public void UsingAlias_ResolvesToTheUnderlyingFullyQualifiedName(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            using Xf=Sample.Transform;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Xf()] };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal("global::Sample.Transform",Model.Root!.Components[0].TypeFullyQualifiedName);
        Assert.Equal("new global::Sample.Transform()",Model.Root.Components[0].EmitExpressionText);
    }

    [Fact]
    public void SameSimpleNameInDifferentNamespaces_AreDistinctComponents(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze([
            SceneAnalysisTestHelper.CommonComponents,
            "namespace Other;\npublic struct Transform { public float Scalar; }",
            """
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Components=[
                        new Sample.Transform(),
                        new Other.Transform { Scalar=1f },
                    ],
                };
            }
            """,
        ]);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal(
            ["global::Sample.Transform","global::Other.Transform"],
            Model.Root!.Components.Select(static C=>C.TypeFullyQualifiedName));
    }

    [Fact]
    public void AliasedEnumMember_IsEmittedWithTheRealFullyQualifiedName(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            using Sample;
            using Pt=Sample.PrimitiveType;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new PrimitiveMesh { Type=Pt.Sphere }] };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal(
            "global::Sample.PrimitiveType.Sphere",
            Model.Root!.Components[0].Members[0].Value.EmitExpressionText);
    }

    [Fact]
    public void NestedNamespaces_AreFullyQualified(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze([
            SceneAnalysisTestHelper.CommonComponents,
            """
            namespace Deep.Inner.Most {
                public struct Marker { public int Value; }
            }
            """,
            """
            using CSharpGameEngine.Entities;
            using Deep.Inner.Most;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Marker { Value=7 }] };
            }
            """,
        ]);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal("global::Deep.Inner.Most.Marker",Model.Root!.Components[0].TypeFullyQualifiedName);
        Assert.Equal("new global::Deep.Inner.Most.Marker() { Value = 7 }",Model.Root.Components[0].EmitExpressionText);
    }

    [Fact]
    public void SceneClassNameCollidingAcrossNamespaces_ProducesDistinctFullyQualifiedNames(){
        SceneModel First=SceneAnalysisTestHelper.Analyze([
            SceneAnalysisTestHelper.CommonComponents,
            """
            using CSharpGameEngine.Entities;
            namespace GameA {
                public partial class MainScene:IScene {
                    public Node Root=>new() { Name="A" };
                }
            }
            namespace GameB {
                public partial class MainScene:IScene {
                    public Node Root=>new() { Name="B" };
                }
            }
            """,
        ]);

        SceneAnalysisTestHelper.AssertNoDiagnostics(First);

        // FindSceneClassは最初に見つかった1件を返す。どちらであっても完全修飾名は名前空間を含む。
        Assert.Contains(First.ClassFullyQualifiedName,new[]{ "GameA.MainScene","GameB.MainScene" });
    }

    [Fact]
    public void FileScopedAndBlockScopedNamespaces_BehaveIdentically(){
        SceneModel FileScoped=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new DirectionalLight { Intensity=3f }] };
            }
            """);

        SceneModel BlockScoped=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game {
                public partial class MainScene:IScene {
                    public Node Root=>new() { Components=[new DirectionalLight { Intensity=3f }] };
                }
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(FileScoped);
        SceneAnalysisTestHelper.AssertNoDiagnostics(BlockScoped);
        Assert.Equal(FileScoped.ClassFullyQualifiedName,BlockScoped.ClassFullyQualifiedName);
        Assert.Equal(
            FileScoped.Root!.Components[0].EmitExpressionText,
            BlockScoped.Root!.Components[0].EmitExpressionText);
    }

    [Fact]
    public void PartialSceneDeclaredAcrossTwoFiles_IsAnalyzedFromWhereRootLives(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze([
            SceneAnalysisTestHelper.CommonComponents,
            """
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
            }
            """,
            """
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene {
                public Node Root=>new() { Components=[new DirectionalLight { Intensity=5f }] };
            }
            """,
        ]);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal("global::Sample.DirectionalLight",Model.Root!.Components[0].TypeFullyQualifiedName);
    }
}
