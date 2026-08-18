using System;
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

    public InspectorToolWindowControl(){
        InitializeComponent();
    }

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
    /// <paramref name="Force"/>=falseのときは、このパネル内のどれかのコントロールがキーボード
    /// フォーカスを持っている間は再構築しない — 選択中Sceneは再解析のたびに変化するため、素直に
    /// 再構築するとタイプ中のTextBoxの入力が消える(Hierarchyの再構築対応と同じ問題)。
    /// </summary>
    private void TryRefresh(bool Force=false){
        if(this.Disposed||(!Force&&this.IsKeyboardFocusWithin)){
            return;
        }

        if(this.Session is null||this.CurrentSnapshot is not { }Snapshot){
            this.ShowEmpty();
            return;
        }

        SceneInspectorMember[]Members=[.. SceneInspectorModelBuilder.Build(Snapshot,this.CurrentSelection)];
        if(Members.Length==0){
            this.ShowEmpty();
            return;
        }

        EmptyMessage.Visibility=Visibility.Collapsed;
        MemberScroll.Visibility=Visibility.Visible;
        MemberList.ItemsSource=Members.Select(static Member=>new InspectorMemberViewModel(Member)).ToArray();
    }

    private void ShowEmpty(){
        MemberList.ItemsSource=null;
        MemberScroll.Visibility=Visibility.Collapsed;
        EmptyMessage.Visibility=Visibility.Visible;
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
