using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Contracts.Tests;
public sealed class AssetThumbnailContractTests {
    [Fact]
    public void ExactlyOneImageOrGeometrySourceIsRequired(){
        Assert.NotNull(AssetThumbnailRequest.Validate(new()));
        Assert.Null(AssetThumbnailRequest.Validate(new(){ImagePath="image.png"}));
        var Open=RenderPreviewHostProtocolTests.CreateOpenRequest();
        var Request=new AssetThumbnailRequest{Scene=Open.Scene,ShaderArtifactPath=Open.ShaderArtifactPath};
        Assert.Null(AssetThumbnailRequest.Validate(Request));
        Request.ImagePath="image.png";Assert.NotNull(AssetThumbnailRequest.Validate(Request));
    }
    [Fact]
    public void ImagePreviewEdgeIsBoundedAndGeometryStaysAtThumbnailSize(){
        Assert.Null(AssetThumbnailRequest.Validate(new(){ImagePath="image.png",MaxEdge=AssetThumbnailRequest.MaximumImageEdge}));
        Assert.Null(AssetThumbnailRequest.Validate(new(){ImagePath="image.png",MaxEdge=AssetThumbnailRequest.MinimumImageEdge}));
        Assert.NotNull(AssetThumbnailRequest.Validate(new(){ImagePath="image.png",MaxEdge=AssetThumbnailRequest.MaximumImageEdge+1}));
        Assert.NotNull(AssetThumbnailRequest.Validate(new(){ImagePath="image.png",MaxEdge=AssetThumbnailRequest.MinimumImageEdge-1}));
        var Open=RenderPreviewHostProtocolTests.CreateOpenRequest();
        Assert.NotNull(AssetThumbnailRequest.Validate(new(){Scene=Open.Scene,ShaderArtifactPath=Open.ShaderArtifactPath,MaxEdge=256}));
    }
    [Fact]
    public void EmptyOrInvalidGeometryCannotMasqueradeAsAnAssetThumbnail(){
        Assert.NotNull(AssetThumbnailRequest.Validate(new(){Scene=new(),ShaderArtifactPath="shader"}));
        var Open=RenderPreviewHostProtocolTests.CreateOpenRequest();
        Open.Scene!.Meshes[0].Indices[0]=uint.MaxValue;
        Assert.NotNull(AssetThumbnailRequest.Validate(new(){Scene=Open.Scene,ShaderArtifactPath=Open.ShaderArtifactPath}));
        Assert.NotNull(AssetThumbnailRequest.Validate(new(){ImagePath="image.png",ProtocolVersion=2}));
    }
}