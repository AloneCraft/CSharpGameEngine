using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Inspector の WPF content(M0031)。選択中ノードの編集可能なComponent memberを一覧し、タイプ入力・
/// チェックボックス・Enumピッカーから1操作=1Undoで<see cref="SceneEditorSessionService.CommitAsync"/>
/// へ流す。<see cref="SceneDesignerToolWindowControl"/>のSession購読パターン(PinnedSnapshotChanged +
/// Selection.SelectionChanged)をそのまま踏襲する。
/// </summary>
public partial class InspectorToolWindowControl:UserControl,IDisposable {
    public static readonly RoutedUICommand ConvertLightCommand=new();
    private SceneEditorSessionService?Session;
    private SceneDocumentSnapshot?CurrentSnapshot;
    private SceneSelection?CurrentSelection;
    private bool SuppressNextLostFocusCommit;
    private bool Disposed;
    private JoinableTask? DeferredComboBoxOpenTask;
    private readonly InspectorSelectionController Selection=new();
    private readonly SceneEditorEscapeRouter EscapeRouter;

    /// <summary>
    /// 畳まれているComponentグループのキー(<see cref="InspectorExpansionKey"/>)。既定は展開なので
    /// 「畳んだものだけ」を覚える。Hierarchyの<c>CollapsedPaths</c>と違って<b>作り直しのたびに
    /// クリアしない</b> — Inspectorは常に1ノード分しか描かないため、クリアすると選択を往復した
    /// だけで畳んだ状態が消えてしまう。キーはSceneの(ノード×Component)数で頭打ちになるので、
    /// 時間とともに際限なく増えることはない。
    /// </summary>
    private readonly HashSet<string>CollapsedKeys=[];

    /// <summary>直前に描いた内容の署名。同じなら<c>ItemsSource</c>を差し替えない
    /// (<see cref="InspectorRefreshPolicy"/>)。</summary>
    private string LastRefreshSignature=InspectorContentSignature.Compute(null);

    /// <summary>入力中でDeferした更新があるか。フォーカスが外れた時点で最新から作り直す。</summary>
    private bool HasDeferredRefresh;

    /// <summary>
    /// 追加した直後で、memberが載るまで強制的に展開しておきたいComponentのキー。
    /// <see cref="InspectorRevealPolicy"/>を参照。
    /// </summary>
    private string?PendingRevealKey;

    public InspectorToolWindowControl(){
        this.InitializeComponent();
        this.CommandBindings.Add(new CommandBinding(ConvertLightCommand,this.ConvertLight_Executed,this.ConvertLight_CanExecute));
        this.EscapeRouter=new(this,this.HandleEscape);
        // TextBox / ComboBox / Expander may mark their mouse and key events handled before
        // an instance handler declared in the template observes them. Select the logical
        // inspector row at the control boundary, while leaving the editor focus to WPF.
        this.AddHandler(
            Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(this.Panel_PreviewMouseLeftButtonDown),
            handledEventsToo:true);
        this.AddHandler(
            Mouse.PreviewMouseUpEvent,
            new MouseButtonEventHandler(this.Panel_PreviewMouseLeftButtonUp),
            handledEventsToo:true);
        this.PreviewMouseRightButtonDown+=this.Panel_PreviewMouseRightButtonDown;
        this.AddHandler(
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(this.Panel_PreviewKeyDown),
            handledEventsToo:true);
        this.AddHandler(
            Keyboard.PreviewGotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(this.Panel_PreviewGotKeyboardFocus),
            handledEventsToo:true);

        // M0051 Step 1-4: Hierarchyと同じ理由で、自分のフォーカス状態をHubへ報告する
        // (Inspectorで数値を編集した直後のCtrl+Sも保存へ届くようにするため)。
        this.IsKeyboardFocusWithinChanged+=this.OnKeyboardFocusWithinChanged;
        this.Unloaded+=this.OnUnloadedForFocus;
        SceneEditorHub.ComponentRevealRequested+=this.OnComponentRevealRequested;
    }

    /// <summary>
    /// M0052: 追加が成功したComponentを展開して見せる。<b>差分検出ではなく明示的な要求</b>で駆動する —
    /// 外部編集やUndoでComponentが復活した場合と、ユーザーが今追加した場合を区別できないため。
    /// </summary>
    private void OnComponentRevealRequested(ComponentRevealRequest Request)=>
        this.DispatchToUi(()=>{
            this.PendingRevealKey=InspectorExpansionKey.Compute(
                Request.NodeId,Request.ComponentTypeFullyQualifiedName);
            this.CollapsedKeys.Remove(this.PendingRevealKey);
            this.TryRefresh(Force:true);
            this.ScrollPendingRevealIntoView();
        });

    /// <summary>
    /// コンテナは<c>ItemsSource</c>代入の直後にはまだ生成されていないので、レイアウト後に回して
    /// から探す。見つからなくても何もしない — 表示位置の調整であって、機能の成否ではない。
    /// </summary>
#pragma warning disable VSTHRD001 // WPF Dispatcher is the owning UI dispatcher of this control; no AsyncPackage/JTF chain is available here.
    private void ScrollPendingRevealIntoView(){
        if(this.PendingRevealKey is null){
            return;
        }

        _=this.Dispatcher.BeginInvoke(
            new Action(()=>{
                if(this.Disposed
                    ||this.MemberList.ItemsSource is not IReadOnlyList<InspectorComponentGroupViewModel>Groups){
                    return;
                }

                var Index=Groups.ToList().FindIndex(
                    Group=>InspectorRevealPolicy.ShouldExpand(this.PendingRevealKey,Group.ExpansionKey));
                if(Index>=0
                    &&this.MemberList.ItemContainerGenerator.ContainerFromIndex(Index)is FrameworkElement Container){
                    Container.BringIntoView();
                }
            }),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }
#pragma warning restore VSTHRD001

    private const string FocusSurfaceName="Inspector";

    private void OnKeyboardFocusWithinChanged(object Sender,DependencyPropertyChangedEventArgs Args){
        SceneEditorHub.SetSurfaceFocus(FocusSurfaceName,this.IsKeyboardFocusWithin);

        // 入力中に来た更新は捨てずに保留してある。保留したモデルではなく「今の」スナップショットから
        // 作り直す — 保留中にさらに解析が進んでいることがあるため(Hierarchyの保留解除と同じ判断)。
        if(!this.IsKeyboardFocusWithin&&this.HasDeferredRefresh){
            this.TryRefresh();
        }
    }

    private void OnUnloadedForFocus(object Sender,RoutedEventArgs Args)=>
        SceneEditorHub.SetSurfaceFocus(FocusSurfaceName,false);

    internal void SetSession(SceneEditorSessionService Session){
        if(this.Disposed||ReferenceEquals(this.Session,Session)){
            return;
        }

        this.DetachSession();
        this.Session=Session;
        Session.PinnedSnapshotChanged+=this.OnPinnedSnapshotChanged;
        Session.Selection.SelectionChanged+=this.OnSelectionChanged;
        this.CurrentSnapshot=Session.PinnedSnapshot;
        this.CurrentSelection=Session.Selection.CurrentSelection;
        this.TryRefresh(Force:true);
    }

    private void DetachSession(){
        if(this.Session is null){
            return;
        }

        this.Session.PinnedSnapshotChanged-=this.OnPinnedSnapshotChanged;
        this.Session.Selection.SelectionChanged-=this.OnSelectionChanged;
        this.Session=null;
    }

    private void OnPinnedSnapshotChanged(SceneDocumentSnapshot?Snapshot){
        this.DispatchToUi(()=>{
            this.CurrentSnapshot=Snapshot;
            this.TryRefresh();
        });
    }

    private void OnSelectionChanged(SceneSelection Selection){
        this.DispatchToUi(()=>{
            this.CurrentSelection=Selection;
            this.TryRefresh(Force:Selection.NodeIds.Count==0&&Selection.ComponentType is null&&Selection.MemberName is null);
        });
    }

    /// <summary>
    /// 再構築してよいかは<see cref="InspectorRefreshPolicy"/>が決める。土台は<b>内容署名による
    /// Skip</b>で、これが無いと再解析のたびに<c>ItemsSource</c>が差し替わってExpanderの展開状態も
    /// スクロール位置も飛ぶ(<c>IsKeyboardFocusWithin</c>だけのガードでは、マウスだけで操作する
    /// 開閉を守れない)。<paramref name="Force"/>は編集確定直後など、署名が同じでも描き直したい場合。
    /// </summary>
    private void TryRefresh(bool Force=false){
        if(this.Disposed){
            return;
        }

        IReadOnlyList<SceneInspectorComponentGroup>?Groups=
            this.Session is not null&&this.CurrentSnapshot is { }Snapshot
                ?SceneInspectorModelBuilder.BuildGroups(Snapshot,this.CurrentSelection)
                :null;

        var Signature=InspectorContentSignature.Compute(Groups);
        switch(InspectorRefreshPolicy.Decide(
            HasKeyboardFocusWithin:this.IsKeyboardFocusWithin,
            ContentUnchanged:Signature==this.LastRefreshSignature,
            Force)){
            case InspectorRefreshAction.Skip:
                return;
            case InspectorRefreshAction.Defer:
                this.HasDeferredRefresh=true;
                return;
        }

        this.HasDeferredRefresh=false;
        this.LastRefreshSignature=Signature;

        // 差し替える前に、生きたViewModelから今の開閉状態を読み戻す。
        this.CaptureExpansionState();
        this.UpdatePendingReveal(Groups);

        if(Groups is null||Groups.Count==0){
            this.ShowEmpty();
            return;
        }

        this.EmptyMessage.Visibility=Visibility.Collapsed;
        this.MemberScroll.Visibility=Visibility.Visible;
        var ViewModels=Groups.Select(this.CreateGroupViewModel).ToArray();
        this.Selection.SetItems(ViewModels);
        this.MemberList.ItemsSource=ViewModels;
    }

    private InspectorComponentGroupViewModel CreateGroupViewModel(SceneInspectorComponentGroup Group){
        var Key=InspectorExpansionKey.Compute(Group.NodeId,Group.ComponentTypeFullyQualifiedName);
        var Rows=Group.Rows.Select(this.BuildRowViewModel).ToArray();
        return new InspectorComponentGroupViewModel(
            Group,Rows,
            IsExpanded:InspectorRevealPolicy.ShouldExpand(this.PendingRevealKey,Key)||!this.CollapsedKeys.Contains(Key));
    }

    /// <summary>
    /// <see cref="SceneInspectorMemberRow"/>は素の<see cref="InspectorMemberViewModel"/>へ、
    /// <see cref="SceneInspectorNestedRow"/>は自分自身を再帰的に呼んで
    /// <see cref="InspectorNestedRowViewModel"/>へ変換する。
    /// </summary>
    private object BuildRowViewModel(SceneInspectorRow Row)=>Row switch {
        SceneInspectorMemberRow Leaf=>new InspectorMemberViewModel(Leaf.Member),
        SceneInspectorNestedRow Nested=>this.CreateNestedRowViewModel(Nested),
        _=>throw new InvalidOperationException(VsixText.Get("InspectorUnknownRowType")),
    };

    private InspectorNestedRowViewModel CreateNestedRowViewModel(SceneInspectorNestedRow Row){
        var Key=InspectorExpansionKey.Compute(Row.NodeId,Row.ComponentTypeFullyQualifiedName,Row.MemberPath);
        var Rows=Row.Rows.Select(this.BuildRowViewModel).ToArray();
        return new InspectorNestedRowViewModel(
            Row,Rows,
            IsExpanded:InspectorRevealPolicy.ShouldExpand(this.PendingRevealKey,Key)||!this.CollapsedKeys.Contains(Key));
    }

    /// <summary>
    /// 作り直しで捨てられる直前のViewModelから、ユーザーの開閉状態を回収する。
    /// <c>Expander.Expanded</c>/<c>Collapsed</c>を購読しないのはこのためで、それらは初期の
    /// データバインド代入自体でも発火するため状態更新の起点にすると再入を招く。
    /// ネストした<see cref="InspectorNestedRowViewModel"/>の開閉も同じ規律で回収する。
    /// </summary>
    private void CaptureExpansionState(){
        if(this.MemberList.ItemsSource is not IReadOnlyList<InspectorComponentGroupViewModel>Existing){
            return;
        }

        foreach(var Group in Existing){
            this.CaptureExpansionKey(Group.ExpansionKey,Group.IsExpanded);
            foreach(var Row in Group.Rows){
                this.CaptureRowExpansionState(Row);
            }
        }
    }

    private void CaptureRowExpansionState(object Row){
        if(Row is not InspectorNestedRowViewModel Nested){
            return;
        }

        this.CaptureExpansionKey(Nested.ExpansionKey,Nested.IsExpanded);
        foreach(var Child in Nested.Rows){
            this.CaptureRowExpansionState(Child);
        }
    }

    private void CaptureExpansionKey(string Key,bool IsExpanded){
        if(IsExpanded){
            this.CollapsedKeys.Remove(Key);
            return;
        }

        this.CollapsedKeys.Add(Key);

        // 自分で畳んだのなら、追加直後の自動展開はもう邪魔でしかない。これを消さないと、
        // memberが最後まで載らないComponent(設定可能な値を持たない型)を畳めなくなる。
        if(InspectorRevealPolicy.ShouldExpand(this.PendingRevealKey,Key)){
            this.PendingRevealKey=null;
        }
    }

    private void UpdatePendingReveal(IReadOnlyList<SceneInspectorComponentGroup>?Groups){
        if(this.PendingRevealKey is not { }Pending){
            return;
        }

        var Target=Groups?.FirstOrDefault(Group=>string.Equals(
            InspectorExpansionKey.Compute(Group.NodeId,Group.ComponentTypeFullyQualifiedName),
            Pending,StringComparison.Ordinal));
        if(InspectorRevealPolicy.ShouldClearPending(Target is not null,Target?.Rows.Count??0)){
            this.PendingRevealKey=null;
        }
    }

    private void Panel_PreviewMouseLeftButtonDown(object Sender,MouseButtonEventArgs Args){
        if(Args.ChangedButton!=MouseButton.Left){
            return;
        }

        if(TryGetSelectable(Args.OriginalSource as DependencyObject,out object ViewModel,out _)){
            this.SelectRow(ViewModel);
        }
    }

    private void Panel_PreviewMouseLeftButtonUp(object Sender,MouseButtonEventArgs Args){
        if(Args.ChangedButton!=MouseButton.Left){
            return;
        }

        if(!TryGetSelectable(Args.OriginalSource as DependencyObject,out object ViewModel,out _)
            ||FindInputControl(Args.OriginalSource as DependencyObject) is not null){
            return;
        }

        this.FocusSelectionEditor(ViewModel);
    }

    private void Panel_PreviewMouseRightButtonDown(object Sender,MouseButtonEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(!TryGetSelectable(Args.OriginalSource as DependencyObject,out object ViewModel,out DefinitionNavigationRequest Request)){
            return;
        }

        this.SelectRow(ViewModel);
        ContextMenu Menu=new();
        MenuItem Item=new(){
            Header=DefinitionNavigationStrings.MenuHeader,
            InputGestureText=DefinitionNavigationStrings.InputGesture};
        Item.Click+=(MenuSender,MenuArgs)=>SceneEditorHub.RequestDefinitionNavigation(Request);
        Menu.Items.Add(Item);
        Menu.PlacementTarget=this;
        Menu.IsOpen=true;
        Args.Handled=true;
    }

    private void HandleEscape(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.TryCancelTextEdit()){
            return;
        }
        if(TryCloseOpenComboBox(this.MemberList)){
            return;
        }
        this.Session?.Selection.TryClearCurrent(SceneSelectionOrigin.Inspector,Guid.NewGuid());
    }
    private bool TryCancelTextEdit(){
        if(Keyboard.FocusedElement is not TextBox Box||Box.DataContext is not InspectorMemberViewModel ViewModel){
            return false;
        }
        this.SuppressNextLostFocusCommit=true;
        Box.Text=ViewModel.DisplayText;
        Keyboard.ClearFocus();
        return true;
    }
    private static bool TryCloseOpenComboBox(DependencyObject Root){
        if(Root is ComboBox { IsDropDownOpen:true }Box){
            Box.IsDropDownOpen=false;
            return true;
        }
        int Count=VisualTreeHelper.GetChildrenCount(Root);
        for(int Index=0;Index<Count;Index++){
            if(TryCloseOpenComboBox(VisualTreeHelper.GetChild(Root,Index))){
                return true;
            }
        }
        return false;
    }
    private void InspectorRow_PreviewKeyDown(object Sender,KeyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.HandleInspectorKey(Args.OriginalSource as DependencyObject??Sender as DependencyObject,Args);
    }

    private void Panel_PreviewKeyDown(object Sender,KeyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.HandleInspectorKey(Args.OriginalSource as DependencyObject??Keyboard.FocusedElement as DependencyObject,Args);
    }

    private void HandleInspectorKey(DependencyObject? Source,KeyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        Key Key=Args.Key==Key.System?Args.SystemKey:Args.Key;
        if(Key==Key.F4){
            // F4 belongs to Visual Studio. Do not handle it here.
            return;
        }

        if(Key==Key.Enter&&this.TryOpenSelectedComboBox(Source)){
            Args.Handled=true;
            return;
        }

        if(Key==Key.Delete){
            object?Selected=this.Selection.Current;
            if(Selected is null&&TryGetSelectable(Source??Keyboard.FocusedElement as DependencyObject,out object FocusedForDelete,out _)){
                Selected=FocusedForDelete;
            }

            if(Selected is InspectorComponentGroupViewModel Group){
                RequestRemoveComponent(Group);
                Args.Handled=true;
            }

            return;
        }

        if(Key==Key.F12){
            object?ViewModel=this.Selection.Current;
            DefinitionNavigationRequest Request;
            if(TryGetSelectable(Source??Keyboard.FocusedElement as DependencyObject,out object FocusedViewModel,out DefinitionNavigationRequest FocusedRequest)){
                ViewModel=FocusedViewModel;
                Request=FocusedRequest;
            } else if(ViewModel is null||!TryCreateRequest(ViewModel,out Request)){
                return;
            }

            this.SelectRow(ViewModel);
            SceneEditorHub.RequestDefinitionNavigation(Request);
            Args.Handled=true;
            return;
        }

        if(!InspectorKeyboardNavigationPolicy.TryGetMoveDelta(
            Key,this.IsAnyComboBoxDropDownOpen(Source),out int Delta)){
            return;
        }

        if(TryGetSelectable(Source??Keyboard.FocusedElement as DependencyObject,out object Focused,out _)){
            this.Selection.Select(Focused);
        }

        InspectorSelectableViewModel?Next=this.Selection.Move(Delta);
        if(Next is null){
            return;
        }

        this.FocusSelectionEditor(Next);
        Args.Handled=true;
    }

    private void Panel_PreviewGotKeyboardFocus(object Sender,KeyboardFocusChangedEventArgs Args){
        if(TryGetSelectable(Args.NewFocus as DependencyObject,out object ViewModel,out _)){
            this.SelectRow(ViewModel);
        }
    }

    private void SelectRow(object ViewModel)=>this.Selection.Select(ViewModel);

    private void FocusSelectionEditor(object ViewModel){
        if(!TryFindDataContextElement(this.MemberList,ViewModel,out FrameworkElement Root)){
            return;
        }

        DependencyObject Target=ViewModel is InspectorMemberViewModel
            ?FindFocusableEditor(Root)??Root
            :Root;
        if(Target is IInputElement Input&&Input.Focus()){
            Keyboard.Focus(Input);
        }
    }

    private bool TryOpenSelectedComboBox(DependencyObject? Source){
        object? ViewModel=this.Selection.Current;
        if(TryGetSelectable(Source??Keyboard.FocusedElement as DependencyObject,out object Focused,out _)){
            ViewModel=Focused;
            this.SelectRow(ViewModel);
        }

        if(ViewModel is not InspectorMemberViewModel Member||Member.EditorKind!=InspectorEditorKind.Enum){
            return false;
        }
        if(!TryFindDataContextElement(this.MemberList,ViewModel,out FrameworkElement Root)){
            return false;
        }

        if(FindComboBox(Root) is not ComboBox Box){
            // The row template may not have materialized its editor yet.
            this.DeferredComboBoxOpenTask=ThreadHelper.JoinableTaskFactory.RunAsync(async delegate {
                await Task.Yield();
                this.TryOpenSelectedComboBox(null);
            });
            return true;
        }

        if(!InspectorKeyboardNavigationPolicy.ShouldOpenDropDown(
            Key.Enter,true,Box.IsDropDownOpen)){
            return Box.IsDropDownOpen;
        }
        Box.Focus();
        Box.IsDropDownOpen=true;
        return true;
    }

    private static ComboBox?FindComboBox(DependencyObject Root){
        if(Root is ComboBox Box&&Box.IsVisible){
            return Box;
        }
        for(var Index=0;Index<VisualTreeHelper.GetChildrenCount(Root);Index++){
            if(FindComboBox(VisualTreeHelper.GetChild(Root,Index)) is { } Found){
                return Found;
            }
        }
        return null;
    }

    private static DependencyObject?FindFocusableEditor(DependencyObject Root){
        if(Root is FrameworkElement { IsVisible:true }&&(Root is TextBox or ComboBox or CheckBox)){
            return Root;
        }

        for(var Index=0;Index<VisualTreeHelper.GetChildrenCount(Root);Index++){
            if(FindFocusableEditor(VisualTreeHelper.GetChild(Root,Index)) is { } Found){
                return Found;
            }
        }

        return null;
    }

    private static DependencyObject?FindInputControl(DependencyObject? Source){
        for(DependencyObject?Current=Source;Current is not null;Current=GetParent(Current)){
            if(Current is TextBox or ComboBox or CheckBox){
                return Current;
            }
        }

        return null;
    }

    private bool IsAnyComboBoxDropDownOpen(DependencyObject? Source)=>
        FindInputControl(Source) is ComboBox { IsDropDownOpen:true }
        ||FindOpenComboBox(this.MemberList);

    private static bool FindOpenComboBox(DependencyObject Root){
        if(Root is ComboBox { IsDropDownOpen:true }){
            return true;
        }

        for(var Index=0;Index<VisualTreeHelper.GetChildrenCount(Root);Index++){
            if(FindOpenComboBox(VisualTreeHelper.GetChild(Root,Index))){
                return true;
            }
        }

        return false;
    }

    private static bool TryFindDataContextElement(DependencyObject Root,object ViewModel,out FrameworkElement Element){
        bool IsRowRoot=Root is Border&&ViewModel is InspectorMemberViewModel
                       ||Root is Expander&&ViewModel is InspectorComponentGroupViewModel or InspectorNestedRowViewModel;
        if(Root is FrameworkElement Candidate&&IsRowRoot&&ReferenceEquals(Candidate.DataContext,ViewModel)){
            Element=Candidate;
            return true;
        }

        for(var Index=0;Index<VisualTreeHelper.GetChildrenCount(Root);Index++){
            if(TryFindDataContextElement(VisualTreeHelper.GetChild(Root,Index),ViewModel,out Element)){
                return true;
            }
        }

        Element=null!;
        return false;
    }
    private static bool TryGetSelectable(DependencyObject? Source,out object ViewModel,out DefinitionNavigationRequest Request){
        for(DependencyObject?Current=Source;Current is not null;Current=GetParent(Current)){
            if(Current is not FrameworkElement Element){
                continue;
            }

            if(Element.DataContext is InspectorComponentGroupViewModel Group){
                ViewModel=Group;
                Request=new DefinitionNavigationRequest(Group.FullTypeName);
                return true;
            }
            if(Element.DataContext is InspectorMemberViewModel Member){
                ViewModel=Member;
                Request=new DefinitionNavigationRequest(
                    Member.Member.ComponentTypeFullyQualifiedName,
                    string.Join(".",Member.Member.DefinitionMemberPath));
                return true;
            }
            if(Element.DataContext is InspectorNestedRowViewModel Nested){
                ViewModel=Nested;
                Request=new DefinitionNavigationRequest(
                    Nested.Row.ComponentTypeFullyQualifiedName,string.Join(".",Nested.Row.MemberPath));
                return true;
            }
        }

        ViewModel=null!;
        Request=null!;
        return false;
    }

    private static bool TryCreateRequest(object ViewModel,out DefinitionNavigationRequest Request){
        switch(ViewModel){
            case InspectorComponentGroupViewModel Group:
                Request=new DefinitionNavigationRequest(Group.FullTypeName);
                return true;
            case InspectorMemberViewModel Member:
                Request=new DefinitionNavigationRequest(
                    Member.Member.ComponentTypeFullyQualifiedName,
                    string.Join(".",Member.Member.DefinitionMemberPath));
                return true;
            case InspectorNestedRowViewModel Nested:
                Request=new DefinitionNavigationRequest(
                    Nested.Row.ComponentTypeFullyQualifiedName,string.Join(".",Nested.Row.MemberPath));
                return true;
            default:
                Request=null!;
                return false;
        }
    }

    private static DependencyObject?GetParent(DependencyObject Source)=>Source switch {
        Visual or Visual3D=>VisualTreeHelper.GetParent(Source),
        FrameworkContentElement Content=>Content.Parent,
        ContentElement Content=>ContentOperations.GetParent(Content),
        _=>LogicalTreeHelper.GetParent(Source),
    };
    private void Panel_DragOver(object Sender,DragEventArgs Args){
        if(Args.Data.GetData(typeof(ComponentDragPayload)) is not ComponentDragPayload Payload){
            return;
        }

        Args.Effects=ComponentDropPolicy.Accepts(this.EvaluateComponentDrop(Payload))
            ?DragDropEffects.Copy
            :DragDropEffects.None;
        Args.Handled=true;
    }

    private void Panel_Drop(object Sender,DragEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Args.Data.GetData(typeof(ComponentDragPayload)) is not ComponentDragPayload Payload){
            return;
        }

        Args.Handled=true;
        var Verdict=this.EvaluateComponentDrop(Payload);
        if(!ComponentDropPolicy.Accepts(Verdict)){
            SetStatus(ComponentDropPolicy.Describe(Verdict,Payload.DisplayName));
            return;
        }

        SceneEditorHub.RequestComponentAdd(new ComponentAddRequest(
            Payload.ComponentTypeFullyQualifiedName,Payload.ComponentExpression,
            TargetNodeId:this.CurrentSelection?.PrimaryNodeId));
    }

    /// <summary>
    /// M0052: Inspectorへのドロップは<b>選択中のノード</b>が対象(Hierarchyはドロップしたノード)。
    /// 判定材料はDocumentの予測投影そのものなので、適用側の判断と必ず一致する。
    /// </summary>
    private ComponentDropVerdict EvaluateComponentDrop(ComponentDragPayload Payload){
        SceneNode?Node=this.CurrentSnapshot is { }Snapshot
                    &&this.CurrentSelection?.PrimaryNodeId is { }NodeId
                    &&Snapshot.Projection?.TryGetNode(NodeId,out var Projected)==true
                        ?Projected.Node
                        :null;
        return ComponentDropPolicy.Evaluate(
            HasScene:this.CurrentSnapshot is not null,
            TargetResolved:Node is not null,
            TargetHasComponentsList:Node?.ComponentsListLocation is not null,
            TargetAlreadyHasType:Node?.FindComponent(Payload.ComponentTypeFullyQualifiedName) is not null);
    }

    private void ShowEmpty(){
        this.Selection.SetItems(Array.Empty<InspectorComponentGroupViewModel>());
        this.MemberList.ItemsSource=null;
        this.MemberScroll.Visibility=Visibility.Collapsed;
        this.EmptyMessage.Visibility=Visibility.Visible;
    }

    private void EditableField_KeyDown(object Sender,KeyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Sender is not TextBox Box||Box.DataContext is not InspectorMemberViewModel ViewModel){
            return;
        }

        if(Args.Key==Key.Enter){
            this.SuppressNextLostFocusCommit=true;
            this.CommitTextField(ViewModel,Box.Text);
            Keyboard.ClearFocus();
            Args.Handled=true;
        } else if(Args.Key==Key.Escape){
            this.SuppressNextLostFocusCommit=true;
            Box.Text=ViewModel.DisplayText;
            Keyboard.ClearFocus();
            Args.Handled=true;
        }
    }

    private void EditableField_LostFocus(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.SuppressNextLostFocusCommit){
            this.SuppressNextLostFocusCommit=false;
            return;
        }

        if(Sender is not TextBox Box||Box.DataContext is not InspectorMemberViewModel ViewModel){
            return;
        }

        this.CommitTextField(ViewModel,Box.Text);
    }

    private void CommitTextField(InspectorMemberViewModel ViewModel,string Text){
        ThreadHelper.ThrowIfNotOnUIThread();
        // Clicking another row makes the old TextBox lose focus. Do not turn an unchanged
        // value into an edit: a forced refresh there discards the newly selected row.
        if(!InspectorTextCommitPolicy.HasChanged(ViewModel.DisplayText,Text)){
            return;
        }

        SceneInspectorMember Member=ViewModel.Member;
        string?NewExpression=Member.EditorKind switch {
            InspectorEditorKind.Number when Member.NumericType is { }Type
                &&SceneInspectorValueFormat.TryFormatNumber(Type,Text,out string Formatted)=>Formatted,
            InspectorEditorKind.PhysicalValue when Member.PhysicalContract?.TryFormatInput(Text,out string PhysicalExpression)==true=>PhysicalExpression,
            InspectorEditorKind.Text=>SceneInspectorValueFormat.FormatText(Text),
            _=>null,
        };
        if(NewExpression is null){
            SetStatus(VsixText.Format("InspectorValueParseFailed",Member.MemberName));
            this.TryRefresh(Force:true);
            return;
        }

        if(Member.PhysicalContract is { }Contract&&Contract.TryReadExpression(Member.SourceExpression,out var Before)
            &&Contract.TryReadExpression(NewExpression,out var After)&&Before.SequenceEqual(After)){return;}
        this.SubmitCommit(Member,NewExpression);
    }

    private void BooleanField_Click(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Sender is not CheckBox Box||Box.DataContext is not InspectorMemberViewModel ViewModel){
            return;
        }

        this.SubmitCommit(ViewModel.Member,SceneInspectorValueFormat.FormatBoolean(Box.IsChecked==true));
    }

    private bool IsGameRunning(){
        var Name=this.CurrentSnapshot is { }Snapshot?this.Session?.TryGetRuntimeProcessName(Snapshot.Key):null;
        return !string.IsNullOrWhiteSpace(Name)&&SpatialDebugClient.FindProcessIds(Name!).Count!=0;
    }
    private void ConvertLight_CanExecute(object Sender,CanExecuteRoutedEventArgs Args){
        Args.CanExecute=Args.Parameter is InspectorComponentGroupViewModel Group
            &&LightComponentContract.IsLight(Group.Group.ComponentTypeFullyQualifiedName)
            &&this.CurrentSnapshot?.HasBlockingErrors==false&&!this.IsGameRunning();
        Args.Handled=true;
    }
    #pragma warning disable VSTHRD100
    private async void ConvertLight_Executed(object Sender,ExecutedRoutedEventArgs Args){
        try{
            if(Args.Parameter is not InspectorComponentGroupViewModel Group||this.Session is not { }Session
                ||this.CurrentSnapshot is not { }Snapshot||this.IsGameRunning()
                ||Snapshot.Projection?.TryGetNode(Group.Group.NodeId,out var Node)!=true
                ||Node.Node.FindComponent(Group.Group.ComponentTypeFullyQualifiedName) is not { }Source){return;}
            var Targets=new List<Microsoft.CodeAnalysis.INamedTypeSymbol>();
            Microsoft.CodeAnalysis.Compilation?Compilation=null;
            foreach(var Type in LightComponentContract.Types.Where(Type=>Type!=Source.TypeFullyQualifiedName)){
                var Resolved=await Session.ResolveComponentTypeAsync(Type,CancellationToken.None);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(Resolved is not { }Value){SetStatus(VsixText.Get("LightConversionConflict"));return;}
                Targets.Add(Value.Symbol);Compilation=Value.Compilation;
            }
            if(Compilation is null||this.IsGameRunning()){return;}
            var Dialog=new LightConversionDialog(Source,Targets,Compilation){Owner=Window.GetWindow(this)};
            if(Dialog.ShowDialog()!=true||Dialog.Replacement is not { }Replacement||this.IsGameRunning()){return;}
            var Operation=new ConvertLightOperation(Snapshot.Key,Group.Group.NodeId,Snapshot.DocumentVersion,
                Source.TypeFullyQualifiedName,Source.EmitExpressionText,Replacement,
                LightConversionPlanner.ContractFingerprint(Source,Replacement.TypeSymbol!));
            this.SubmitCommit(new SceneEditCommitRequest(Snapshot.Key,Snapshot.DocumentVersion,VsixText.Get("LightConversionTitle"),
                Array.Empty<SceneTransformChange>(),SceneTransformMemberMask.None,DirectOperations:[Operation]));
        }catch(Exception Error){
            ActivityLog.LogError(nameof(InspectorToolWindowControl),Error.ToString());
            SetStatus(VsixText.Get("LightConversionConflict"));
        }
    }
    #pragma warning restore VSTHRD100
    private void RemoveComponentButton_Click(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        Args.Handled=true;
        if(Sender is not Button Button||Button.DataContext is not InspectorComponentGroupViewModel ViewModel){
            return;
        }

        RequestRemoveComponent(ViewModel);
    }

    /// <summary>
    /// ×ボタン・Deleteキー共通の削除経路。Hierarchyの削除確認
    /// (<c>HierarchyToolWindowControl.RequestRemove</c>)と同じ<see cref="MessageBox"/>の
    /// パターンで確認してから<see cref="SceneEditorHub.RequestComponentRemove"/>を発行する。
    /// </summary>
    private static void RequestRemoveComponent(InspectorComponentGroupViewModel ViewModel){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(MessageBox.Show(
            VsixText.Format("InspectorRemovePrompt",ViewModel.Header),
            VsixText.Get("InspectorRemoveTitle"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes){
            return;
        }

        SceneEditorHub.RequestComponentRemove(
            new ComponentRemoveRequest(ViewModel.Group.NodeId,ViewModel.Group.ComponentTypeFullyQualifiedName));
    }

    private void ChoiceField_DropDownClosed(object Sender,EventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Sender is not ComboBox Box||Box.DataContext is not InspectorMemberViewModel ViewModel
            ||Box.SelectedItem is not string SelectedName){
            return;
        }

        if(SceneInspectorCommitPlanner.IsCurrentChoice(ViewModel.Member,SelectedName)){ return; }

        if(this.CurrentSnapshot is not { }Snapshot||!SceneInspectorCommitPlanner.TryCreateChoiceCommit(
            Snapshot.Key,Snapshot.DocumentVersion,ViewModel.Member,SelectedName,out var Request)){
            SetStatus(VsixText.Format("InspectorValueParseFailed",ViewModel.Member.MemberName));
            this.TryRefresh(Force:true);
            return;
        }
        this.SubmitCommit(Request);
    }

    /// <summary>One completed Inspector gesture is written immediately as one native source Undo unit.</summary>
    private void SubmitCommit(SceneInspectorMember Member,string NewExpression){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Session is not { }Session||this.CurrentSnapshot is not { }Snapshot){
            return;
        }

        SceneEditCommitRequest Request=SceneInspectorCommitPlanner.CreateCommit(
            Snapshot.Key,Snapshot.DocumentVersion,Member,NewExpression);
        this.SubmitCommit(Request);
    }

    #pragma warning disable VSTHRD100 // Event-handler leaf; exceptions are observed below.
    private async void SubmitCommit(SceneEditCommitRequest Request){
    #pragma warning restore VSTHRD100
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if(this.Session is not { }Session){ return; }
        SceneEditCommitResult Commit;
        try {
            Commit=await Session.CommitAsync(Request,CancellationToken.None);
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(InspectorToolWindowControl),Ex.ToString());
            Commit=new SceneEditCommitResult(
                false,VsixText.Get("InspectorCommitFailed"),WasRebased:false);
        }
        if(this.Disposed||!ReferenceEquals(this.Session,Session)){ return; }
        if(!Commit.Succeeded){
            SetStatus(Commit.ErrorMessage??VsixText.Get("InspectorCommitFailed"));
        }

        this.TryRefresh(Force:true);
    }

    private static void SetStatus(string Text){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Package.GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
            StatusBar.SetText(Text);
        }
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;
        this.EscapeRouter.Dispose();
        SceneEditorHub.ComponentRevealRequested-=this.OnComponentRevealRequested;
        this.DetachSession();
    }

#pragma warning disable VSTHRD001 // WPF Dispatcher is the owning UI dispatcher of this control; no AsyncPackage/JTF chain is available here.
    private void DispatchToUi(Action Action){
        if(this.Disposed){
            return;
        }

        if(this.Dispatcher.CheckAccess()){
            Action();
        } else {
            _=this.Dispatcher.BeginInvoke(Action);
        }
    }
#pragma warning restore VSTHRD001
}
