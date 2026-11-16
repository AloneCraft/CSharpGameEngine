using System;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class RenderSurfaceFocusPolicyTests {
    [Fact]
    public void MouseActivate_DoesNotActivateSurfaceButStillAllowsMouseInput(){
        Assert.Equal(
            RenderSurfaceFocusAction.MouseWithoutActivation,
            RenderSurfaceFocusPolicy.GetAction(NativeMethods.WM_MOUSEACTIVATE));
        Assert.Equal(3,NativeMethods.MA_NOACTIVATE);
    }

    [Fact]
    public void SetFocus_UsesTheFocusReturnPath(){
        Assert.Equal(
            RenderSurfaceFocusAction.ReturnFocus,
            RenderSurfaceFocusPolicy.GetAction(NativeMethods.WM_SETFOCUS));
    }

    [Fact]
    public void SetFocus_PrefersTheWindowThatPreviouslyOwnedFocus(){
        IntPtr Surface=new(10);
        IntPtr PreviousEditor=new(20);
        IntPtr ToolWindowParent=new(30);

        var Target=RenderSurfaceFocusPolicy.SelectReturnTarget(
                                                               PreviousEditor,
                                                               PreviousIsWindow:true,
                                                               Surface,
                                                               ToolWindowParent,
                                                               ParentIsWindow:true);

        Assert.Equal(PreviousEditor,Target);
    }

    [Fact]
    public void SetFocus_FallsBackToParentWhenPreviousWindowIsUnavailable(){
        IntPtr Surface=new(10);
        IntPtr ToolWindowParent=new(30);

        var Target=RenderSurfaceFocusPolicy.SelectReturnTarget(
                                                               PreviousFocus:IntPtr.Zero,
                                                               PreviousIsWindow:false,
                                                               Surface,
                                                               ToolWindowParent,
                                                               ParentIsWindow:true);

        Assert.Equal(ToolWindowParent,Target);
    }

    [Fact]
    public void SetFocus_NeverReturnsFocusToTheSurfaceItself(){
        IntPtr Surface=new(10);

        var Target=RenderSurfaceFocusPolicy.SelectReturnTarget(
                                                               PreviousFocus:Surface,
                                                               PreviousIsWindow:true,
                                                               Surface,
                                                               ParentHandle:IntPtr.Zero,
                                                               ParentIsWindow:false);

        Assert.Equal(IntPtr.Zero,Target);
    }

    [Fact]
    public void Dispose_RestoresFocusOnlyWhenSurfaceIsFocusedAndParentIsValid(){
        IntPtr Surface=new(10);
        IntPtr Parent=new(20);

        Assert.True(RenderSurfaceFocusPolicy.ShouldRestoreFocus(
            Surface,Surface,Parent));
        Assert.False(RenderSurfaceFocusPolicy.ShouldRestoreFocus(
            Parent,Surface,Parent));
        Assert.False(RenderSurfaceFocusPolicy.ShouldRestoreFocus(
            Surface,Surface,IntPtr.Zero));
        Assert.False(RenderSurfaceFocusPolicy.ShouldRestoreFocus(
            Surface,Surface,Surface));
    }

    [Fact]
    public void TabTraversal_NeverEntersRenderSurface(){
        Assert.False(RenderSurfaceFocusPolicy.AllowTabInto);
    }
}
