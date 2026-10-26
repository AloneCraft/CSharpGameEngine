using System.Numerics;
using System.Runtime.InteropServices;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>スクリーン上（またはボックス内）で点を配置する場所。四隅、辺の中点、中央を指定できる。UMG のアンカーと配置と同様に、要素の <c>Pivot</c> を画面の <c>Anchor</c> にオフセットを加えた位置へ置く。</summary>
public enum ScreenAnchor:byte {
    TopLeft=0,
    TopCenter=1,
    TopRight=2,
    MiddleLeft=3,
    Center=4,
    MiddleRight=5,
    BottomLeft=6,
    BottomCenter=7,
    BottomRight=8,
}

/// <summary>
/// 完成フレーム上に描画するテキスト（ADR-0078/M0104）。サイズとオフセットは、短辺が <see cref="ScreenUi.ReferenceShortestSide"/>（1080）
/// ピクセルの画面を実際の描画面に合わせて拡大縮小した座標で指定する。色はアルファ乗算前の sRGB。UMG の TextBlock をモデルとする。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,20)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/ScreenText.svg")]
public struct ScreenText {
    /// <summary>テキスト。<c>\n</c> で改行する。</summary>
    public string Text;
    /// <summary>
    /// 焼き付け済みフォント。エンジン付属の <c>Fonts/Roboto-Regular.cgefont</c> と <c>Fonts/Roboto-Light.cgefont</c>、またはゲームの
    /// Assets 内の <c>.cgefont</c> を指定する。
    /// </summary>
    public string Font;
    /// <summary>em あたりのピクセル数。</summary>
    public float Size;
    /// <summary>テキストボックス内での行の配置。</summary>
    public TextAlignment Alignment;
    /// <summary>画面上でテキストを配置する基準点。</summary>
    public ScreenAnchor Anchor;
    /// <summary>アンカーに重ねるテキストボックス側の基準点。</summary>
    public ScreenAnchor Pivot;
    /// <summary>アンカーから右への距離（ピクセル）。</summary>
    public float OffsetX;
    /// <summary>アンカーから下への距離（ピクセル）。</summary>
    public float OffsetY;
    public float ColorR;
    public float ColorG;
    public float ColorB;
    public float ColorA;
    /// <summary>高いレイヤーの要素が低いレイヤーの要素を覆う。同一レイヤー内の描画順は未定義。</summary>
    public int Layer;
    public bool Visible;

    public ScreenText(){
        this.Text=string.Empty;
        this.Font=ScreenUi.RobotoRegular;
        this.Size=24f;
        this.ColorR=1f;
        this.ColorG=1f;
        this.ColorB=1f;
        this.ColorA=1f;
        this.Visible=true;
    }
}

/// <summary>完成フレーム上に描画する画像。<see cref="Image"/> が空の場合は単色の長方形を描画する（ADR-0078/M0104）。UMG の Image をモデルとする。</summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,21)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/ScreenImage.svg")]
public struct ScreenImage {
    /// <summary>ゲームの Assets 内にある <c>.png</c> または <c>.jpg</c>。空なら色だけの長方形。</summary>
    public string Image;
    /// <summary>幅（ピクセル）。0なら画像本来の幅を使う。</summary>
    public float Width;
    /// <summary>高さ（ピクセル）。0なら画像本来の高さを使う。</summary>
    public float Height;
    public ScreenAnchor Anchor;
    public ScreenAnchor Pivot;
    public float OffsetX;
    public float OffsetY;
    /// <summary>画像の色に乗算する値（アルファ乗算前の sRGB）。</summary>
    public float ColorR;
    public float ColorG;
    public float ColorB;
    public float ColorA;
    public int Layer;
    public bool Visible;

    public ScreenImage(){
        this.Image=string.Empty;
        this.ColorR=1f;
        this.ColorG=1f;
        this.ColorB=1f;
        this.ColorA=1f;
        this.Visible=true;
    }
}

/// <summary>
/// 完成フレーム上に描画するゲーム UI（ADR-0078/M0104）。Presentation 中に収集システムが各 <see cref="ScreenText"/> と
/// <see cref="ScreenImage"/> をこのサービスへ渡し、<see cref="ScreenUiSubmit"/> がレンダラー用の1つのレイヤーにまとめる。システムは
/// <c>ScreenUi Ui</c> のように <see cref="EngineHost"/> サービスをパラメーターとして受け取る。フォントと画像は初回使用時にゲームの隣にあるファイルから読み込む
/// 。読み込みに失敗した場合は一度だけ <see cref="Diagnostics"/> に報告し、その要素は描画しない。
/// </summary>
public sealed class ScreenUi {
    public const string RobotoRegular="Fonts/Roboto-Regular.cgefont";
    public const string RobotoLight="Fonts/Roboto-Light.cgefont";
    /// <summary>1フレームに収集できる要素数の上限。超過分は破棄し、一度だけ報告する。</summary>
    public const int MaximumElements=4096;
    private const float MinimumScale=0.1f;
    private const float MaximumScale=8f;

    private enum ElementKind:byte { Text,Image }
    private readonly record struct Element(ElementKind Kind,int Layer,int Order,int Index);

    private readonly List<Element>Elements=[];
    private readonly List<ScreenText>Texts=[];
    private readonly List<ScreenImage>Images=[];
    private readonly List<RenderScreenVertex>VertexBuffer=[];
    private readonly List<RenderScreenBatch>BatchBuffer=[];
    private readonly Dictionary<string,FontAtlas?>Fonts=new(StringComparer.Ordinal);
    private readonly Dictionary<string,RenderTextureResource?>ImageTextures=new(StringComparer.Ordinal);
    private readonly List<string>DiagnosticList=[];
    private readonly HashSet<string>Reported=new(StringComparer.Ordinal);
    private readonly Func<string,byte[]?>ReadFile;

    internal ScreenUi(EngineHost Host):this(ReadBesideTheGame){
        ArgumentNullException.ThrowIfNull(Host);
    }

    /// <summary>テストとツール用。ファイルの読み込み元に <paramref name="ReadFile"/> を使う UI（ファイルがない場合は null）。</summary>
    public ScreenUi(Func<string,byte[]?>ReadFile){
        ArgumentNullException.ThrowIfNull(ReadFile);
        this.ReadFile=ReadFile;
    }

    /// <summary>サイズとオフセットの基準となる画面の短辺の長さ。実際の描画面はこの値に合わせて拡大縮小する。</summary>
    public float ReferenceShortestSide { get;set; }=1080f;

    /// <summary>フォントまたは画像の読み込みで発生した問題。一種類につき一度だけ記録する。</summary>
    public IReadOnlyList<string>Diagnostics=>this.DiagnosticList;

    public void Add(in ScreenText Text){
        if(!Text.Visible||string.IsNullOrEmpty(Text.Text)){ return; }
        if(this.Elements.Count>=MaximumElements){ this.ReportOnce("The screen UI has more than "+MaximumElements+" elements; the rest are not drawn."); return; }
        this.Elements.Add(new Element(ElementKind.Text,Text.Layer,this.Elements.Count,this.Texts.Count));
        this.Texts.Add(Text);
    }

    public void Add(in ScreenImage Image){
        if(!Image.Visible){ return; }
        if(this.Elements.Count>=MaximumElements){ this.ReportOnce("The screen UI has more than "+MaximumElements+" elements; the rest are not drawn."); return; }
        this.Elements.Add(new Element(ElementKind.Image,Image.Layer,this.Elements.Count,this.Images.Count));
        this.Images.Add(Image);
    }

    /// <summary>指定サイズの描画面に対し、前回の呼び出し以降に収集した要素をレイヤーにまとめ、要素を消去する。描画対象がなければ false を返す。戻り値の範囲は次の呼び出しまで有効。</summary>
    public bool Build(int SurfaceWidth,int SurfaceHeight,out ReadOnlySpan<RenderScreenVertex>Vertices,out ReadOnlySpan<RenderScreenBatch>Batches){
        this.VertexBuffer.Clear();
        this.BatchBuffer.Clear();
        try {
            if(this.Elements.Count==0||SurfaceWidth<=0||SurfaceHeight<=0){
                Vertices=default;
                Batches=default;
                return false;
            }
            float Scale=Math.Clamp(Math.Min(SurfaceWidth,SurfaceHeight)/Math.Max(1f,this.ReferenceShortestSide),MinimumScale,MaximumScale);
            this.Elements.Sort(static(Left,Right)=>Left.Layer!=Right.Layer?Left.Layer.CompareTo(Right.Layer):Left.Order.CompareTo(Right.Order));
            RenderTextureResource?BatchTexture=null;
            RenderScreenShading BatchShading=RenderScreenShading.Image;
            int BatchFirst=0;
            foreach(Element Element in this.Elements){
                int Before=this.VertexBuffer.Count;
                RenderTextureResource?Texture;
                RenderScreenShading Shading;
                if(Element.Kind==ElementKind.Text){
                    ScreenText Text=this.Texts[Element.Index];
                    Texture=this.AppendText(in Text,SurfaceWidth,SurfaceHeight,Scale);
                    Shading=RenderScreenShading.DistanceField;
                } else {
                    ScreenImage Image=this.Images[Element.Index];
                    Texture=this.AppendImage(in Image,SurfaceWidth,SurfaceHeight,Scale);
                    Shading=RenderScreenShading.Image;
                }
                if(Texture is null||this.VertexBuffer.Count==Before){
                    this.VertexBuffer.RemoveRange(Before,this.VertexBuffer.Count-Before);
                    continue;
                }
                if(this.VertexBuffer.Count>RenderScreenLayer.MaximumVertices){
                    this.VertexBuffer.RemoveRange(Before,this.VertexBuffer.Count-Before);
                    this.ReportOnce("The screen UI needs more than "+RenderScreenLayer.MaximumVertices+" vertices; the rest are not drawn.");
                    break;
                }
                bool SameBatch=BatchTexture is not null&&ReferenceEquals(BatchTexture,Texture)&&BatchShading==Shading;
                if(!SameBatch){
                    if(BatchTexture is not null){ this.BatchBuffer.Add(new RenderScreenBatch(BatchTexture,BatchShading,BatchFirst,Before-BatchFirst)); }
                    if(this.BatchBuffer.Count>=RenderScreenLayer.MaximumBatches){
                        this.VertexBuffer.RemoveRange(Before,this.VertexBuffer.Count-Before);
                        this.ReportOnce("The screen UI uses more than "+RenderScreenLayer.MaximumBatches+" fonts and images in one frame; the rest are not drawn.");
                        BatchTexture=null;
                        break;
                    }
                    BatchTexture=Texture;
                    BatchShading=Shading;
                    BatchFirst=Before;
                }
            }
            if(BatchTexture is not null){ this.BatchBuffer.Add(new RenderScreenBatch(BatchTexture,BatchShading,BatchFirst,this.VertexBuffer.Count-BatchFirst)); }
            if(this.BatchBuffer.Count==0){
                Vertices=default;
                Batches=default;
                return false;
            }
            Vertices=CollectionsMarshal.AsSpan(this.VertexBuffer);
            Batches=CollectionsMarshal.AsSpan(this.BatchBuffer);
            return true;
        } finally {
            this.Elements.Clear();
            this.Texts.Clear();
            this.Images.Clear();
        }
    }

    private RenderTextureResource?AppendText(in ScreenText Text,int SurfaceWidth,int SurfaceHeight,float Scale){
        if(!(Text.Size>0f)||!IsFinite(Text.OffsetX,Text.OffsetY,Text.ColorR,Text.ColorG,Text.ColorB,Text.ColorA)){ return null; }
        FontAtlas?Font=this.LoadFont(Text.Font);
        if(Font is null){ return null; }
        float Pixels=Text.Size*Scale;
        Vector2 Box=ScreenTextLayout.Measure(Font,Text.Text,Pixels);
        Vector2 TopLeft=ScreenUiPlacement.TopLeft(Text.Anchor,Text.Pivot,new Vector2(Text.OffsetX*Scale,Text.OffsetY*Scale),Box,SurfaceWidth,SurfaceHeight);
        ScreenTextLayout.Append(this.VertexBuffer,Font,Text.Text,Pixels,Text.Alignment,TopLeft,ColorOf(Text.ColorR,Text.ColorG,Text.ColorB,Text.ColorA));
        return Font.Texture;
    }

    private RenderTextureResource?AppendImage(in ScreenImage Image,int SurfaceWidth,int SurfaceHeight,float Scale){
        if(!IsFinite(Image.Width,Image.Height,Image.OffsetX,Image.OffsetY,Image.ColorR,Image.ColorG,Image.ColorB,Image.ColorA)||Image.Width<0f||Image.Height<0f){ return null; }
        RenderTextureResource Texture;
        float Width=Image.Width*Scale;
        float Height=Image.Height*Scale;
        if(string.IsNullOrEmpty(Image.Image)){
            Texture=RenderMaterialDefaults.White;
        } else {
            RenderTextureResource?Loaded=this.LoadImage(Image.Image);
            if(Loaded is null){ return null; }
            Texture=Loaded;
            if(Image.Width==0f){ Width=Texture.Width*Scale; }
            if(Image.Height==0f){ Height=Texture.Height*Scale; }
        }
        if(!(Width>0f)||!(Height>0f)){ return null; }
        Vector2 TopLeft=ScreenUiPlacement.TopLeft(Image.Anchor,Image.Pivot,new Vector2(Image.OffsetX*Scale,Image.OffsetY*Scale),new Vector2(Width,Height),SurfaceWidth,SurfaceHeight);
        Vector4 Color=ColorOf(Image.ColorR,Image.ColorG,Image.ColorB,Image.ColorA);
        float Right=TopLeft.X+Width;
        float Bottom=TopLeft.Y+Height;
        RenderScreenVertex A=new(TopLeft,new Vector2(0f,0f),Color);
        RenderScreenVertex B=new(new Vector2(Right,TopLeft.Y),new Vector2(1f,0f),Color);
        RenderScreenVertex C=new(new Vector2(TopLeft.X,Bottom),new Vector2(0f,1f),Color);
        RenderScreenVertex D=new(new Vector2(Right,Bottom),new Vector2(1f,1f),Color);
        this.VertexBuffer.Add(A);
        this.VertexBuffer.Add(B);
        this.VertexBuffer.Add(C);
        this.VertexBuffer.Add(B);
        this.VertexBuffer.Add(D);
        this.VertexBuffer.Add(C);
        return Texture;
    }

    private FontAtlas?LoadFont(string Path){
        if(this.Fonts.TryGetValue(Path,out FontAtlas?Cached)){ return Cached; }
        FontAtlas?Font=null;
        try {
            byte[]?Bytes=this.ReadFile(Path);
            if(Bytes is null){ this.ReportOnce($"The font '{Path}' was not found."); }
            else { Font=FontAtlas.Parse(Bytes); }
        } catch(Exception Error) when(Error is FormatException or IOException or UnauthorizedAccessException){
            this.ReportOnce($"The font '{Path}' could not be read: {Error.Message}");
        }
        this.Fonts[Path]=Font;
        return Font;
    }

    private RenderTextureResource?LoadImage(string Path){
        if(this.ImageTextures.TryGetValue(Path,out RenderTextureResource?Cached)){ return Cached; }
        RenderTextureResource?Texture=null;
        try {
            byte[]?Bytes=this.ReadFile(Path);
            if(Bytes is null){ this.ReportOnce($"The image '{Path}' was not found."); }
            else { Texture=RenderTextureArtifactAdapter.DecodeOptional(Bytes); }
        } catch(Exception Error) when(Error is InvalidDataException or IOException or UnauthorizedAccessException){
            this.ReportOnce($"The image '{Path}' could not be read: {Error.Message}");
        }
        this.ImageTextures[Path]=Texture;
        return Texture;
    }

    private void ReportOnce(string Message){
        if(!this.Reported.Add(Message)){ return; }
        this.DiagnosticList.Add(Message);
        Console.Error.WriteLine("CGE-SCREEN-UI "+Message);
    }

    /// <summary>頂点バイト列の64ビット FNV-1a ハッシュ。描画結果が異なる2つのレイヤーは、ほぼ確実に異なるハッシュを持つ。同じレイヤーならハッシュも同じ。</summary>
    public static ulong Fingerprint(ReadOnlySpan<RenderScreenVertex>Vertices){
        ulong Hash=14695981039346656037UL;
        foreach(byte Value in MemoryMarshal.AsBytes(Vertices)){
            Hash^=Value;
            Hash*=1099511628211UL;
        }
        return Hash;
    }

    private static byte[]?ReadBesideTheGame(string Path){
        string Full=System.IO.Path.IsPathRooted(Path)?Path:System.IO.Path.Combine(AppContext.BaseDirectory,Path);
        return File.Exists(Full)?File.ReadAllBytes(Full):null;
    }

    private static bool IsFinite(params ReadOnlySpan<float>Values){
        foreach(float Value in Values){
            if(!float.IsFinite(Value)){ return false; }
        }
        return true;
    }

    private static Vector4 ColorOf(float R,float G,float B,float A)=>new(Math.Clamp(R,0f,1f),Math.Clamp(G,0f,1f),Math.Clamp(B,0f,1f),Math.Clamp(A,0f,1f));
}

/// <summary>画面上のアンカー、要素自身のボックス内のピボット、オフセットを使って配置する。</summary>
public static class ScreenUiPlacement {
    /// <summary>
    /// 画面の <paramref name="Anchor"/> を <paramref name="Offset"/>（右方向と下方向）へ移動した位置に <paramref name="Pivot"/>
    /// を置く、サイズ <paramref name="Size"/> のボックスの左上角（整数ピクセル）。
    /// </summary>
    public static Vector2 TopLeft(ScreenAnchor Anchor,ScreenAnchor Pivot,Vector2 Offset,Vector2 Size,float SurfaceWidth,float SurfaceHeight){
        float X=(FractionX(Anchor)*SurfaceWidth)+Offset.X-(FractionX(Pivot)*Size.X);
        float Y=(FractionY(Anchor)*SurfaceHeight)+Offset.Y-(FractionY(Pivot)*Size.Y);
        return new Vector2(MathF.Round(X),MathF.Round(Y));
    }

    public static float FractionX(ScreenAnchor Anchor)=>((int)Anchor%3) switch{ 0=>0f,1=>0.5f,_=>1f };

    public static float FractionY(ScreenAnchor Anchor)=>((int)Anchor/3) switch{ 0=>0f,1=>0.5f,_=>1f };
}
