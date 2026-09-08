namespace CSharpGameEngine.CodeAnalysis.Tests;
public sealed class SpatialWorldGeneratorEmissionTests {
    [Fact]
    public void TransformAndSpatialBoundsArchetype_EmitsGlobalSnapshotSynchronization(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator("""
            namespace CSharpGameEngine.Entities { public struct SpatialBounds { } public struct Transform { } }
            namespace Sample {
                public struct SpatialSystem : CSharpGameEngine.Entities.ISystem {
                    public bool Update(ref CSharpGameEngine.Entities.Transform Transform,ref CSharpGameEngine.Entities.SpatialBounds Bounds,ref CSharpGameEngine.Entities.State State) => true;
                }
            }
            """);
        string World=GameSystemGeneratorTestHelper.GeneratedText(Result,"SpatialWorld.g.cs");
        string Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");
        Assert.Contains("SynchronizeGlobalSpatialIndexCore",World);
        Assert.Contains("SynchronizeGlobalSpatialEntity",World);
        Assert.Contains("RefreshGlobalSpatialIndex",Loop);
    }
}
