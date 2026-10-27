using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
public partial class AssetsToolWindowControl:UserControl,IDisposable {
    private AssetWorkspaceController?Controller;
    private CancellationTokenSource?SearchCancellation;
    private bool Updating=true;
    private bool Disposed;
    private Point DragOrigin;
    public AssetsToolWindowControl(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.InitializeComponent();
        this.AllowDrop=true;
        this.DragOver+=(_,Args)=>{ Args.Handled=true;Args.Effects=this.Controller is not null&&Args.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None; };
        this.Drop+=(_,Args)=>{
            if(this.Controller is { }Controller&&Args.Data.GetData(DataFormats.FileDrop) is string[]Files){ Args.Handled=true;Controller.Run(()=>AssetWorkspaceCommands.ImportFilesAsync(Controller,Files)); }
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
                if(ItemsControl.ContainerFromElement(List,Args.OriginalSource as DependencyObject) is ListBoxItem Container&&!Container.IsSelected){ List.SelectedItem=Container.DataContext; }
            };
            List.ContextMenuOpening+=(_,Args)=>{ Args.Handled=true;this.OnOrganize(List,Args); };
        }
        AssetWorkspaceSelection.Changed+=this.OnGlobalSelection;
        this.Updating=false;
    }
    internal void Attach(AssetWorkspaceController?Controller){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed||ReferenceEquals(this.Controller,Controller)){ return; }
        if(this.Controller is { }Previous){ Previous.Changed-=this.OnInventoryChanged; }
        this.Controller=Controller;
        if(Controller is not null){ Controller.Changed+=this.OnInventoryChanged;this.OnInventoryChanged(); }
    }
    private void OnInventoryChanged(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Controller is not { }Controller||this.Disposed){ return; }
        AssetThumbnailService.Invalidate();
        this.Updating=true;
        this.ProjectChoice.ItemsSource=Controller.Projects.Select(static Project=>new AssetProjectChoice(Project.ProjectFilePath)).ToArray();
        this.ProjectChoice.SelectedValue=Controller.Query.ProjectPath;
        this.LockLocation.IsChecked=Controller.Query.Locked;this.WholeProject.IsChecked=Controller.Query.WholeProject;
        this.Details.IsChecked=Controller.Query.Details;
        this.Location.Text=Controller.Query.Folder;
        this.Search.Text=Controller.Query.Search;
        this.KindChoice.SelectedValue=Controller.Query.Kind;
        this.ProviderChoice.SelectedValue=Controller.Query.Provider;
        this.StateChoice.SelectedValue=Controller.Query.State;
        this.Favorites.ItemsSource=Controller.LoadFavorites();
        this.Updating=false;
        _=this.ApplyQueryAsync();
    }
    private void OnProjectChanged(object Sender,SelectionChangedEventArgs Args){
        if(this.Updating||this.Controller is not { }Controller||this.ProjectChoice.SelectedValue is not string Project){ return; }
        Controller.Query.ProjectPath=Project;
        Controller.Query.Folder=Controller.Projects.FirstOrDefault(Item=>Item.ProjectFilePath==Project)?.ProjectDirectory??string.Empty;
        this.Location.Text=Controller.Query.Folder;
        _=this.ApplyQueryAsync();
    }
    private void OnQueryChanged(object Sender,RoutedEventArgs Args){
        if(this.Updating||this.Controller is not { }Controller){ return; }
        Controller.Query.Search=this.Search.Text;Controller.Query.Kind=(this.KindChoice.SelectedValue as string)??string.Empty;
        Controller.Query.Provider=(this.ProviderChoice.SelectedValue as string)??string.Empty;Controller.Query.State=(this.StateChoice.SelectedValue as string)??string.Empty;
        Controller.Query.Locked=this.LockLocation.IsChecked==true;Controller.Query.WholeProject=this.WholeProject.IsChecked==true;
        Controller.Query.Details=this.Details.IsChecked==true;
        _=this.ApplyQueryAsync();
    }
    private async Task ApplyQueryAsync(){
        if(this.Disposed||this.Controller is not { }Controller){ return; }
        this.SearchCancellation?.Cancel();this.SearchCancellation?.Dispose();
        var Cancellation=this.SearchCancellation=new CancellationTokenSource();
        var Token=Cancellation.Token;
        var Query=new AssetWorkspaceQuery{ProjectPath=Controller.Query.ProjectPath,Folder=Controller.Query.Folder,Search=Controller.Query.Search,
            Kind=Controller.Query.Kind,Provider=Controller.Query.Provider,State=Controller.Query.State,WholeProject=Controller.Query.WholeProject};
        var Items=Controller.Items;
        try {
            var Result=await Task.Run(()=>Query.Apply(Items,Token),Token);
            if(this.Disposed||Token.IsCancellationRequested){ return; }
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
        if(this.Updating||Sender is not ListBox List){ return; }
        AssetWorkspaceSelection.Select(List.SelectedItems.Cast<AssetWorkspaceRow>().Select(static Row=>Row.Item).ToArray());
    }
    private void OnGlobalSelection(IReadOnlyList<AssetWorkspaceItem>Items){
        if(this.Disposed){ return; }
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
    private void OnOpen(object Sender,MouseButtonEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread(); if(this.Selected is { }Item){ this.Controller?.Open(Item); } }
    private void OnShowInSolution(object Sender,RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread(); if(this.Selected is { }Item){ this.Controller?.ShowInSolution(Item); } }
    private void OnRefresh(object Sender,RoutedEventArgs Args){ if(this.Controller is { }Controller){ Controller.Run(Controller.RefreshAsync); } }
    private void OnCancel(object Sender,RoutedEventArgs Args){ if(this.Disposed){ return; }this.SearchCancellation?.Cancel();this.Controller?.CancelRefresh(); }
    private void OnKeyDown(object Sender,KeyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
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
        Controller.Query.ProjectPath=Favorite.Project;Controller.Query.Folder=Favorite.Folder;Controller.Query.Search=Favorite.Search;
        Controller.Query.Kind=Favorite.Kind;Controller.Query.Provider=Favorite.Provider;Controller.Query.State=Favorite.State;Controller.Query.WholeProject=Favorite.WholeProject;
        this.Updating=true;
        this.Search.Text=Favorite.Search;this.KindChoice.SelectedValue=Favorite.Kind;this.ProviderChoice.SelectedValue=Favorite.Provider;this.StateChoice.SelectedValue=Favorite.State;
        this.Updating=false;this.OnInventoryChanged();
    }
    private void OnDragStart(object Sender,MouseButtonEventArgs Args){
        this.DragOrigin=Args.GetPosition(this);
        if(Sender is ListBox List&&ItemsControl.ContainerFromElement(List,Args.OriginalSource as DependencyObject) is ListBoxItem Container){ Container.Focus(); }
    }
    private void OnDragMove(object Sender,MouseEventArgs Args){
        if(Args.LeftButton!=MouseButtonState.Pressed||this.Selected is not { }Item){ return; }
        var Point=Args.GetPosition(this);
        if(Math.Abs(Point.X-this.DragOrigin.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(Point.Y-this.DragOrigin.Y)<SystemParameters.MinimumVerticalDragDistance){ return; }
        var Data=new DataObject(typeof(AssetWorkspaceItem),Item);Data.SetData(DataFormats.FileDrop,new[]{Item.Source.SourcePath});
        DragDrop.DoDragDrop(this,Data,DragDropEffects.Copy);
    }
    private void OnCreate(object Sender,RoutedEventArgs Args){ ThreadHelper.ThrowIfNotOnUIThread(); if(this.Controller is { }Controller){ Controller.Run(()=>AssetWorkspaceCommands.ShowCreateMenuAsync(this,Controller)); } }
    private void OnOrganize(object Sender,RoutedEventArgs Args){ ThreadHelper.ThrowIfNotOnUIThread(); if(this.Controller is { }Controller){ AssetWorkspaceCommands.ShowOrganizeMenu(this,Controller,AssetWorkspaceSelection.Current); } }
    public void Dispose(){
        if(this.Disposed){ return; }
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
        public string Name=>System.IO.Path.GetFileNameWithoutExtension(this.Path);
        internal AssetProjectChoice(string Path)=>this.Path=Path;
    }
}
