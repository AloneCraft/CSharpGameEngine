using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// UE5 の Reference Viewer に倣った参照関係グラフ。左が参照元、中央が起点、右が依存先。
/// ホイールでズーム、背景のドラッグでパン、ノードのダブルクリックでそのファイルを開き、ノードのクリックでアセットなら起点を切り替える。
/// </summary>
internal sealed class AssetReferenceViewerControl:Grid,IDisposable {
    private static readonly Brush FocusBrush=Frozen(Color.FromRgb(0x2E,0x7D,0x32));
    private static readonly Brush ReferencerBrush=Frozen(Color.FromRgb(0x15,0x65,0xC0));
    private static readonly Brush DependencyBrush=Frozen(Color.FromRgb(0xC4,0x90,0x00));
    private static readonly Brush EdgeBrush=Frozen(Color.FromRgb(0xE6,0xE6,0xDC));
    private readonly Func<AssetWorkspaceItem,int,int,CancellationToken,Task<AssetReferenceGraph>> BuildGraph;
    private readonly Func<string,AssetWorkspaceItem?> Resolve;
    private readonly TextBox Address=new(){IsReadOnly=true,Margin=new Thickness(4),VerticalContentAlignment=VerticalAlignment.Center};
    private readonly ComboBox ReferencerDepth=DepthBox();
    private readonly ComboBox DependencyDepth=DepthBox();
    private readonly TextBox Search=new(){Width=200,Margin=new Thickness(4)};
    private readonly TextBlock Message=new(){HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Opacity=0.7f,IsHitTestVisible=false};
    private readonly Canvas Content=new(){Background=Brushes.Transparent};
    private readonly Grid Viewport=new(){ClipToBounds=true,Background=new SolidColorBrush(Color.FromRgb(0x18,0x18,0x18))};
    private readonly MatrixTransform Transform=new(Matrix.Identity);
    private readonly Dictionary<string,FrameworkElement> Elements=new(StringComparer.OrdinalIgnoreCase);
    private AssetWorkspaceItem?Current;
    private AssetReferenceGraph?Graph;
    private CancellationTokenSource?Cancellation;
    private (float X,float Y) PanStart;
    private JoinableTask?ReloadTask;
    private bool Panning;
    private bool Disposed;
    internal event Action<string>?OpenRequested;
    internal event Action<AssetWorkspaceItem>?FocusChanged;
    internal AssetReferenceViewerControl(Func<AssetWorkspaceItem,int,int,CancellationToken,Task<AssetReferenceGraph>> BuildGraph,Func<string,AssetWorkspaceItem?> Resolve){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.BuildGraph=BuildGraph;
        this.Resolve=Resolve;
        AssetTheme.Apply(this);
        this.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        this.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        var Bar=new DockPanel{LastChildFill=true,Margin=new Thickness(4)};
        var Refresh=new Button{Content=VsixText.Get("ReferenceViewer_Refresh"),Margin=new Thickness(4),Padding=new Thickness(10,2,10,2)};
        Refresh.Click+=(_,_)=>this.Reload();
        DockPanel.SetDock(Refresh,Dock.Right);Bar.Children.Add(Refresh);
        DockPanel.SetDock(this.Search,Dock.Right);Bar.Children.Add(this.Search);
        Bar.Children.Add(this.Address);
        var Options=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(4,0,4,4)};
        Options.Children.Add(Label("ReferenceViewer_ReferencersDepth"));Options.Children.Add(this.ReferencerDepth);
        Options.Children.Add(Label("ReferenceViewer_DependenciesDepth"));Options.Children.Add(this.DependencyDepth);
        var Header=new StackPanel();Header.Children.Add(Bar);Header.Children.Add(Options);
        SetRow(Header,0);this.Children.Add(Header);
        this.Content.RenderTransform=this.Transform;
        this.Viewport.Children.Add(this.Content);this.Viewport.Children.Add(this.Message);
        SetRow(this.Viewport,1);this.Children.Add(this.Viewport);
        this.ReferencerDepth.SelectionChanged+=(_,_)=>this.Reload();
        this.DependencyDepth.SelectionChanged+=(_,_)=>this.Reload();
        this.Search.TextChanged+=(_,_)=>this.ApplySearch();
        this.Viewport.MouseWheel+=this.OnWheel;
        this.Viewport.MouseLeftButtonDown+=this.OnPanStart;
        this.Viewport.MouseMove+=this.OnPanMove;
        this.Viewport.MouseLeftButtonUp+=this.OnPanEnd;
        this.Viewport.MouseDown+=(Sender,Args)=>{ if(Args.ChangedButton==MouseButton.Middle){ this.OnPanStart(Sender,Args); } };
        this.Viewport.MouseUp+=(Sender,Args)=>{ if(Args.ChangedButton==MouseButton.Middle){ this.OnPanEnd(Sender,Args); } };
    }
    private static Brush Frozen(Color Value){ var Brush=new SolidColorBrush(Value);Brush.Freeze();return Brush; }
    private static ComboBox DepthBox(){
        var Box=new ComboBox{Width=56,Margin=new Thickness(4,0,16,0)};
        for(var Depth=0;Depth<=AssetReferenceGraphBuilder.MaximumDepth;Depth++){ Box.Items.Add(Depth); }
        Box.SelectedItem=1;
        return Box;
    }
    private static TextBlock Label(string Key)=>new(){Text=VsixText.Get(Key),VerticalAlignment=VerticalAlignment.Center};
    private int SelectedDepth(ComboBox Box)=>Box.SelectedItem is int Value?Value:1;
    /// <summary>起点を切り替えて描き直す。</summary>
    internal void Show(AssetWorkspaceItem Item){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Current=Item;
        this.Address.Text=Item.Source.RelativePath.Length>0?"/"+Item.Source.RelativePath:Item.Source.SourcePath;
        this.Reload();
    }
    private void Reload(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed||this.Current is not{ }Item){ return; }
        this.Cancellation?.Cancel();
        var Source=new CancellationTokenSource();
        this.Cancellation=Source;
        var Referencers=this.SelectedDepth(this.ReferencerDepth);
        var Dependencies=this.SelectedDepth(this.DependencyDepth);
        this.Message.Text=VsixText.Get("ReferenceViewer_Loading");
        this.ReloadTask=ThreadHelper.JoinableTaskFactory.RunAsync(async()=>{
            try {
                var Result=await this.BuildGraph(Item,Referencers,Dependencies,Source.Token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(this.Disposed||Source.IsCancellationRequested){ return; }
                this.Render(Result);
            }catch(OperationCanceledException){ }
            catch(Exception Error){
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(!this.Disposed&&!Source.IsCancellationRequested){ this.Message.Text=Error.Message; }
            }
        });
    }
    private void Render(AssetReferenceGraph Result){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Graph=Result;
        this.Content.Children.Clear();
        this.Elements.Clear();
        var Layout=AssetReferenceGraphLayout.Compute(Result);
        foreach(var Edge in Result.Edges){
            if(!Layout.TryGetValue(Edge.FromPath,out var From)||!Layout.TryGetValue(Edge.ToPath,out var To)){ continue; }
            this.Content.Children.Add(CreateEdge(From.X+AssetReferenceGraphLayout.NodeWidth,From.Y+AssetReferenceGraphLayout.NodeHeight/2,To.X,To.Y+AssetReferenceGraphLayout.NodeHeight/2));
        }
        foreach(var Node in Result.Nodes){
            if(!Layout.TryGetValue(Node.Path,out var Position)){ continue; }
            var Element=this.CreateNode(Node);
            Canvas.SetLeft(Element,Position.X);Canvas.SetTop(Element,Position.Y);
            this.Content.Children.Add(Element);
            this.Elements[Node.Path]=Element;
        }
        this.Message.Text=Result.Nodes.Count<=1?VsixText.Get("ReferenceViewer_Empty"):string.Empty;
        this.ApplySearch();
        this.FitToView();
    }
    private static Path CreateEdge(float StartX,float StartY,float EndX,float EndY){
        var Distance=Math.Max(40f,(EndX-StartX)/2);
        var Figure=new PathFigure{StartPoint=new Point(StartX,StartY),IsClosed=false};
        Figure.Segments.Add(new BezierSegment(new Point(StartX+Distance,StartY),new Point(EndX-Distance,EndY),new Point(EndX,EndY),true));
        var Geometry=new PathGeometry();Geometry.Figures.Add(Figure);
        return new Path{Data=Geometry,Stroke=EdgeBrush,StrokeThickness=2,IsHitTestVisible=false};
    }
    private FrameworkElement CreateNode(AssetReferenceNode Node){
        var HeaderBrush=Node.Role switch{ AssetReferenceNodeRole.Focus=>FocusBrush,AssetReferenceNodeRole.Referencer=>ReferencerBrush,_=>DependencyBrush };
        var Stack=new StackPanel();
        Stack.Children.Add(new Border{Background=HeaderBrush,Padding=new Thickness(6,3,6,3),Child=new TextBlock{Text=Node.Name,Foreground=Brushes.White,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis}});
        Stack.Children.Add(new TextBlock{Text=Node.Kind is{ }Kind?VsixText.Get("Assets_Kind_"+Kind):System.IO.Path.GetExtension(Node.Path).TrimStart('.').ToUpperInvariant(),Foreground=new SolidColorBrush(Color.FromRgb(0xB4,0xB4,0xB4)),FontSize=10,Margin=new Thickness(6,2,6,2),TextTrimming=TextTrimming.CharacterEllipsis});
        var Preview=new Image{Height=60,Margin=new Thickness(6,0,6,6),Stretch=Stretch.Uniform};
        if(this.Resolve(Node.Path) is{ }Item&&Item.Kind is AssetWorkspaceKind.Model or AssetWorkspaceKind.Texture or AssetWorkspaceKind.Image){
            Preview.SetBinding(Image.SourceProperty,new System.Windows.Data.Binding(nameof(AssetWorkspaceRow.Thumbnail)){Source=new AssetWorkspaceRow(Item)});
        }
        Stack.Children.Add(Preview);
        var Border=new Border{Width=AssetReferenceGraphLayout.NodeWidth,Height=AssetReferenceGraphLayout.NodeHeight,Background=new SolidColorBrush(Color.FromRgb(0x2D,0x2D,0x30)),BorderBrush=Node.Role==AssetReferenceNodeRole.Focus?Brushes.White:new SolidColorBrush(Color.FromRgb(0x5A,0x5A,0x5A)),BorderThickness=new Thickness(Node.Role==AssetReferenceNodeRole.Focus?2:1),CornerRadius=new CornerRadius(3),Child=Stack,ToolTip=Node.Path,Cursor=Cursors.Hand};
        Border.MouseLeftButtonDown+=(_,Args)=>{
            ThreadHelper.ThrowIfNotOnUIThread();
            Args.Handled=true;
            if(Args.ClickCount>=2){ this.OpenRequested?.Invoke(Node.Path);return; }
            if(Node.Role!=AssetReferenceNodeRole.Focus&&Node.IsAsset&&this.Resolve(Node.Path) is{ }Target){ this.FocusChanged?.Invoke(Target);this.Show(Target); }
        };
        return Border;
    }
    private void ApplySearch(){
        var Text=this.Search.Text.Trim();
        foreach(var Pair in this.Elements){
            Pair.Value.Opacity=Text.Length==0||System.IO.Path.GetFileName(Pair.Key).IndexOf(Text,StringComparison.OrdinalIgnoreCase)>=0?1f:0.3f;
        }
    }
    private void FitToView(){
        if(this.Graph is null||this.Graph.Nodes.Count==0){ return; }
        var Layout=AssetReferenceGraphLayout.Compute(this.Graph);
        var Width=Layout.Values.Max(static Position=>Position.X)+AssetReferenceGraphLayout.NodeWidth;
        var Height=Layout.Values.Max(static Position=>Position.Y)+AssetReferenceGraphLayout.NodeHeight;
        this.Viewport.UpdateLayout();
        var Measured=ViewportSize(this.Viewport);
        var ViewWidth=Measured.Width>0?Measured.Width:800f;
        var ViewHeight=Measured.Height>0?Measured.Height:500f;
        var Scale=Math.Min(1f,Math.Min((ViewWidth-80)/Width,(ViewHeight-80)/Height));
        Scale=Math.Max(0.2f,Scale);
        this.Transform.Matrix=new Matrix(Scale,0,0,Scale,(ViewWidth-Width*Scale)/2,(ViewHeight-Height*Scale)/2);
    }
    /// <summary>WPF の寸法（double）をここだけで float に変換する。</summary>
    private static (float Width,float Height) ViewportSize(FrameworkElement Element)=>(FloatMath.FromExternal(Element.ActualWidth),FloatMath.FromExternal(Element.ActualHeight));
    private static (float X,float Y) PointerPosition(MouseEventArgs Args,IInputElement Relative){
        var Position=Args.GetPosition(Relative);
        return (FloatMath.FromExternal(Position.X),FloatMath.FromExternal(Position.Y));
    }
    private void OnWheel(object Sender,MouseWheelEventArgs Args){
        var Factor=Args.Delta>0?1.1f:1/1.1f;
        var Matrix=this.Transform.Matrix;
        var Current=FloatMath.FromExternal(Matrix.M11);
        var Scale=Math.Max(0.1f,Math.Min(3f,Current*Factor));
        Factor=Scale/Current;
        var Pointer=PointerPosition(Args,this.Viewport);
        Matrix.ScaleAt(Factor,Factor,Pointer.X,Pointer.Y);
        this.Transform.Matrix=Matrix;
        Args.Handled=true;
    }
    private void OnPanStart(object Sender,MouseButtonEventArgs Args){
        this.Panning=true;
        this.PanStart=PointerPosition(Args,this.Viewport);
        this.Viewport.CaptureMouse();
    }
    private void OnPanMove(object Sender,MouseEventArgs Args){
        if(!this.Panning){ return; }
        var Pointer=PointerPosition(Args,this.Viewport);
        var Matrix=this.Transform.Matrix;
        Matrix.Translate(Pointer.X-this.PanStart.X,Pointer.Y-this.PanStart.Y);
        this.Transform.Matrix=Matrix;
        this.PanStart=Pointer;
    }
    private void OnPanEnd(object Sender,MouseButtonEventArgs Args){
        this.Panning=false;
        this.Viewport.ReleaseMouseCapture();
    }
    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        this.Cancellation?.Cancel();
        this.Cancellation?.Dispose();
        this.Cancellation=null;
    }
}
