using System.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class InitialLayoutToolWindowPlanTests {
    [Fact]
    public void ToolWindows_UsesTheExactNonActivatingInitialLayout(){
        IReadOnlyList<InitialLayoutToolWindowSpec> ToolWindows=InitialLayoutToolWindowPlan.ToolWindows;

        Assert.Collection(ToolWindows,
            Spec=>{
                Assert.Equal(InitialLayoutToolWindowKind.Hierarchy,Spec.Kind);
                Assert.Equal(300,Spec.Width);
                Assert.Equal(ToolWindowPresentation.NonActivating,Spec.Presentation);
            },
            Spec=>{
                Assert.Equal(InitialLayoutToolWindowKind.ShapePalette,Spec.Kind);
                Assert.Equal(260,Spec.Width);
                Assert.Equal(ToolWindowPresentation.NonActivating,Spec.Presentation);
            });
    }

    [Fact]
    public void ToolWindows_DoesNotOpenSceneDesignerWithoutAnExplicitSceneSelection(){
        Assert.DoesNotContain(
            InitialLayoutToolWindowPlan.ToolWindows,
            Spec=>Spec.Kind==InitialLayoutToolWindowKind.SceneDesigner);
    }
}
