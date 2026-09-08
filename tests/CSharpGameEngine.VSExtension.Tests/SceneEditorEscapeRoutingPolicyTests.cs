namespace CSharpGameEngine.VSExtension.Tests;
public sealed class SceneEditorEscapeRoutingPolicyTests {
    [Theory]
    [InlineData(false,false,false)]
    [InlineData(false,true,false)]
    [InlineData(true,false,false)]
    [InlineData(true,true,true)]
    public void EscapeRouting_ConsumesOnlyEscapeFromFocusedDesignerSurface(bool OwnerHasKeyboardFocus,bool IsEscape,bool Expected){
        Assert.Equal(Expected,SceneEditorEscapeRoutingPolicy.ShouldConsume(OwnerHasKeyboardFocus,IsEscape));
    }
    [Fact]
    public void EscapeRouting_InvokesOnlyTheFirstKeyDownUntilKeyUpOrFocusLoss(){
        Assert.True(SceneEditorEscapeRoutingPolicy.ShouldInvoke(EscapeAlreadyPressed:false));
        Assert.False(SceneEditorEscapeRoutingPolicy.ShouldInvoke(EscapeAlreadyPressed:true));
    }
    [Theory]
    [InlineData(NativeMethods.WM_KEYDOWN,NativeMethods.VK_ESCAPE,true)]
    [InlineData(NativeMethods.WM_KEYUP,NativeMethods.VK_ESCAPE,false)]
    [InlineData(NativeMethods.WM_KEYDOWN,NativeMethods.VK_S,false)]
    public void VisualStudioMessageBoundary_ConsumesOnlyEscapeKeyDown(
        int Message,int VirtualKey,bool Expected){
        Assert.Equal(
            Expected,
            SceneEditorEscapeRoutingPolicy.IsVisualStudioEscapeKeyDown(
                Message,VirtualKey));
    }
}
