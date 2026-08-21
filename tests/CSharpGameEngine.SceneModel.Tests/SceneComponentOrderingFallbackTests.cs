namespace CSharpGameEngine.SceneAnalysis.Tests;

public sealed class SceneComponentOrderingFallbackTests {
    [Fact]
    public void UnresolvedComponentType_StillDefaultsToGameplay(){
        var Key=SceneComponentOrdering.GetSortKey(
            TypeSymbol:null,"global::CSharpGameEngine.Runtime.Windows.Transform");

        Assert.Equal(6,Key.Category);
        Assert.Equal(0,Key.Order);
    }
}
