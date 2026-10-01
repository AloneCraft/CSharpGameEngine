using System.Numerics;
namespace CSharpGameEngine.Numerics.Tests;
/// <summary>
/// M0096: the Scene Editor builds rotations with System.Numerics (X=R, Y=U, Z=F, yaw about U, pitch about R,
/// roll about F) and hands them to the runtime as quaternion components R, U, F, W. That is only the same
/// rotation if the engine's EulerRuf convention composes the same way.
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
        // q and -q are the same rotation.
        Assert.True(Math.Abs(Quaternion.Dot(EngineSingle,Editor))>0.99999f,$"engine {EngineSingle} editor {Editor}");
    }
}
