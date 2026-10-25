using System.Numerics;
using CSharpGameEngine.Rendering.View;
namespace CSharpGameEngine.Rendering.Tests;
public sealed class RenderViewTests {
    [Theory]
    [InlineData(1000f)]
    [InlineData(1e18f)]
    public void CameraPolicyPreservesTheOriginalProjectionAndPhysicalBasis(float Far){
        var Camera=RenderCamera.CreateDefault(new Vector3(2f,3f,4f));
        Camera.FarZ=Far;
        Camera.ExposureEv100=2f;
        var Actual=RenderViewPolicy.FromCamera(Camera,16f/9f);
        Assert.True(Actual.IsValid);
        Assert.Equal(Camera.GetView()*Camera.GetProjection(16f/9f),Actual.ViewProjection);
        Assert.Equal(Camera.GetEyePosition(),Actual.CameraPosition);
        Assert.Equal(2f,Actual.ExposureEv100);
        Assert.Equal(RenderDepthEncoding.Logarithmic,Actual.Depth.Encoding);
        var Basis=Assert.IsType<RenderViewBasis>(Actual.Basis);
        Assert.Equal(Camera.GetForward().X,Basis.Forward.X,5);
        Assert.Equal(Camera.GetForward().Y,Basis.Forward.Y,5);
        Assert.Equal(Camera.GetForward().Z,Basis.Forward.Z,5);
        Assert.True(Matrix4x4.Invert(Actual.AuxiliaryViewProjection,out var Inverse));
        Assert.Equal(Inverse,Actual.InverseViewProjection);
    }
    [Fact]
    public void PerspectiveMatrixKeepsPhysicalFarMetadataAndHasNoControllerBasis(){
        var Camera=RenderCamera.CreateFirstPerson(Vector3.Zero);
        Camera.FarZ=1e18f;
        var Vp=Camera.GetView()*Camera.GetProjection(1f);
        var Actual=RenderViewPolicy.FromPerspective(Vp,Vector3.Zero,new(Camera.FovYRadians,Camera.NearZ,Camera.FarZ),1f);
        Assert.Equal(Vp,Actual.ViewProjection);
        Assert.Equal(Camera.FarZ,Actual.Depth.Projection.FarZ);
        Assert.Null(Actual.Basis);
        Assert.True(Matrix4x4.Invert(Actual.AuxiliaryViewProjection,out _));
    }
    [Fact]
    public void OrthographicPolicyPreservesLinearDepthAndExactMatrix(){
        var Vp=Matrix4x4.CreateLookAt(new Vector3(0f,2f,-4f),Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreateOrthographic(6f,4f,0.1f,100f);
        var Actual=RenderViewPolicy.FromOrthographic(Vp,Vector3.Zero);
        Assert.Equal(Vp,Actual.ViewProjection);
        Assert.Equal(Vp,Actual.AuxiliaryViewProjection);
        Assert.Equal(RenderViewDepth.ForOrthographic(Vp),Actual.Depth);
        Assert.Null(Actual.Basis);
    }
    [Fact]
    public void DefaultValuesAndMixedProjectionEncodingsAreRejected(){
        Assert.False(default(RenderView).IsValid);
        var Camera=RenderCamera.CreateDefault(Vector3.Zero);
        var View=RenderViewPolicy.FromCamera(Camera,1f);
        Assert.Throws<ArgumentException>(()=>RenderView.Create(View.ViewProjection,View.AuxiliaryViewProjection,Vector3.Zero,0f,default));
        Assert.Throws<ArgumentException>(()=>RenderView.Create(Matrix4x4.Identity,Matrix4x4.Identity,Vector3.Zero,0f,View.Depth));
        Assert.Throws<ArgumentException>(()=>RenderViewPolicy.FromOrthographic(View.ViewProjection,Vector3.Zero));
        Assert.Throws<ArgumentException>(()=>RenderView.Create(View.ViewProjection,View.AuxiliaryViewProjection,Vector3.Zero,0f,View.Depth,default(RenderViewBasis)));
        Assert.Throws<ArgumentException>(()=>RenderView.Create(View.ViewProjection,View.AuxiliaryViewProjection,Vector3.Zero,0f,RenderViewDepth.ForObserver(View.Depth.Projection,Vector3.Zero)));
    }
    [Fact]
    public void InvalidReconstructionAndNonFiniteValuesAreRejectedBeforeSubmission(){
        var View=RenderViewPolicy.FromCamera(RenderCamera.CreateDefault(Vector3.Zero),1f);
        var WrongRays=View.AuxiliaryViewProjection;
        WrongRays.M11*=2f;
        Assert.Throws<ArgumentException>(()=>RenderView.Create(View.ViewProjection,WrongRays,Vector3.Zero,0f,View.Depth));
        Assert.Throws<ArgumentException>(()=>RenderView.Create(View.ViewProjection,default,Vector3.Zero,0f,View.Depth));
        Assert.Throws<ArgumentException>(()=>RenderView.Create(View.ViewProjection,View.AuxiliaryViewProjection,new(float.NaN,0f,0f),0f,View.Depth));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RenderView.Create(View.ViewProjection,View.AuxiliaryViewProjection,Vector3.Zero,float.NaN,View.Depth));
    }
    [Fact]
    public void CameraBasisRejectsScaledParallelAndMismatchedAxes(){
        Assert.Throws<ArgumentException>(()=>RenderViewBasis.Create(Vector3.UnitX*2f,Vector3.UnitY,Vector3.UnitZ,1f,1f));
        Assert.Throws<ArgumentException>(()=>RenderViewBasis.Create(Vector3.UnitX,Vector3.UnitX,Vector3.UnitZ,1f,1f));
        var Camera=RenderCamera.CreateDefault(Vector3.Zero);
        var View=RenderViewPolicy.FromCamera(Camera,1f);
        Camera.Yaw+=0.5f;
        var Other=RenderViewPolicy.FromCamera(Camera,1f);
        Assert.Throws<ArgumentException>(()=>RenderView.Create(View.ViewProjection,View.AuxiliaryViewProjection,View.CameraPosition,0f,View.Depth,Other.Basis));
        var Basis=View.Basis!.Value;
        var WrongScale=RenderViewBasis.Create(Basis.Right,Basis.Up,Basis.Forward,Basis.TanHalfFovY,2f);
        Assert.Throws<ArgumentException>(()=>RenderView.Create(View.ViewProjection,View.AuxiliaryViewProjection,View.CameraPosition,0f,View.Depth,WrongScale));
    }
    [Theory]
    [InlineData(0f,0.1f,100f,1f,1f)]
    [InlineData(1f,0f,100f,1f,1f)]
    [InlineData(1f,0.1f,0.1f,1f,1f)]
    [InlineData(1f,0.1f,100f,0f,1f)]
    [InlineData(1f,0.1f,100f,1f,0f)]
    public void PolicyRejectsInvalidPhysicalProjection(float Fov,float Near,float Far,float Curve,float Aspect){
        var Camera=RenderCamera.CreateFirstPerson(Vector3.Zero);
        Camera.FovYRadians=Fov;
        Camera.NearZ=Near;
        Camera.FarZ=Far;
        Camera.RadialTransitionMeters=Curve;
        Assert.Throws<ArgumentException>(()=>RenderViewPolicy.FromCamera(Camera,Aspect));
    }
}
