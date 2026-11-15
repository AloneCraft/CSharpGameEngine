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
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// 1 つのフォントアセットの中身（描ける文字の判定・対応状況・見本の作り方）。<c>.cgefont</c> はブリッジから字形の一覧を受け取り、
/// <c>.ttf/.otf</c> は WPF の <see cref="GlyphTypeface"/> の対応表を使う。タイル・ツールチップ・Inspector が共有し、直近のものはキャッシュする。
/// </summary>
internal sealed class AssetFontData {
    private const int MaximumCached=16;
    private static readonly object Gate=new();
    private static readonly LinkedList<string>Recency=new();
    private static readonly Dictionary<string,(AssetFontData Data,LinkedListNode<string>Node)>Cache=new(StringComparer.OrdinalIgnoreCase);
    private AssetFontData(string Path,string Name,int GlyphCount,Func<int,bool>Has,FontCoverageReport Report){
        this.Path=Path;this.Name=Name;this.GlyphCount=GlyphCount;this.Has=Has;this.Report=Report;
    }
    internal string Path { get;}
    internal string Name { get;}
    internal int GlyphCount { get;}
    internal Func<int,bool>Has { get;}
    internal FontCoverageReport Report { get;}
    /// <summary><c>.cgefont</c> のとき、ブリッジから受け取った字形・指標・距離画像。</summary>
    internal AssetFontPreview?Baked { get;private init;}
    /// <summary><c>.ttf/.otf</c> のとき、見本の文字に使うフォントファミリー。</summary>
    internal FontFamily?Family { get;private init;}
    internal int AtlasWidth { get;private init;}
    internal int AtlasHeight { get;private init;}
    internal static void ClearCache(){ lock(Gate){ Cache.Clear();Recency.Clear(); } }
    /// <summary>ファイルを読んで作る（バックグラウンドで呼ぶ）。読めなければ null。同じファイル（長さと更新時刻が同じ）はキャッシュを返す。</summary>
    internal static async Task<AssetFontData?>LoadAsync(string Path,CancellationToken Cancellation){
        var Info=new FileInfo(Path);
        if(!Info.Exists){ return null; }
        var Key=Path+"|"+Info.Length+"|"+Info.LastWriteTimeUtc.Ticks+"|"+VsixLocalization.UICulture.Name;
        lock(Gate){
            if(Cache.TryGetValue(Key,out var Hit)){ Recency.Remove(Hit.Node);Recency.AddFirst(Hit.Node);return Hit.Data; }
        }
        AssetFontData?Data;
        var Extension=System.IO.Path.GetExtension(Path).ToLowerInvariant();
        if(Extension==".cgefont"){
            var Context=CgeProjectContext.Resolve(Path);
            if(Context is null){ return null; }
            var Request=new AssetWorkspaceRequest { WorkspaceCommand=2,RequestId=Guid.NewGuid(),Sources=[new AssetWorkspaceSource { SourcePath=Path }] };
            var Result=await Task.Run(()=>AssetAuthoringHostClient.InspectWorkspace(Request,Context.WorkspaceRoot),Cancellation).ConfigureAwait(false);
            Cancellation.ThrowIfCancellationRequested();
            if(Result.FontPreview is not{ }Preview){ return null; }
            Data=FromBaked(Path,Preview);
        } else if(Extension is ".ttf" or ".otf"){
            Data=FromTrueType(Path);
        } else { return null; }
        if(Data is null){ return null; }
        lock(Gate){
            if(Cache.TryGetValue(Key,out var Old)){ Recency.Remove(Old.Node); }
            Cache[Key]=(Data,Recency.AddFirst(Key));
            while(Cache.Count>MaximumCached){ var Last=Recency.Last!;Cache.Remove(Last.Value);Recency.RemoveLast(); }
        }
        return Data;
    }
    internal static AssetFontData FromBaked(string Path,AssetFontPreview Preview){
        var Has=AssetFontCoverage.FromGlyphs(Preview.Glyphs);
        return new AssetFontData(Path,System.IO.Path.GetFileNameWithoutExtension(Path),Preview.GlyphCount,Has,AssetFontCoverage.Analyze(Has,VsixLocalization.UICulture)){
            Baked=Preview,AtlasWidth=Preview.SourceWidth,AtlasHeight=Preview.SourceHeight,
        };
    }
    private static AssetFontData?FromTrueType(string Path){
        try {
            var Typeface=new GlyphTypeface(new Uri(Path));
            var Name=Typeface.FamilyNames.Values.FirstOrDefault()??System.IO.Path.GetFileNameWithoutExtension(Path);
            var Family=new FontFamily(new Uri(System.IO.Path.GetDirectoryName(Path)!+System.IO.Path.DirectorySeparatorChar),"./#"+Name);
            var Has=AssetFontCoverage.FromCharacterMap(Typeface.CharacterToGlyphMap.Keys);
            return new AssetFontData(Path,Name,Typeface.CharacterToGlyphMap.Count,Has,AssetFontCoverage.Analyze(Has,VsixLocalization.UICulture)){ Family=Family };
        } catch(Exception Error)when(Error is IOException or FileFormatException or ArgumentException or UriFormatException or UnauthorizedAccessException or NotSupportedException){
            return null;
        }
    }
    /// <summary>見本の文字列を、このフォントで描いた要素にする（UI スレッドで呼ぶ）。描けない文字は赤い下線つき。大きすぎれば null。</summary>
    internal FrameworkElement?CreateSample(string Text,float Size,Brush Foreground){
        var Ink=Foreground is SolidColorBrush Solid?Solid.Color:Colors.White;
        var Alert=Color.FromRgb(255,96,96);
        if(this.Baked is{ } Preview){
            try {
                var Image=AssetFontSampleRenderer.Render(Preview,Text,Size,Ink,Alert);
                return new Image { Source=AssetFontSampleRenderer.ToBitmap(Image),Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Left,SnapsToDevicePixels=true };
            } catch(ArgumentOutOfRangeException){ return null; }
        }
        if(this.Family is{ } Family){
            var Block=new TextBlock { FontFamily=Family,FontSize=Size,Foreground=Foreground,TextWrapping=TextWrapping.Wrap };
            foreach(var CodePoint in AssetFontCoverage.CodePoints(Text)){
                var Run=new System.Windows.Documents.Run(char.ConvertFromUtf32(CodePoint));
                if(!AssetFontCoverage.IsBlank(CodePoint)&&!this.Has(CodePoint)){ Run.Foreground=new SolidColorBrush(Alert);Run.TextDecorations=System.Windows.TextDecorations.Underline; }
                Block.Inlines.Add(Run);
            }
            return Block;
        }
        return null;
    }
    /// <summary>タイル用の正方形の画像（暗い背景に "Aa"、無ければ最初の描ける 2 文字）。<c>.cgefont</c> は純粋な画素処理で作るのでどのスレッドでも呼べる。</summary>
    internal BitmapSource?CreateTile(int Edge){
        if(this.Baked is not{ } Preview){ return null; }
        var Text=Preview.Glyphs.Any(static Glyph=>Glyph.CodePoint=='A'&&Glyph.HasQuad)&&Preview.Glyphs.Any(static Glyph=>Glyph.CodePoint=='a'&&Glyph.HasQuad)
            ?"Aa":string.Concat(Preview.Glyphs.Where(static Glyph=>Glyph.HasQuad).Take(2).Select(static Glyph=>char.ConvertFromUtf32(Glyph.CodePoint)));
        if(Text.Length==0){ return null; }
        var Size=Edge*0.6f;
        FontSamplePixels?Image=null;
        for(var Attempt=0;Attempt<4;Attempt++){
            Image=AssetFontSampleRenderer.Render(Preview,Text,Size,Color.FromRgb(242,242,242),Color.FromRgb(255,96,96));
            if(Image.Width<=Edge-8&&Image.Height<=Edge-8){ break; }
            Size*=0.8f;
        }
        var Canvas=new byte[Edge*Edge*4];
        for(var Index=0;Index<Canvas.Length;Index+=4){ Canvas[Index]=0x28;Canvas[Index+1]=0x26;Canvas[Index+2]=0x26;Canvas[Index+3]=255; }
        var OffsetX=Math.Max(0,(Edge-Image!.Width)/2);var OffsetY=Math.Max(0,(Edge-Image.Height)/2);
        for(var Y=0;Y<Math.Min(Image.Height,Edge);Y++){
            for(var X=0;X<Math.Min(Image.Width,Edge);X++){
                var Source=(Y*Image.Width+X)*4;var Target=((Y+OffsetY)*Edge+X+OffsetX)*4;
                var Alpha=Image.Pixels[Source+3];
                if(Alpha==0){ continue; }
                // 事前乗算済みの画素を不透明な背景に重ねる。
                for(var Channel=0;Channel<3;Channel++){ Canvas[Target+Channel]=(byte)Math.Min(255,Image.Pixels[Source+Channel]+Canvas[Target+Channel]*(255-Alpha)/255); }
            }
        }
        var Bitmap=BitmapSource.Create(Edge,Edge,96,96,PixelFormats.Bgra32,null,Canvas,Edge*4);
        Bitmap.Freeze();
        return Bitmap;
    }
    /// <summary>タイル用の "Aa"（<c>.ttf/.otf</c>、UI スレッドで呼ぶ）。</summary>
    [ExternalNumericAdapter]
    internal BitmapSource?CreateTrueTypeTile(int Edge){
        if(this.Family is not{ } Family){ return null; }
        var Typeface=new Typeface(Family,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
        var Text=new FormattedText("Aa",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Typeface,Edge*0.5,new SolidColorBrush(Color.FromRgb(242,242,242)),1d);
        var Visual=new DrawingVisual();
        using(var Context=Visual.RenderOpen()){
            Context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x26,0x26,0x28)),null,new Rect(0,0,Edge,Edge));
            Context.DrawText(Text,new Point((Edge-Text.Width)/2,(Edge-Text.Height)/2));
        }
        var Target=new RenderTargetBitmap(Edge,Edge,96,96,PixelFormats.Pbgra32);
        Target.Render(Visual);
        Target.Freeze();
        return Target;
    }
}
