using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>ADR-0077: カメラとポインターの位置から、ワールドの光線を作る。</summary>
public sealed class PickingTests {
    private const float QuarterPi=MathF.PI/4f;
    private static float ToSingle(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private static PhysicsRay Ray(LogicalAffineTransform Camera,float X,float Y,float Aspect=2f){
        Assert.True(Picking.TryCreateRay(Camera,QuarterPi*2f,Aspect,X,Y,out var Result));
        return Result;
    }
    [Fact]
    public void TheCenterOfTheScreenLooksStraightForwardFromTheCamera(){
        var Camera=LogicalAffineTransform.Identity;
        var Ray=PickingTests.Ray(Camera,0.5f,0.5f);
        Assert.Equal(LogicalVector3.Zero,Ray.Origin);
        Assert.InRange(ToSingle(Ray.Direction.R),-0.0001f,0.0001f);
        Assert.InRange(ToSingle(Ray.Direction.U),-0.0001f,0.0001f);
        Assert.InRange(ToSingle(Ray.Direction.F),0.9999f,1.0001f);
        Assert.Equal(LogicalScalar.FromInt64(Picking.MaxDistanceMeters),Ray.MaxDistance);
    }
    [Fact]
    public void TheEdgesOfTheScreenSpanTheFieldOfView(){
        // 縦の視野角が90度(半分が45度)なので、画面の上端は上へ45度、横は縦×アスペクト比(2)で右端は tan = 2。
        var Top=PickingTests.Ray(LogicalAffineTransform.Identity,0.5f,0f).Direction;
        Assert.InRange(ToSingle(Top.U/Top.F),0.9999f,1.0001f);
        var Right=PickingTests.Ray(LogicalAffineTransform.Identity,1f,0.5f).Direction;
        Assert.InRange(ToSingle(Right.R/Right.F),1.9999f,2.0001f);
        var Left=PickingTests.Ray(LogicalAffineTransform.Identity,0f,0.5f).Direction;
        Assert.InRange(ToSingle(Left.R/Left.F),-2.0001f,-1.9999f);
    }
    [Fact]
    public void ARayStartsAtTheCameraAndIsRotatedByIt(){
        // 真下を向くカメラ: 前(F)軸が下(-U)、上(U)軸が前(+F)を向く。
        var Down=new LogicalAffineTransform(new LogicalVector3(1,0,0),new LogicalVector3(0,0,1),new LogicalVector3(0,-1,0),new LogicalVector3(3,10,4));
        var Ray=PickingTests.Ray(Down,0.5f,0.5f);
        Assert.Equal(new LogicalVector3(3,10,4),Ray.Origin);
        Assert.InRange(ToSingle(Ray.Direction.U),-1.0001f,-0.9999f);
    }
    [Theory]
    [InlineData(0f,2f,0.5f,0.5f)]
    [InlineData(3.2f,2f,0.5f,0.5f)]
    [InlineData(1.5f,0f,0.5f,0.5f)]
    [InlineData(1.5f,2f,float.NaN,0.5f)]
    [InlineData(1.5f,2f,0.5f,float.PositiveInfinity)]
    public void ARayIsRefusedForAnUnusableFieldOfViewAspectOrPoint(float Fov,float Aspect,float X,float Y){
        Assert.False(Picking.TryCreateRay(LogicalAffineTransform.Identity,Fov,Aspect,X,Y,out _));
    }
}
