namespace CSharpGameEngine.Contracts;
/// <summary>Disposable content preview. Geometry thumbnails are 128px (geometry and base color only; authored textures, lights, animation and game code are excluded). An image request may ask for a larger derived preview up to <see cref="MaximumImageEdge"/> for the Texture Editor.</summary>
public sealed class AssetThumbnailRequest {
    public const int CurrentVersion=1;
    public const int DefaultEdge=128;
    public const int MinimumImageEdge=16;
    public const int MaximumImageEdge=2048;
    public int ProtocolVersion{get;set;}=CurrentVersion;
    public RenderPreviewScene?Scene{get;set;}
    public string ImagePath{get;set;}=string.Empty;
    public string ShaderArtifactPath{get;set;}=string.Empty;
    /// <summary>Longest edge of an image preview in pixels. Geometry requests always render <see cref="DefaultEdge"/>.</summary>
    public int MaxEdge{get;set;}=DefaultEdge;
    public static string?Validate(AssetThumbnailRequest Request){
        if(Request is null||Request.ProtocolVersion!=CurrentVersion){ return "Unsupported thumbnail protocol."; }
        if((Request.Scene is null)==string.IsNullOrWhiteSpace(Request.ImagePath)){ return "Exactly one image or geometry scene is required."; }
        if(Request.Scene is null){ return Request.MaxEdge is<MinimumImageEdge or>MaximumImageEdge?"Image preview edge is out of range.":null; }
        if(Request.MaxEdge!=DefaultEdge){ return "Geometry thumbnails are always 128px."; }
        if(Request.Scene.Objects is not {Length:>0}){ return "Thumbnail geometry is unavailable."; }
        return RenderPreviewHostProtocol.Validate(new(){RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=1,Command=RenderPreviewHostCommand.Open,SurfaceHandle=1,Width=128,Height=128,ShaderArtifactPath=Request.ShaderArtifactPath,Scene=Request.Scene});
    }
}