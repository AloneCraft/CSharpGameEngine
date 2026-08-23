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

        // Help is deliberately used instead of the standard prohibited cursor: paired with this
        // visible message it communicates that the target is valid but already owns the component.
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
