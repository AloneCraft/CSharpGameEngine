using System.Windows.Controls;

namespace CSharpGameEngine.VSExtension;

/// <summary>WPF content for <see cref="AssetPreviewWindowPane"/> (ADR-0013) - hosts one <see cref="AssetPreviewHwndHost"/> pointed at the opened file.</summary>
public partial class AssetPreviewControl : UserControl
{
    private readonly AssetPreviewHwndHost _host;

    public AssetPreviewControl(string modelPath)
    {
        InitializeComponent();
        _host = new AssetPreviewHwndHost(modelPath);
        RootGrid.Children.Add(_host);
    }

    public void SetModelPath(string modelPath) => _host.SetModelPath(modelPath);
}
