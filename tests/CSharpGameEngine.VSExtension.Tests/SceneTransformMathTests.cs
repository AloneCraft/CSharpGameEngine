using System.Numerics;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneTransformMathTests {
    [Fact]
    public void ToWorld_ComposesLocalBeforeParentWorld(){
        SceneTransformValue Parent=new(new Vector3(10f,0f,0f),Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)Math.PI/2f),Vector3.One);
        SceneTransformValue Child=new(new Vector3(1f,0f,0f),Quaternion.Identity,Vector3.One);

        Matrix4x4 World=SceneTransformMath.ToWorld(Child,SceneTransformMath.ToMatrix(Parent));

        Assert.Equal(10f,World.Translation.X,3);
        Assert.Equal(0f,World.Translation.Y,3);
        Assert.Equal(-1f,World.Translation.Z,3);
    }

    [Fact]
    public void TryCalculateLocal_RoundTripsThroughParentWorld(){
        SceneTransformValue Expected=new(
            new Vector3(2f,3f,4f),
            Quaternion.CreateFromYawPitchRoll(0.3f,-0.2f,0.1f),
            new Vector3(2f,1.5f,0.75f));
        Matrix4x4 Parent=Matrix4x4.CreateFromYawPitchRoll(-0.4f,0.15f,0.2f)*Matrix4x4.CreateTranslation(5f,-2f,8f);
        Matrix4x4 World=SceneTransformMath.ToWorld(Expected,Parent);

        bool Succeeded=SceneTransformMath.TryCalculateLocal(World,Parent,out SceneTransformValue Actual);

        Assert.True(Succeeded);
        AssertVector(Expected.Position,Actual.Position);
        AssertVector(Expected.Scale,Actual.Scale);
        Assert.True(Math.Abs(Quaternion.Dot(Expected.Rotation,Actual.Rotation))>0.9999f);
    }

    [Fact]
    public void TryCalculateLocal_SingularParent_ReturnsFalse(){
        Matrix4x4 Parent=Matrix4x4.CreateScale(0f,1f,1f);

        Assert.False(SceneTransformMath.TryCalculateLocal(Matrix4x4.Identity,Parent,out _));
    }

    private static void AssertVector(Vector3 Expected,Vector3 Actual){
        Assert.Equal(Expected.X,Actual.X,4);
        Assert.Equal(Expected.Y,Actual.Y,4);
        Assert.Equal(Expected.Z,Actual.Z,4);
    }
}
