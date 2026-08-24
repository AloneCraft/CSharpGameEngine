using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// WPF content for <see cref="HierarchyToolWindowPane"/>: a themed TreeView of the scene declared
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
/// <see cref="SceneModel"/> (ADR-0040 Phase 9). ContextMenu/F2/Delete and drag-and-drop request
/// structural C# source edits through <c>SceneEditOperation</c>; source focus is never changed by
/// those requests.
/// </para>
/// </summary>
public partial class HierarchyToolWindowControl:UserControl {
    private readonly HashSet<NodePath>CollapsedPaths=[];
    private bool SuppressSelectionPublish;
    private bool PublishingHierarchySelection;
    private HierarchyNodeSelection CurrentSelection=HierarchyNodeSelection.Empty;
    private SceneNodeViewModel? DragNode;
    private Point DragStart;

    /// <summary>
    /// M0051 Step 1-7: 直前に<see cref="Populate"/>した内容の署名。一致するなら
    /// <c>ItemsSource</c>を差し替えない(<see cref="HierarchyPopulatePolicy"/>)。
    /// </summary>
    private string LastPopulatedSignature=HierarchyContentSignature.Compute(null);

    /// <summary>
    /// M0051 Step 1-7: インラインリネーム中に届いたModel。<b>保留した内容そのものは使わない</b> —
    /// 解除時に<see cref="SceneEditorHub.ActiveSceneModel"/>の最新を取り直す。保留したModelを流すと、
    /// リネーム確定で作られた新しいDocumentモデルを古い内容で上書きしてしまう
    /// (Step 1-4で直した「編集が消えたように見える」症状の再来)。
    /// ここではあくまで「保留が起きた」ことだけを覚える。
    /// </summary>
    private bool HasDeferredPopulate;

    public HierarchyToolWindowControl(){
        this.InitializeComponent();

        SceneEditorHub.SceneModelChanged+=this.OnSceneModelChanged;
        SceneEditorHub.SelectedNodePathChanged+=this.OnSelectedNodePathChanged;

        // M0051 Step 1-4: Ctrl+Sの検知はSceneHwndHostのポーリング1本しか無く、以前はビューポートの
        // フォーカスだけを見ていたため、Hierarchyで編集した直後のCtrl+Sが保存へ届かなかった。
        // 自分のフォーカス状態をHubへ報告して、保存の門番(SceneSaveTriggerPolicy)から見えるようにする。
        this.IsKeyboardFocusWithinChanged+=this.OnKeyboardFocusWithinChanged;
        this.Unloaded+=this.OnUnloadedForFocus;

        this.Populate(SceneEditorHub.ActiveSceneModel);
    }

    private const string FocusSurfaceName="Hierarchy";

    private void OnKeyboardFocusWithinChanged(object Sender,DependencyPropertyChangedEventArgs Args)=>
        SceneEditorHub.SetSurfaceFocus(FocusSurfaceName,this.IsKeyboardFocusWithin);

    private void OnUnloadedForFocus(object Sender,RoutedEventArgs Args)=>
        SceneEditorHub.SetSurfaceFocus(FocusSurfaceName,false);

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
        var Signature=HierarchyContentSignature.Compute(Model);
        var RenamingNode=this.TryFindRenamingNode();
        switch(HierarchyPopulatePolicy.Decide(
            IsInlineRenaming:RenamingNode is not null,
            ContentUnchanged:Signature==this.LastPopulatedSignature)){
            case HierarchyPopulateAction.Skip:
                return;
            case HierarchyPopulateAction.Defer:
                // 木を作り直すとリネーム中のViewModelごと破棄され、Enterを受けるTextBoxが消える
                // (実機報告「リネームが確定しない」の実体)。対象ノードがもう無いなら確定しても
                // TryGetIdが失敗するので、その場で取り消してから反映する。
                if(!HierarchyPopulatePolicy.ShouldCancelRenameOnRelease(
                    RenamedPathStillExists:ContainsPath(Model,RenamingNode!.Path))){
                    this.HasDeferredPopulate=true;
                    return;
                }

                RenamingNode!.CancelRename();
                break;
        }

        this.LastPopulatedSignature=Signature;
        this.CaptureCollapsedPaths();

        var Roots=SceneNodeViewModel.BuildWithSelection(Model,this.CollapsedPaths,this.CurrentSelection.Paths);

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

        foreach(var Node in Existing.SelectMany(static Root=>Root.SelfAndDescendants())){
            if(!Node.IsExpanded){
                this.CollapsedPaths.Add(Node.Path);
            }
        }
    }

    /// <summary>
    /// M0046: ここは矢印キーでのツリー内移動の受け口だが、再解析で<c>ItemsSource</c>を差し替えた
    /// 後のコンテナ遅延生成でも発火する。かつては無条件に<c>Tree.Focus()</c>を呼んでいたため、
    /// ユーザーがコードエディタで入力を止めて再解析が着地するたびにHierarchyがフォーカスを奪い、
    /// エディタのUndo履歴・Ctrl+Zが実質使えなくなっていた。フォーカスは奪わず、
    /// <b>既に持っていること</b>を発行の条件にする(<see cref="HierarchyFocusPolicy"/>)。
    /// </summary>
    private void Tree_SelectedItemChanged(object Sender,RoutedPropertyChangedEventArgs<object>Args){
        if(Args.NewValue is not SceneNodeViewModel Item
            ||!HierarchyFocusPolicy.ShouldPublishSelectionChange(
                this.SuppressSelectionPublish,this.Tree.IsKeyboardFocusWithin)){
            return;
        }

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
        this.DragNode=Item;
        this.DragStart=Args.GetPosition(this.Tree);
        this.PublishCurrentSelection();
        Args.Handled=true;
        if(Args.ClickCount==2){
            SceneEditorHub.RequestHierarchyNavigation(Item.Path);
        }
    }

    private void Tree_PreviewKeyDown(object Sender,KeyEventArgs Args){
        if(Args.OriginalSource is TextBox){
            return;
        }

        if(Args.Key==Key.F2&&this.CurrentSelection.PrimaryPath is { } RenamePath
            &&this.FindViewModel(RenamePath) is { } RenameNode){
            RenameNode.BeginRename();
            Args.Handled=true;
            this.UpdateLayout();
            this.FocusRenameBox(RenameNode);
            return;
        }

        if(Args.Key==Key.Delete&&this.CurrentSelection.Paths.Count>0){
            this.RequestRemove();
            Args.Handled=true;
            return;
        }

        if(Args.Key!=Key.Enter||this.CurrentSelection.PrimaryPath is not { } Primary){
            return;
        }

        SceneEditorHub.RequestHierarchyNavigation(Primary);
        Args.Handled=true;
    }

    private void Tree_PreviewMouseRightButtonDown(object Sender,MouseButtonEventArgs Args){
        if(this.FindNode(Args.OriginalSource as DependencyObject) is not { } Item){
            return;
        }

        if(!this.CurrentSelection.Paths.Contains(Item.Path)){
            this.Tree.Focus();
            this.CurrentSelection=new HierarchyNodeSelection([Item.Path],Item.Path,Item.Path);
            this.PublishCurrentSelection();
        }

        ContextMenu Menu=new();
        Menu.Items.Add(this.CreateMenuItem(VsixText.Get("HierarchyAddChild"),(_,_)=>SceneEditorHub.RequestHierarchyEdit(
            new HierarchyEditRequest(HierarchyEditKind.AddChild,this.CurrentSelection.Paths))));
        Menu.Items.Add(this.CreateMenuItem(VsixText.Get("HierarchyNormalizeComponents"),(_,_)=>SceneEditorHub.RequestHierarchyEdit(
            new HierarchyEditRequest(HierarchyEditKind.NormalizeComponents,this.CurrentSelection.Paths))));
        Menu.Items.Add(this.CreateMenuItem(VsixText.Get("HierarchyRename"),(_,_)=>{
            Item.BeginRename();
            this.UpdateLayout();
            this.FocusRenameBox(Item);
        }));
        Menu.Items.Add(new Separator());
        Menu.Items.Add(this.CreateMenuItem(VsixText.Get("HierarchyRemove"),(_,_)=>this.RequestRemove()));
        Menu.PlacementTarget=this.Tree;
        Menu.IsOpen=true;
        Args.Handled=true;
    }

    private MenuItem CreateMenuItem(string Header,RoutedEventHandler Handler){
        MenuItem Item=new(){Header=Header};
        Item.Click+=Handler;
        return Item;
    }

    private void RequestRemove(){
        IReadOnlyList<NodePath>Paths=this.CurrentSelection.Paths.Where(static Path=>!Path.IsRoot).ToArray();
        if(Paths.Count==0){
            return;
        }

        var HasChildren=Paths.Select(this.FindViewModel).Any(Node=>Node is { HasChildren:true });
        if(HasChildren
            &&MessageBox.Show(VsixText.Get("HierarchyRemoveSelectionWithChildrenPrompt"),
                VsixText.Get("HierarchyRemoveTitle"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes){
            return;
        }

        SceneEditorHub.RequestHierarchyEdit(new HierarchyEditRequest(HierarchyEditKind.Remove,Paths));
    }

    private void RenameBox_KeyDown(object Sender,KeyEventArgs Args){
        if(this.FindNode(Args.OriginalSource as DependencyObject) is not { } Node){
            return;
        }

        if(Args.Key==Key.Enter){
            this.CommitRename(Node);
            Args.Handled=true;
        } else if(Args.Key==Key.Escape){
            Node.CancelRename();
            this.ReleaseDeferredPopulate();
            Args.Handled=true;
        }
    }

    private void RenameBox_LostFocus(object Sender,RoutedEventArgs Args){
        if(this.FindNode(Args.OriginalSource as DependencyObject) is { IsRenaming:true } Node){
            this.CommitRename(Node);
        }
    }

    private void CommitRename(SceneNodeViewModel Node){
        var Name=Node.EditName.Trim();
        Node.CancelRename();
        if(Name.Length!=0&&!Name.Equals(Node.DisplayName,StringComparison.Ordinal)){
            SceneEditorHub.RequestHierarchyEdit(
                new HierarchyEditRequest(HierarchyEditKind.Rename,[Node.Path],NewName:Name));
        }

        // M0051 Step 1-7: 保留の解除は必ずここで、RequestHierarchyEditの<b>あと</b>に行う。
        // CommitRenameは先頭でCancelRenameを呼ぶので、順序を誤ると編集要求より前に解除が走り、
        // 古いモデルでツリーを作り直してしまう。
        this.ReleaseDeferredPopulate();
    }

    /// <summary>
    /// M0051 Step 1-7: 保留していたツリー再構築を解除する。流し込むのは保留時のModelではなく
    /// <b>そのとき<see cref="SceneEditorHub.ActiveSceneModel"/>が持つ最新</b> —
    /// 保留時のModelを使うと、直前のリネーム確定で作られた新しいDocumentモデルを上書きする。
    /// </summary>
    private void ReleaseDeferredPopulate(){
        if(!this.HasDeferredPopulate){
            return;
        }

        this.HasDeferredPopulate=false;
        this.Populate(SceneEditorHub.ActiveSceneModel);
    }

    private SceneNodeViewModel?TryFindRenamingNode(){
        if(this.Tree.ItemsSource is not IReadOnlyList<SceneNodeViewModel>Existing){
            return null;
        }

        foreach(var Node in Existing.SelectMany(static Root=>Root.SelfAndDescendants())){
            if(Node.IsRenaming){
                return Node;
            }
        }

        return null;
    }

    private static bool ContainsPath(SceneModel?Model,NodePath Path){
        if(Model?.Root is null){
            return false;
        }

        foreach(var Node in Model.EnumerateNodes()){
            if(Node.Path.Equals(Path)){
                return true;
            }
        }

        return false;
    }

    private void FocusRenameBox(SceneNodeViewModel Node){
        if(this.FindContainer(Node) is not { } Container){
            return;
        }

        var Box=FindVisualChild<TextBox>(Container);
        Box?.Focus();
        Box?.SelectAll();
    }

    private TreeViewItem? FindContainer(SceneNodeViewModel Node)=>
        FindContainer(this.Tree,Node);

    private static TreeViewItem? FindContainer(ItemsControl Parent,SceneNodeViewModel Node){
        if(Parent.ItemContainerGenerator.ContainerFromItem(Node) is TreeViewItem Item){
            return Item;
        }

        foreach(var Child in Parent.Items){
            if(Parent.ItemContainerGenerator.ContainerFromItem(Child) is TreeViewItem ChildItem
                &&FindContainer(ChildItem,Node) is { } Found){
                return Found;
            }
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject Root) where T:DependencyObject{
        for(var Index=0;Index<VisualTreeHelper.GetChildrenCount(Root);Index++){
            var Child=VisualTreeHelper.GetChild(Root,Index);
            if(Child is T Match){
                return Match;
            }

            if(FindVisualChild<T>(Child) is { } Nested){
                return Nested;
            }
        }

        return null;
    }

    private void Tree_PreviewMouseMove(object Sender,MouseEventArgs Args){
        if(this.DragNode is not { } Node||Args.LeftButton!=MouseButtonState.Pressed){
            return;
        }

        var Current=Args.GetPosition(this.Tree);
        if(Math.Abs(Current.X-this.DragStart.X)<SystemParameters.MinimumHorizontalDragDistance
            &&Math.Abs(Current.Y-this.DragStart.Y)<SystemParameters.MinimumVerticalDragDistance){
            return;
        }

        this.DragNode=null;
        var DraggedPaths=this.CurrentSelection.Paths.Contains(Node.Path)
                            ?this.CurrentSelection.Paths
                            :new[]{Node.Path};
        DragDrop.DoDragDrop(this.Tree,new HierarchyDragPayload(DraggedPaths),DragDropEffects.Move);
    }

    private void Tree_DragOver(object Sender,DragEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Args.Data.GetData(typeof(ComponentDragPayload)) is ComponentDragPayload ComponentPayload){
            ComponentDropVerdict Verdict=this.EvaluateComponentDrop(Args,ComponentPayload);
            Args.Effects=ComponentDropPolicy.Accepts(Verdict)
                ?DragDropEffects.Copy
                :DragDropEffects.None;
            this.UpdateComponentDropFeedback(Verdict,ComponentPayload);
            Args.Handled=true;
            return;
        }

        if(!Args.Data.GetDataPresent(typeof(HierarchyDragPayload))
            ||FindNodeContainer(Args.OriginalSource as DependencyObject) is not { } Container
            ||Container.DataContext is not SceneNodeViewModel){
            Args.Effects=DragDropEffects.None;
            Args.Handled=true;
            this.HideReorderDropIndicator();
            return;
        }

        Args.Effects=DragDropEffects.Move;
        Args.Handled=true;
        var Position=Args.GetPosition(Container);
        var Kind=HierarchyInsertionPolicy.Resolve(Position.Y,Container.ActualHeight);
        this.UpdateReorderDropFeedback(Container,Kind);
    }

    /// <summary>
    /// M0052: パレットからのドロップは<b>ドロップしたノード</b>が対象(Inspectorは選択中ノードが対象)。
    /// 判定材料は<see cref="SceneEditorHub.ActiveSceneModel"/>から採るので、未保存の追加が絡む一瞬だけ
    /// 甘くなることがある(<see cref="ComponentDropPolicy"/>参照)。最終判断は適用側が行う。
    /// </summary>
    private ComponentDropVerdict EvaluateComponentDrop(DragEventArgs Args,ComponentDragPayload Payload){
        var Model=SceneEditorHub.ActiveSceneModel;
        if(this.FindNode(Args.OriginalSource as DependencyObject) is not { } Target){
            return ComponentDropPolicy.Evaluate(
                Model is not null,TargetResolved:false,TargetHasComponentsList:false,TargetAlreadyHasType:false,
                CanCreateRootChild:true);
        }

        var Node=FindSceneNode(Model,Target.Path);
        return ComponentDropPolicy.Evaluate(
            Model is not null,
            TargetResolved:Node is not null,
            TargetHasComponentsList:Node?.ComponentsListLocation is not null,
            TargetAlreadyHasType:Node?.FindComponent(Payload.ComponentTypeFullyQualifiedName) is not null);
    }

    private static SceneNode? FindSceneNode(SceneModel?Model,NodePath Path){
        if(Model?.Root is null){
            return null;
        }

        foreach(var Node in Model.EnumerateNodes()){
            if(Node.Path.Equals(Path)){
                return Node;
            }
        }

        return null;
    }

    private void Tree_Drop(object Sender,DragEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Args.Data.GetData(typeof(ComponentDragPayload)) is ComponentDragPayload ComponentPayload){
            this.DropComponent(Args,ComponentPayload);
            Args.Handled=true;
            return;
        }

        this.HideReorderDropIndicator();
        if(Args.Data.GetData(typeof(HierarchyDragPayload)) is not HierarchyDragPayload Payload
            ||Payload.Paths.Count==0
            ||FindNodeContainer(Args.OriginalSource as DependencyObject) is not { } Container
            ||Container.DataContext is not SceneNodeViewModel Target){
            return;
        }

        var Position=Args.GetPosition(Container);
        var Kind=HierarchyInsertionPolicy.Resolve(Position.Y,Container.ActualHeight);
        if(Kind==HierarchyInsertionKind.Before&&Target.Path.Parent is { } Parent){
            SceneEditorHub.RequestHierarchyEdit(new HierarchyEditRequest(
                HierarchyEditKind.Reparent,Payload.Paths,TargetPath:Parent,
                TargetIndex:Target.Path.Indices.Last()));
        } else if(Kind==HierarchyInsertionKind.After&&Target.Path.Parent is { } ParentAfter){
            SceneEditorHub.RequestHierarchyEdit(new HierarchyEditRequest(
                HierarchyEditKind.Reparent,Payload.Paths,TargetPath:ParentAfter,
                TargetIndex:Target.Path.Indices.Last()+1));
        } else {
            SceneEditorHub.RequestHierarchyEdit(new HierarchyEditRequest(
                HierarchyEditKind.Reparent,Payload.Paths,TargetPath:Target.Path,
                TargetIndex:Target.Children.Count));
        }

        Args.Handled=true;
    }

    private void DropComponent(DragEventArgs Args,ComponentDragPayload Payload){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Verdict=this.EvaluateComponentDrop(Args,Payload);
        if(!ComponentDropPolicy.Accepts(Verdict)){
            // DragOverの拒否カーソルと同じ関数から文言を作るので、プレビューと結果が食い違わない。
            SetStatusBarText(ComponentDropPolicy.Describe(Verdict,Payload.DisplayName));
            return;
        }

        if(this.FindNode(Args.OriginalSource as DependencyObject) is not { } Target){
            if(Verdict==ComponentDropVerdict.AcceptCreateRootChild){
                SceneEditorHub.RequestComponentAdd(new ComponentAddRequest(
                    Payload.ComponentTypeFullyQualifiedName,Payload.ComponentExpression,
                    NewEntityName:Payload.DisplayName,CreateRootChild:true));
            }
            return;
        }

        SceneEditorHub.RequestComponentAdd(new ComponentAddRequest(
            Payload.ComponentTypeFullyQualifiedName,Payload.ComponentExpression,TargetPath:Target.Path));
    }

    private static void SetStatusBarText(string Text){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Package.GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
            StatusBar.SetText(Text);
        }
    }

    private SceneNodeViewModel? FindViewModel(NodePath Path)=>
        this.Tree.ItemsSource is IReadOnlyList<SceneNodeViewModel>Roots
            ?Roots.SelectMany(static Root=>Root.SelfAndDescendants()).FirstOrDefault(Node=>Node.Path.Equals(Path))
            :null;

    private sealed record HierarchyDragPayload(IReadOnlyList<NodePath> Paths);

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

        // M0046: 呼び出し元(Populate)が張ったガードを黙って解除しないよう、元の値へ戻す。
        var Previous=this.SuppressSelectionPublish;
        this.SuppressSelectionPublish=true;
        try {
            foreach(var Node in Roots.SelectMany(static Root=>Root.SelfAndDescendants())){
                Node.IsMultiSelected=this.CurrentSelection.Paths.Contains(Node.Path);
                Node.IsSelected=this.CurrentSelection.PrimaryPath is { } Primary&&Primary.Equals(Node.Path);
            }
        } finally {
            this.SuppressSelectionPublish=Previous;
        }
    }

    private IReadOnlyList<NodePath> OrderedPaths(){
        return this.Tree.ItemsSource is not IReadOnlyList<SceneNodeViewModel>Roots
            ?[]
            :Roots.SelectMany(static Root=>Root.SelfAndDescendants()).Select(static Node=>Node.Path).ToArray();
    }

    private static bool IsExpanderClick(DependencyObject?Element){
        for(var Current=Element;Current is not null;Current=VisualTreeHelper.GetParent(Current)){
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
        for(var Current=Element;Current is not null;Current=VisualTreeHelper.GetParent(Current)){
            if(Current is TreeViewItem { DataContext:SceneNodeViewModel Node }){
                return Node;
            }
        }

        return null;
    }

    /// <summary>
    /// <see cref="FindNode"/>と同じ探索だが、ノードだけでなくその<see cref="TreeViewItem"/>コンテナも
    /// 返す。並べ替えドラッグの挿入位置プレビュー(<see cref="Tree_DragOver"/>)と実際の適用
    /// (<see cref="Tree_Drop"/>)が同じ幾何情報(コンテナの高さ・座標)を参照できるようにするため。
    /// </summary>
    private static TreeViewItem?FindNodeContainer(DependencyObject?Element){
        for(var Current=Element;Current is not null;Current=VisualTreeHelper.GetParent(Current)){
            if(Current is TreeViewItem Item){
                return Item;
            }
        }

        return null;
    }
}
