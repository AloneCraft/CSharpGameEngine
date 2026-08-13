namespace CSharpGameEngine.VSExtension.Tests;

public sealed class StartupAutoOpenPolicyTests {
    [Fact]
    public void ShouldOpenUserEcsProject_WhenNoSolutionIsOpen(){
        Assert.True(StartupAutoOpenPolicy.ShouldOpenUserEcsProject(IsSolutionAlreadyOpen:false));
    }

    [Fact]
    public void DoesNotOpenOrActivate_WhenSolutionIsAlreadyOpen(){
        Assert.False(StartupAutoOpenPolicy.ShouldOpenUserEcsProject(IsSolutionAlreadyOpen:true));
        Assert.False(StartupAutoOpenPolicy.ShouldActivateMainWindow);
    }
}
