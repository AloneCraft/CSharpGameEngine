using System.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class InitialLayoutToolWindowPlanTests {
    [Fact]
    public void ToolWindows_UsesTheExactNonActivatingInitialLayout(){
        IReadOnlyList<InitialLayoutToolWindowSpec> ToolWindows=InitialLayoutToolWindowPlan.ToolWindows;

        // シェイプ パレットは M0020 Phase 0 で撤去したので、初回レイアウトはヒエラルキー1件だけ。
        Assert.Collection(ToolWindows,
            Spec=>{
                Assert.Equal(InitialLayoutToolWindowKind.Hierarchy,Spec.Kind);
                Assert.Equal(300,Spec.Width);
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
