using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class SceneCameraTests {
    [Fact]
    public void NewCamera_IsActivePerspectiveWithPlayableClipRange(){
        Camera Camera=new();

        Assert.True(Camera.Active);
        Assert.Equal(CameraProjection.Perspective,Camera.Projection);
        Assert.True(Camera.FovYRadians>0f);
        Assert.True(Camera.NearZ>0f);
        Assert.True(Camera.FarZ>Camera.NearZ);
        Assert.Equal(0f,Camera.ExposureEv100);
        Assert.Equal(CameraDepthMapping.Standard,Camera.DepthMapping);
        Assert.Equal(1f,Camera.RadialTransitionMeters);
    }
}
