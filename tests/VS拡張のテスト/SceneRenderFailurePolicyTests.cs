namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneRenderFailurePolicyTests {
    [Theory]
    [InlineData(true,false,true)]
    [InlineData(true,true,false)]
    [InlineData(false,false,false)]
    public void FailedNativeSurfaceShowsVisibleWpfStateOnlyAfterBuildCompletes(
        bool HasFatalRenderFailure,bool IsBuildPending,bool Expected){
        Assert.Equal(
            Expected,
            SceneRenderFailurePolicy.ShouldShowFailure(HasFatalRenderFailure,IsBuildPending));
    }

    [Fact]
    public void SupersededWorkerCompletion_DoesNotClearTheNewTargetPendingLease(){
        var Lifecycle=new SceneRenderBuildLifecycle();
        long Previous=Lifecycle.Begin();

        Lifecycle.Invalidate(); // 対象の切り替え
        long Current=Lifecycle.Begin();

        Assert.False(Lifecycle.TryComplete(Previous));
        Assert.True(Lifecycle.IsBuildPending);
        Assert.True(Lifecycle.TryComplete(Current));
        Assert.False(Lifecycle.IsBuildPending);
    }

    [Fact]
    public void RenderFailureInvalidation_AllowsTheWpfFailureStateImmediately(){
        var Lifecycle=new SceneRenderBuildLifecycle();
        _=Lifecycle.Begin();

        Lifecycle.Invalidate(); // RenderFrame/Resize の致命的エラー経路。

        Assert.False(Lifecycle.IsBuildPending);
        Assert.True(SceneRenderFailurePolicy.ShouldShowFailure(
            HasFatalRenderFailure:true,IsBuildPending:Lifecycle.IsBuildPending));
    }
}