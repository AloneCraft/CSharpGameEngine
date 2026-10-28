using System.Text.Json;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.EditorPreview.Host;
/// <summary>One isolated snapshot; owns a hidden surface and never starts a game/editor world.</summary>
internal static class AssetThumbnailWorker {
    internal const int Edge=128;
    internal static void Run(string RequestPath,string OutputPath){
        var Input=new FileInfo(RequestPath);
        if(Input.Length is <2 or >32*1024*1024){ throw new InvalidDataException("Thumbnail request exceeds its bound."); }
        var Request=JsonSerializer.Deserialize<AssetThumbnailRequest>(File.ReadAllBytes(RequestPath))??throw new InvalidDataException("Thumbnail request is missing.");
        var Error=AssetThumbnailRequest.Validate(Request);
        if(Error is not null){ throw new InvalidDataException(Error); }
        var Frame=Render(Request,null);
        var Width=Frame.Width;var Height=Frame.Height;var Pixels=Pack(Frame);
        var Destination=Path.GetFullPath(OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        var Temporary=Destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(Temporary,Encode(Width,Height,Pixels));File.Move(Temporary,Destination,true); }
        finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
    }
    /// <summary>Renders one hidden-surface frame and reads it back. A null <paramref name="AdapterOptions"/> uses the default adapter (tests pass WARP).</summary>
    internal static PixelReadbackResult Render(AssetThumbnailRequest Request,GraphicsDeviceOptions?AdapterOptions){
        if(Request.Scene is null){ throw new InvalidDataException("Image thumbnails belong to the Cook worker."); }
        using var Surface=new Win32Window(new(){Title="CSharpGameEngine Asset Thumbnail",Width=Edge,Height=Edge,Visible=false,IgnoreDeviceInput=true});
        using var Session=RenderPreviewRenderSession.Open(new(){RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=1,Command=RenderPreviewHostCommand.Open,
            SurfaceHandle=(long)Surface.Handle,Width=Edge,Height=Edge,ShaderArtifactPath=Request.ShaderArtifactPath,Scene=Request.Scene},AdapterOptions);
        // プレビューレベル（Studio の部屋など）の照明・背景を適用してから描く。なければ従来の既定の見た目。
        if(Request.Lighting is{ } Lighting&&Request.Scene.Camera is{ } Camera){
            Session.UpdateFrame(new RenderPreviewFrame {
                Camera=Camera,PresentationMode=RenderPreviewPresentationMode.Lit,Lighting=Lighting,
                Objects=Request.Scene.Objects.Select(static Object=>new RenderPreviewObjectTransform { ObjectId=Object.ObjectId,World=Object.World,SelectionWeight=0f }).ToArray(),
            });
        }
        Session.Render(true);
        return Session.LastCapturedFrame??throw new InvalidDataException("Thumbnail readback is unavailable.");
    }
    internal static byte[]Pack(PixelReadbackResult Frame){
        if(Frame.Width is <1 or >Edge||Frame.Height is <1 or >Edge||Frame.RowPitch<(uint)Frame.Width*4||Frame.Pixels.LongLength<(long)Frame.RowPitch*Frame.Height
            ||Frame.Format is not(RenderPixelFormat.Rgba8Unorm or RenderPixelFormat.Bgra8Unorm)){ throw new InvalidDataException("Thumbnail readback format is invalid."); }
        var Pixels=new byte[checked(Frame.Width*Frame.Height*4)];
        for(var Y=0;Y<Frame.Height;Y++){
            Buffer.BlockCopy(Frame.Pixels,checked((int)(Y*Frame.RowPitch)),Pixels,Y*Frame.Width*4,Frame.Width*4);
        }
        if(Frame.Format==RenderPixelFormat.Bgra8Unorm){ for(var I=0;I<Pixels.Length;I+=4){ (Pixels[I],Pixels[I+2])=(Pixels[I+2],Pixels[I]); } }
        return Pixels;
    }
    internal static byte[]Encode(int Width,int Height,byte[]Pixels)=>CSharpGameEngine.Tools.Imaging.ThumbnailPng.Encode(Width,Height,Pixels);
}