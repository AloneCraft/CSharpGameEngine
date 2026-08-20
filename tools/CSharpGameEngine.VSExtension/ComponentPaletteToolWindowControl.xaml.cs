using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0052: Componentパレットの WPF content。追加できるComponent型を並べ、ダブルクリック/Enterで
/// 選択中のノードへ追加する。適用そのものは行わず、<see cref="SceneEditorHub.RequestComponentAdd"/>
/// へ要求を出すだけ — Undoラベルと追加後のRevealをPackage境界の1か所へ寄せるため。
/// </summary>
public partial class ComponentPaletteToolWindowControl:UserControl,IDisposable {
    private const string FocusSurfaceName="ComponentPalette";

    private SceneEditorSessionService?Session;
    private IReadOnlyList<ComponentPaletteEntry>Entries=[];
    private CancellationTokenSource?CatalogCancellation;
    private long CatalogGeneration;
    private ComponentPaletteEntry?DragEntry;
    private Point DragStart;
    private bool Disposed;

    /// <summary>
    /// 直前に並べた候補の署名。同じなら<c>ItemsSource</c>を差し替えない — 再解析のたびに作り直すと
    /// 検索の選択位置が飛び、<c>DoDragDrop</c>のモーダルループ中なら掴んでいる項目ごと消える
    /// (Inspectorに入れたSkipと同じ規律)。
    /// </summary>
    private string LastCatalogSignature=string.Empty;

    public ComponentPaletteToolWindowControl(){
        this.InitializeComponent();
        this.EntryList.PreviewMouseRightButtonDown+=this.EntryList_PreviewMouseRightButtonDown;
        this.AddHandler(
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(this.Control_PreviewKeyDown),
            handledEventsToo:true);
        this.IsKeyboardFocusWithinChanged+=this.OnKeyboardFocusWithinChanged;
        this.Unloaded+=this.OnUnloadedForFocus;
    }

    private void OnKeyboardFocusWithinChanged(object Sender,DependencyPropertyChangedEventArgs Args)=>
        SceneEditorHub.SetSurfaceFocus(FocusSurfaceName,this.IsKeyboardFocusWithin);

    private void OnUnloadedForFocus(object Sender,RoutedEventArgs Args)=>
        SceneEditorHub.SetSurfaceFocus(FocusSurfaceName,false);

    internal void SetSession(SceneEditorSessionService Session){
        if(this.Disposed||ReferenceEquals(this.Session,Session)){
            return;
        }

        this.DetachSession();
        this.Session=Session;
        Session.PinnedSnapshotChanged+=this.OnPinnedSnapshotChanged;
        this.RefreshCatalog();
    }

    private void DetachSession(){
        if(this.Session is null){
            return;
        }

        this.Session.PinnedSnapshotChanged-=this.OnPinnedSnapshotChanged;
        this.Session=null;
    }

    /// <summary>
    /// Projectが変わらない限り<see cref="SceneWorkspaceService.GetProjectComponentCatalogAsync"/>は
    /// <c>Project.Version</c>キャッシュを返すだけなので、スナップショットのたびに呼んで問題ない。
    /// </summary>
    private void OnPinnedSnapshotChanged(SceneDocumentSnapshot?Snapshot)=>
        this.DispatchToUi(this.RefreshCatalog);

    private void RefreshCatalog(){
        SceneEditorSessionService?Session=this.Session;
        if(this.Disposed||Session is null){
            return;
        }

        this.CatalogCancellation?.Cancel();
        this.CatalogCancellation?.Dispose();
        CancellationTokenSource Cancellation=new();
        this.CatalogCancellation=Cancellation;
        var Generation=++this.CatalogGeneration;
        _=this.RefreshCatalogAsync(Session,Generation,Cancellation.Token);
    }

#pragma warning disable VSTHRD001 // WPF Dispatcher is the owning UI dispatcher of this control; no AsyncPackage/JTF chain is available here.
    private async Task RefreshCatalogAsync(
        SceneEditorSessionService Session,long Generation,CancellationToken CancellationToken){
        try {
            IReadOnlyList<ComponentPaletteEntry>Loaded=
                await Session.GetProjectComponentCatalogAsync(CancellationToken).ConfigureAwait(false);
            if(CancellationToken.IsCancellationRequested){
                return;
            }

            await this.Dispatcher.InvokeAsync(()=>{
                if(this.Disposed||!ReferenceEquals(this.Session,Session)||Generation!=this.CatalogGeneration){
                    return;
                }

                var Signature=ComputeSignature(Loaded);
                if(string.Equals(Signature,this.LastCatalogSignature,StringComparison.Ordinal)){
                    return;
                }

                this.LastCatalogSignature=Signature;
                this.Entries=Loaded;
                this.ApplyFilter();
            });
        } catch(OperationCanceledException){
            // より新しいProject/Sceneの要求に追い越された。
        }
    }

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

    private static string ComputeSignature(IReadOnlyList<ComponentPaletteEntry>Entries){
        StringBuilder Builder=new();
        foreach(var Entry in Entries){
            Builder.Append(Entry.FullyQualifiedName).Append((char)0x1f);
        }

        return Builder.ToString();
    }

    private void SearchBox_TextChanged(object Sender,TextChangedEventArgs Args)=>this.ApplyFilter();

    private void ApplyFilter(){
        var Filtered=ComponentPaletteFilterPolicy.Filter(this.Entries,this.SearchBox.Text);
        this.EntryList.ItemsSource=Filtered;
        this.StatusMessage.Text=this.Entries.Count==0
            ?"追加できるComponent型が見つかりません。シーンを開いてください。"
            :$"{Filtered.Count} / {this.Entries.Count} 件。ダブルクリックまたは Enter で、選択中のノードに追加します。";
    }

    private void EntryList_MouseDoubleClick(object Sender,MouseButtonEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.AddSelectedEntry();
    }

    private void EntryList_KeyDown(object Sender,KeyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Args.Key!=Key.Enter){
            return;
        }

        this.AddSelectedEntry();
        Args.Handled=true;
    }

    private void Control_PreviewKeyDown(object Sender,KeyEventArgs Args){
        if(Args.Key!=Key.F12||this.EntryList.SelectedItem is not ComponentPaletteEntry Entry){
            return;
        }

        SceneEditorHub.RequestDefinitionNavigation(new DefinitionNavigationRequest(Entry.FullyQualifiedName));
        Args.Handled=true;
    }

    private void EntryList_PreviewMouseRightButtonDown(object Sender,MouseButtonEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Args.OriginalSource is not DependencyObject Source||FindEntry(Source) is not { } Entry){
            return;
        }

        this.EntryList.SelectedItem=Entry;
        this.EntryList.Focus();
        ContextMenu Menu=new();
        Menu.Items.Add(CreateDefinitionMenuItem(Entry.FullyQualifiedName,null));
        Menu.PlacementTarget=this.EntryList;
        Menu.IsOpen=true;
        Args.Handled=true;
    }

    private static MenuItem CreateDefinitionMenuItem(string ComponentType,string? MemberName){
        MenuItem Item=new(){
            Header=DefinitionNavigationStrings.MenuHeader,
            InputGestureText=DefinitionNavigationStrings.InputGesture};
        Item.Click+=(Sender,Args)=>SceneEditorHub.RequestDefinitionNavigation(
            new DefinitionNavigationRequest(ComponentType,MemberName));
        return Item;
    }

    private void EntryList_PreviewMouseLeftButtonDown(object Sender,MouseButtonEventArgs Args){
        this.DragEntry=(Args.OriginalSource as DependencyObject)is { }Source
            ?FindEntry(Source)
            :null;
        this.DragStart=Args.GetPosition(this.EntryList);
    }

    /// <summary>
    /// しきい値を越えてから<c>DoDragDrop</c>を呼ぶ(Hierarchyのノードドラッグと同じ作法)。
    /// クリックしただけで一覧の選択ができなくなるのを防ぐ。
    /// </summary>
    private void EntryList_PreviewMouseMove(object Sender,MouseEventArgs Args){
        if(this.DragEntry is not { } Entry||Args.LeftButton!=MouseButtonState.Pressed){
            return;
        }

        var Current=Args.GetPosition(this.EntryList);
        if(Math.Abs(Current.X-this.DragStart.X)<SystemParameters.MinimumHorizontalDragDistance
            &&Math.Abs(Current.Y-this.DragStart.Y)<SystemParameters.MinimumVerticalDragDistance){
            return;
        }

        this.DragEntry=null;
        DragDrop.DoDragDrop(
            this.EntryList,
            new ComponentDragPayload(Entry.FullyQualifiedName,Entry.ComponentExpression,Entry.DisplayName),
            DragDropEffects.Copy);
    }

    private static ComponentPaletteEntry? FindEntry(DependencyObject Source){
        for(DependencyObject?Current=Source;Current is not null;
            Current=System.Windows.Media.VisualTreeHelper.GetParent(Current)){
            if(Current is FrameworkElement { DataContext:ComponentPaletteEntry Entry }){
                return Entry;
            }
        }

        return null;
    }

    private void AddSelectedEntry(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.EntryList.SelectedItem is not ComponentPaletteEntry Entry||this.Session is not { }Session){
            return;
        }

        SceneEditorHub.RequestComponentAdd(new ComponentAddRequest(
            Entry.FullyQualifiedName,
            Entry.ComponentExpression,
            TargetNodeId:Session.Selection.CurrentSelection?.PrimaryNodeId));
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;
        this.DetachSession();
        this.CatalogCancellation?.Cancel();
        this.CatalogCancellation?.Dispose();
        this.CatalogCancellation=null;
    }
}
