using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
    private bool PublishingHierarchySelection;
    private HierarchyNodeSelection CurrentSelection=HierarchyNodeSelection.Empty;

    public HierarchyToolWindowControl(){
        InitializeComponent();

        SceneEditorHub.SceneModelChanged+=this.OnSceneModelChanged;
        SceneEditorHub.SelectedNodePathChanged+=this.OnSelectedNodePathChanged;
        this.Populate(SceneEditorHub.ActiveSceneModel);
    }

    // SceneWorkspaceService marshals to the UI thread before raising this, so no further marshaling
    // is needed here.
    private void OnSceneModelChanged(SceneModel?Model)=>this.Populate(Model);

    /// <summary>
    /// M0020 Phase 2: ビューポートでの選択(や将来の他origin)がここへ届く。<see cref="Populate"/>と
    /// 違いツリーは再構築せず、既存の<see cref="SceneNodeViewModel"/>群の<c>IsSelected</c>だけを
    /// 更新する(再解析のたびの木の再構築を待たずに即座に反映するため)。<c>IsSelected</c>は
    /// <c>TreeViewItem.IsSelected</c>へTwoWayバインドされているので<see cref="Tree_SelectedItemChanged"/>
    /// も発火するが、既存の<see cref="SuppressSelectionPublish"/>ガードで再Publishを止める
    /// (元々「再構築後の選択復元」用に存在するガードをそのまま流用する)。
    /// </summary>
    private void OnSelectedNodePathChanged(NodePath?Path){
        if(this.PublishingHierarchySelection){
            return;
        }

        this.CurrentSelection=Path is { } Selected
            ?new HierarchyNodeSelection([Selected],Selected,Selected)
            :HierarchyNodeSelection.Empty;
        this.ApplySelectionToTree();
    }

    private void Populate(SceneModel?Model){
        this.CaptureCollapsedPaths();

        IReadOnlyList<SceneNodeViewModel>Roots=SceneNodeViewModel.BuildWithSelection(Model,this.CollapsedPaths,this.CurrentSelection.Paths);

        // Rebinding raises SelectedItemChanged for the restored selection; republishing it would
        // bounce a redundant selection change back through the hub on every keystroke.
        this.SuppressSelectionPublish=true;

        try {
            this.Tree.ItemsSource=Roots;
            this.ApplySelectionToTree();
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

    private void Tree_SelectedItemChanged(object Sender,RoutedPropertyChangedEventArgs<object>Args){
        if(this.SuppressSelectionPublish||Args.NewValue is not SceneNodeViewModel Item){
            return;
        }

        this.Tree.Focus();
        this.CurrentSelection=HierarchySelectionPlanner.Select(
            this.CurrentSelection,this.OrderedPaths(),Item.Path,ControlPressed:false,ShiftPressed:false);
        this.PublishCurrentSelection();
    }

    private void Tree_PreviewMouseLeftButtonDown(object Sender,MouseButtonEventArgs Args){
        // The explicit expand/collapse ToggleButton is not a row selection gesture. In
        // particular, leave this preview event unhandled so WPF can toggle IsExpanded without
        // replacing a Ctrl/Shift multi-selection or publishing a source-sync selection.
        if(IsExpanderClick(Args.OriginalSource as DependencyObject)){
            return;
        }

        if(this.FindNode(Args.OriginalSource as DependencyObject) is not SceneNodeViewModel Item){
            return;
        }

        this.Tree.Focus();
        this.CurrentSelection=HierarchySelectionPlanner.Select(
            this.CurrentSelection,this.OrderedPaths(),Item.Path,
            (Keyboard.Modifiers&ModifierKeys.Control)!=0,(Keyboard.Modifiers&ModifierKeys.Shift)!=0);
        this.PublishCurrentSelection();
        Args.Handled=true;
        if(Args.ClickCount==2){
            SceneEditorHub.RequestHierarchyNavigation(Item.Path);
        }
    }

    private void Tree_PreviewKeyDown(object Sender,KeyEventArgs Args){
        if(Args.Key!=Key.Enter||this.CurrentSelection.PrimaryPath is not { } Primary){
            return;
        }

        SceneEditorHub.RequestHierarchyNavigation(Primary);
        Args.Handled=true;
    }

    private void PublishCurrentSelection(){
        this.ApplySelectionToTree();
        this.PublishingHierarchySelection=true;
        try {
            SceneEditorHub.PublishHierarchySelection(this.CurrentSelection);
        } finally {
            this.PublishingHierarchySelection=false;
        }
    }

    private void ApplySelectionToTree(){
        if(this.Tree.ItemsSource is not IReadOnlyList<SceneNodeViewModel>Roots){
            return;
        }

        this.SuppressSelectionPublish=true;
        try {
            foreach(SceneNodeViewModel Node in Roots.SelectMany(static Root=>Root.SelfAndDescendants())){
                Node.IsMultiSelected=this.CurrentSelection.Paths.Contains(Node.Path);
                Node.IsSelected=this.CurrentSelection.PrimaryPath is { } Primary&&Primary.Equals(Node.Path);
            }
        } finally {
            this.SuppressSelectionPublish=false;
        }
    }

    private IReadOnlyList<NodePath> OrderedPaths(){
        return this.Tree.ItemsSource is not IReadOnlyList<SceneNodeViewModel>Roots
            ?[]
            :Roots.SelectMany(static Root=>Root.SelfAndDescendants()).Select(static Node=>Node.Path).ToArray();
    }

    private static bool IsExpanderClick(DependencyObject?Element){
        for(DependencyObject?Current=Element;Current is not null;Current=VisualTreeHelper.GetParent(Current)){
            if(Current is ToggleButton { Name:"ExpanderToggle" }){
                return true;
            }

            if(Current is TreeView){
                break;
            }
        }

        return false;
    }
    private SceneNodeViewModel?FindNode(DependencyObject?Element){
        for(DependencyObject?Current=Element;Current is not null;Current=VisualTreeHelper.GetParent(Current)){
            if(Current is TreeViewItem { DataContext:SceneNodeViewModel Node }){
                return Node;
            }
        }

        return null;
    }
}
