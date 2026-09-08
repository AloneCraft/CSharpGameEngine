namespace CSharpGameEngine.CodeAnalysis.Tests;
public sealed class SpatialWorldGeneratorCompilationTests {
    [Fact]
    public void SpatialSnapshotGeneratedSource_CompilesWithTransformArchetype(){
        _=GameSystemGeneratorTestHelper.CompileAndLoad("""
            namespace CSharpGameEngine.Entities {
                public struct SpatialBounds { }
                public struct Transform { }
                public delegate bool TryGetLocalTransform(int EntityId,out Transform Transform);
                public delegate bool TryGetParentEntity(int EntityId,out int ParentEntityId);
                public partial struct World {
                    partial void SynchronizeGlobalSpatialIndexCore();
                    public void RefreshGlobalSpatialIndex() { }
                    private void SynchronizeGlobalSpatialEntity(Entity Entity,SpatialBounds Bounds,TryGetLocalTransform GetTransform,TryGetParentEntity GetParent) { }
                }
            }
            namespace Sample {
                public struct SpatialSystem : CSharpGameEngine.Entities.ISystem {
                    public bool Update(ref CSharpGameEngine.Entities.Transform Transform,ref CSharpGameEngine.Entities.SpatialBounds Bounds,ref CSharpGameEngine.Entities.State State) => false;
                }
            }
            """);
    }
}
