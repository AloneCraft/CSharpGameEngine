namespace CSharpGameEngine.VSExtension.Tests;
public sealed class SceneSelectionRequestLifecycleTests {
    [Fact]
    public void NewRequestSupersedesEveryEarlierCompletion(){
        var Lifecycle=new SceneSelectionRequestLifecycle();
        long First=Lifecycle.Begin();
        long Second=Lifecycle.Begin();
        Assert.False(Lifecycle.IsCurrent(First));
        Assert.True(Lifecycle.IsCurrent(Second));
    }
    [Fact]
    public void InvalidateRejectsSuccessAndFailureFromTheDetachedSession(){
        var Lifecycle=new SceneSelectionRequestLifecycle();
        long Request=Lifecycle.Begin();
        Lifecycle.Invalidate();
        Assert.False(Lifecycle.IsCurrent(Request));
    }
    [Fact]
    public void CurrentRequestCanCompleteOnlyOnce(){
        var Lifecycle=new SceneSelectionRequestLifecycle();
        long Request=Lifecycle.Begin();
        Assert.True(Lifecycle.TryComplete(Request));
        Assert.False(Lifecycle.TryComplete(Request));
        Assert.False(Lifecycle.IsCurrent(Request));
    }
}
