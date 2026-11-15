using System;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Hierarchy の行、C# のグリフマージン、アイコンピッカーで共有する WPF レンダラー。Visual Studio のモニカーは <see cref="CrispImage"/> で、プロジェクト画像アセットは凍結された WPF <see cref="ImageSource"/> 値で描画するため、同じ SVG/PNG に対しファイルバージョンごとに 1 つのキャッシュ項目を持つ。
/// </summary>
public sealed class SceneVisualIconPresenter : ContentControl {
    public static readonly DependencyProperty IconProperty=DependencyProperty.Register(
        nameof(Icon),typeof(SceneVisualIcon),typeof(SceneVisualIconPresenter),
        new PropertyMetadata(null,OnVisualPropertyChanged));

    public static readonly DependencyProperty IconSizeProperty=CreateIconSizeProperty();

    public SceneVisualIcon? Icon {
        get=>(SceneVisualIcon?)this.GetValue(IconProperty);
        set=>this.SetValue(IconProperty,value);
    }

    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
    public double IconSize {
        get=>(double)this.GetValue(IconSizeProperty);
        set=>this.SetValue(IconSizeProperty,value);
    }

    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
    private static DependencyProperty CreateIconSizeProperty()=>DependencyProperty.Register(
        nameof(IconSize),typeof(double),typeof(SceneVisualIconPresenter),
        new PropertyMetadata(16d,OnVisualPropertyChanged));

    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
    private float ReadIconSize(){
        var Value=this.IconSize;
        if(double.IsNaN(Value)||double.IsInfinity(Value)||Value<float.MinValue||Value>float.MaxValue){ throw new ArithmeticException("The WPF icon size cannot be represented as float."); }
        return(float)Value;
    }

    public SceneVisualIconPresenter(){
        this.Focusable=false;
        this.IsTabStop=false;
        this.SnapsToDevicePixels=true;
        this.IsHitTestVisible=false;
        this.RefreshContent();
    }

    private static void OnVisualPropertyChanged(DependencyObject Object,DependencyPropertyChangedEventArgs Args){
        ((SceneVisualIconPresenter)Object).RefreshContent();
    }

    private void RefreshContent(){
        var Icon=this.Icon??SceneVisualIcon.ComponentDefault();
        float Size=Math.Max(1f,this.ReadIconSize());
        this.Width=Size;
        this.Height=Size;
        this.ToolTip=Icon.ToolTip;

        ImageSource?AssetSource=Icon.IsProjectAsset&&Icon.AbsolutePath is { } AssetPath
            ?SceneProjectIconImageCache.TryGet(AssetPath)
            :null;
        this.Content=AssetSource is not null
            ?new Image {
                Source=AssetSource,
                Width=Size,
                Height=Size,
                Stretch=Stretch.Uniform,
                SnapsToDevicePixels=true,
            }
            :new CrispImage {
                Moniker=Icon.Moniker,
                Width=Size,
                Height=Size,
                Stretch=Stretch.Uniform,
                SnapsToDevicePixels=true,
            };
    }
}

/// <summary>ファイルバージョンをキーにした画像キャッシュ。不正または欠落した SVG/PNG ファイルに対しては安全に null を返す。</summary>
internal static class SceneProjectIconImageCache {
    private readonly struct CacheKey : IEquatable<CacheKey> {
        internal CacheKey(string Path,long LastWriteTicks){
            this.Path=Path;
            this.LastWriteTicks=LastWriteTicks;
        }

        internal string Path { get; }

        internal long LastWriteTicks { get; }

        public bool Equals(CacheKey Other)=>
            this.LastWriteTicks==Other.LastWriteTicks
            &&string.Equals(this.Path,Other.Path,StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object?Object)=>Object is CacheKey Other&&this.Equals(Other);

        public override int GetHashCode()=>
            (StringComparer.OrdinalIgnoreCase.GetHashCode(this.Path)*397)^this.LastWriteTicks.GetHashCode();
    }

    private static readonly ConcurrentDictionary<CacheKey,ImageSource?>Images=[];

    internal static ImageSource?TryGet(string AbsolutePath){
        try {
            if(!File.Exists(AbsolutePath)){
                return null;
            }

            CacheKey Key=new(Path.GetFullPath(AbsolutePath),File.GetLastWriteTimeUtc(AbsolutePath).Ticks);
            return Images.GetOrAdd(Key,static Candidate=>Load(Candidate.Path));
        } catch(ArgumentException){
            return null;
        } catch(IOException){
            return null;
        } catch(UnauthorizedAccessException){
            return null;
        }
    }

    private static ImageSource?Load(string AbsolutePath){
        try {
            var Extension=Path.GetExtension(AbsolutePath);
            if(string.Equals(Extension,".png",StringComparison.OrdinalIgnoreCase)){
                BitmapImage Bitmap=new();
                Bitmap.BeginInit();
                Bitmap.CacheOption=BitmapCacheOption.OnLoad;
                Bitmap.UriSource=new Uri(AbsolutePath,UriKind.Absolute);
                Bitmap.EndInit();
                Bitmap.Freeze();
                return Bitmap;
            }

            if(!string.Equals(Extension,".svg",StringComparison.OrdinalIgnoreCase)){
                return null;
            }

            using(FileSvgReader Reader=new(new WpfDrawingSettings(),isEmbedded:false)){
                var Drawing=Reader.Read(AbsolutePath);
                if(Drawing is null){
                    return null;
                }

                if(Drawing.CanFreeze){
                    Drawing.Freeze();
                }
                DrawingImage Image=new(Drawing);
                if(Image.CanFreeze){
                    Image.Freeze();
                }
                return Image;
            }
        } catch(Exception){
            // 手作りのアセットによって、エディターのマージンや Hierarchy が使えなくなってはならない。
            // デコードに失敗した場合、呼び出し側は標準の Component モニカーを描画する。
            return null;
        }
    }
}
