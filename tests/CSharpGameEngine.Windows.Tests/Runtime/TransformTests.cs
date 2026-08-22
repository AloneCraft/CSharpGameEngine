using System.Numerics;
using CSharpGameEngine.Entities;
using Logical=CSharpGameEngine.Numerics.Logical256;
using CSharpGameEngine.Runtime.Windows;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class TransformTests {
    [Fact]
    public void NewTransform_IsIdentity(){
        Transform Transform=new();

        Assert.Equal(0f,(float)Transform.Position.R);
        Assert.Equal(0f,(float)Transform.Position.U);
        Assert.Equal(0f,(float)Transform.Position.F);
        Assert.Equal(0f,(float)Transform.Rotation.R);
        Assert.Equal(0f,(float)Transform.Rotation.U);
        Assert.Equal(0f,(float)Transform.Rotation.F);
        Assert.Equal(1f,(float)Transform.Scale.R);
        Assert.Equal(1f,(float)Transform.Scale.U);
        Assert.Equal(1f,(float)Transform.Scale.F);
    }

    [Fact]
    public void ToWorld_ComposesChildLocalTransformWithParentWorldTransform(){
        Transform Parent=new() { Position=new Logical.Vector3(10f,0f,0f),Scale=new Logical.Vector3(2f,2f,2f) };
        Transform Child=new() { Position=new Logical.Vector3(3f,1f,0f) };

        var World=TransformMath.ToWorld(Child,TransformMath.ToMatrix(Parent));

        Assert.True(Matrix4x4.Decompose(World,out var Scale,out var Rotation,out var Position));
        Assert.Equal(new Vector3(2f),Scale);
        Assert.Equal(Quaternion.Identity,Rotation);
        Assert.Equal(new Vector3(16f,2f,0f),Position);
    }

    [Fact]
    public void TryCalculateLocal_PreservesWorldTransformWhenReparented(){
        Transform OldParent=new() { Position=new Logical.Vector3(10f,0f,0f),Scale=new Logical.Vector3(2f,2f,2f) };
        Transform Child=new() { Position=new Logical.Vector3(3f,1f,0f) };
        var World=TransformMath.ToWorld(Child,TransformMath.ToMatrix(OldParent));
        Transform NewParent=new() { Position=new Logical.Vector3(-4f,2f,0f),Scale=new Logical.Vector3(0.5f,0.5f,0.5f) };

        Assert.True(TransformMath.TryCalculateLocal(World,TransformMath.ToMatrix(NewParent),out var Local));
        Assert.Equal(World,TransformMath.ToWorld(Local,TransformMath.ToMatrix(NewParent)));
    }

    [Fact]
    public void TryCalculateLocal_RejectsNonInvertibleParent(){
        Transform Parent=new() { Scale=new Logical.Vector3(0f,1f,1f) };

        Assert.False(TransformMath.TryCalculateLocal(Matrix4x4.Identity,TransformMath.ToMatrix(Parent),out _));
    }

    [Fact]
    public void TryToCameraRelativeMatrix_SubtractsFixedOriginBeforeFloatConversion(){
        Logical.Scalar LargeOrigin=Logical.Scalar.ParseInvariant("1000000000000000000000000000000");
        Assert.True(Logical.Scalar.TryAdd(
            LargeOrigin,
            Logical.Scalar.ParseInvariant("0.125"),
            out Logical.Scalar NearbyPosition));

        Transform Camera=new() { Position=new Logical.Vector3(LargeOrigin,0f,0f) };
        Transform Object=new() { Position=new Logical.Vector3(NearbyPosition,0f,0f) };

        FixedTransformStatus Status=TransformMath.TryToCameraRelativeMatrix(
            Object,Camera,out Matrix4x4 Matrix);

        Assert.Equal(FixedTransformStatus.Success,Status);
        Assert.Equal(0.125f,Matrix.M41);
        Assert.Equal(0f,Matrix.M42);
        Assert.Equal(0f,Matrix.M43);
    }
    [Fact]
    public void FixedTransformWorldResolver_ComposesHierarchyBeforeCameraRelativeFloatConversion(){
        Logical.Scalar LargeOrigin=Logical.Scalar.ParseInvariant("1000000000000000000000000000000");
        Transform Parent=new() { Position=new Logical.Vector3(LargeOrigin,0f,0f) };
        Transform Child=new() {
            Position=new Logical.Vector3(Logical.Scalar.ParseInvariant("0.125"),2f,0f),
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
        Logical.Scalar LargeOrigin=Logical.Scalar.ParseInvariant("1000000000000000000000000000000");
        Transform Root=new() { Position=new Logical.Vector3(LargeOrigin,0f,0f) };
        Transform Camera=new() { Position=new Logical.Vector3(2f,3f,0f) };
        Transform Object=new() { Position=new Logical.Vector3(2.125f,4f,0f) };
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
        Logical.Scalar LargeOrigin=Logical.Scalar.ParseInvariant("1000000000000000000000000000000");
        Transform Root=new() { Position=new Logical.Vector3(LargeOrigin,0f,0f) };
        Transform Parent=new() {
            Rotation=new Logical.Vector3(0f,MathF.PI/2f,0f),
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
