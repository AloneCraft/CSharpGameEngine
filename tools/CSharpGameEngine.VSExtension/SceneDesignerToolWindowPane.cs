using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Dockable VS tool window showing the 3D preview of one <c>IScene</c>-implementing class (ADR-0040
/// Phase 9), opened via <see cref="OpenSceneDesignerCommand"/>'s code-editor context menu entry
/// rather than a document tab. Single instance, like <see cref="HierarchyToolWindowPane"/> - retargeted
/// in place via <see cref="SetTarget"/> rather than opening a new window per scene.
/// </summary>
[Guid(PaneGuidString)]
public sealed class SceneDesignerToolWindowPane:ToolWindowPane {
    public const string PaneGuidString="6f2c6a0e-6e8a-4a7a-9a3a-3b6f2b8d7c4a";

    private readonly SceneDesignerToolWindowControl Control;

    public SceneDesignerToolWindowPane():base(null){
        Caption="シーン デザイナー";
        Control=new SceneDesignerToolWindowControl();
        Content=Control;
    }

    public void SetTarget(string FilePath,string ClassName)=>Control.SetTarget(FilePath,ClassName);
}
