using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Dockable VS tool window showing the currently-open scene's node tree (ADR-0020), Unity Hierarchy-
/// style. Contains no scene-loading logic itself - <see cref="HierarchyToolWindowControl"/> reacts to
/// <see cref="SceneEditorHub"/> events.
/// </summary>
[Guid(PaneGuidString)]
public sealed class HierarchyToolWindowPane:ToolWindowPane {
    public const string PaneGuidString="21310c74-a151-49af-a47b-67c543d80eb9";

    public HierarchyToolWindowPane():base(null){
        this.Caption=VsixText.Get("PaneHierarchy");
        this.Content=new HierarchyToolWindowControl();
    }

    protected override bool PreProcessMessage(
        ref System.Windows.Forms.Message Message){
        ThreadHelper.ThrowIfNotOnUIThread();
        // ToolWindowPane.TranslateAccelerator is the first Visual Studio-owned keyboard
        // boundary. Escape can be consumed by shell command routing before WPF raises a
        // KeyEvent, so handle it here when this pane owns the accelerator.
        if(SceneEditorEscapeRoutingPolicy.IsVisualStudioEscapeKeyDown(
                Message.Msg,Message.WParam.ToInt64())
            &&this.Content is HierarchyToolWindowControl Hierarchy){
            Hierarchy.HandleVisualStudioEscape();
            return true;
        }
        return base.PreProcessMessage(ref Message);
    }
}
