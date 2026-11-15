using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Texture Editor の画像表示。ホイールでズーム、左ドラッグでパン、アルファは市松模様の上に合成する。
/// チャンネル表示は BGRA32 のピクセル列から作り直した <see cref="WriteableBitmap"/> を表示する（<see cref="AssetImageChannels"/>）。
/// 倍率は元画像のピクセルに対する比で、プレビューが縮小されている場合は <see cref="AssetImageZoom.DisplayScale"/> で補正する。
/// </summary>
internal sealed class AssetImageView:Grid {
    private readonly ScrollViewer Scroller=new(){ HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=false };
    private readonly Image Picture=new(){ Stretch=Stretch.Fill,SnapsToDevicePixels=true };
    private readonly Border Backdrop=new(){ HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
    private byte[]?Pixels;
    private int PreviewWidth;
    private int PreviewHeight;
    private int OriginalWidth;
    private AssetImageChannelMode Mode=AssetImageChannelMode.Color;
    private float CurrentZoom=1f;
    private bool FitOnNextLayout=true;
    private Point DragStart;
    private float DragHorizontalOffset;
    private float DragVerticalOffset;
    private bool Dragging;
    internal event Action<float>?ZoomChanged;
    internal AssetImageView(){
        this.Backdrop.Background=CreateCheckerboard();
        this.Backdrop.Child=this.Picture;
        this.Scroller.Content=this.Backdrop;
        this.Children.Add(this.Scroller);
        this.ClipToBounds=true;
        this.SetResourceReference(Panel.BackgroundProperty,VsBrushes.WindowKey);
        this.SizeChanged+=this.OnSizeChanged;
        this.MouseWheel+=this.OnMouseWheel;
        this.MouseLeftButtonDown+=this.OnMouseDown;
        this.MouseMove+=this.OnMouseMove;
        this.MouseLeftButtonUp+=this.OnMouseUp;
    }
    internal float Zoom=>this.CurrentZoom;
    internal bool HasImage=>this.Pixels is not null;
    /// <summary>派生プレビューを表示する。<paramref name="OriginalWidth"/> は元画像の幅（不明なら 0）。</summary>
    internal void SetImage(BitmapSource Preview,int OriginalWidth){
        this.Pixels=AssetImagePreviewService.ToBgra(Preview,out this.PreviewWidth,out this.PreviewHeight);
        this.OriginalWidth=OriginalWidth;
        this.Refresh();
        this.FitOnNextLayout=true;
        this.FitToView();
    }
    internal AssetImageChannelMode ChannelMode {
        get=>this.Mode;
        set{
            if(this.Mode==value){ return; }
            this.Mode=value;
            this.Refresh();
        }
    }
    internal void FitToView(){
        if(this.Pixels is null){ return; }
        var ViewWidth=(float)this.ActualWidth;
        var ViewHeight=(float)this.ActualHeight;
        if(ViewWidth<=0f||ViewHeight<=0f){ this.FitOnNextLayout=true; return; }
        var OriginalWidthForFit=this.OriginalWidth>0?this.OriginalWidth:this.PreviewWidth;
        var OriginalHeightForFit=this.OriginalWidth>0?(int)((long)this.PreviewHeight*this.OriginalWidth/Math.Max(1,this.PreviewWidth)):this.PreviewHeight;
        this.SetZoom(AssetImageZoom.Fit(ViewWidth-24f,ViewHeight-24f,OriginalWidthForFit,OriginalHeightForFit));
    }
    internal void ActualSize()=>this.SetZoom(1f);
    private void SetZoom(float Zoom){
        this.CurrentZoom=Zoom;
        this.ApplyScale();
        this.ZoomChanged?.Invoke(Zoom);
    }
    private void ApplyScale(){
        if(this.Pixels is null){ return; }
        var Scale=AssetImageZoom.DisplayScale(this.CurrentZoom,this.OriginalWidth,this.PreviewWidth);
        this.Picture.Width=this.PreviewWidth*Scale;
        this.Picture.Height=this.PreviewHeight*Scale;
        RenderOptions.SetBitmapScalingMode(this.Picture,AssetImageZoom.UseNearestNeighbor(this.CurrentZoom)?BitmapScalingMode.NearestNeighbor:BitmapScalingMode.HighQuality);
    }
    private void Refresh(){
        if(this.Pixels is null){ return; }
        var Shown=AssetImageChannels.Extract(this.Pixels,this.Mode);
        var Bitmap=new WriteableBitmap(this.PreviewWidth,this.PreviewHeight,96,96,PixelFormats.Bgra32,null);
        Bitmap.WritePixels(new Int32Rect(0,0,this.PreviewWidth,this.PreviewHeight),Shown,this.PreviewWidth*4,0);
        Bitmap.Freeze();
        this.Picture.Source=Bitmap;
        this.ApplyScale();
    }
    private void OnSizeChanged(object Sender,SizeChangedEventArgs Args){
        if(this.FitOnNextLayout&&this.Pixels is not null){
            this.FitOnNextLayout=false;
            this.FitToView();
        }
    }
    private void OnMouseWheel(object Sender,MouseWheelEventArgs Args){
        if(this.Pixels is null){ return; }
        var Notches=Args.Delta/120;
        if(Notches==0){ return; }
        this.SetZoom(AssetImageZoom.Step(this.CurrentZoom,Notches));
        Args.Handled=true;
    }
    private void OnMouseDown(object Sender,MouseButtonEventArgs Args){
        this.Dragging=true;
        this.DragStart=Args.GetPosition(this);
        this.DragHorizontalOffset=(float)this.Scroller.HorizontalOffset;
        this.DragVerticalOffset=(float)this.Scroller.VerticalOffset;
        this.CaptureMouse();
    }
    private void OnMouseMove(object Sender,MouseEventArgs Args){
        if(!this.Dragging){ return; }
        var Position=Args.GetPosition(this);
        this.Scroller.ScrollToHorizontalOffset(this.DragHorizontalOffset-(Position.X-this.DragStart.X));
        this.Scroller.ScrollToVerticalOffset(this.DragVerticalOffset-(Position.Y-this.DragStart.Y));
    }
    private void OnMouseUp(object Sender,MouseButtonEventArgs Args){
        this.Dragging=false;
        this.ReleaseMouseCapture();
    }
    private static Brush CreateCheckerboard(){
        var Group=new DrawingGroup();
        Group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xB8,0xB8,0xB8)),null,new RectangleGeometry(new Rect(0,0,16,16))));
        var Dark=new GeometryGroup();
        Dark.Children.Add(new RectangleGeometry(new Rect(0,0,8,8)));
        Dark.Children.Add(new RectangleGeometry(new Rect(8,8,8,8)));
        Group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x80,0x80,0x80)),null,Dark));
        var Brush=new DrawingBrush(Group){ TileMode=TileMode.Tile,Viewport=new Rect(0,0,16,16),ViewportUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,16,16),ViewboxUnits=BrushMappingMode.Absolute };
        Brush.Freeze();
        return Brush;
    }
}
