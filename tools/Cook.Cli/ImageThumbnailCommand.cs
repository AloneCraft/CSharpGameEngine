using System.Text.Json;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Tools.Imaging;
namespace CSharpGameEngine.Cook.Cli;
/// <summary>Read-only derived output; no metadata identity or import manifest is published.</summary>
internal static class ImageThumbnailCommand {
    internal static void Run(string RequestPath,string OutputPath){
        if(!OperatingSystem.IsWindows()){ throw new PlatformNotSupportedException("Image thumbnail decoding requires Windows WIC."); }
        var RequestFile=new FileInfo(RequestPath);
        if(RequestFile.Length is <2 or >64*1024){ throw new InvalidDataException("Thumbnail image request exceeds its bound."); }
        var Request=JsonSerializer.Deserialize<AssetThumbnailRequest>(File.ReadAllBytes(RequestPath))??throw new InvalidDataException("Thumbnail request is missing.");
        var Error=AssetThumbnailRequest.Validate(Request);
        if(Error is not null||Request.Scene is not null){ throw new InvalidDataException(Error??"Image source is required."); }
        var Source=new FileInfo(Request.ImagePath);
        if(Source.Length is <1 or >64*1024*1024){ throw new InvalidDataException("Thumbnail image source exceeds its bound."); }
        var Decoded=ThumbnailImageDecoder.DecodeThumbnail(File.ReadAllBytes(Request.ImagePath),Request.MaxEdge);
        if(!Decoded.Success||Decoded.Image is not { }Image){ throw new InvalidDataException(Decoded.Error??"Image preview is unavailable."); }
        var Destination=Path.GetFullPath(OutputPath);Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        var Temporary=Destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(Temporary,ThumbnailPng.Encode(Image.Width,Image.Height,Image.Pixels));File.Move(Temporary,Destination,true); }
        finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
    }
}