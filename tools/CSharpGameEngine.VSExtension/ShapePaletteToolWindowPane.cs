using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Dockable VS tool window showing the primitive-shape palette (ADR-0021), Unity's GameObject-
/// creation-shortcuts style. Contains no mesh-generation logic itself - <see cref="ShapePaletteControl"/>
/// drives <see cref="PrimitiveSpawner"/> for the enabled buttons.
/// </summary>
[Guid(PaneGuidString)]
public sealed class ShapePaletteToolWindowPane:ToolWindowPane {
    public const string PaneGuidString="e9476ca8-8e2d-4597-bb05-e0c89c156d5c";

    public ShapePaletteToolWindowPane():base(null){
        Caption="シェイプ";
        Content=new ShapePaletteControl();
    }
}
