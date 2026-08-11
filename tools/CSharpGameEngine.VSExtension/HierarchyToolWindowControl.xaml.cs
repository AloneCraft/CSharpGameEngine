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

        if (SceneEditorHub.ActiveScene is { } Active)
        {
            PopulateTree(Active.Scene);
        }
    }

    // SceneEditorHub.SceneOpened is always raised from SceneHwndHost.LoadIntoSurface, itself only
    // ever called from HwndHost.BuildWindowCore/DestroyWindowCore - both always on the UI thread -
    // so no thread marshaling is needed here.
    private void OnSceneOpened(SceneFile Scene, string path) => PopulateTree(Scene);

    private void PopulateTree(SceneFile Scene)
    {
        try
        {
            Tree.ItemsSource = Scene.BuildTree();
        }
        catch (InvalidOperationException Ex)
        {
            System.Windows.MessageBox.Show(
                $"ヒエラルキーの表示に失敗しました: {Ex.Message}",
                "C# Game Engine Hierarchy",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void Tree_SelectedItemChanged(object Sender, RoutedPropertyChangedEventArgs<object> E)
    {
        Guid? NodeId = E.NewValue is SceneFileNodeTreeItem Item ? Item.Node.Id : null;
        SceneEditorHub.PublishSelection(NodeId);
    }

    private static readonly string[] SupportedDropExtensions = [".glb", ".gltf", ".fbx"];

    private void Tree_DragEnterOrOver(object Sender, DragEventArgs E)
    {
        E.Effects = HasSupportedDroppedFile(E.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        E.Handled = true;
    }

    private void Tree_Drop(object Sender, DragEventArgs E)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

        E.Handled = true;

        if (!E.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        string[] Paths = (string[])E.Data.GetData(DataFormats.FileDrop);
        foreach (string path in Paths)
        {
            if (!IsSupportedAssetPath(path))
            {
                continue;
            }

            string NameHint = Path.GetFileNameWithoutExtension(path);
            if (!SceneNodeAdder.TryAddAssetNode(path, NameHint, out string? ErrorMessage))
            {
                System.Windows.MessageBox.Show(
                    ErrorMessage,
                    "C# Game Engine Hierarchy",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }
        }
    }

    private static bool HasSupportedDroppedFile(IDataObject Data)
    {
        if (!Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        string[] Paths = (string[])Data.GetData(DataFormats.FileDrop);
        return Paths.Any(IsSupportedAssetPath);
    }

    private static bool IsSupportedAssetPath(string path) =>
        SupportedDropExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
