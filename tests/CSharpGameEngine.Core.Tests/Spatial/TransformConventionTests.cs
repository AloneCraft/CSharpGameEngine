using System.Numerics;
using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md "Transform Tests", against
/// <see cref="StandardTransformConvention"/>. Preset-independent: <see cref="ITransformConvention"/> is
/// orthogonal to <see cref="ICoordinateConvention"/>, so these tests never reference Unity/Unreal/Blender.
/// </summary>
public class TransformConventionTests {
    [Fact]
    public void Compose_Identity_ProducesIdentityMatrix(){
        Matrix4x4 Matrix=StandardTransformConvention.Compose(RufVector.Zero,RufQuaternion.Identity,new RufVector(1f,1f,1f));

        Assert.Equal(Matrix4x4.Identity,Matrix);
    }

    [Fact]
    public void Compose_TranslationOnly_MovesPointByPosition(){
        RufVector Position=new(5f,-2f,10f);
        Matrix4x4 Matrix=StandardTransformConvention.Compose(Position,RufQuaternion.Identity,new RufVector(1f,1f,1f));

        RufVector Transformed=StandardTransformConvention.TransformPoint(Matrix,RufVector.Zero);

        AssertApproximatelyEqual(Position,Transformed);
    }

    [Fact]
    public void Compose_RotationOnly_RotatesPointAroundOrigin(){
        RufQuaternion Rotation=RufQuaternion.FromAxisAngle(RufVector.Up,MathF.PI/2f);
        Matrix4x4 Matrix=StandardTransformConvention.Compose(RufVector.Zero,Rotation,new RufVector(1f,1f,1f));

        RufVector Transformed=StandardTransformConvention.TransformPoint(Matrix,RufVector.Right);
        RufVector Expected=RufQuaternion.RotateVector(Rotation,RufVector.Right);

        AssertApproximatelyEqual(Expected,Transformed);
    }

    [Fact]
    public void Compose_UniformScale_ScalesPointFromOrigin(){
        Matrix4x4 Matrix=StandardTransformConvention.Compose(RufVector.Zero,RufQuaternion.Identity,new RufVector(2f,2f,2f));

        RufVector Transformed=StandardTransformConvention.TransformPoint(Matrix,RufVector.Right);

        AssertApproximatelyEqual(RufVector.Right*2f,Transformed);
    }

    [Fact]
    public void Compose_NonUniformScale_ScalesEachAxisIndependently(){
        RufVector Scale=new(2f,3f,4f);
        Matrix4x4 Matrix=StandardTransformConvention.Compose(RufVector.Zero,RufQuaternion.Identity,Scale);

        RufVector Transformed=StandardTransformConvention.TransformPoint(Matrix,new RufVector(1f,1f,1f));

        AssertApproximatelyEqual(Scale,Transformed);
    }

    [Fact]
    public void Compose_AppliesScaleThenRotationThenTranslation(){
        RufVector Scale=new(2f,1f,1f);
        RufQuaternion Rotation=RufQuaternion.FromAxisAngle(RufVector.Up,MathF.PI/2f);
        RufVector Position=new(10f,0f,0f);
        Matrix4x4 Matrix=StandardTransformConvention.Compose(Position,Rotation,Scale);

        RufVector Point=RufVector.Right;
        RufVector ScaledPoint=new(Point.R*Scale.R,Point.U*Scale.U,Point.F*Scale.F);
        RufVector Expected=RufQuaternion.RotateVector(Rotation,ScaledPoint)+Position;
        RufVector Actual=StandardTransformConvention.TransformPoint(Matrix,Point);

        AssertApproximatelyEqual(Expected,Actual);
    }

    [Fact]
    public void Combine_ChainsChildThroughParent_SameAsApplyingBothTransformsInOrder(){
        Matrix4x4 ParentWorld=StandardTransformConvention.Compose(new RufVector(5f,0f,0f),RufQuaternion.FromAxisAngle(RufVector.Up,MathF.PI/2f),new RufVector(1f,1f,1f));
        Matrix4x4 Local=StandardTransformConvention.Compose(new RufVector(0f,1f,0f),RufQuaternion.Identity,new RufVector(2f,2f,2f));
        Matrix4x4 Combined=StandardTransformConvention.Combine(ParentWorld,Local);

        RufVector Point=new(1f,0f,0f);
        RufVector Expected=StandardTransformConvention.TransformPoint(ParentWorld,StandardTransformConvention.TransformPoint(Local,Point));
        RufVector Actual=StandardTransformConvention.TransformPoint(Combined,Point);

        AssertApproximatelyEqual(Expected,Actual);
    }

    [Fact]
    public void TransformDirection_IgnoresTranslation(){
        Matrix4x4 Matrix=StandardTransformConvention.Compose(new RufVector(100f,50f,-25f),RufQuaternion.Identity,new RufVector(1f,1f,1f));

        RufVector Direction=StandardTransformConvention.TransformDirection(Matrix,RufVector.Right);

        AssertApproximatelyEqual(RufVector.Right,Direction);
    }

    [Fact]
    public void TransformNormal_StaysPerpendicularToSurfaceUnderNonUniformScale(){
        Matrix4x4 Matrix=StandardTransformConvention.Compose(RufVector.Zero,RufQuaternion.Identity,new RufVector(2f,1f,5f));

        RufVector TangentA=StandardTransformConvention.TransformDirection(Matrix,RufVector.Right);
        RufVector TangentB=StandardTransformConvention.TransformDirection(Matrix,RufVector.Forward);
        RufVector Normal=StandardTransformConvention.TransformNormal(Matrix,RufVector.Up);

        Assert.Equal(0f,RufVector.Dot(Normal,TangentA),precision:4);
        Assert.Equal(0f,RufVector.Dot(Normal,TangentB),precision:4);
    }

    private static void AssertApproximatelyEqual(RufVector Expected,RufVector Actual){
        Assert.Equal(Expected.R,Actual.R,precision:3);
        Assert.Equal(Expected.U,Actual.U,precision:3);
        Assert.Equal(Expected.F,Actual.F,precision:3);
    }
}
