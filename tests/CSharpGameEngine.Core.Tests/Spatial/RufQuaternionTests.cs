using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md の「Quaternion仕様」。プリセットに依存せず、<see cref="RufQuaternion"/> は R/U/F の三つ組を直接回転する。この三つ組を、アクティブな <see cref="ICoordinateConvention"/> の左右系に関係なく、自己整合する右手系基底として扱う。
/// </summary>
public class RufQuaternionTests {
    [Fact]
    public void Identity_DoesNotRotateVector(){
        var Rotated=RufQuaternion.RotateVector(RufQuaternion.Identity,RufVector.Forward);

        Assert.Equal(RufVector.Forward.R,Rotated.R,precision:5);
        Assert.Equal(RufVector.Forward.U,Rotated.U,precision:5);
        Assert.Equal(RufVector.Forward.F,Rotated.F,precision:5);
    }

    [Fact]
    public void FromAxisAngle_RotatesAroundAxisByRightHandRule(){
        var Rotation=RufQuaternion.FromAxisAngle(RufVector.Forward,MathF.PI/2f);

        var Rotated=RufQuaternion.RotateVector(Rotation,RufVector.Right);

        // F 軸周りの右手系の法則：R は U の方向へ回転する（Quaternion.CreateFromAxisAngle を内部の右手系基底として扱う (R, U, F) の組に適用した場合と一致する）
        // 場合）。
        Assert.Equal(0f,Rotated.R,precision:4);
        Assert.Equal(1f,Rotated.U,precision:4);
        Assert.Equal(0f,Rotated.F,precision:4);
    }

    [Fact]
    public void Multiply_AppliesChildThenParent(){
        var Parent=RufQuaternion.FromAxisAngle(RufVector.Up,MathF.PI/2f);
        var Child=RufQuaternion.FromAxisAngle(RufVector.Right,MathF.PI/4f);
        RufVector V=new(0.3f,0.6f,0.8f);

        var Expected=RufQuaternion.RotateVector(Parent,RufQuaternion.RotateVector(Child,V));
        var Actual=RufQuaternion.RotateVector(Parent*Child,V);

        Assert.Equal(Expected.R,Actual.R,precision:4);
        Assert.Equal(Expected.U,Actual.U,precision:4);
        Assert.Equal(Expected.F,Actual.F,precision:4);
    }

    [Fact]
    public void LookRotation_ForwardMapsToRequestedDirection(){
        var Forward=new RufVector(1f,1f,1f).Normalize();
        var Rotation=RufQuaternion.LookRotation(Forward,RufVector.Up);

        var RotatedForward=RufQuaternion.RotateVector(Rotation,RufVector.Forward);

        Assert.Equal(Forward.R,RotatedForward.R,precision:4);
        Assert.Equal(Forward.U,RotatedForward.U,precision:4);
        Assert.Equal(Forward.F,RotatedForward.F,precision:4);
    }

    [Fact]
    public void LookRotation_ForwardParallelToUp_FallsBackDeterministically(){
        var First=RufQuaternion.LookRotation(RufVector.Up,RufVector.Up);
        var Second=RufQuaternion.LookRotation(RufVector.Up,RufVector.Up);

        Assert.Equal(First.R,Second.R,precision:5);
        Assert.Equal(First.U,Second.U,precision:5);
        Assert.Equal(First.F,Second.F,precision:5);
        Assert.Equal(First.W,Second.W,precision:5);

        Assert.True(float.IsFinite(First.R)&&float.IsFinite(First.U)&&float.IsFinite(First.F)&&float.IsFinite(First.W));
    }

    [Fact]
    public void LookRotation_ForwardParallelToCanonicalUp_FallsBackToRightWithoutNaN(){
        var Rotation=RufQuaternion.LookRotation(RufVector.Up,RufVector.Zero);

        Assert.True(float.IsFinite(Rotation.R)&&float.IsFinite(Rotation.U)&&float.IsFinite(Rotation.F)&&float.IsFinite(Rotation.W));
    }

    [Fact]
    public void LookRotation_ZeroForward_ThrowsSpatialConfigurationException()
        =>Assert.Throws<SpatialConfigurationException>(()=>RufQuaternion.LookRotation(RufVector.Zero,RufVector.Up));
}
