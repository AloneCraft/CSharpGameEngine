namespace CSharpGameEngine.SceneAnalysis.Tests;

public sealed class SceneComponentOrderingTests {
    private const string OrderAttribute="""
        namespace CSharpGameEngine.Entities {
            public enum SceneComponentCategory {
                Spatial=0, Camera=1, LightingEnvironment=2, Rendering=3,
                Physics=4, Audio=5, Gameplay=6, Runtime=7,
            }

            [System.AttributeUsage(System.AttributeTargets.Struct)]
            public sealed class SceneComponentOrderAttribute:System.Attribute {
                public SceneComponentOrderAttribute(SceneComponentCategory category,int order) { }
            }
        }
        """;

    [Fact]
    public void AttributedComponent_UsesItsDeclaredCategoryAndOrder(){
        const string Components="""
            using CSharpGameEngine.Entities;
            namespace Sample {
                [SceneComponentOrder(SceneComponentCategory.LightingEnvironment,17)]
                public struct Ordered { }
            }
            """;
        const string Scene="""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Ordered()] };
            }
            """;

        var Model=SceneAnalysisTestHelper.Analyze([OrderAttribute,Components,Scene]);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Component=Assert.Single(Model.Root!.Components);
        Assert.Equal(2,Component.OrderKey.Category);
        Assert.Equal(17,Component.OrderKey.Order);
    }

    [Fact]
    public void UnattributedComponent_DefaultsToGameplayEvenWhenItsNameLooksSpatial(){
        const string Components="""
            namespace Sample { public struct Transform { } }
            """;
        const string Scene="""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Transform()] };
            }
            """;

        var Model=SceneAnalysisTestHelper.Analyze([Components,Scene]);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Component=Assert.Single(Model.Root!.Components);
        Assert.Equal(6,Component.OrderKey.Category);
        Assert.Equal(0,Component.OrderKey.Order);
    }

    [Fact]
    public void Normalize_TieBreaksEqualOrderByFullyQualifiedNameOrdinal(){
        const string Components="""
            namespace A { public struct Shared { } }
            namespace B { public struct Shared { } }
            """;
        const string Scene="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new B.Shared(),new A.Shared()] };
            }
            """;

        var Model=SceneAnalysisTestHelper.Analyze([Components,Scene]);
        var Normalized=SceneComponentOrdering.Normalize(Model.Root!.Components);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal(
            ["global::A.Shared","global::B.Shared"],
            Normalized.Select(static Component=>Component.TypeFullyQualifiedName));
    }
}
