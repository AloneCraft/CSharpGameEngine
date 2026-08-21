using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class TransformTests {
    [Fact]
    public void NewTransform_IsIdentity(){
        Transform Transform=new();

        Assert.Equal(0f,(float)Transform.PositionX);
        Assert.Equal(0f,(float)Transform.PositionY);
        Assert.Equal(0f,(float)Transform.PositionZ);
        Assert.Equal(0f,(float)Transform.RotationX);
        Assert.Equal(0f,(float)Transform.RotationY);
        Assert.Equal(0f,(float)Transform.RotationZ);
        Assert.Equal(1f,(float)Transform.RotationW);
        Assert.Equal(1f,(float)Transform.ScaleX);
        Assert.Equal(1f,(float)Transform.ScaleY);
        Assert.Equal(1f,(float)Transform.ScaleZ);
    }

    [Fact]
    public void ToWorld_ComposesChildLocalTransformWithParentWorldTransform(){
        Transform Parent=new() { PositionX=10f,RotationW=1f,ScaleX=2f,ScaleY=2f,ScaleZ=2f };
        Transform Child=new() { PositionX=3f,PositionY=1f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f };

        var World=TransformMath.ToWorld(Child,TransformMath.ToMatrix(Parent));

        Assert.True(Matrix4x4.Decompose(World,out var Scale,out var Rotation,out var Position));
        Assert.Equal(new Vector3(2f),Scale);
        Assert.Equal(Quaternion.Identity,Rotation);
        Assert.Equal(new Vector3(16f,2f,0f),Position);
    }

    [Fact]
    public void TryCalculateLocal_PreservesWorldTransformWhenReparented(){
        Transform OldParent=new() { PositionX=10f,RotationW=1f,ScaleX=2f,ScaleY=2f,ScaleZ=2f };
        Transform Child=new() { PositionX=3f,PositionY=1f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f };
        var World=TransformMath.ToWorld(Child,TransformMath.ToMatrix(OldParent));
        Transform NewParent=new() { PositionX=-4f,PositionY=2f,RotationW=1f,ScaleX=0.5f,ScaleY=0.5f,ScaleZ=0.5f };

        Assert.True(TransformMath.TryCalculateLocal(World,TransformMath.ToMatrix(NewParent),out var Local));
        Assert.Equal(World,TransformMath.ToWorld(Local,TransformMath.ToMatrix(NewParent)));
    }

    [Fact]
    public void TryCalculateLocal_RejectsNonInvertibleParent(){
        Transform Parent=new() { RotationW=1f,ScaleX=0f,ScaleY=1f,ScaleZ=1f };

        Assert.False(TransformMath.TryCalculateLocal(Matrix4x4.Identity,TransformMath.ToMatrix(Parent),out _));
    }

    [Fact]
    public void TryToCameraRelativeMatrix_SubtractsFixedOriginBeforeFloatConversion(){
        FixedScalar LargeOrigin=FixedScalar.ParseInvariant("1000000000000000000000000000000");
        Assert.True(FixedScalar.TryAdd(
            LargeOrigin,
            FixedScalar.ParseInvariant("0.125"),
            out FixedScalar NearbyPosition));

        Transform Camera=new() { PositionX=LargeOrigin };
        Transform Object=new() { PositionX=NearbyPosition };

        FixedTransformStatus Status=TransformMath.TryToCameraRelativeMatrix(
            Object,Camera,out Matrix4x4 Matrix);

        Assert.Equal(FixedTransformStatus.Success,Status);
        Assert.Equal(0.125f,Matrix.M41);
        Assert.Equal(0f,Matrix.M42);
        Assert.Equal(0f,Matrix.M43);
    }
    [Fact]
    public void FixedTransformWorldResolver_ComposesHierarchyBeforeCameraRelativeFloatConversion(){
        FixedScalar LargeOrigin=FixedScalar.ParseInvariant("1000000000000000000000000000000");
        Transform Parent=new() { PositionX=LargeOrigin };
        Transform Child=new() {
            PositionX=FixedScalar.ParseInvariant("0.125"),
            PositionY=2f,
        };
        FixedTransformWorldResolver Resolver=new();

        FixedTransformResolveStatus ResolveStatus=Resolver.Resolve(
            2,TryGetTransform,TryGetParent,out FixedAffineTransform World);
        FixedTransformStatus MatrixStatus=World.TryToCameraRelativeMatrix(
            Parent,out Matrix4x4 Matrix);

        Assert.Equal(FixedTransformResolveStatus.Success,ResolveStatus);
        Assert.Equal(FixedTransformStatus.Success,MatrixStatus);
        Assert.Equal(0.125f,Matrix.M41);
        Assert.Equal(2f,Matrix.M42);

        bool TryGetTransform(int EntityId,out Transform Result){
            switch(EntityId){
                case 1:
                    Result=Parent;
                    return true;
                case 2:
                    Result=Child;
                    return true;
                default:
                    Result=default;
                    return false;
            }
        }

        static bool TryGetParent(int EntityId,out int ParentEntityId){
            ParentEntityId=EntityId==2?1:default;
            return EntityId==2;
        }
    }

    [Fact]
    public void FixedTransformWorldResolver_UsesParentedCameraWorldBeforeFloatConversion(){
        FixedScalar LargeOrigin=FixedScalar.ParseInvariant("1000000000000000000000000000000");
        Transform Root=new() { PositionX=LargeOrigin };
        Transform Camera=new() { PositionX=2f,PositionY=3f };
        Transform Object=new() { PositionX=2.125f,PositionY=4f };
        FixedTransformWorldResolver Resolver=new();

        Assert.Equal(
            FixedTransformResolveStatus.Success,
            Resolver.Resolve(2,TryGetTransform,TryGetParent,out FixedAffineTransform CameraWorld));
        Assert.Equal(
            FixedTransformResolveStatus.Success,
            Resolver.Resolve(3,TryGetTransform,TryGetParent,out FixedAffineTransform ObjectWorld));

        FixedTransformStatus Status=ObjectWorld.TryToCameraRelativeMatrix(
            CameraWorld,out Matrix4x4 CameraRelative);

        Assert.Equal(FixedTransformStatus.Success,Status);
        Assert.Equal(0.125f,CameraRelative.M41);
        Assert.Equal(1f,CameraRelative.M42);
        Assert.Equal(0f,CameraRelative.M43);

        bool TryGetTransform(int EntityId,out Transform Result){
            switch(EntityId){
                case 1:
                    Result=Root;
                    return true;
                case 2:
                    Result=Camera;
                    return true;
                case 3:
                    Result=Object;
                    return true;
                default:
                    Result=default;
                    return false;
            }
        }

        static bool TryGetParent(int EntityId,out int ParentEntityId){
            ParentEntityId=EntityId is 2 or 3?1:default;
            return EntityId is 2 or 3;
        }
    }

    [Fact]
    public void FixedTransformWorldResolver_UsesParentedCameraLocalPositiveZForViewAtLargeCoordinates(){
        FixedScalar LargeOrigin=FixedScalar.ParseInvariant("1000000000000000000000000000000");
        Transform Root=new() { PositionX=LargeOrigin };
        Transform Parent=new() {
            RotationY=0.70710678f,
            RotationW=0.70710678f,
        };
        Transform Camera=new();
        FixedTransformWorldResolver Resolver=new();

        Assert.Equal(
            FixedTransformResolveStatus.Success,
            Resolver.Resolve(3,TryGetTransform,TryGetParent,out FixedAffineTransform CameraWorld));
        Assert.Equal(
            FixedTransformStatus.Success,
            CameraWorld.TryGetCameraBasis(out Vector3 Forward,out Vector3 Up));
        Assert.Equal(
            FixedTransformStatus.Success,
            CameraWorld.TryToCameraRelativeMatrix(CameraWorld,out Matrix4x4 CameraRelative));

        RenderCamera RenderCamera=RenderCamera.CreateFirstPerson(Vector3.Zero);
        Assert.True(RenderCamera.TrySetWorldOrientation(Forward,Up));

        AssertApprox(1f,RenderCamera.GetForward().X);
        AssertApprox(0f,RenderCamera.GetForward().Y);
        AssertApprox(0f,RenderCamera.GetForward().Z);
        AssertApprox(0f,RenderCamera.GetUp().X);
        AssertApprox(1f,RenderCamera.GetUp().Y);
        AssertApprox(0f,RenderCamera.GetUp().Z);
        Assert.Equal(0f,CameraRelative.M41);
        Assert.Equal(0f,CameraRelative.M42);
        Assert.Equal(0f,CameraRelative.M43);

        bool TryGetTransform(int EntityId,out Transform Result){
            switch(EntityId){
                case 1:
                    Result=Root;
                    return true;
                case 2:
                    Result=Parent;
                    return true;
                case 3:
                    Result=Camera;
                    return true;
                default:
                    Result=default;
                    return false;
            }
        }

        static bool TryGetParent(int EntityId,out int ParentEntityId){
            ParentEntityId=EntityId switch { 2=>1,3=>2,_=>default };
            return EntityId is 2 or 3;
        }
    }

    private static void AssertApprox(float Expected,float Actual,float Tolerance=1e-4f){
        Assert.InRange(Actual,Expected-Tolerance,Expected+Tolerance);
    }
}
