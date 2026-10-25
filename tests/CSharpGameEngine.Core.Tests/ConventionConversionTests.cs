using System.Numerics;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Gltf;
using CSharpGameEngine.Spatial.Unity;
using CSharpGameEngine.Spatial.Unreal;
using ConvertGltf=CSharpGameEngine.Spatial.Abstractions.ConventionConversion<CSharpGameEngine.Spatial.Gltf.GltfCoordinateConvention,CSharpGameEngine.Spatial.Unity.UnityCoordinateConvention>;
namespace CSharpGameEngine.Core.Tests;
public sealed class ConventionConversionTests {
    [Fact]
    public void Mirror_ConvertsVectorsRotationMatrixAndTangentConsistently(){
        Assert.True(ConvertGltf.IsMirror);
        Assert.Equal(new Vector3(2f,3f,-4f),ConvertGltf.Vector(new(2f,3f,4f)));
        Assert.Equal(new Vector3(-2f,3f,-4f),ConvertGltf.Scale(new(-2f,3f,-4f)));
        Assert.Equal(new Vector4(1f,2f,-3f,-1f),ConvertGltf.Tangent(new(1f,2f,3f,1f)));
        var Rotation=Quaternion.Normalize(new Quaternion(0.2f,0.3f,-0.1f,0.9f));
        var Transform=Matrix4x4.CreateScale(-2f,3f,4f)*Matrix4x4.CreateFromQuaternion(Rotation)*Matrix4x4.CreateTranslation(7f,8f,9f);
        var Point=new Vector3(2f,-3f,4f);
        AssertNear(ConvertGltf.Vector(Vector3.Transform(Point,Transform)),Vector3.Transform(ConvertGltf.Vector(Point),ConvertGltf.Matrix(Transform)));
        var ConvertedRotation=Matrix4x4.CreateFromQuaternion(ConvertGltf.Rotation(Rotation));
        var Expected=ConvertGltf.Matrix(Matrix4x4.CreateFromQuaternion(Rotation));
        AssertNear(Vector3.Transform(Point,Expected),Vector3.Transform(Point,ConvertedRotation));
    }
    [Fact]
    public void Unreal_PermutesSignedScaleAndRoundTripsTranslatedTransform(){
        Assert.False(ConventionConversion<UnrealCoordinateConvention,UnityCoordinateConvention>.IsMirror);
        Assert.Equal(new Vector3(2f,3f,1f),ConventionConversion<UnrealCoordinateConvention,UnityCoordinateConvention>.Vector(new(1f,2f,3f)));
        Assert.Equal(new Vector3(-2f,3f,-1f),ConventionConversion<UnrealCoordinateConvention,UnityCoordinateConvention>.Scale(new(-1f,-2f,3f)));
        var Matrix=Matrix4x4.CreateRotationZ(0.3f)*Matrix4x4.CreateTranslation(2f,3f,4f);
        var Engine=ConventionConversion<UnrealCoordinateConvention,UnityCoordinateConvention>.Matrix(Matrix);
        var RoundTrip=ConventionConversion<UnityCoordinateConvention,UnrealCoordinateConvention>.Matrix(Engine);
        for(var Row=0;Row<4;Row++){
            for(var Column=0;Column<4;Column++){
                Assert.Equal(Matrix[Row,Column],RoundTrip[Row,Column]);
            }
        }
    }
    private static void AssertNear(Vector3 Expected,Vector3 Actual)=>Assert.InRange(Vector3.Distance(Expected,Actual),0f,1e-5f);
}
