using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Inspector の WPF content(M0031)。選択中ノードの編集可能なComponent memberを一覧し、タイプ入力・
/// チェックボックス・Enumピッカーから1操作=1Undoで<see cref="SceneEditorSessionService.CommitAsync"/>
/// へ流す。<see cref="SceneDesignerToolWindowControl"/>のSession購読パターン(PinnedSnapshotChanged +
/// Selection.SelectionChanged)をそのまま踏襲する。
/// </summary>
public partial class InspectorToolWindowControl:UserControl,IDisposable {
    private SceneEditorSessionService?Session;
    private SceneDocumentSnapshot?CurrentSnapshot;
    private SceneSelection?CurrentSelection;
    private bool SuppressNextLostFocusCommit;
    private bool Disposed;

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
            this.TryRefresh();
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
        this.MemberList.ItemsSource=Groups.Select(this.CreateGroupViewModel).ToArray();
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
        _=>throw new InvalidOperationException("未知のInspector行種別です。"),
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
        SceneInspectorMember Member=ViewModel.Member;
        string?NewExpression=Member.EditorKind switch {
            InspectorEditorKind.Number when Member.NumericType is { }Type
                &&SceneInspectorValueFormat.TryFormatNumber(Type,Text,out string Formatted)=>Formatted,
            InspectorEditorKind.Text=>SceneInspectorValueFormat.FormatText(Text),
            _=>null,
        };
        if(NewExpression is null){
            SetStatus($"{Member.MemberName} の値を解釈できません。");
            this.TryRefresh(Force:true);
            return;
        }

        this.SubmitCommit(Member,NewExpression);
    }

    private void BooleanField_Click(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Sender is not CheckBox Box||Box.DataContext is not InspectorMemberViewModel ViewModel){
            return;
        }

        this.SubmitCommit(ViewModel.Member,SceneInspectorValueFormat.FormatBoolean(Box.IsChecked==true));
    }

    private void EnumField_DropDownClosed(object Sender,EventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Sender is not ComboBox Box||Box.DataContext is not InspectorMemberViewModel ViewModel
            ||Box.SelectedItem is not string SelectedName){
            return;
        }

        string?EmitExpression=null;
        foreach(InspectorEnumCandidate Candidate in ViewModel.Member.EnumCandidates){
            if(string.Equals(Candidate.Name,SelectedName,StringComparison.Ordinal)){
                EmitExpression=Candidate.EmitExpression;
                break;
            }
        }

        if(EmitExpression is null){
            return;
        }

        this.SubmitCommit(ViewModel.Member,EmitExpression);
    }

    /// <summary>M0047: Documentへの同期適用なので、非同期の多重発行防止(<c>CommitInProgress</c>)はもう不要。</summary>
    private void SubmitCommit(SceneInspectorMember Member,string NewExpression){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Session is not { }Session||this.CurrentSnapshot is not { }Snapshot){
            return;
        }

        SceneEditCommitRequest Request=SceneInspectorCommitPlanner.CreateCommit(
            Snapshot.Key,Snapshot.DocumentVersion,Member,NewExpression);
        if(!Session.Apply(Request)){
            SetStatus("インスペクターの編集を反映できませんでした。");
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
