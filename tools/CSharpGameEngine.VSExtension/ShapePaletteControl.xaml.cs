using System.Windows;
using System.Windows.Controls;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// WPF content for <see cref="ShapePaletteToolWindowPane"/> (ADR-0021): a fixed palette of
/// primitive-shape buttons. Enabled buttons call <see cref="PrimitiveSpawner.SpawnAsync"/>; the rest
/// are disabled placeholders for interactive-gizmo-driven tools out of today's scope.
/// </summary>
public partial class ShapePaletteControl : UserControl
{
    public ShapePaletteControl()
    {
        InitializeComponent();
    }

    private void PrimitiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string primitiveKind })
        {
            return;
        }

        _ = PrimitiveSpawner.SpawnAsync(primitiveKind);
    }
}
