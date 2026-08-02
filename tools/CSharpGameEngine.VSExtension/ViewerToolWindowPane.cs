using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Dockable VS tool window that embeds the glTF viewer via <see cref="ViewerHwndHost"/>
/// (ADR-0007). Contains no rendering/engine logic itself.
/// </summary>
[Guid(PaneGuidString)]
public sealed class ViewerToolWindowPane : ToolWindowPane
{
    public const string PaneGuidString = "18d0443a-6d47-4c9a-9ad2-c878b24dc915";

    public ViewerToolWindowPane() : base(null)
    {
        Caption = "C# Game Engine Viewer";
        Content = new ViewerToolWindowControl();
    }
}
