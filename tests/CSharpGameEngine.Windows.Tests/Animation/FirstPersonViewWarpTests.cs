using System.Numerics;
using RenderCamera=CSharpGameEngine.Rendering.View.RenderCamera;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0107：UEのプリミティブに対する一人称描画（FirstPersonFieldOfView 70、FirstPersonScale 0.6）。メッシュのカメラ座標系内の位置を変更する。
/// カメラ本来の投影で見ると、より狭い一人称投影での位置に現れ、さらに視点に近づく。
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
        // 視点が向く方向へ、視点の少し前に置く。
        var Forward=Camera.GetForward();
        var Up=Camera.GetUp();
        var Right=Vector3.Normalize(Vector3.Cross(Forward,Up));
        foreach(var (X,Y,Z) in new[] { (0.3f,-0.4f,0.5f),(-0.1f,0.2f,0.9f),(0f,-0.5f,0.4f) }){
            var World=(Right*X)+(Up*Y)+(Forward*Z);
            var Before=InView(Camera,World);
            var After=InView(Camera,Vector3.Transform(World,Warp));
            // x/zとy/zが画面上の位置を表し、spreadを掛ける。深度には0.6を掛ける。
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
        // 視点より1メートル前方の点は、横方向の拡大を除いて0.6 mの位置に来る。スケールは原点ではなく視点を中心とする。
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
