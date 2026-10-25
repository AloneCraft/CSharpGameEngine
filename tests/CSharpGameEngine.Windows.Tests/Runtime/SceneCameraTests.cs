using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class SceneCameraTests {
    [Fact]
    public void SceneViewVocabularyBelongsToTheGameRuntime(){
        var RuntimeAssembly=typeof(EngineHost).Assembly;
        Assert.Equal(RuntimeAssembly,typeof(Camera).Assembly);
        Assert.Equal(RuntimeAssembly,typeof(CameraProjection).Assembly);
        Assert.Equal(RuntimeAssembly,typeof(PrimitiveMesh).Assembly);
        Assert.Equal(RuntimeAssembly,typeof(PrimitiveType).Assembly);
        Assert.NotEqual(typeof(CSharpGameEngine.Graphics.D3D12.StageRenderer).Assembly,RuntimeAssembly);
    }
    [Fact]
    public void NewCamera_IsActivePerspectiveWithPlayableClipRange(){
        Camera Camera=new();

        Assert.True(Camera.Active);
        Assert.Equal(CameraProjection.Perspective,Camera.Projection);
        Assert.True(Camera.FovYRadians>0f);
        Assert.True(Camera.NearZ>0f);
        Assert.True(Camera.FarZ>Camera.NearZ);
        Assert.Equal(0f,Camera.ExposureEv100);
        Assert.Equal(1f,Camera.RadialTransitionMeters);
    }
}
