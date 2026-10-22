using System.Numerics;
using System.Runtime.InteropServices;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// Where on the screen (or inside a box) a point sits: the corners, the edge middles and the centre. An element is placed by putting its
/// <c>Pivot</c> on the screen's <c>Anchor</c> plus an offset, the way UMG's anchors and alignment do.
/// </summary>
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
/// Text drawn over the finished frame (ADR-0078/M0104). Sizes and offsets are in pixels of a screen whose shorter side is
/// <see cref="ScreenUi.ReferenceShortestSide"/> (1080), scaled to the real surface; the color is straight sRGB. UMG's TextBlock is the model.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,20)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/ScreenText.svg")]
public struct ScreenText {
    /// <summary>The text; a <c>\n</c> starts a new line.</summary>
    public string Text;
    /// <summary>A baked font: the engine's <c>Fonts/Roboto-Regular.cgefont</c> and <c>Fonts/Roboto-Light.cgefont</c>, or a <c>.cgefont</c> under the game's Assets.</summary>
    public string Font;
    /// <summary>Pixels per em.</summary>
    public float Size;
    /// <summary>Where the lines sit inside the text's box.</summary>
    public TextAlignment Alignment;
    /// <summary>The point of the screen the text is placed from.</summary>
    public ScreenAnchor Anchor;
    /// <summary>The point of the text's box that sits on the anchor.</summary>
    public ScreenAnchor Pivot;
    /// <summary>Right of the anchor, in pixels.</summary>
    public float OffsetX;
    /// <summary>Below the anchor, in pixels.</summary>
    public float OffsetY;
    public float ColorR;
    public float ColorG;
    public float ColorB;
    public float ColorA;
    /// <summary>Elements with a higher layer cover those with a lower one; within a layer the order is not defined.</summary>
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

/// <summary>
/// An image, or a solid rectangle when <see cref="Image"/> is empty, drawn over the finished frame (ADR-0078/M0104). UMG's Image is the model.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,21)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/ScreenImage.svg")]
public struct ScreenImage {
    /// <summary>A <c>.png</c> or <c>.jpg</c> under the game's Assets; empty for a rectangle of the color alone.</summary>
    public string Image;
    /// <summary>Pixels; 0 uses the image's own width.</summary>
    public float Width;
    /// <summary>Pixels; 0 uses the image's own height.</summary>
    public float Height;
    public ScreenAnchor Anchor;
    public ScreenAnchor Pivot;
    public float OffsetX;
    public float OffsetY;
    /// <summary>The image's color is multiplied by this (straight sRGB).</summary>
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
/// The game's UI over the finished frame (ADR-0078/M0104). The collect systems hand each <see cref="ScreenText"/> and <see cref="ScreenImage"/> to
/// this service during Presentation; <see cref="ScreenUiSubmit"/> turns them into one layer for the renderer. A system takes it as a parameter
/// (<c>ScreenUi Ui</c>) like any <see cref="EngineHost"/> service. Fonts and images load from files next to the game on first use; one that cannot
/// load is reported once in <see cref="Diagnostics"/> and its element is not drawn.
/// </summary>
public sealed class ScreenUi {
    public const string RobotoRegular="Fonts/Roboto-Regular.cgefont";
    public const string RobotoLight="Fonts/Roboto-Light.cgefont";
    /// <summary>The most elements one frame collects; the rest are dropped (and reported once).</summary>
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

    /// <summary>For tests and tools: a UI whose files come from <paramref name="ReadFile"/> (null when the file does not exist).</summary>
    public ScreenUi(Func<string,byte[]?>ReadFile){
        ArgumentNullException.ThrowIfNull(ReadFile);
        this.ReadFile=ReadFile;
    }

    /// <summary>The length of the screen's shorter side that sizes and offsets are written for; the real surface is scaled to it.</summary>
    public float ReferenceShortestSide { get;set; }=1080f;

    /// <summary>What went wrong loading fonts and images, once each.</summary>
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

    /// <summary>
    /// Builds the layer of what was collected since the last call, for a surface of the given size, and forgets the elements. Returns false when
    /// there is nothing to draw. The returned spans are valid until the next call.
    /// </summary>
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

    /// <summary>A 64-bit FNV-1a hash of the vertices' bytes: two layers that draw differently almost surely differ, and equal layers are equal.</summary>
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

/// <summary>Placing an element by its anchor on the screen, its pivot in its own box and an offset.</summary>
public static class ScreenUiPlacement {
    /// <summary>
    /// The top-left corner, in whole pixels, of a box of <paramref name="Size"/> whose <paramref name="Pivot"/> point sits on the surface's
    /// <paramref name="Anchor"/> point moved by <paramref name="Offset"/> (right and down).
    /// </summary>
    public static Vector2 TopLeft(ScreenAnchor Anchor,ScreenAnchor Pivot,Vector2 Offset,Vector2 Size,float SurfaceWidth,float SurfaceHeight){
        float X=(FractionX(Anchor)*SurfaceWidth)+Offset.X-(FractionX(Pivot)*Size.X);
        float Y=(FractionY(Anchor)*SurfaceHeight)+Offset.Y-(FractionY(Pivot)*Size.Y);
        return new Vector2(MathF.Round(X),MathF.Round(Y));
    }

    public static float FractionX(ScreenAnchor Anchor)=>((int)Anchor%3) switch{ 0=>0f,1=>0.5f,_=>1f };

    public static float FractionY(ScreenAnchor Anchor)=>((int)Anchor/3) switch{ 0=>0f,1=>0.5f,_=>1f };
}
