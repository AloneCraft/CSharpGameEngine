using System.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class InitialLayoutToolWindowPlanTests {
    [Fact]
    public void ToolWindows_UsesTheExactNonActivatingInitialLayout(){
        var ToolWindows=InitialLayoutToolWindowPlan.ToolWindows;

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
            },
            Spec=>{
                Assert.Equal(InitialLayoutToolWindowKind.ComponentPalette,Spec.Kind);
                Assert.Equal(300,Spec.Width);
                Assert.Equal(ToolWindowPresentation.NonActivating,Spec.Presentation);
            });
    }

    /// <summary>
    /// 初期レイアウトはどれもフォーカスを奪ってはいけない。M0046でHierarchyがコードエディタから
    /// フォーカスを奪う不具合を出しているので、新しく足したパレットも同じ規律に載っていることを固定する。
    /// </summary>
    [Fact]
    public void ToolWindows_NeverTakeTheKeyboardFocus(){
        Assert.All(
            InitialLayoutToolWindowPlan.ToolWindows,
            static Spec=>Assert.Equal(ToolWindowPresentation.NonActivating,Spec.Presentation));
    }

    /// <summary>
    /// 同じCommandSet内でIDが衝突すると、片方のメニュー項目が黙って効かなくなる。
    /// 退役済みID(0x0101/0x0107/0x0108)を再利用していないことも併せて守る。
    /// </summary>
    [Fact]
    public void ToolWindowCommandIds_AreUniqueAndAvoidRetiredIds(){
        int[]Ids=[
            ShowHierarchyToolWindowCommand.CommandId,
            ShowInspectorToolWindowCommand.CommandId,
            ShowComponentPaletteToolWindowCommand.CommandId,
            ApplyGameEngineLayoutCommand.CommandId,
            OpenSceneDesignerCommand.CommandId,
        ];

        Assert.Equal(Ids.Length,Ids.Distinct().Count());
        Assert.DoesNotContain(0x0101,Ids);
        Assert.DoesNotContain(0x0107,Ids);
        Assert.DoesNotContain(0x0108,Ids);

        // Scene一覧のスロットとも重ならないこと。
        Assert.DoesNotContain(Ids,static Id=>Id>=OpenSceneDesignerCommand.SceneItemCommandIdFirst);
    }

    [Fact]
    public void ToolWindows_DoesNotOpenSceneDesignerWithoutAnExplicitSceneSelection(){
        Assert.DoesNotContain(
            InitialLayoutToolWindowPlan.ToolWindows,
            Spec=>Spec.Kind==InitialLayoutToolWindowKind.SceneDesigner);
    }
}
