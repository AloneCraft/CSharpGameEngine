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
/// WPF renderer shared by hierarchy rows, the C# glyph margin, and the icon picker.  It renders
/// Visual Studio monikers through <see cref="CrispImage"/> and project image assets through frozen
/// WPF <see cref="ImageSource"/> values, so the same SVG/PNG has one cache entry per file version.
/// </summary>
public sealed class SceneVisualIconPresenter : ContentControl {
    public static readonly DependencyProperty IconProperty=DependencyProperty.Register(
        nameof(Icon),typeof(SceneVisualIcon),typeof(SceneVisualIconPresenter),
        new PropertyMetadata(null,OnVisualPropertyChanged));

    public static readonly DependencyProperty IconSizeProperty=DependencyProperty.Register(
        nameof(IconSize),typeof(double),typeof(SceneVisualIconPresenter),
        new PropertyMetadata(16d,OnVisualPropertyChanged));

    public SceneVisualIcon? Icon {
        get=>(SceneVisualIcon?)this.GetValue(IconProperty);
        set=>this.SetValue(IconProperty,value);
    }

    public double IconSize {
        get=>(double)this.GetValue(IconSizeProperty);
        set=>this.SetValue(IconSizeProperty,value);
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
        SceneVisualIcon Icon=this.Icon??SceneVisualIcon.ComponentDefault();
        double Size=Math.Max(1d,this.IconSize);
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

/// <summary>File-version keyed image cache.  Invalid/missing SVG and PNG files return null safely.</summary>
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
            string Extension=Path.GetExtension(AbsolutePath);
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
                DrawingGroup?Drawing=Reader.Read(AbsolutePath);
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
            // Hand-authored assets must never make an editor margin or hierarchy unusable.  The
            // caller renders the standard Component moniker when decoding fails.
            return null;
        }
    }
}
