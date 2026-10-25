using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// ADR-0084 §6: the logarithmic depth of a perspective camera against the double-precision oracle of
/// <see cref="DepthOracle"/>, on WARP and on every run. A quad facing the camera is drawn from 4 µm to 5e18 m; in the
/// middle of the view every pixel's depth is within 4 ulps of the expected one, and no pixel of a surface reaches the
/// 1.0 the depth buffer is cleared to (the sky composite and the far discard rely on that). WARP evaluates the shader
/// arithmetic exactly, so <see cref="RadialDepthPrecisionEvidence"/> repeats the measurement on the GPU for the
/// hardware's own error.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class LogarithmicDepthOracleTests {
    [WindowsOnlyTheory]
    [Trait("Category","GPU")]
    [InlineData(4e-6f,1e-7f,5e-5f)]
    [InlineData(0.5f,0.1f,300f)]
    [InlineData(50f,0.1f,1000f)]
    [InlineData(2e4f,0.1f,3e4f)]
    [InlineData(1e15f,0.1f,2e15f)]
    [InlineData(5e18f,0.1f,1e19f)]
    public void Depth_IsWithinFourUlpsOfTheDoubleOracleInTheMiddleOfTheView(float Distance,float Near,float Far){
        var Measurement=DepthOracle.Measure(Hardware:false,Distance,Near,Far);
        Assert.True(Measurement.Centre<=4d,$"The depth at {Distance} m is {Measurement.Centre:F1} ulps off in the middle of the view.");
        Assert.True(Measurement.DistanceRelativeError<=3e-6d,$"Physical scene distance at {Distance} m: relative error {Measurement.DistanceRelativeError:R}.");
        Assert.True(Measurement.MaximumDepth<1f,$"The depth at {Distance} m reaches {Measurement.MaximumDepth}, the clear value.");
    }
}
