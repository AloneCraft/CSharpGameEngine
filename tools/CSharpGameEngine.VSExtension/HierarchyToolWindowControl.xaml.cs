using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// WPF content for <see cref="HierarchyToolWindowPane"/>: a read-only TreeView of the scene declared
/// by the active C# document, refreshed from live Roslyn via
/// <see cref="SceneEditorHub.SceneModelChanged"/> (ADR-0040).
///
/// <para>
/// The tree is rebuilt on every analysis, which happens while the user is typing, so expansion state
/// is carried across rebuilds keyed by <see cref="NodePath"/>. Without that the tree would collapse
/// itself on each keystroke. Only the exceptions are tracked - a node is expanded unless the user
/// collapsed it.
/// </para>
///
/// <para>
/// Selecting a node publishes <see cref="SceneEditorHub.PublishSelectedNodePath"/>. The 3D Scene view
/// still renders from the .scene file and listens on the older Guid-keyed channel; it moves onto this
/// one when it is switched over to SceneModel. Drag and drop still writes to .scene for the same
/// reason - it becomes a C# source insertion in the same step.
/// </para>
/// </summary>
public partial class HierarchyToolWindowControl:UserControl {
    private readonly HashSet<NodePath>CollapsedPaths=[];
    private bool SuppressSelectionPublish;

    public HierarchyToolWindowControl(){
        InitializeComponent();

        SceneEditorHub.SceneModelChanged+=this.OnSceneModelChanged;
        this.Populate(SceneEditorHub.ActiveSceneModel);
    }

    // SceneWorkspaceService marshals to the UI thread before raising this, so no further marshaling
    // is needed here.
    private void OnSceneModelChanged(SceneModel?Model)=>this.Populate(Model);

    private void Populate(SceneModel?Model){
        this.CaptureCollapsedPaths();

        IReadOnlyList<SceneNodeViewModel>Roots=SceneNodeViewModel.Build(Model,this.CollapsedPaths,SceneEditorHub.SelectedNodePath);

        // Rebinding raises SelectedItemChanged for the restored selection; republishing it would
        // bounce a redundant selection change back through the hub on every keystroke.
        this.SuppressSelectionPublish=true;

        try {
            this.Tree.ItemsSource=Roots;
        } finally {
            this.SuppressSelectionPublish=false;
        }

        this.EmptyMessage.Visibility=Roots.Count==0?Visibility.Visible:Visibility.Collapsed;
    }

    /// <summary>
    /// Reads the user's current expand/collapse state out of the live tree before it is replaced.
    /// Paths that vanished from the scene are dropped so the set cannot grow without bound while a
    /// scene is being edited.
    /// </summary>
    private void CaptureCollapsedPaths(){
        if(this.Tree.ItemsSource is not IReadOnlyList<SceneNodeViewModel>Existing){
            return;
        }

        this.CollapsedPaths.Clear();

        foreach(SceneNodeViewModel Node in Existing.SelectMany(static Root=>Root.SelfAndDescendants())){
            if(!Node.IsExpanded){
                this.CollapsedPaths.Add(Node.Path);
            }
        }
    }

    private void Tree_SelectedItemChanged(object Sender,RoutedPropertyChangedEventArgs<object>E){
        if(this.SuppressSelectionPublish){
            return;
        }

        NodePath?Path=E.NewValue is SceneNodeViewModel Item?Item.Path:null;
        SceneEditorHub.PublishSelectedNodePath(Path);
    }

    private static readonly string[]SupportedDropExtensions=[".glb",".gltf",".fbx"];

    private void Tree_DragEnterOrOver(object Sender,DragEventArgs E){
        E.Effects=HasSupportedDroppedFile(E.Data)?DragDropEffects.Copy:DragDropEffects.None;
        E.Handled=true;
    }

    private void Tree_Drop(object Sender,DragEventArgs E){
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

        E.Handled=true;

        if(!E.Data.GetDataPresent(DataFormats.FileDrop)){
            return;
        }

        var Paths=(string[])E.Data.GetData(DataFormats.FileDrop);
        foreach(var path in Paths){
            if(!IsSupportedAssetPath(path)){
                continue;
            }

            var NameHint=Path.GetFileNameWithoutExtension(path);
            if(!SceneNodeAdder.TryAddAssetNode(path,NameHint,out var ErrorMessage)){
                System.Windows.MessageBox.Show(
                    ErrorMessage,
                    "C# Game Engine Hierarchy",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }
        }
    }

    private static bool HasSupportedDroppedFile(IDataObject Data){
        if(!Data.GetDataPresent(DataFormats.FileDrop)){
            return false;
        }

        var Paths=(string[])Data.GetData(DataFormats.FileDrop);
        return Paths.Any(IsSupportedAssetPath);
    }

    private static bool IsSupportedAssetPath(string path)=>
        SupportedDropExtensions.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
}
