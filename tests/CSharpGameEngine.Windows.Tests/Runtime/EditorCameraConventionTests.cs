using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Windows.Tests.Runtime;
/// <summary>
/// ADR-0070 B1: the Scene Designer sends its camera as an RUF quaternion built from the rows right,
/// up and forward (System.Numerics). The engine must read that quaternion with the same axes: its
/// camera basis is the transform's +F and +U.
/// </summary>
public sealed class EditorCameraConventionTests {
    [Theory]
    [InlineData(0f,0f,1f)]
    [InlineData(1f,-0.5f,2f)]
    [InlineData(-3f,1f,-1f)]
    public void NumericsQuaternionFromBasisRows_IsTheEngineCameraBasis(float ForwardR,float ForwardU,float ForwardF){
        var Forward=Vector3.Normalize(new Vector3(ForwardR,ForwardU,ForwardF));
        var Right=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,Forward));
        var Up=Vector3.Cross(Forward,Right);
        var Rotation=Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            Right.X,Right.Y,Right.Z,0f,Up.X,Up.Y,Up.Z,0f,Forward.X,Forward.Y,Forward.Z,0f,0f,0f,0f,1f));

        var Pose=new Transform { Rotation=new LogicalQuaternion(Rotation.X,Rotation.Y,Rotation.Z,Rotation.W) };
        Assert.Equal(FixedTransformStatus.Success,FixedAffineTransform.TryFromTransform(Pose,out var World));
        Assert.Equal(FixedTransformStatus.Success,World.TryGetCameraBasis(out var EngineForward,out var EngineUp));

        Assert.True(Vector3.Distance(Forward,Vector3.Normalize(EngineForward))<0.0001f,$"Forward {Forward} but the engine reads {EngineForward}.");
        Assert.True(Vector3.Distance(Up,Vector3.Normalize(EngineUp))<0.0001f,$"Up {Up} but the engine reads {EngineUp}.");
    }
}
