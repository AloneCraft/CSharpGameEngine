using System;
using System.Windows.Controls;

namespace CSharpGameEngine.VSExtension;

/// <summary>WPF content for <see cref="SceneDesignerToolWindowPane"/> (ADR-0040 Phase 9) - hosts one <see cref="SceneHwndHost"/>, retargeted via <see cref="SetTarget"/> each time the pane is asked to show a different class.</summary>
public partial class SceneDesignerToolWindowControl:UserControl,IDisposable {
    private readonly SceneHwndHost Host;

    public SceneDesignerToolWindowControl(){
        InitializeComponent();
        Host=new SceneHwndHost();
        RootGrid.Children.Add(Host);
    }

    public void SetTarget(string FilePath,string ClassName)=>Host.SetTarget(FilePath,ClassName);

    // WindowPane/ToolWindowPane only disposes Content when it implements IDisposable - without this,
    // the native window inside SceneHwndHost never runs DestroyWindowCore when the tool window closes
    // (same fix as the old SceneWindowControl, see RenderSurfaceWindow.Dispose).
    public void Dispose()=>Host.Dispose();
}
