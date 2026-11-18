using Xunit;
using System.Numerics;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>画面上の大きさの式（M0129 単位 6）。シェーダーと一致することは Windowsのテストの ScreenSizeMetricGpuTests で確かめる。</summary>
public sealed class ScreenSizeMetricTests {
    [Fact]
    public void APerspectiveCameraWithANinetyDegreeViewShowsOneMeterAtOneMeterAsHalfTheHeight(){
        // 画角 90 度なら、1 メートル先の画面の縦は 2 メートル。1 メートルの物は高さの半分。
        var Metric=ScreenSizeMetric.Perspective(MathF.PI/2f,ViewportHeightPixels:1000,MinimumDistanceMeters:0.1f);
        Assert.Equal(500f,Metric.ProjectedPixels(1f,1f),3);
        Assert.Equal(50f,Metric.ProjectedPixels(1f,10f),3);
        // 距離が倍なら半分、大きさが倍なら倍。
        Assert.Equal(Metric.ProjectedPixels(1f,20f)*2f,Metric.ProjectedPixels(1f,10f),3);
        Assert.Equal(Metric.ProjectedPixels(1f,10f)*2f,Metric.ProjectedPixels(2f,10f),3);
    }
    [Fact]
    public void DistancesCloserThanTheMinimumAreTreatedAsTheMinimum(){
        var Metric=ScreenSizeMetric.Perspective(1f,720,MinimumDistanceMeters:0.5f);
        Assert.Equal(Metric.ProjectedPixels(1f,0.5f),Metric.ProjectedPixels(1f,0f));
        Assert.Equal(Metric.ProjectedPixels(1f,0.5f),Metric.ProjectedPixels(1f,-3f));
        Assert.True(float.IsFinite(Metric.SphereProjectedPixels(Vector3.Zero,2f)));
    }
    [Fact]
    public void ASphereIsMeasuredFromItsNearestPointSoItIsNeverUnderestimated(){
        var Metric=ScreenSizeMetric.Perspective(1f,720,0.01f);
        var Centre=new Vector3(3f,4f,0f);
        Assert.Equal(Metric.ProjectedPixels(2f,5f-1f),Metric.SphereProjectedPixels(Centre,1f));
        // どの向きに置いても、中心までの距離で測った値より小さくならない。
        foreach(var Direction in new[]{ Vector3.UnitX,-Vector3.UnitY,Vector3.Normalize(new Vector3(1f,-2f,3f)) }){
            Assert.True(Metric.SphereProjectedPixels(Direction*50f,1f)>=Metric.ProjectedPixels(2f,50f));
        }
    }
    [Fact]
    public void AnOrthographicCameraDoesNotDependOnDistance(){
        var Metric=ScreenSizeMetric.Orthographic(VerticalExtentMeters:20f,ViewportHeightPixels:1000);
        Assert.Equal(50f,Metric.ProjectedPixels(1f,1f));
        Assert.Equal(50f,Metric.ProjectedPixels(1f,1e6f));
        Assert.Equal(100f,Metric.SphereProjectedPixels(new Vector3(0f,0f,1e4f),1f));
        Assert.Equal(new Vector4(50f,1f,1f,0f),Metric.ShaderParameters);
    }
    [Fact]
    public void InvalidCamerasAreRefused(){
        Assert.Throws<ArgumentOutOfRangeException>(()=>ScreenSizeMetric.Perspective(0f,720,0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ScreenSizeMetric.Perspective(MathF.PI,720,0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ScreenSizeMetric.Perspective(1f,0,0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ScreenSizeMetric.Perspective(1f,720,0f));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ScreenSizeMetric.Perspective(1f,720,float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ScreenSizeMetric.Orthographic(0f,720));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ScreenSizeMetric.Orthographic(float.PositiveInfinity,720));
    }
}
