using System.Numerics;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0107: UE's first person rendering of a primitive (FirstPersonFieldOfView 70, FirstPersonScale 0.6) as a change of the mesh's place in the camera's frame:
/// seen through the camera's own projection, it lands where the narrower first-person projection would put it, and closer to the eye.
/// </summary>
public sealed class FirstPersonViewWarpTests {
    private static Vector3 InView(RenderCamera Camera,Vector3 Point)=>Vector3.Transform(Point,Camera.GetView());
    [Fact]
    public void TheSpreadIsTheRatioOfTheTangentsOfHalfTheTwoFieldsOfView(){
        Assert.Equal(MathF.Tan(MathF.PI/4f)/MathF.Tan(35f*MathF.PI/180f),FirstPersonViewWarp.Spread(new FirstPersonView()),4);
        Assert.Equal(1f,FirstPersonViewWarp.Spread(new FirstPersonView { FieldOfViewDegrees=90f,DefaultFieldOfViewDegrees=90f }),5);
        Assert.Equal(1f,FirstPersonViewWarp.Spread(new FirstPersonView { FieldOfViewDegrees=0f }));
        Assert.Equal(1f,FirstPersonViewWarp.Spread(new FirstPersonView { DefaultFieldOfViewDegrees=180f }));
        Assert.Equal(1f,FirstPersonViewWarp.Spread(new FirstPersonView { FieldOfViewDegrees=float.NaN }));
        Assert.Equal(1f,FirstPersonViewWarp.Spread(new FirstPersonView { FieldOfViewDegrees=-5f }));
    }
    [Theory]
    [InlineData(0f,0f)]
    [InlineData(0.7f,-0.3f)]
    [InlineData(-2.1f,0.4f)]
    public void APointSeenThroughTheCameraLandsWhereTheFirstPersonProjectionPutsItAndNearer(float Yaw,float Pitch){
        var Camera=RenderCamera.CreateFirstPerson(new Vector3(0f,0f,0f));
        Camera.Yaw=Yaw;
        Camera.Pitch=Pitch;
        var View=new FirstPersonView();
        var Warp=FirstPersonViewWarp.Matrix(Camera,View);
        var Spread=FirstPersonViewWarp.Spread(View);
        // Points a little way in front of the eye, in whatever direction the camera looks.
        var Forward=Camera.GetForward();
        var Up=Camera.GetUp();
        var Right=Vector3.Normalize(Vector3.Cross(Forward,Up));
        foreach(var (X,Y,Z) in new[] { (0.3f,-0.4f,0.5f),(-0.1f,0.2f,0.9f),(0f,-0.5f,0.4f) }){
            var World=(Right*X)+(Up*Y)+(Forward*Z);
            var Before=InView(Camera,World);
            var After=InView(Camera,Vector3.Transform(World,Warp));
            // x/z and y/z are the screen position: multiplied by the spread. The depth is scaled by 0.6.
            Assert.Equal(Spread*Before.X/Before.Z,After.X/After.Z,3);
            Assert.Equal(Spread*Before.Y/Before.Z,After.Y/After.Z,3);
            Assert.Equal(0.6f*Before.Z,After.Z,3);
        }
    }
    [Fact]
    public void TheEyeStaysWhereItIsEvenWhenTheCameraIsNotAtTheOrigin(){
        var Camera=RenderCamera.CreateFirstPerson(new Vector3(3f,2f,-1f));
        Camera.Yaw=0.4f;
        var Warp=FirstPersonViewWarp.Matrix(Camera,new FirstPersonView());
        var Eye=Camera.GetEyePosition();
        Assert.True(Vector3.Distance(Eye,Vector3.Transform(Eye,Warp))<1e-4f);
        // A point one metre ahead of the eye comes to 0.6 m (its sideways spread aside): the scale is about the eye, not about the origin.
        var Ahead=Eye+Camera.GetForward();
        Assert.Equal(0.6f,Vector3.Distance(Eye,Vector3.Transform(Ahead,Warp)),1);
    }
    [Fact]
    public void AScaleThatIsNotPositiveLeavesTheMeshAlone(){
        var Camera=RenderCamera.CreateFirstPerson(Vector3.Zero);
        Assert.Equal(Matrix4x4.Identity,FirstPersonViewWarp.Matrix(Camera,new FirstPersonView { Scale=0f }));
        Assert.Equal(Matrix4x4.Identity,FirstPersonViewWarp.Matrix(Camera,new FirstPersonView { Scale=-1f }));
        Assert.Equal(Matrix4x4.Identity,FirstPersonViewWarp.Matrix(Camera,new FirstPersonView { Scale=float.NaN }));
        var Same=FirstPersonViewWarp.Matrix(Camera,new FirstPersonView { FieldOfViewDegrees=90f,DefaultFieldOfViewDegrees=90f,Scale=1f });
        Assert.True(Vector3.Distance(new Vector3(1f,2f,3f),Vector3.Transform(new Vector3(1f,2f,3f),Same))<1e-4f);
    }
    [Fact]
    public void TheDefaultsAreUnrealsFirstPersonTemplateValues(){
        var View=new FirstPersonView();
        Assert.Equal(70f,View.FieldOfViewDegrees);
        Assert.Equal(90f,View.DefaultFieldOfViewDegrees);
        Assert.Equal(0.6f,View.Scale,5);
        var Visible=new MeshVisibility();
        Assert.True(Visible.VisibleToCamera&&Visible.CastsShadow);
    }
}
