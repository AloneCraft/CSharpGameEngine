using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class PrefractureDebugServiceTests {
    [Fact]
    public void FindFractureAsset_UsesCanonicalGlobalComponentIdentity(){
        const string Components="""
            namespace CSharpGameEngine.Runtime.Physics {
                public struct Destructible {
                    public string FractureAsset;
                }
            }
            """;
        const string Scene="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Physics;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[
                    new Destructible { FractureAsset="Physics/Wall.cgefracture" },
                ]};
            }
            """;
        var Model=SceneAnalysisTestHelper.Analyze([Components,Scene]);

        Assert.NotNull(Model.Root);
        Assert.Equal(
            "Physics/Wall.cgefracture",
            PrefractureDebugService.FindFractureAsset(Model.Root!));
        Assert.Equal(
            "global::CSharpGameEngine.Runtime.Physics.Destructible",
            Assert.Single(Model.Root!.Components).TypeFullyQualifiedName);
    }
}
