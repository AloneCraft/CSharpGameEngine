using System;
using System.Windows.Controls;

namespace CSharpGameEngine.VSExtension;

/// <summary>WPF content for <see cref="SceneWindowPane"/> (ADR-0020) - hosts one <see cref="SceneHwndHost"/> pointed at the opened scene file.</summary>
public partial class SceneWindowControl:UserControl,IDisposable {
    private readonly SceneHwndHost Host;

    public SceneWindowControl(string ScenePath){
        InitializeComponent();
        Host=new SceneHwndHost(ScenePath);
        RootGrid.Children.Add(Host);
    }

    public void SetScenePath(string ScenePath)=>Host.SetScenePath(ScenePath);

    // WindowPane only disposes Content when it implements IDisposable - without this, the native
    // window inside SceneHwndHost never runs DestroyWindowCore when the "シーン" tab closes, so the
    // focus-release fix there (see RenderSurfaceWindow.Dispose) never fires either.
    public void Dispose()=>Host.Dispose();
}
