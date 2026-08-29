using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CSharpGameEngine.ShaderGraph;
namespace CSharpGameEngine.VSExtension;
internal sealed class ShaderGraphEntryTab {
    internal ShaderGraphEntryTab(string Name,StableId NodeId) { this.Name=Name;this.NodeId=NodeId; }
    internal string Name { get; }
    internal StableId NodeId { get; }
    public override string ToString()=>this.Name;
}
public partial class ShaderGraphEditorControl:UserControl {
    private readonly HashSet<StableId>Selection=[];
    private readonly Dictionary<StableId,FrameworkElement>NodeElements=[];
    private IReadOnlyList<StableId>ClipboardNodeIds=[];
    private StableId?ConnectionSource;
    private StableId?DragNode;
    private Point DragOffset;
    private float ViewOriginX;
    private float ViewOriginY;
    internal ShaderGraphEditorControl(ShaderGraphEditorDocument Document) {
        this.Document=Document;
        this.InitializeComponent();
        this.Document.Changed+=this.OnDocumentChanged;
        this.PreviewKeyDown+=this.OnPreviewKeyDown;
        this.PopulateEntryTabs();
        this.RefreshPalette();
        this.RefreshAll();
    }
    internal ShaderGraphEditorDocument Document { get; }
    internal event Action?SaveRequested;
    internal event Action?OpenGeneratedHlslRequested;
    internal event Action<string>?OpenDependencyRequested;
    internal IReadOnlyList<string>DependencyPaths=>this.Document.Graph.Dependencies.Select(Dependency=>Dependency.Path).ToArray();
    internal void SetStatusText(string Text)=>this.StatusText.Text=Text;
    private void OnDocumentChanged() {
        this.RefreshAll();
    }
    private void PopulateEntryTabs() {
        var Selected=(this.EntryTabs.SelectedItem as ShaderGraphEntryTab)?.NodeId;
        this.EntryTabs.Items.Clear();
        var Root=this.Document.Graph.Subgraphs.Single(Subgraph=>Subgraph.Id==this.Document.Graph.RootSubgraphId);
        var Translation=Root.Nodes.FirstOrDefault(Node=>Node.GetArgumentValue(HlslSyntaxGraphContract.ClrTypeArgument)=="UnityShaderParser.HLSL.TranslationUnitNode");
        if(Translation is not null) {
            foreach(var Edge in Root.Edges.Where(Edge=>Edge.ToNodeId==Translation.Id).OrderBy(Edge=>Edge.ToPort,StringComparer.Ordinal)) {
                var Node=Root.Nodes.First(Item=>Item.Id==Edge.FromNodeId);
                this.EntryTabs.Items.Add(new ShaderGraphEntryTab(Node.Name??Node.Type,Node.Id));
            }
        }
        if(this.EntryTabs.Items.Count>0) {
            this.EntryTabs.SelectedIndex=Math.Max(0,this.EntryTabs.Items.Cast<ShaderGraphEntryTab>().ToList().FindIndex(Item=>Item.NodeId==Selected));
        }
    }
    private void RefreshAll() {
        this.UndoButton.IsEnabled=this.Document.CanUndo;
        this.RedoButton.IsEnabled=this.Document.CanRedo;
        this.SaveButton.IsEnabled=this.Document.IsDirty;
        this.RefreshDependencies();
        this.RefreshCanvas();
        this.RefreshInspector();
        this.RefreshDiagnostics();
    }
    private void RefreshDependencies() {
        this.DependencyList.ItemsSource=this.DependencyPaths;
    }
    private void RefreshPalette() {
        this.PaletteList.ItemsSource=this.Document.GetPaletteNodeTypes(this.PaletteSearch?.Text);
    }
    private void RefreshCanvas() {
        this.NodeCanvas.Children.Clear();
        this.NodeElements.Clear();
        var Root=this.Document.Graph.Subgraphs.Single(Subgraph=>Subgraph.Id==this.Document.Graph.RootSubgraphId);
        var Visible=this.GetVisibleNodeIds(Root);
        var Layout=this.Document.Graph.Layout?.Nodes.ToDictionary(Item=>Item.NodeId)??new Dictionary<StableId,ShaderGraphNodeLayout>();
        var VisibleLayouts=Visible.Where(Layout.ContainsKey).Select(Id=>Layout[Id]).ToArray();
        this.ViewOriginX=VisibleLayouts.Length==0?0:VisibleLayouts.Min(Item=>Item.X)-40;
        this.ViewOriginY=VisibleLayouts.Length==0?0:VisibleLayouts.Min(Item=>Item.Y)-40;
        var MaxX=VisibleLayouts.Length==0?1200:VisibleLayouts.Max(Item=>Item.X)-this.ViewOriginX+360;
        var MaxY=VisibleLayouts.Length==0?900:VisibleLayouts.Max(Item=>Item.Y)-this.ViewOriginY+180;
        this.NodeCanvas.Width=Math.Max(1200,MaxX);
        this.NodeCanvas.Height=Math.Max(900,MaxY);
        this.DrawRegions(Root,Visible,Layout);
        foreach(var Edge in Root.Edges.Where(Edge=>Visible.Contains(Edge.FromNodeId)&&Visible.Contains(Edge.ToNodeId))) {
            if(!Layout.TryGetValue(Edge.FromNodeId,out var From)||!Layout.TryGetValue(Edge.ToNodeId,out var To)) { continue; }
            this.NodeCanvas.Children.Add(new Line { X1=From.X-this.ViewOriginX+220,Y1=From.Y-this.ViewOriginY+34,X2=To.X-this.ViewOriginX,Y2=To.Y-this.ViewOriginY+34,Stroke=new SolidColorBrush(Color.FromRgb(74,156,211)),StrokeThickness=1.5,IsHitTestVisible=false });
        }
        foreach(var Node in Root.Nodes.Where(Node=>Visible.Contains(Node.Id))) {
            var Position=Layout.TryGetValue(Node.Id,out var Item)?Item:new ShaderGraphNodeLayout { NodeId=Node.Id,X=0,Y=0 };
            var Border=this.CreateNodeElement(Node);
            Canvas.SetLeft(Border,Position.X-this.ViewOriginX);
            Canvas.SetTop(Border,Position.Y-this.ViewOriginY);
            this.NodeCanvas.Children.Add(Border);
            this.NodeElements[Node.Id]=Border;
        }
        this.NodeCanvas.LayoutTransform=new ScaleTransform(this.ZoomSlider.Value,this.ZoomSlider.Value);
        this.StatusText.Text=VsixText.Format("ShaderGraphStatus",this.Document.Graph.Target.Kind,Visible.Count,Root.Nodes.Count,ShaderGraphCanonicalization.ComputeSemanticHash(this.Document.Graph).Substring(0,12));
    }
    private HashSet<StableId>GetVisibleNodeIds(ShaderGraphSubgraph Root) {
        if(this.EntryTabs.SelectedItem is not ShaderGraphEntryTab Tab) { return new HashSet<StableId>(Root.Nodes.Select(Node=>Node.Id)); }
        var Visible=new HashSet<StableId>();
        var Stack=new Stack<StableId>();
        Stack.Push(Tab.NodeId);
        while(Stack.Count>0) {
            var Current=Stack.Pop();
            if(!Visible.Add(Current)) { continue; }
            foreach(var Edge in Root.Edges.Where(Edge=>Edge.ToNodeId==Current)) { Stack.Push(Edge.FromNodeId); }
        }
        return Visible;
    }
    private void DrawRegions(ShaderGraphSubgraph Root,HashSet<StableId>Visible,IReadOnlyDictionary<StableId,ShaderGraphNodeLayout>Layout) {
        foreach(var Region in Root.Regions.Where(Region=>Region.NodeIds.Any(Visible.Contains))) {
            var RegionLayout=this.Document.Graph.Layout?.Regions.FirstOrDefault(Item=>Item.RegionId==Region.Id);
            if(RegionLayout is null) { continue; }
            var Rectangle=new Rectangle { Width=RegionLayout.Width,Height=RegionLayout.Height,Fill=new SolidColorBrush(Color.FromArgb(24,86,156,214)),Stroke=new SolidColorBrush(Color.FromRgb(86,156,214)),StrokeThickness=1,IsHitTestVisible=false,ToolTip=Region.Name };
            Canvas.SetLeft(Rectangle,RegionLayout.X-this.ViewOriginX);
            Canvas.SetTop(Rectangle,RegionLayout.Y-this.ViewOriginY);
            this.NodeCanvas.Children.Add(Rectangle);
        }
    }
    private Border CreateNodeElement(ShaderGraphNode Node) {
        var Panel=new StackPanel();
        Panel.Children.Add(new TextBlock { Text=Node.Name??Node.Type,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(6,4,6,2) });
        Panel.Children.Add(new TextBlock { Text=Node.Type,Foreground=new SolidColorBrush(Color.FromRgb(180,180,180)),FontSize=10,Margin=new Thickness(6,0,6,4),TextTrimming=TextTrimming.CharacterEllipsis });
        var Ports=HlslSyntaxGraphContract.GetRequiredChildPorts(Node);
        if(Ports.Count>0) { Panel.Children.Add(new TextBlock { Text=VsixText.Format("ShaderGraphPortSummary",string.Join(", ",Ports.Take(3))+(Ports.Count>3?" …":"")),FontSize=9,Margin=new Thickness(6,0,6,4) }); }
        var Border=new Border { Width=230,MinHeight=64,Background=new SolidColorBrush(Color.FromRgb(45,45,48)),BorderBrush=this.Selection.Contains(Node.Id)?Brushes.DeepSkyBlue:new SolidColorBrush(Color.FromRgb(90,90,90)),BorderThickness=new Thickness(this.Selection.Contains(Node.Id)?2:1),CornerRadius=new CornerRadius(3),Child=Panel,Tag=Node.Id,ToolTip=Node.Id.ToString() };
        Border.MouseLeftButtonDown+=this.Node_MouseLeftButtonDown;
        Border.MouseMove+=this.Node_MouseMove;
        Border.MouseLeftButtonUp+=this.Node_MouseLeftButtonUp;
        Border.ContextMenu=this.CreateNodeContextMenu(Node);
        return Border;
    }
    private ContextMenu CreateNodeContextMenu(ShaderGraphNode Node) {
        var Menu=new ContextMenu();
        var Source=new MenuItem { Header=VsixText.Get("ShaderGraphUseSyntaxSource"),Tag=Node.Id };
        Source.Click+=(Sender,Arguments)=>{ this.ConnectionSource=(StableId)((MenuItem)Sender).Tag;this.StatusText.Text=VsixText.Get("ShaderGraphConnectionSelected"); };
        Menu.Items.Add(Source);
        if(this.ConnectionSource is { }SourceId&&SourceId!=Node.Id) {
            foreach(var Port in HlslSyntaxGraphContract.GetRequiredChildPorts(Node)) {
                var Connect=new MenuItem { Header=VsixText.Format("ShaderGraphConnectSourceTo",Port),Tag=Port };
                Connect.Click+=(Sender,Arguments)=>{ if(this.Document.TryConnect(SourceId,Node.Id,(string)((MenuItem)Sender).Tag,out var Error)){this.ConnectionSource=null;}else{this.StatusText.Text=Error;} };
                Menu.Items.Add(Connect);
            }
        }
        return Menu;
    }
    private void Node_MouseLeftButtonDown(object Sender,MouseButtonEventArgs Arguments) {
        var Element=(FrameworkElement)Sender;
        var Id=(StableId)Element.Tag;
        if((Keyboard.Modifiers&ModifierKeys.Control)==0) { this.Selection.Clear(); }
        if(!this.Selection.Add(Id)&&(Keyboard.Modifiers&ModifierKeys.Control)!=0) { this.Selection.Remove(Id); }
        this.DragNode=Id;
        var Position=Arguments.GetPosition(this.NodeCanvas);
        this.DragOffset=new Point(Position.X-Canvas.GetLeft(Element),Position.Y-Canvas.GetTop(Element));
        Element.CaptureMouse();
        this.RefreshInspector();
        this.RefreshCanvas();
        Arguments.Handled=true;
    }
    private void Node_MouseMove(object Sender,MouseEventArgs Arguments) {
        if(this.DragNode is null||Arguments.LeftButton!=MouseButtonState.Pressed) { return; }
        var Element=(FrameworkElement)Sender;
        var Position=Arguments.GetPosition(this.NodeCanvas);
        Canvas.SetLeft(Element,Math.Max(0,Position.X-this.DragOffset.X));
        Canvas.SetTop(Element,Math.Max(0,Position.Y-this.DragOffset.Y));
    }
    private void Node_MouseLeftButtonUp(object Sender,MouseButtonEventArgs Arguments) {
        var Element=(FrameworkElement)Sender;
        Element.ReleaseMouseCapture();
        if(this.DragNode is { }Id) {
            this.Document.MoveNode(Id,(float)Canvas.GetLeft(Element)+this.ViewOriginX,(float)Canvas.GetTop(Element)+this.ViewOriginY);
        }
        this.DragNode=null;
    }
    private void RefreshInspector() {
        this.PropertyPanel.Children.Clear();
        if(this.Selection.Count!=1) { this.PropertyPanel.Children.Add(new TextBlock { Text=this.Selection.Count==0?VsixText.Get("ShaderGraphSelectNode"):VsixText.Format("ShaderGraphSelectedNodes",this.Selection.Count),Margin=new Thickness(4) });return; }
        var Node=this.Document.GetNodes().FirstOrDefault(Item=>Item.Id==this.Selection.Single());
        if(Node is null) { return; }
        this.PropertyPanel.Children.Add(new TextBlock { Text=Node.Name??Node.Type,FontWeight=FontWeights.Bold,Margin=new Thickness(4) });
        foreach(var Argument in Node.Arguments.Where(Item=>Item.Name.StartsWith(HlslSyntaxGraphContract.PropertyPrefix,StringComparison.Ordinal))) {
            var Label=new TextBlock { Text=Argument.Name.Substring(HlslSyntaxGraphContract.PropertyPrefix.Length),Margin=new Thickness(4,4,4,0) };
            var Editor=new TextBox { Text=Argument.Value,Tag=(Node.Id,Argument.Name),Margin=new Thickness(4,0,4,2) };
            Editor.LostKeyboardFocus+=(Sender,Arguments)=>{ var Box=(TextBox)Sender;var Tag=((StableId Id,string Name))Box.Tag;_=this.Document.SetArgument(Tag.Id,Tag.Name,Box.Text); };
            this.PropertyPanel.Children.Add(Label);
            this.PropertyPanel.Children.Add(Editor);
        }
    }
    private void RefreshDiagnostics() {
        var Diagnostics=this.Document.Validate().Diagnostics;
        this.DiagnosticsList.ItemsSource=Diagnostics.Select(Item=>$"{Item.Code}: {Item.Message}").ToArray();
    }
    private void PaletteSearch_TextChanged(object Sender,TextChangedEventArgs Arguments)=>this.RefreshPalette();
    private void DependencyList_MouseDoubleClick(object Sender,MouseButtonEventArgs Arguments) {
        if(this.DependencyList.SelectedItem is string PathValue) { this.OpenDependencyRequested?.Invoke(PathValue); }
    }
    private void PaletteList_MouseDoubleClick(object Sender,MouseButtonEventArgs Arguments) {
        if(this.PaletteList.SelectedItem is string Type) { _=this.Document.AddFromPrototype(Type,(float)this.CanvasScroll.HorizontalOffset+80+this.ViewOriginX,(float)this.CanvasScroll.VerticalOffset+80+this.ViewOriginY); }
    }
    private void EntryTabs_SelectionChanged(object Sender,SelectionChangedEventArgs Arguments)=>this.RefreshCanvas();
    private void ZoomSlider_ValueChanged(object Sender,RoutedPropertyChangedEventArgs<double>Arguments) { if(this.NodeCanvas is not null) { this.NodeCanvas.LayoutTransform=new ScaleTransform(Arguments.NewValue,Arguments.NewValue); } }
    private void CanvasScroll_PreviewMouseWheel(object Sender,MouseWheelEventArgs Arguments) { if((Keyboard.Modifiers&ModifierKeys.Control)==0){return;}this.ZoomSlider.Value=Math.Max(this.ZoomSlider.Minimum,Math.Min(this.ZoomSlider.Maximum,this.ZoomSlider.Value+(Arguments.Delta>0?0.1:-0.1)));Arguments.Handled=true; }
    private void Undo_Click(object Sender,RoutedEventArgs Arguments)=>_=this.Document.Undo();
    private void Redo_Click(object Sender,RoutedEventArgs Arguments)=>_=this.Document.Redo();
    private void Save_Click(object Sender,RoutedEventArgs Arguments)=>this.SaveRequested?.Invoke();
    private void OpenHlsl_Click(object Sender,RoutedEventArgs Arguments)=>this.OpenGeneratedHlslRequested?.Invoke();
    private void Copy_Click(object Sender,RoutedEventArgs Arguments) { this.ClipboardNodeIds=this.Selection.ToArray();Clipboard.SetText(string.Join(Environment.NewLine,this.ClipboardNodeIds)); }
    private void Paste_Click(object Sender,RoutedEventArgs Arguments) { var Added=this.Document.Duplicate(this.ClipboardNodeIds);this.Selection.Clear();foreach(var Id in Added){this.Selection.Add(Id);} }
    private void Duplicate_Click(object Sender,RoutedEventArgs Arguments) { var Added=this.Document.Duplicate(this.Selection.ToArray());this.Selection.Clear();foreach(var Id in Added){this.Selection.Add(Id);} }
    private void Delete_Click(object Sender,RoutedEventArgs Arguments) { if(this.Document.Delete(this.Selection.ToArray())){this.Selection.Clear();} }
    private void Frame_Click(object Sender,RoutedEventArgs Arguments)=>_=this.Document.CreateRegion(this.Selection.ToArray(),"Comment");
    private void OnPreviewKeyDown(object Sender,KeyEventArgs Arguments) {
        if((Keyboard.Modifiers&ModifierKeys.Control)!=0&&Arguments.Key==Key.Z){_=this.Document.Undo();Arguments.Handled=true;}
        else if((Keyboard.Modifiers&ModifierKeys.Control)!=0&&Arguments.Key==Key.Y){_=this.Document.Redo();Arguments.Handled=true;}
        else if((Keyboard.Modifiers&ModifierKeys.Control)!=0&&Arguments.Key==Key.C){this.Copy_Click(Sender,new RoutedEventArgs());Arguments.Handled=true;}
        else if((Keyboard.Modifiers&ModifierKeys.Control)!=0&&Arguments.Key==Key.V){this.Paste_Click(Sender,new RoutedEventArgs());Arguments.Handled=true;}
        else if(Arguments.Key==Key.Delete){this.Delete_Click(Sender,new RoutedEventArgs());Arguments.Handled=true;}
    }
}