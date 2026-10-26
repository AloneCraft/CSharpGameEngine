using System.Numerics;
namespace CSharpGameEngine.Numerics.Tests;
/// <summary>
/// M0096：Scene Editor は System.Numerics で回転を構築する（X=R、Y=U、Z=F、yaw は U 軸、pitch は R 軸、roll は F 軸周り）。それをクォータニオン成分 R、U、F、W としてランタイムに渡す。同じ回転になるのは、エンジンの EulerRuf 規約が同じ順序で合成する場合に限られる。
/// </summary>
public sealed class LogicalQuaternionConventionTests {
    [Theory]
    [InlineData(0f,0f,0f)]
    [InlineData(0f,1.5707964f,0f)]
    [InlineData(1.2f,0.3f,0f)]
    [InlineData(1.0607965f,1.3092037f,0.845869f)]
    [InlineData(-0.75f,2.75f,-0.4f)]
    public void EulerRuf_ComposesLikeTheEditorsYawPitchRoll(float R,float U,float F){
        Assert.True(LogicalQuaternion.TryFromEulerRuf(new LogicalVector3(LogicalScalar.FromSingle(R),LogicalScalar.FromSingle(U),LogicalScalar.FromSingle(F)),out var Engine));
        Assert.True(Engine.TryToSingle(out var EngineSingle));
        var Editor=Quaternion.CreateFromYawPitchRoll(U,R,F);
        // q と -q は同じ回転を表す。
        Assert.True(Math.Abs(Quaternion.Dot(EngineSingle,Editor))>0.99999f,$"engine {EngineSingle} editor {Editor}");
    }
}
