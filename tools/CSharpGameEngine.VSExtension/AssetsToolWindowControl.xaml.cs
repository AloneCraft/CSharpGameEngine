using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
public partial class AssetsToolWindowControl:UserControl,IDisposable {
    private AssetWorkspaceController?Controller;
    private CancellationTokenSource?SearchCancellation;
    private bool Updating=true;
    private bool Disposed;
    private Point DragOrigin;
    private readonly DispatcherTimer RenameTimer=new();
    private AssetWorkspaceRow?RenameCandidate;
    private AssetWorkspaceRow?EditingRow;
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
    public AssetsToolWindowControl(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.InitializeComponent();
        this.RenameTimer.Interval=TimeSpan.FromMilliseconds(GetDoubleClickTime());
        this.RenameTimer.Tick+=(_,_)=>{
            var Row=this.RenameCandidate;this.CancelRenameWait();
            var List=this.ActiveList;
            if(Row is not null&&List.IsKeyboardFocusWithin&&List.SelectedItems.Count==1&&ReferenceEquals(List.SelectedItem,Row)){ this.BeginRename(Row); }
        };
        this.Unloaded+=(_,_)=>this.CancelRename();
        this.AllowDrop=true;
        this.DragOver+=(_,Args)=>{ Args.Handled=true;Args.Effects=this.Controller?.Query.CanChooseDestination==true&&Args.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None; };
        this.Drop+=(_,Args)=>{
            Args.Handled=true;
            if(this.Controller is { Query.CanChooseDestination:true }Controller&&Args.Data.GetData(DataFormats.FileDrop) is string[]Files){ Controller.Run(()=>AssetWorkspaceCommands.ImportFilesAsync(Controller,Files)); }
        };
        AssetTheme.Apply(this);
        // Aero テンプレートは独自の選択背景を描画するため、VS のパレットを上書きする。
        // コンテンツプレゼンターは維持しつつ、VS テーマに対応した項目スタイルで両方の色を管理する。
        var TileStyle=new Style(typeof(ListBoxItem),(Style)this.Resources[typeof(ListBoxItem)]);
        TileStyle.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)this.Resources["AssetTileItemTemplate"]));
        TileStyle.Setters.Add(new Setter(Control.BackgroundProperty,System.Windows.Media.Brushes.Transparent));
        TileStyle.Setters.Add(new Setter(Control.BorderBrushProperty,System.Windows.Media.Brushes.Transparent));
        TileStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));
        TileStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(4)));
        var SelectedTile=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};
        SelectedTile.Setters.Add(new Setter(Control.BorderBrushProperty,new DynamicResourceExtension(Microsoft.VisualStudio.Shell.VsBrushes.HighlightKey)));
        TileStyle.Triggers.Add(SelectedTile);
        var DisabledTile=new Trigger{Property=IsEnabledProperty,Value=false};
        DisabledTile.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension(Microsoft.VisualStudio.Shell.VsBrushes.GrayTextKey)));
        DisabledTile.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension(Microsoft.VisualStudio.Shell.VsBrushes.WindowKey)));
        TileStyle.Triggers.Add(DisabledTile);this.Tiles.ItemContainerStyle=TileStyle;
        this.KindChoice.ItemsSource=new[]{string.Empty}.Concat(Enum.GetNames(typeof(AssetWorkspaceKind))).Select(static Value=>new AssetChoice(Value,string.IsNullOrEmpty(Value)?VsixText.Get("Assets_AllKinds"):VsixText.Get("Assets_Kind_"+Value))).ToArray();
        this.ProviderChoice.ItemsSource=new[]{string.Empty,"Project","ProjectPlugin","Engine","Package"}.Select(static Value=>new AssetChoice(Value,string.IsNullOrEmpty(Value)?VsixText.Get("Assets_AllProviders"):VsixText.Get("Assets_Provider_"+Value))).ToArray();
        this.StateChoice.ItemsSource=new[]{string.Empty}.Concat(Enum.GetNames(typeof(AssetWorkspaceState))).Select(static Value=>new AssetChoice(Value,string.IsNullOrEmpty(Value)?VsixText.Get("Assets_AllStates"):VsixText.Get("Assets_State_"+Value))).ToArray();
        foreach(var Choice in new[]{this.KindChoice,this.ProviderChoice,this.StateChoice}){ Choice.DisplayMemberPath="Name";Choice.SelectedValuePath="Value";Choice.SelectedIndex=0; }
        this.Rows.AddHandler(GridViewColumnHeader.ClickEvent,new RoutedEventHandler(this.OnSort));
        foreach(var List in new ListBox[]{this.Tiles,this.Rows}){
            List.ContextMenu=new ContextMenu();
            List.PreviewMouseRightButtonDown+=(_,Args)=>{
                var Container=ItemsControl.ContainerFromElement(List,Args.OriginalSource as DependencyObject) as ListBoxItem;
                if(Container is null){ List.SelectedItems.Clear(); }
                else if(!Container.IsSelected){ List.SelectedItem=Container.DataContext; }
            };
            List.ContextMenuOpening+=(_,Args)=>{ Args.Handled=true;this.OnOrganize(List,Args); };
        }
        AssetWorkspaceSelection.Changed+=this.OnGlobalSelection;
        this.Updating=false;
    }
    internal void Attach(AssetWorkspaceController?Controller){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed||ReferenceEquals(this.Controller,Controller)){ return; }
        this.CancelRename();
        if(this.Controller is { }Previous){ Previous.Changed-=this.OnInventoryChanged; }
        this.Controller=Controller;
        if(Controller is not null){ Controller.Changed+=this.OnInventoryChanged;this.OnInventoryChanged(); }
    }
    private void OnInventoryChanged(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Controller is not { }Controller||this.Disposed){ return; }
        // アセット一覧そのものが変わったときだけサムネイルのキャッシュを捨てる（選択や診断だけの通知では残す）。
        var ItemsChanged=!ReferenceEquals(this.LastItems,Controller.Items);
        if(ItemsChanged){ AssetThumbnailService.Invalidate();this.LastItems=Controller.Items; }
        this.Updating=true;
        this.ProjectChoice.ItemsSource=new[]{new AssetProjectChoice(string.Empty)}.Concat(Controller.Projects.Select(static Project=>new AssetProjectChoice(Project.ProjectFilePath))).ToArray();
        this.ProjectChoice.SelectedValue=Controller.Query.Scope==AssetWorkspaceScope.Solution?string.Empty:Controller.Query.ProjectPath;
        this.CreateButton.IsEnabled=Controller.Query.CanChooseDestination;
        this.WholeProject.IsEnabled=Controller.Query.Scope==AssetWorkspaceScope.Folder;
        this.LockLocation.IsChecked=Controller.Query.Locked;this.WholeProject.IsChecked=Controller.Query.Scope!=AssetWorkspaceScope.Folder||Controller.Query.WholeProject;
        this.Details.IsChecked=Controller.Query.Details;
        this.Search.Text=Controller.Query.Search;
        this.KindChoice.SelectedValue=Controller.Query.Kind;
        this.ProviderChoice.SelectedValue=Controller.Query.Provider;
        this.StateChoice.SelectedValue=Controller.Query.State;
        this.Favorites.ItemsSource=Controller.LoadFavorites();
        this.Updating=false;
        if(!ItemsChanged&&this.AppliedViewKey==Controller.Query.ViewKey&&this.Rows.ItemsSource is AssetWorkspaceRow[]Existing){
            // 一覧も条件も同じ: 行（とサムネイル）はそのまま、ステータス行だけ更新する。
            this.Status.Text=string.IsNullOrEmpty(Controller.Diagnostic)?VsixText.Format("Assets_Count",Existing.Length):Controller.Diagnostic;
            return;
        }
        _=this.ApplyQueryAsync();
    }
    private AssetWorkspaceItem[]?LastItems;
    private string?AppliedViewKey;
    private void OnProjectChanged(object Sender,SelectionChangedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Updating||this.Controller is not { }Controller||this.ProjectChoice.SelectedValue is not string Project){ return; }
        Controller.Query.MoveTo(Project,Controller.Projects.FirstOrDefault(Item=>Item.ProjectFilePath==Project)?.ProjectDirectory??string.Empty,
            string.IsNullOrEmpty(Project)?AssetWorkspaceScope.Solution:AssetWorkspaceScope.Project);
        this.OnInventoryChanged();
    }
    private void OnQueryChanged(object Sender,RoutedEventArgs Args){
        if(this.Updating||this.Controller is not { }Controller){ return; }
        Controller.Query.Search=this.Search.Text;Controller.Query.Kind=(this.KindChoice.SelectedValue as string)??string.Empty;
        Controller.Query.Provider=(this.ProviderChoice.SelectedValue as string)??string.Empty;Controller.Query.State=(this.StateChoice.SelectedValue as string)??string.Empty;
        Controller.Query.Locked=this.LockLocation.IsChecked==true;
        if(Controller.Query.Scope==AssetWorkspaceScope.Folder){ Controller.Query.WholeProject=this.WholeProject.IsChecked==true; }
        Controller.Query.Details=this.Details.IsChecked==true;
        _=this.ApplyQueryAsync();
    }
    private async Task ApplyQueryAsync(){
        if(this.Disposed||this.Controller is not { }Controller){ return; }
        this.CancelRename();
        this.SearchCancellation?.Cancel();this.SearchCancellation?.Dispose();
        var Cancellation=this.SearchCancellation=new CancellationTokenSource();
        var Token=Cancellation.Token;
        var Query=new AssetWorkspaceQuery{Scope=Controller.Query.Scope,ProjectPath=Controller.Query.ProjectPath,Folder=Controller.Query.Folder,Search=Controller.Query.Search,
            Kind=Controller.Query.Kind,Provider=Controller.Query.Provider,State=Controller.Query.State,WholeProject=Controller.Query.WholeProject};
        var Items=Controller.Items;
        var ViewKey=Controller.Query.ViewKey;
        try {
            var Result=await Task.Run(()=>Query.Apply(Items,Token),Token);
            if(this.Disposed||Token.IsCancellationRequested){ return; }
            this.AppliedViewKey=ViewKey;
            this.Updating=true;
            var Rows=Result.Select(static Item=>new AssetWorkspaceRow(Item)).ToArray();
            this.Rows.ItemsSource=Rows;this.Tiles.ItemsSource=Rows;
            this.Rows.Visibility=Controller.Query.Details?Visibility.Visible:Visibility.Collapsed;
            this.Tiles.Visibility=Controller.Query.Details?Visibility.Collapsed:Visibility.Visible;
            this.Status.Text=string.IsNullOrEmpty(Controller.Diagnostic)?VsixText.Format("Assets_Count",Result.Length):Controller.Diagnostic;
            this.Updating=false;
            this.OnGlobalSelection(AssetWorkspaceSelection.Current);
        }catch(OperationCanceledException){ }
    }
    private void OnSelectionChanged(object Sender,SelectionChangedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Updating||Sender is not ListBox List){ return; }
        this.CancelRename();
        var Items=List.SelectedItems.Cast<AssetWorkspaceRow>().Select(static Row=>Row.Item).ToArray();
        AssetWorkspaceSelection.Select(Items);
        // Assets での選択をソリューション エクスプローラーへ同期する（1 件だけ選んだとき）。
        if(Items.Length==1){ this.Controller?.SyncSolutionSelection(Items[0]); }
    }
    private void OnGlobalSelection(IReadOnlyList<AssetWorkspaceItem>Items){
        if(this.Disposed){ return; }
        if(this.EditingRow is { }Editing&&(Items.Count!=1||!string.Equals(Items[0].Source.SourcePath,Editing.Item.Source.SourcePath,StringComparison.OrdinalIgnoreCase))){ this.CancelRename(); }
        this.Updating=true;
        foreach(var List in new ListBox[]{this.Rows,this.Tiles}){
            var Paths=new HashSet<string>(Items.Select(static Item=>Item.Source.SourcePath),StringComparer.OrdinalIgnoreCase);
            foreach(var Row in List.SelectedItems.Cast<AssetWorkspaceRow>().Where(Row=>!Paths.Contains(Row.Item.Source.SourcePath)).ToArray()){ List.SelectedItems.Remove(Row); }
            foreach(var Row in List.Items.Cast<AssetWorkspaceRow>().Where(Row=>Paths.Contains(Row.Item.Source.SourcePath)&&!List.SelectedItems.Contains(Row))){ List.SelectedItems.Add(Row); }
            if(List.Visibility==Visibility.Visible&&List.SelectedItem is { }Selected){ List.ScrollIntoView(Selected); }
        }
        this.Updating=false;
    }
    private AssetWorkspaceItem?Selected=>AssetWorkspaceSelection.Current.Count==1?AssetWorkspaceSelection.Current[0]:null;
    private ListBox ActiveList=>this.Rows.Visibility==Visibility.Visible?this.Rows:this.Tiles;
    private void OnOpen(object Sender,MouseButtonEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();this.CancelRenameWait();
        if(this.EditingRow is null&&Args.ChangedButton==MouseButton.Left&&Sender is ListBox List
            &&ItemsControl.ContainerFromElement(List,Args.OriginalSource as DependencyObject) is ListBoxItem&&this.Selected is { }Item){ this.Controller?.Open(Item); }
    }
    private void OnRefresh(object Sender,RoutedEventArgs Args){ if(this.Controller is { }Controller){ Controller.Run(Controller.RefreshAsync); } }
    private void OnCancel(object Sender,RoutedEventArgs Args){ if(this.Disposed){ return; }this.SearchCancellation?.Cancel();this.Controller?.CancelRefresh(); }
    private void OnKeyDown(object Sender,KeyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.CancelRenameWait();
        if(this.EditingRow is not null){
            if(Args.Key==Key.Escape){ Args.Handled=this.HandleRenameEscape(); }
            else if(Args.Key==Key.Enter){ this.CommitRename();Args.Handled=true; }
            return;
        }
        if(Keyboard.FocusedElement is TextBox){ return; }
        if(Args.Key==Key.F2&&Keyboard.Modifiers==ModifierKeys.None){
            if(this.ActiveList.SelectedItems.Count==1&&this.ActiveList.SelectedItem is AssetWorkspaceRow Row){ this.BeginRename(Row); }
            Args.Handled=true;return;
        }
        if(Keyboard.Modifiers==ModifierKeys.Control&&this.HandleNativeShortcut(Args.Key)){ Args.Handled=true;return; }
        if(Args.Key==Key.Enter&&this.Selected is { }Item){ this.Controller?.Open(Item);Args.Handled=true; }
        if(Args.Key==Key.F5){ this.OnRefresh(Sender,Args);Args.Handled=true; }
    }
    internal bool HandleNativeShortcut(Key Key){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(!this.IsKeyboardFocusWithin||Keyboard.FocusedElement is TextBox||Key is not(Key.Z or Key.Y)){ return false; }
            var History=AssetFileTransaction.History;
        if(Key==Key.Z&&History.CanUndo){ History.Undo(1); }
        if(Key==Key.Y&&History.CanRedo){ History.Redo(1); }
        if(this.Controller is { }Controller){ Controller.Run(Controller.RefreshAsync); }
        return true;
    }
    private void OnSort(object Sender,RoutedEventArgs Args){
        if(Args.OriginalSource is not GridViewColumnHeader Header||Header.Column is null){ return; }
        var Binding=Header.Column.DisplayMemberBinding as System.Windows.Data.Binding;
        var Property=Binding?.Path.Path??Header.Tag as string;
        if(Property is null){ return; }
        var View=System.Windows.Data.CollectionViewSource.GetDefaultView(this.Rows.ItemsSource);
        var Direction=View.SortDescriptions.Count>0&&View.SortDescriptions[0].PropertyName==Property&&View.SortDescriptions[0].Direction==ListSortDirection.Ascending?ListSortDirection.Descending:ListSortDirection.Ascending;
        View.SortDescriptions.Clear();View.SortDescriptions.Add(new SortDescription(Property,Direction));
    }
    private void OnFavorite(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Controller is not { }Controller){ return; }
        Controller.SaveFavorite();this.Favorites.ItemsSource=Controller.LoadFavorites();
    }
    private void OnFavoriteSelected(object Sender,SelectionChangedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Updating||this.Controller is not { }Controller||this.Favorites.SelectedItem is not AssetWorkspaceFavorite Favorite){ return; }
        Favorite.Restore(Controller.Query);
        this.Updating=true;
        this.Search.Text=Favorite.Search;this.KindChoice.SelectedValue=Favorite.Kind;this.ProviderChoice.SelectedValue=Favorite.Provider;this.StateChoice.SelectedValue=Favorite.State;
        this.Updating=false;this.OnInventoryChanged();
    }
    private void OnDragStart(object Sender,MouseButtonEventArgs Args){
        this.CancelRenameWait();
        this.DragOrigin=Args.GetPosition(this);
        if(Sender is not ListBox List){ return; }
        if(ItemsControl.ContainerFromElement(List,Args.OriginalSource as DependencyObject) is ListBoxItem Container){
            if(Container.DataContext is AssetWorkspaceRow { IsRenaming:true }){ return; }
            if(Container.IsSelected&&List.SelectedItems.Count==1&&Args.ClickCount==1&&Keyboard.Modifiers==ModifierKeys.None){ this.RenameCandidate=Container.DataContext as AssetWorkspaceRow; }
            Container.Focus();return;
        }
        // アイテム以外の空き領域のクリックは選択を解除する（スクロールバーと列ヘッダーは除く）。
        if(IsEmptySpace(Args.OriginalSource as DependencyObject)){ List.SelectedItems.Clear(); }
    }
    /// <summary>スクロールバーと列ヘッダーの上ではない、一覧の空き領域か。</summary>
    internal static bool IsEmptySpace(DependencyObject?Source){
        for(var Node=Source;Node is not null;Node=Node is System.Windows.Media.Visual||Node is System.Windows.Media.Media3D.Visual3D?System.Windows.Media.VisualTreeHelper.GetParent(Node):LogicalTreeHelper.GetParent(Node)){
            if(Node is System.Windows.Controls.Primitives.ScrollBar or GridViewColumnHeader or ListBoxItem){ return false; }
        }
        return true;
    }
    private void OnDragMove(object Sender,MouseEventArgs Args){
        if(Args.LeftButton!=MouseButtonState.Pressed||Keyboard.FocusedElement is TextBox||this.Selected is not { }Item){ return; }
        var Point=Args.GetPosition(this);
        if(Math.Abs(Point.X-this.DragOrigin.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(Point.Y-this.DragOrigin.Y)<SystemParameters.MinimumVerticalDragDistance){ return; }
        this.CancelRenameWait();
        var Data=new DataObject(typeof(AssetWorkspaceItem),Item);Data.SetData(DataFormats.FileDrop,new[]{Item.Source.SourcePath});
        DragDrop.DoDragDrop(this,Data,DragDropEffects.Copy);
    }
    private void OnCreate(object Sender,RoutedEventArgs Args){ ThreadHelper.ThrowIfNotOnUIThread(); if(this.Controller is { }Controller){ Controller.Run(()=>AssetWorkspaceCommands.ShowCreateMenuAsync(this,Controller)); } }
    private void OnRenameClick(object Sender,MouseButtonEventArgs Args){
        if(this.RenameCandidate is not null&&Args.ClickCount==1){ this.RenameTimer.Start(); }
    }
    private void CancelRenameWait(){ this.RenameTimer?.Stop();this.RenameCandidate=null; }
    private void CancelRename(){
        this.CancelRenameWait();var Row=this.EditingRow;this.EditingRow=null;Row?.EndRename();
    }
    internal bool HandleRenameEscape(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.EditingRow is null){ return false; }
        this.CancelRename();this.ActiveList.Focus();return true;
    }
    private void BeginRename(AssetWorkspaceRow Row){
        this.CancelRename();
        if(this.Controller is null||this.Disposed){ return; }
        this.EditingRow=Row;
        if(!Row.BeginRename()){ this.EditingRow=null;return; }
        this.ActiveList.ScrollIntoView(Row);
    }
    private void OnRenameVisible(object Sender,DependencyPropertyChangedEventArgs Args){
        if(Sender is not TextBox Input||!Input.IsVisible||this.Controller is not { }Controller){ return; }
        Controller.Run(async()=>{
            await Controller.Owner.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield:true);
            if(Input.IsVisible&&Input.DataContext is AssetWorkspaceRow { IsRenaming:true }Row&&ReferenceEquals(this.EditingRow,Row)){
                Input.Focus();Input.Select(0,Path.GetFileNameWithoutExtension(Row.Name).Length);
            }
        });
    }
    private void OnRenameLostFocus(object Sender,KeyboardFocusChangedEventArgs Args){
        if(Sender is TextBox { DataContext:AssetWorkspaceRow Row }&&ReferenceEquals(this.EditingRow,Row)){ this.CommitRename(); }
    }
    private void CommitRename(){
        if(this.EditingRow is not { }Row||this.Controller is not { }Controller){ return; }
        string Destination;
        try { Destination=Row.RenameDestination(); }catch(ArgumentException Error){ this.Status.Text=Error.Message;return; }
        this.CancelRename();this.ActiveList.Focus();
        if(!string.Equals(Row.Item.Source.SourcePath,Destination,StringComparison.Ordinal)){ Controller.Run(()=>AssetWorkspaceCommands.RenameAsync(Controller,Row.Item,Destination)); }
    }
    private void OnOrganize(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();this.CancelRenameWait();
        if(this.Controller is { }Controller){ AssetWorkspaceCommands.ShowOrganizeMenu(this,Controller,AssetWorkspaceSelection.Current,Item=>{
            if(this.ActiveList.Items.Cast<AssetWorkspaceRow>().FirstOrDefault(Row=>ReferenceEquals(Row.Item,Item)) is { }Row){ this.BeginRename(Row); }
        }); }
    }
    public void Dispose(){
        if(this.Disposed){ return; }
        this.CancelRename();
        this.Disposed=true;
        var Cancellation=Interlocked.Exchange(ref this.SearchCancellation,null);
        Cancellation?.Cancel();Cancellation?.Dispose();
        AssetWorkspaceSelection.Changed-=this.OnGlobalSelection;
        if(this.Controller is { }Controller){ Controller.Changed-=this.OnInventoryChanged; }
        this.Controller=null;
    }
    private sealed record AssetChoice(string Value,string Name);
    private sealed class AssetProjectChoice {
        public string Path{get;}
        public string Name=>string.IsNullOrEmpty(this.Path)?VsixText.Get("Assets_WholeSolution"):System.IO.Path.GetFileNameWithoutExtension(this.Path);
        internal AssetProjectChoice(string Path)=>this.Path=Path;
    }
}
