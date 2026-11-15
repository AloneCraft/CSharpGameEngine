using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Texture Editor（.png／.jpg／.jpeg 等）。中央に拡大縮小・パン・チャンネル表示（RGBA／RGB／R／G／B／A）できる画像、右に寸法・ファイルサイズと
/// 取り込み設定（Details、Inspector と同じ経路）。ソース画像のデコードは L7 Cook worker の派生プレビューだけで行う
/// （<see cref="AssetImagePreviewService"/>）。デコーダーのない形式はその旨を表示し、取り込み設定は引き続き編集できる。
/// </summary>
internal sealed class TextureEditorContent:AssetEditorContent {
    private readonly AssetImageView View=new();
    private readonly AssetDetailsView DetailsView;
    private readonly Grid DetailsRoot=new();
    private readonly StackPanel Facts=new(){ Margin=new Thickness(8,6,8,6) };
    private readonly TextBlock ZoomText=new(){ VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,4,0),MinWidth=48 };
    private readonly UIElement[]Toolbar;
    private readonly Grid CenterRoot=new();
    private readonly TextBlock Message=new(){ HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(24) };
    private CancellationTokenSource Loading=new();
    private int OriginalWidth;
    private int OriginalHeight;
    private JoinableTask?LoadingTask;
    private bool Disposed;
    internal TextureEditorContent(string FilePath):base(FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.DetailsView=new AssetDetailsView(FilePath);
        this.DetailsRoot.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        this.DetailsRoot.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        Grid.SetRow(this.Facts,0);
        Grid.SetRow(this.DetailsView,1);
        this.DetailsRoot.Children.Add(this.Facts);
        this.DetailsRoot.Children.Add(this.DetailsView);
        this.Message.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        this.CenterRoot.Children.Add(this.View);
        this.CenterRoot.Children.Add(this.Message);
        this.View.MouseDown+=(_,_)=>this.RequestFocus();
        this.View.ZoomChanged+=this.OnZoomChanged;
        this.Toolbar=[this.CreateChannelBox(),AssetEditorShell.CreateButton(VsixText.Get("AssetEditor_FitView"),this.View.FitToView),AssetEditorShell.CreateButton(VsixText.Get("AssetEditor_ActualSize"),this.View.ActualSize),this.ZoomText];
        this.StartLoading();
    }
    internal override UIElement Center=>this.CenterRoot;
    internal override UIElement?Details=>this.DetailsRoot;
    internal override IReadOnlyList<UIElement>ToolbarItems=>this.Toolbar;
    private ComboBox CreateChannelBox(){
        var Box=new ComboBox { Margin=new Thickness(8,0,2,0),MinWidth=130,Focusable=false,ToolTip=VsixText.Get("AssetEditor_Channel") };
        var Modes=new (string Key,AssetImageChannelMode Mode)[] {
            ("AssetEditor_ChannelColor",AssetImageChannelMode.Color),
            ("AssetEditor_ChannelColorOpaque",AssetImageChannelMode.ColorOpaque),
            ("AssetEditor_ChannelRed",AssetImageChannelMode.Red),
            ("AssetEditor_ChannelGreen",AssetImageChannelMode.Green),
            ("AssetEditor_ChannelBlue",AssetImageChannelMode.Blue),
            ("AssetEditor_ChannelAlpha",AssetImageChannelMode.Alpha),
        };
        foreach(var Entry in Modes){ Box.Items.Add(new ComboBoxItem { Content=VsixText.Get(Entry.Key),Tag=Entry.Mode }); }
        Box.SelectedIndex=0;
        Box.SelectionChanged+=(_,_)=>{
            if(Box.SelectedItem is ComboBoxItem{ Tag:AssetImageChannelMode Mode }){ this.View.ChannelMode=Mode; }
        };
        return Box;
    }
    private void OnZoomChanged(float Zoom)=>this.ZoomText.Text=VsixText.Format("AssetEditor_Zoom",(Zoom*100f).ToString("0",CultureInfo.CurrentCulture));
    private void StartLoading(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Loading.Cancel();
        this.Loading.Dispose();
        this.Loading=new CancellationTokenSource();
        var Token=this.Loading.Token;
        var Path=this.FilePath;
        this.View.Visibility=Visibility.Collapsed;
        this.Message.Text=VsixText.Get("AssetEditor_TextureLoading");
        this.Message.Visibility=Visibility.Visible;
        this.SetStatus(VsixText.Get("AssetEditor_TextureLoading"));
        this.ShowFacts(Path,null);
        this.LoadingTask=ThreadHelper.JoinableTaskFactory.RunAsync(async()=>{
            try {
                var Preview=await Task.Run(()=>AssetImagePreviewService.Render(Path,AssetThumbnailRequest.MaximumImageEdge,Token),Token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(this.Disposed||Token.IsCancellationRequested){ return; }
                this.View.SetImage(Preview,this.OriginalWidth);
                this.View.Visibility=Visibility.Visible;
                this.Message.Visibility=Visibility.Collapsed;
                this.ShowFacts(Path,Preview);
                this.SetStatus(VsixText.Get("AssetEditor_TextureReady"));
            } catch(OperationCanceledException){
            } catch(Exception Error){
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(this.Disposed||Token.IsCancellationRequested){ return; }
                var Text=VsixText.Format("AssetEditor_TextureFailed",Error.Message);
                this.Message.Text=Text;
                this.Message.Visibility=Visibility.Visible;
                this.SetStatus(Text);
            }
        });
    }
    private void ShowFacts(string Path,System.Windows.Media.Imaging.BitmapSource?Preview){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Facts.Children.Clear();
        var Culture=CultureInfo.CurrentCulture;
        this.OriginalWidth=0;
        this.OriginalHeight=0;
        if(AssetImageHeader.TryReadFile(Path,out var Width,out var Height)){
            this.OriginalWidth=Width;
            this.OriginalHeight=Height;
            this.Facts.Children.Add(Line(VsixText.Format("AssetEditor_TextureSize",Width.ToString("N0",Culture),Height.ToString("N0",Culture))));
        } else if(Preview is not null){
            this.Facts.Children.Add(Line(VsixText.Format("AssetEditor_TextureSize",Preview.PixelWidth.ToString("N0",Culture),Preview.PixelHeight.ToString("N0",Culture))));
        }
        try {
            this.Facts.Children.Add(Line(VsixText.Format("AssetEditor_TextureFileSize",FormatBytes(new FileInfo(Path).Length,Culture))));
        } catch(Exception Error) when(Error is IOException or UnauthorizedAccessException){
        }
        if(Preview is not null&&this.OriginalWidth>Preview.PixelWidth){
            this.Facts.Children.Add(Line(VsixText.Format("AssetEditor_TexturePreviewScaled",Preview.PixelWidth.ToString("N0",Culture),Preview.PixelHeight.ToString("N0",Culture))));
        }
    }
    private static string FormatBytes(long Length,CultureInfo Culture){
        if(Length<1024){ return Length.ToString("N0",Culture)+" B"; }
        if(Length<1024*1024){ return (Length/1024f).ToString("0.#",Culture)+" KB"; }
        return (Length/(1024f*1024f)).ToString("0.##",Culture)+" MB";
    }
    private static TextBlock Line(string Text){
        var Block=new TextBlock { Text=Text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,2) };
        Block.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        return Block;
    }
    internal override bool HandleKeyDown(Key Key,ModifierKeys Modifiers){
        if(Key==Key.F&&Modifiers==ModifierKeys.None){ this.View.FitToView();return true; }
        if(Key==Key.D1&&Modifiers==ModifierKeys.None){ this.View.ActualSize();return true; }
        return false;
    }
    internal override bool HandleNativeShortcut(Key Key){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.DetailsView.HandleNativeShortcut(Key);
    }
    protected override void OnReload(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.DetailsView.Retarget(this.FilePath);
        this.StartLoading();
    }
    public override void Dispose(){
        this.Disposed=true;
        this.Loading.Cancel();
        this.Loading.Dispose();
        this.View.ZoomChanged-=this.OnZoomChanged;
        this.DetailsView.Dispose();
    }
}
