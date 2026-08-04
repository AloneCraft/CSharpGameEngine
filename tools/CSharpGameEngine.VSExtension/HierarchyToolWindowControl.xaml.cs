using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// WPF content for <see cref="HierarchyToolWindowPane"/> (ADR-0020): a read-only TreeView of the
/// currently-open scene's nodes, populated/refreshed via <see cref="SceneEditorHub.SceneOpened"/>.
/// Selecting a node publishes a one-directional highlight request to the "シーン" preview via
/// <see cref="SceneEditorHub.PublishSelection"/> - the reverse direction (clicking an object in the
/// Scene view to select it here) is not implemented (out of scope, ADR-0020). Also accepts
/// .glb/.gltf/.fbx files dragged from Solution Explorer (ADR-0022, EditorSpec.md Phase 2), adding
/// each as a new node via <see cref="SceneNodeAdder"/> - the same mechanism
/// <see cref="PrimitiveSpawner"/> uses for shape-palette clicks.
/// </summary>
public partial class HierarchyToolWindowControl : UserControl
{
    public HierarchyToolWindowControl()
    {
        InitializeComponent();

        SceneEditorHub.SceneOpened += OnSceneOpened;

        if (SceneEditorHub.ActiveScene is { } active)
        {
            PopulateTree(active.Scene);
        }
    }

    // SceneEditorHub.SceneOpened is always raised from SceneHwndHost.LoadIntoSurface, itself only
    // ever called from HwndHost.BuildWindowCore/DestroyWindowCore - both always on the UI thread -
    // so no thread marshaling is needed here.
    private void OnSceneOpened(SceneFile scene, string path) => PopulateTree(scene);

    private void PopulateTree(SceneFile scene)
    {
        try
        {
            Tree.ItemsSource = scene.BuildTree();
        }
        catch (InvalidOperationException ex)
        {
            System.Windows.MessageBox.Show(
                $"ヒエラルキーの表示に失敗しました: {ex.Message}",
                "C# Game Engine Hierarchy",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        Guid? nodeId = e.NewValue is SceneFileNodeTreeItem item ? item.Node.Id : null;
        SceneEditorHub.PublishSelection(nodeId);
    }

    private static readonly string[] SupportedDropExtensions = [".glb", ".gltf", ".fbx"];

    private void Tree_DragEnterOrOver(object sender, DragEventArgs e)
    {
        e.Effects = HasSupportedDroppedFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Tree_Drop(object sender, DragEventArgs e)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

        e.Handled = true;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (string path in paths)
        {
            if (!IsSupportedAssetPath(path))
            {
                continue;
            }

            string nameHint = Path.GetFileNameWithoutExtension(path);
            if (!SceneNodeAdder.TryAddAssetNode(path, nameHint, out string? errorMessage))
            {
                System.Windows.MessageBox.Show(
                    errorMessage,
                    "C# Game Engine Hierarchy",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }
        }
    }

    private static bool HasSupportedDroppedFile(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        string[] paths = (string[])data.GetData(DataFormats.FileDrop);
        return paths.Any(IsSupportedAssetPath);
    }

    private static bool IsSupportedAssetPath(string path) =>
        SupportedDropExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
