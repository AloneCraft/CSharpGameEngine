using System;
using System.Windows.Controls;

namespace CSharpGameEngine.VSExtension;

/// <summary>WPF content for <see cref="SceneWindowPane"/> (ADR-0020) - hosts one <see cref="SceneHwndHost"/> pointed at the opened scene file.</summary>
public partial class SceneWindowControl : UserControl, IDisposable
{
    private readonly SceneHwndHost _host;

    public SceneWindowControl(string scenePath)
    {
        InitializeComponent();
        _host = new SceneHwndHost(scenePath);
        RootGrid.Children.Add(_host);
    }

    public void SetScenePath(string scenePath) => _host.SetScenePath(scenePath);

    // WindowPane only disposes Content when it implements IDisposable - without this, the native
    // window inside SceneHwndHost never runs DestroyWindowCore when the "シーン" tab closes, so the
    // focus-release fix there (see RenderSurfaceWindow.Dispose) never fires either.
    public void Dispose() => _host.Dispose();
}
