using System.Collections.Generic;
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
/// Selecting a node publishes <see cref="SceneEditorHub.PublishSelectedNodePath"/>, which the Scene
/// Designer turns into a viewport highlight. Both sides now read the same live-Roslyn
/// <see cref="SceneModel"/> (ADR-0040 Phase 9). Drag-and-drop asset placement used to write to a
/// <c>.scene</c> file and was removed in M0020 Phase 0 together with that dead write path; it returns
/// as a C# source insertion (<c>SceneEditOperation</c>) in a later milestone.
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
}
