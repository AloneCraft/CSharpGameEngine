namespace CSharpGameEngine.Contracts;
/// <summary>Disposable content preview. Geometry previews use the tile or inspector size.</summary>
public sealed class AssetThumbnailRequest {
    public const int CurrentVersion=1;
    public const int DefaultEdge=128;
    public const int InspectorEdge=512;
    public const int MinimumImageEdge=16;
    public const int MaximumImageEdge=2048;
    public int ProtocolVersion{get;set;}=CurrentVersion;
    public RenderPreviewScene?Scene{get;set;}
    public string ImagePath{get;set;}=string.Empty;
    public string ShaderArtifactPath{get;set;}=string.Empty;
    /// <summary>
    /// 形状のサムネイルを描くときの照明・背景（プレビューレベル）。null なら従来どおりレンダラーの既定。版は上げない追加項目で、
    /// 古いワーカーは無視する（VSIX とホストは同じパッケージで配布する）。
    /// </summary>
    public RenderPreviewLighting?Lighting{get;set;}
    /// <summary>Longest preview edge in pixels.</summary>
    public int MaxEdge{get;set;}=DefaultEdge;
    public static string?Validate(AssetThumbnailRequest Request){
        if(Request is null||Request.ProtocolVersion!=CurrentVersion){ return "Unsupported thumbnail protocol."; }
        if((Request.Scene is null)==string.IsNullOrWhiteSpace(Request.ImagePath)){ return "Exactly one image or geometry scene is required."; }
        if(Request.Scene is null){ return Request.MaxEdge is<MinimumImageEdge or>MaximumImageEdge?"Image preview edge is out of range.":null; }
        if(Request.MaxEdge is not(DefaultEdge or InspectorEdge)){ return "Unsupported geometry preview size."; }
        if(Request.Scene.Objects is not {Length:>0}){ return "Thumbnail geometry is unavailable."; }
        var SceneError=RenderPreviewHostProtocol.Validate(new(){RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=1,Command=RenderPreviewHostCommand.Open,SurfaceHandle=1,Width=Request.MaxEdge,Height=Request.MaxEdge,ShaderArtifactPath=Request.ShaderArtifactPath,Scene=Request.Scene});
        if(SceneError is not null){ return SceneError; }
        return Request.Lighting is null?null:RenderPreviewLightingValidation.Validate(Request.Lighting);
    }
}
