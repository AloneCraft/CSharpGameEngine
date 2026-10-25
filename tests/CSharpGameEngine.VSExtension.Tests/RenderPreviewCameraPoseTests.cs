using System.Numerics;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// ADR-0070 B1: the runtime view's camera is read back from the Scene view's look-at view and
/// projection: its rotation turns +F into the view direction and +U into the view's up, and its
/// near and far planes are the projection's.
/// </summary>
public sealed class RenderPreviewCameraPoseTests {
    [Theory]
    [InlineData(0f,2f,-6f,0f,0f,0f)]
    [InlineData(3f,4f,5f,-1f,0.5f,2f)]
    [InlineData(-2f,1f,0f,4f,1f,0.5f)]
    public void Pose_TurnsForwardAndUpIntoTheView(float EyeR,float EyeU,float EyeF,float TargetR,float TargetU,float TargetF){
        var Eye=new Vector3(EyeR,EyeU,EyeF);
        var Target=new Vector3(TargetR,TargetU,TargetF);
        var View=Matrix4x4.CreateLookAt(Eye,Target,Vector3.UnitY);
        var Camera=new RenderPreviewCamera();

        RenderPreviewSceneBuilder.DescribePose(Camera,View,Matrix4x4.CreatePerspectiveFieldOfView(0.8f,1.5f,0.25f,120f),0.8f);

        var Rotation=new Quaternion(Camera.Rotation[0],Camera.Rotation[1],Camera.Rotation[2],Camera.Rotation[3]);
        AssertNear(Vector3.Normalize(Target-Eye),Vector3.Transform(Vector3.UnitZ,Rotation));
        var Up=Vector3.Transform(Vector3.UnitY,Rotation);
        Assert.InRange(Vector3.Dot(Up,Vector3.UnitY),0f,1.0001f);
        Assert.InRange(Vector3.Dot(Up,Vector3.Normalize(Target-Eye)),-0.0001f,0.0001f);
        Assert.Equal(0.8f,Camera.FovYRadians);
        Assert.InRange(Camera.NearZ,0.2499f,0.2501f);
        Assert.InRange(Camera.FarZ,119.9f,120.1f);
    }

    [Fact]
    public void Pose_ReadsTheOrthographicPlanes(){
        var Camera=new RenderPreviewCamera();
        RenderPreviewSceneBuilder.DescribePose(Camera,Matrix4x4.CreateLookAt(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY),Matrix4x4.CreateOrthographic(10f,6f,0.5f,80f),0.8f);
        Assert.InRange(Camera.NearZ,0.4999f,0.5001f);
        Assert.InRange(Camera.FarZ,79.99f,80.01f);
    }

    [Fact]
    public void Pose_PreservesCurveAndExplicitHugeFarPlane(){
        var View=Matrix4x4.CreateLookAt(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY);
        var Projection=Matrix4x4.CreatePerspectiveFieldOfView(0.8f,1.5f,0.25f,1e18f);
        Assert.Equal(-1f,Projection.M33);
        var Camera=new RenderPreviewCamera { ViewProjection=RenderPreviewSceneBuilder.Matrix(View*Projection) };
        RenderPreviewSceneBuilder.DescribePose(Camera,View,Projection,0.8f,RadialTransitionMeters:3f,NearZ:0.25f,FarZ:1e18f);
        Assert.Equal(0.25f,Camera.NearZ);
        Assert.Equal(1e18f,Camera.FarZ);
        Assert.Equal(3f,Camera.RadialTransitionMeters);
        var Request=new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=1,
            Command=RenderPreviewHostCommand.UpdateFrame,
            Frame=new RenderPreviewFrame { Camera=Camera },
        };
        Assert.Null(RenderPreviewHostProtocol.Validate(Request));
    }

    private static void AssertNear(Vector3 Expected,Vector3 Actual)=>
        Assert.True(Vector3.Distance(Expected,Actual)<0.0001f,$"Expected {Expected}, got {Actual}.");
}
