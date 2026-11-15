using System.Windows;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

public partial class HierarchyToolWindowControl {
    private void UpdateComponentDropFeedback(
        ComponentDropVerdict Verdict,ComponentDragPayload Payload){
        string Message=ComponentDropPolicy.Describe(Verdict,Payload.DisplayName);
        ThreadHelper.ThrowIfNotOnUIThread();
        bool AlreadyExists=Verdict==ComponentDropVerdict.RejectDuplicate;
        this.Tree.Cursor=AlreadyExists?Cursors.Help:null;
        this.Tree.ToolTip=AlreadyExists?Message:null;
        if(!AlreadyExists){
            this.ComponentDropFeedback.Visibility=Visibility.Collapsed;
            return;
        }

        // 禁止カーソルではなく Help を使います。表示されるこのメッセージと組み合わせることで、
        // 対象は有効だが、すでにコンポーネントを所有していることを伝えます。
        this.ComponentDropFeedbackText.Text=Message;
        this.ComponentDropFeedback.Visibility=Visibility.Visible;
        SetStatusBarText(Message);
    }

    private void Tree_DragLeave(object Sender,DragEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Tree.Cursor=null;
        this.Tree.ToolTip=null;
        this.ComponentDropFeedback.Visibility=Visibility.Collapsed;
        this.HideReorderDropIndicator();
    }
}
