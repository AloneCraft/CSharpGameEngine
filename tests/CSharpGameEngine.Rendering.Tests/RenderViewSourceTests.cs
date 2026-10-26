using System.Numerics;
using CSharpGameEngine.Rendering.View;
namespace CSharpGameEngine.Rendering.Tests;
public sealed class RenderViewSourceTests {
    [Theory]
    [InlineData(1000f,0f)]
    [InlineData(1e18f,0f)]
    [InlineData(1000f,2f)]
    [InlineData(1e18f,2f)]
    public void CameraSourceRecomputesTheOriginalProjectionWithoutRetainingTheController(float Far,float Override){
        var Camera=RenderCamera.CreateDefault(new Vector3(2f,3f,4f));
        Camera.FarZ=Far;
        Camera.ExposureEv100=2f;
        var Source=RenderViewPolicy.FromCameraSource(Camera,Override);
        var Original=Camera;
        Camera.Yaw+=1f;
        foreach(var Size in new[]{(Width:133,Height:71),(Width:71,Height:133),(Width:900,Height:700)}){
            var Aspect=Override>0f?Override:(float)Size.Width/Size.Height;
            var Actual=Source.Resolve(Size.Width,Size.Height);
            Assert.Equal(Original.GetView()*Original.GetProjection(Aspect),Actual.ViewProjection);
            Assert.Equal(Original.GetEyePosition(),Actual.CameraPosition);
            Assert.Equal(Original.ExposureEv100,Actual.ExposureEv100);
            Assert.True(RadialDepthCurve.TryCreate(Original.RadialTransitionMeters,out var Curve));
            Assert.True(RadialDepthProjection.TryCreate(Curve,Original.NearZ,Original.FarZ,Original.FovYRadians,Aspect,out var Projection));
            Assert.Equal(RenderViewDepth.ForPerspective(Projection),Actual.Depth);
            Assert.Equal(Original.GetView()*Projection.CreateAuxiliaryProjection(Original.FovYRadians,Aspect),Actual.AuxiliaryViewProjection);
            Assert.Equal(Aspect,Assert.IsType<RenderViewBasis>(Actual.Basis).AspectRatio);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedSubmissionKeepsItsProjectionWhenTheSurfaceChanges(bool Orthographic){
        var Camera=RenderCamera.CreateDefault(Vector3.Zero);
        var View=Orthographic?RenderViewPolicy.FromOrthographic(Matrix4x4.CreateOrthographic(4f,3f,0.1f,100f),Vector3.Zero):RenderViewPolicy.FromCamera(Camera,1.5f);
        var Source=RenderViewSource.Fixed(View);
        Assert.Equal(View,Source.Resolve(123,57));
        Assert.Equal(View,Source.Resolve(57,123));
    }
    [Fact]
    public void ExplicitMatrixKeepsRaysWhilePhysicalDepthTracksTheSurface(){
        var Camera=RenderCamera.CreateFirstPerson(Vector3.Zero);
        Camera.FarZ=1e18f;
        var Vp=Camera.GetView()*Camera.GetProjection(1.5f);
        var Parameters=new RadialViewParameters(Camera.FovYRadians,Camera.NearZ,Camera.FarZ,Camera.RadialTransitionMeters);
        var Source=RenderViewPolicy.FromPerspectiveSource(Vp,Camera.GetEyePosition(),Parameters);
        var Wide=Source.Resolve(200,100);
        var Tall=Source.Resolve(100,200);
        Assert.Equal(Vp,Wide.ViewProjection);
        Assert.Equal(Vp,Tall.ViewProjection);
        Assert.NotEqual(Wide.Depth,Tall.Depth);
        Assert.Null(Tall.Basis);
        Assert.True(Matrix4x4.Invert(Tall.AuxiliaryViewProjection,out _));
    }
    [Fact]
    public void WarmResolutionAllocatesNoManagedMemory(){
        var Camera=RenderCamera.CreateDefault(Vector3.Zero);
        var View=RenderViewPolicy.FromCamera(Camera,1.5f);
        var Parameters=new RadialViewParameters(Camera.FovYRadians,Camera.NearZ,Camera.FarZ,Camera.RadialTransitionMeters);
        RenderViewSource[]Sources=[RenderViewPolicy.FromCameraSource(Camera),
            RenderViewPolicy.FromPerspectiveSource(View.ViewProjection,Camera.GetEyePosition(),Parameters),RenderViewSource.Fixed(View)];
        var Sum=0f;
        foreach(var Source in Sources){
            for(var Iteration=0;Iteration<64;Iteration++){ Sum+=Source.Resolve(133,71).ViewProjection.M11; }
        }
        var Before=GC.GetAllocatedBytesForCurrentThread();
        foreach(var Source in Sources){
            for(var Iteration=0;Iteration<128;Iteration++){ Sum+=Source.Resolve(71,133).ViewProjection.M11; }
        }
        var Allocated=GC.GetAllocatedBytesForCurrentThread()-Before;
        Assert.Equal(0L,Allocated);
        Assert.True(float.IsFinite(Sum));
    }
    [Fact]
    public void UninitializedAndInvalidInputsFailAtResolutionBeforeSubmission(){
        Assert.False(default(RenderViewSource).HasValue);
        Assert.Throws<ArgumentException>(()=>default(RenderViewSource).Resolve(100,100));
        Assert.Throws<ArgumentException>(()=>RenderViewSource.Fixed(default));
        var Invalid=RenderViewSource.Perspective(Matrix4x4.Identity,Vector3.Zero,default);
        Assert.Throws<ArgumentException>(()=>Invalid.Resolve(100,100));
        var Camera=RenderCamera.CreateDefault(Vector3.Zero);
        var Source=RenderViewPolicy.FromCameraSource(Camera);
        Assert.Throws<ArgumentOutOfRangeException>(()=>Source.Resolve(0,100));
        Assert.Throws<ArgumentException>(()=>Source.Resolve(float.NaN));
    }
}
