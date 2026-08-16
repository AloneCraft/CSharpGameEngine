using System.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class InitialLayoutToolWindowPlanTests {
    [Fact]
    public void ToolWindows_UsesTheExactNonActivatingInitialLayout(){
        IReadOnlyList<InitialLayoutToolWindowSpec> ToolWindows=InitialLayoutToolWindowPlan.ToolWindows;

        // シェイプ パレットは M0020 Phase 0 で撤去した。M0031でInspectorをHierarchyの隣へ追加した
        // (どちらもD3D12/HWNDコストが無く、空選択状態を安全に見せられるため)。
        Assert.Collection(ToolWindows,
            Spec=>{
                Assert.Equal(InitialLayoutToolWindowKind.Hierarchy,Spec.Kind);
                Assert.Equal(300,Spec.Width);
                Assert.Equal(ToolWindowPresentation.NonActivating,Spec.Presentation);
            },
            Spec=>{
                Assert.Equal(InitialLayoutToolWindowKind.Inspector,Spec.Kind);
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
