using System.Numerics;
namespace CSharpGameEngine.Rendering.Tests;
public sealed class RenderViewDepthTests {
    [Fact]
    public void PerspectiveAndCaptureUseTheSamePhysicalEncodingWithDifferentRasterWeights(){
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,0.1f,1e18f,MathF.PI/4f,16f/9f,out var Projection));
        var Main=RenderViewDepth.ForPerspective(Projection);
        var Capture=RenderViewDepth.ForPerspective(Projection,ConservativeRasterDepth:false);
        Assert.True(Main.IsValid&&Capture.IsValid);
        Assert.Equal(RenderDepthEncoding.Logarithmic,Main.Encoding);
        Assert.Equal(Main.Packet0,Capture.Packet0);
        Assert.Equal(1f,Main.Packet1.W);
        Assert.Equal(0f,Capture.Packet1.W);
        var Distance=5e17f;
        Assert.Equal(Projection.EncodeDepth(Distance),MathF.Min(Projection.Curve.G(Distance)*Main.Packet0.Z,RadialDepthProjection.MaximumDepth));
    }
    [Fact]
    public void OrthographicPacketPreservesTheLinearDepthOfARotatedView(){
        var View=Matrix4x4.CreateLookAt(new Vector3(2f,3f,4f),Vector3.Zero,Vector3.UnitY);
        var Projection=Matrix4x4.CreateOrthographic(20f,10f,0.1f,1000f);
        var Depth=RenderViewDepth.ForOrthographic(View*Projection);
        Assert.True(Depth.IsValid);
        Assert.Equal(RenderDepthEncoding.Linear,Depth.Encoding);
        Assert.InRange(Depth.Packet0.Y,1f/(1000f-0.1f)*0.99999f,1f/(1000f-0.1f)*1.00001f);
        Assert.Equal(0f,Depth.Packet1.W);
    }
    [Fact]
    public void ALinearPacketCannotBeCreatedForAPerspectiveOrDegenerateProjection(){
        Assert.Throws<ArgumentException>(()=>RenderViewDepth.ForOrthographic(Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/4f,1f,0.1f,1000f)));
        Assert.Throws<ArgumentException>(()=>RenderViewDepth.ForOrthographic(default));
        var Invalid=Matrix4x4.Identity;
        Invalid.M11=0f;
        Assert.Throws<ArgumentException>(()=>RenderViewDepth.ForOrthographic(Invalid));
        Invalid=Matrix4x4.Identity;
        Invalid.M43=float.NaN;
        Assert.Throws<ArgumentException>(()=>RenderViewDepth.ForOrthographic(Invalid));
    }
    [Fact]
    public void ObserverPreservesItsPhysicalCurveAndCenterWithoutAMainCameraTangent(){
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,0.1f,100f,MathF.PI/4f,1f,out var Projection));
        var Center=new Vector3(1f,2f,3f);
        var Depth=RenderViewDepth.ForObserver(Projection,Center);
        Assert.Equal(RenderDepthEncoding.Observer,Depth.Encoding);
        Assert.Equal(new Vector4(Center,0f),Depth.Packet1);
        Assert.Equal(Projection.InverseMaximumG,Depth.Packet0.Z);
    }
    [Fact]
    public void InvalidStateCannotAcquireAnImplicitPerspectiveFallback(){
        Assert.False(default(RenderViewDepth).IsValid);
        Assert.Throws<ArgumentException>(()=>RenderViewDepth.ForPerspective(default));
        Assert.Throws<ArgumentException>(()=>RenderViewDepth.ForObserver(default,Vector3.Zero));
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,0.1f,100f,MathF.PI/4f,1f,out var Projection));
        Assert.Throws<ArgumentException>(()=>RenderViewDepth.ForObserver(Projection,new Vector3(float.PositiveInfinity,0f,0f)));
    }
}
