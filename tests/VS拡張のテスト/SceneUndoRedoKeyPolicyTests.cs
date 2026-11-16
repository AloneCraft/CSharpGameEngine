namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneUndoRedoKeyPolicyTests {
    [Theory]
    [InlineData(false,false,false)]
    [InlineData(true,false,true)]
    [InlineData(true,true,false)]
    [InlineData(false,true,false)]
    public void IsRisingEdge_FiresOnlyOnTheTransitionFromUpToDown(
        bool CurrentlyDown,bool WasDownLastTick,bool Expected){
        Assert.Equal(Expected,SceneUndoRedoKeyPolicy.IsRisingEdge(CurrentlyDown,WasDownLastTick));
    }
}
