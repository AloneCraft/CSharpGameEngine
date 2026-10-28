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
    [Fact]
    public void AGeometryThumbnailMayCarryAValidLightingAndRejectsAnInvalidOne(){
        var Open=RenderPreviewHostProtocolTests.CreateOpenRequest();
        var Lighting=new RenderPreviewLighting {
            EnvironmentRevision=1,Ambient=[0.3f,0.3f,0.3f],Background=[0.17f,0.18f,0.2f],
            Points=[new(){ Position=[0f,3f,0f],FluxLumens=680f,RangeMeters=20f,CastsShadows=true }],
        };
        Assert.Null(AssetThumbnailRequest.Validate(new(){Scene=Open.Scene,ShaderArtifactPath=Open.ShaderArtifactPath,Lighting=Lighting}));
        Lighting.Points[0].FluxLumens=float.NaN;
        Assert.NotNull(AssetThumbnailRequest.Validate(new(){Scene=Open.Scene,ShaderArtifactPath=Open.ShaderArtifactPath,Lighting=Lighting}));
    }
    [Fact]
    public void TheLightingSurvivesTheRequestFileRoundTrip(){
        var Open=RenderPreviewHostProtocolTests.CreateOpenRequest();
        var Request=new AssetThumbnailRequest{Scene=Open.Scene,ShaderArtifactPath=Open.ShaderArtifactPath,Lighting=new RenderPreviewLighting { EnvironmentRevision=3,Background=[0.1f,0.2f,0.3f] }};
        var Back=System.Text.Json.JsonSerializer.Deserialize<AssetThumbnailRequest>(System.Text.Json.JsonSerializer.Serialize(Request))!;
        Assert.Equal(3,Back.Lighting!.EnvironmentRevision);
        Assert.Equal(new[]{ 0.1f,0.2f,0.3f },Back.Lighting.Background);
        // 古いワーカー向けの JSON（照明なし）も読める。
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<AssetThumbnailRequest>("{\"ProtocolVersion\":1,\"ImagePath\":\"a.png\"}")!.Lighting);
    }
}
