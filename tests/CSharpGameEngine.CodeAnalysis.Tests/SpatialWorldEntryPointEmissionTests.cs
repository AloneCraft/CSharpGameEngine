namespace CSharpGameEngine.CodeAnalysis.Tests;
public sealed class SpatialWorldEntryPointEmissionTests {
    [Fact]
    public void GeneratedEntryPoint_SeedsSpatialSnapshotBeforeFirstFrame(){
        var Compilation=GameSystemGeneratorTestHelper.CreateApplicationCompilation("""
            namespace CSharpGameEngine.Entities { public struct SpatialBounds { } public struct Transform { } }
            namespace Sample {
                public struct SpatialSystem : CSharpGameEngine.Entities.ISystem {
                    public bool Update(ref CSharpGameEngine.Entities.Transform Transform,ref CSharpGameEngine.Entities.SpatialBounds Bounds,ref CSharpGameEngine.Entities.State State) => false;
                }
            }
            """);
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Compilation);
        string EntryPoint=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.EntryPoint.g.cs");
        Assert.Contains("State.World.RefreshGlobalSpatialIndex();",EntryPoint);
    }
}
