using Xunit;
using System.Numerics;
using CSharpGameEngine.Rendering.View;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>起動直後の既定のビュー（M0119・M0120 の起動の契約）。L4 がビット列で持つ値が、L5 の既定のカメラから作るビューと一致する。</summary>
public sealed class StageRenderDefaultsTests {
    [Fact]
    public void TheInitialViewSourceIsTheDefaultCameraBitForBit(){
        var Expected=RenderViewPolicy.FromCameraSource(RenderCamera.CreateDefault(Vector3.Zero));
        Assert.Equal(Expected,StageRenderDefaults.InitialViewSource);
        foreach(var AspectRatio in new[]{1f,16f/9f,80f/48f,0.5f}){
            Assert.Equal(RenderViewPolicy.FromCamera(RenderCamera.CreateDefault(Vector3.Zero),AspectRatio),StageRenderDefaults.InitialViewSource.Resolve(AspectRatio));
        }
    }
}
