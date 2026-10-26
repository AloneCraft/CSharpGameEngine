using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// ADR-0084 §6：WARP上および各実行で、透視カメラの対数深度を倍精度の<see cref="DepthOracle"/>と比較する。
/// カメラ向きのクアッドを4 µmから5e18 mまで描画する。ビュー中央では全ピクセルが期待値から4 ULP以内であり、
/// クリア値1.0に達するサーフェスはない（空の合成と遠方破棄がこの性質に依存する）。
/// WARPはシェーダー演算を正確に評価するため、<see cref="RadialDepthPrecisionEvidence"/>でGPU上でも測定し、ハードウェア固有の誤差を調べる。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class LogarithmicDepthOracleTests {
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ObjectInitializer_WritesLogarithmicDepthWithoutAFactory(){
        var Measurement=DepthOracle.Measure(Hardware:false,Distance:50f,Near:0.1f,Far:1e18f,UseFactory:false);
        Assert.True(Measurement.Centre<=4d,$"Object initializer depth error: {Measurement.Centre:R} ulps.");
        Assert.True(Measurement.MaximumDepth<1f);
    }
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
