using System.Windows.Controls;

namespace CSharpGameEngine.VSExtension;

/// <summary>WPF content for <see cref="SceneWindowPane"/> (ADR-0020) - hosts one <see cref="SceneHwndHost"/> pointed at the opened scene file.</summary>
public partial class SceneWindowControl : UserControl
{
    private readonly SceneHwndHost _host;

    public SceneWindowControl(string scenePath)
    {
        InitializeComponent();
        _host = new SceneHwndHost(scenePath);
        RootGrid.Children.Add(_host);
    }

    public void SetScenePath(string scenePath) => _host.SetScenePath(scenePath);
}
