extern alias PreviewHost;
using PreviewHost::CSharpGameEngine.EditorPreview.Host;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Windows.Tests.Runtime;
/// <summary>CPU-only byte/codec contracts; these checks create no D3D12 device or window.</summary>
public sealed class AssetThumbnailWorkerTests {
    [Fact]
    public void ReadbackPaddingAndBgraDoNotLeakIntoThePngPixels(){
        var Frame=new PixelReadbackResult{Width=1,Height=2,RowPitch=8,Format=RenderPixelFormat.Bgra8Unorm,Pixels=[3,2,1,255,99,99,99,99,6,5,4,128,99,99,99,99]};
        Assert.Equal(new byte[]{1,2,3,255,4,5,6,128},AssetThumbnailWorker.Pack(Frame));
        Frame=new PixelReadbackResult{Width=1,Height=2,RowPitch=4,Format=RenderPixelFormat.Rgba8Unorm,Pixels=[1,2,3,255,4,5,6,128]};
        Assert.Equal(Frame.Pixels,AssetThumbnailWorker.Pack(Frame));
    }
    [Fact]
    public void InvalidReadbacksFailBeforeAllocatingTheirClaimedPixelSize(){
        Assert.Throws<InvalidDataException>(()=>AssetThumbnailWorker.Pack(new(){Width=129,Height=1,RowPitch=516,Format=RenderPixelFormat.Rgba8Unorm,Pixels=[]}));
        Assert.Throws<InvalidDataException>(()=>AssetThumbnailWorker.Pack(new(){Width=1,Height=2,RowPitch=4,Format=RenderPixelFormat.Rgba8Unorm,Pixels=[1,2,3,4]}));
    }
}