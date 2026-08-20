using System.Numerics;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class TransformTests {
    [Fact]
    public void NewTransform_IsIdentity(){
        Transform Transform=new();

        Assert.Equal(0f,Transform.PositionX);
        Assert.Equal(0f,Transform.PositionY);
        Assert.Equal(0f,Transform.PositionZ);
        Assert.Equal(0f,Transform.RotationX);
        Assert.Equal(0f,Transform.RotationY);
        Assert.Equal(0f,Transform.RotationZ);
        Assert.Equal(1f,Transform.RotationW);
        Assert.Equal(1f,Transform.ScaleX);
        Assert.Equal(1f,Transform.ScaleY);
        Assert.Equal(1f,Transform.ScaleZ);
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
}
