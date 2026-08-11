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
        Caption="ヒエラルキー";
        Content=new HierarchyToolWindowControl();
    }
}
