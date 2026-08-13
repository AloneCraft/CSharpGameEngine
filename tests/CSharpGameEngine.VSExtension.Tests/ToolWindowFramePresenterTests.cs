using System;
using System.Collections.Generic;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class ToolWindowFramePresenterTests {
    [Fact]
    public void ShowWithoutActivation_ShowsThenRestoresFocus(){
        List<string>Calls=[];

        ToolWindowFramePresenter.ShowWithoutActivation(
            ()=>Calls.Add("show-no-activate"),
            ()=>Calls.Add("restore-focus"));

        Assert.Equal(["show-no-activate","restore-focus"],Calls);
    }

    [Fact]
    public void ShowWithoutActivation_AllowsMissingFocusTarget(){
        var ShowCount=0;

        ToolWindowFramePresenter.ShowWithoutActivation(
            ()=>ShowCount++,
            RestoreFocus:null);

        Assert.Equal(1,ShowCount);
    }

    [Fact]
    public void ShowWithoutActivation_DoesNotRestoreFocusWhenShowingFails(){
        var Restored=false;

        Assert.Throws<InvalidOperationException>(()=>
            ToolWindowFramePresenter.ShowWithoutActivation(
                ()=>throw new InvalidOperationException("show failed"),
                ()=>Restored=true));

        Assert.False(Restored);
    }

    [Fact]
    public void ShowWithoutActivation_RejectsMissingShowAction(){
        Assert.Throws<ArgumentNullException>(()=>
            ToolWindowFramePresenter.ShowWithoutActivation(null!,null));
    }
}
