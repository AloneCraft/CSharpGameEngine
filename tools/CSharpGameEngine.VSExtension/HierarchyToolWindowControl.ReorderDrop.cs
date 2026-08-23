using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

public partial class HierarchyToolWindowControl {
    /// <summary>
    /// ノード並べ替えドラッグ中、<see cref="HierarchyInsertionPolicy.Resolve"/>が返した挿入位置を
    /// 対象<see cref="TreeViewItem"/>の実座標に重ねて表示する。<c>ComponentDropFeedback</c>と同じ
    /// 「単一オーバーレイをコードから位置決めする」作法(<see cref="HierarchyToolWindowControl.ComponentDrop"/>参照)。
    /// </summary>
    private void UpdateReorderDropFeedback(TreeViewItem Container,HierarchyInsertionKind Kind){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Origin=Container.TranslatePoint(new Point(0,0),this.Tree);
        var Height=Container.ActualHeight;

        if(Kind==HierarchyInsertionKind.Into){
            this.ReorderInsertionLine.Visibility=Visibility.Collapsed;
            this.ReorderIntoHighlight.Margin=new Thickness(0,Origin.Y,0,0);
            this.ReorderIntoHighlight.Height=Height;
            this.ReorderIntoHighlight.Visibility=Visibility.Visible;
            return;
        }

        this.ReorderIntoHighlight.Visibility=Visibility.Collapsed;
        var LineY=Kind==HierarchyInsertionKind.Before?Origin.Y:Origin.Y+Height;
        this.ReorderInsertionLine.Margin=new Thickness(0,LineY,0,0);
        this.ReorderInsertionLine.Visibility=Visibility.Visible;
    }

    private void HideReorderDropIndicator(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.ReorderInsertionLine.Visibility=Visibility.Collapsed;
        this.ReorderIntoHighlight.Visibility=Visibility.Collapsed;
    }
}
