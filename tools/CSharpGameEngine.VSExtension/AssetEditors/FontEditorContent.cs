using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Font Editor。.cgefont は焼いた SDF アトラス（Asset Authoring ブリッジが共通の FontAtlas で読んだ距離場）をそのまま表示し、
/// .ttf／.otf は WPF の書体で見本文字を表示する。アトラスの寸法とグリフ数を右に出し、焼き込みの設定は Details（Inspector と同じ経路）に委ねる。
/// フォントの編集・焼き直しは FontBaker の範囲で、このエディターは確認のみ。
/// </summary>
internal sealed class FontEditorContent:AssetEditorContent {
    private readonly AssetDetailsView DetailsView;
    private readonly ScrollViewer CenterRoot=new(){ HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=false };
    private readonly StackPanel CenterPanel=new(){ Margin=new Thickness(16) };
    private readonly Grid DetailsRoot=new();
    private readonly StackPanel Facts=new(){ Margin=new Thickness(8,6,8,6) };
    private CancellationTokenSource Loading=new();
    private JoinableTask?LoadingTask;
    private bool Disposed;
    internal FontEditorContent(string FilePath):base(FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.DetailsView=new AssetDetailsView(FilePath);
        this.DetailsRoot.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        this.DetailsRoot.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        Grid.SetRow(this.Facts,0);
        Grid.SetRow(this.DetailsView,1);
        this.DetailsRoot.Children.Add(this.Facts);
        this.DetailsRoot.Children.Add(this.DetailsView);
        this.CenterRoot.Content=this.CenterPanel;
        this.CenterRoot.SetResourceReference(Control.BackgroundProperty,VsBrushes.WindowKey);
        this.StartLoading();
    }
    internal override UIElement Center=>this.CenterRoot;
    internal override UIElement?Details=>this.DetailsRoot;
    private void StartLoading(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Loading.Cancel();
        this.Loading.Dispose();
        this.Loading=new CancellationTokenSource();
        var Token=this.Loading.Token;
        var Path=this.FilePath;
        this.CenterPanel.Children.Clear();
        this.Facts.Children.Clear();
        this.SetStatus(VsixText.Get("AssetEditor_FontLoading"));
        if(!System.IO.Path.GetExtension(Path).Equals(".cgefont",StringComparison.OrdinalIgnoreCase)){
            this.ShowTrueType(Path);
            return;
        }
        this.LoadingTask=ThreadHelper.JoinableTaskFactory.RunAsync(async()=>{
            try {
                var Context=CgeProjectContext.Resolve(Path)??throw new InvalidOperationException(VsixText.Get("Common_GameProjectNotFound"));
                var Request=new AssetWorkspaceRequest { WorkspaceCommand=2,RequestId=Guid.NewGuid(),Sources=[new AssetWorkspaceSource { SourcePath=Path }] };
                var Result=await Task.Run(()=>AssetAuthoringHostClient.InspectWorkspace(Request,Context.WorkspaceRoot),Token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(this.Disposed||Token.IsCancellationRequested||Result.FontPreview is not{ }Preview){ return; }
                var Bitmap=BitmapSource.Create(Preview.Width,Preview.Height,96,96,PixelFormats.Gray8,null,Preview.Distance,Preview.Width);
                Bitmap.Freeze();
                this.CenterPanel.Children.Add(new Image { Source=Bitmap,Stretch=Stretch.None,HorizontalAlignment=HorizontalAlignment.Left,SnapsToDevicePixels=true });
                var Culture=CultureInfo.CurrentCulture;
                this.Facts.Children.Add(Line(VsixText.Format("AssetEditor_FontAtlas",Preview.SourceWidth.ToString("N0",Culture),Preview.SourceHeight.ToString("N0",Culture))));
                this.Facts.Children.Add(Line(VsixText.Format("AssetEditor_FontGlyphs",Preview.GlyphCount.ToString("N0",Culture))));
                if(Preview.SourceWidth>Preview.Width||Preview.SourceHeight>Preview.Height){
                    this.Facts.Children.Add(Line(VsixText.Format("AssetEditor_FontPreviewScaled",Preview.Width.ToString(Culture),Preview.Height.ToString(Culture))));
                }
                this.SetStatus(VsixText.Get("AssetEditor_FontReady"));
            } catch(OperationCanceledException){
            } catch(Exception Error){
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(this.Disposed||Token.IsCancellationRequested){ return; }
                var Text=VsixText.Format("AssetEditor_FontFailed",Error.Message);
                this.CenterPanel.Children.Add(Line(Text));
                this.SetStatus(Text);
            }
        });
    }
    private void ShowTrueType(string Path){
        try {
            var Typeface=new GlyphTypeface(new Uri(Path));
            var Name=Typeface.FamilyNames.Values.First();
            var Family=new FontFamily(new Uri(System.IO.Path.GetDirectoryName(Path)!+System.IO.Path.DirectorySeparatorChar),"./#"+Name);
            this.Facts.Children.Add(Line(VsixText.Format("AssetEditor_FontFamily",Name)));
            foreach(var Size in new[]{ 12,18,28,48 }){
                this.CenterPanel.Children.Add(new TextBlock { Text=VsixText.Get("Assets_FontSample"),FontFamily=Family,FontSize=Size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,4) });
            }
            foreach(var Child in this.CenterPanel.Children.OfType<TextBlock>()){ Child.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey); }
            this.SetStatus(VsixText.Get("AssetEditor_FontReady"));
        } catch(Exception Error) when(Error is IOException or FileFormatException or ArgumentException or UriFormatException){
            var Text=VsixText.Format("AssetEditor_FontFailed",Error.Message);
            this.CenterPanel.Children.Add(Line(Text));
            this.SetStatus(Text);
        }
    }
    private static TextBlock Line(string Text){
        var Block=new TextBlock { Text=Text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,2) };
        Block.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        return Block;
    }
    internal override bool HandleNativeShortcut(System.Windows.Input.Key Key){
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
        this.DetailsView.Dispose();
    }
}
