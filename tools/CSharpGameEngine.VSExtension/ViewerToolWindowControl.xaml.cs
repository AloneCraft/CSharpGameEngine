using System.Windows.Controls;

namespace CSharpGameEngine.VSExtension;

/// <summary>WPF content for <see cref="ViewerToolWindowPane"/> - just hosts one <see cref="ViewerHwndHost"/>.</summary>
public partial class ViewerToolWindowControl : UserControl
{
    public ViewerToolWindowControl()
    {
        InitializeComponent();
    }
}
