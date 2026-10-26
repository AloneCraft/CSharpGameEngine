using System.Text.RegularExpressions;

namespace CSharpGameEngine.ShaderArtifacts.Tests;

public sealed class StagePresentationShaderSourceTests {
    [Fact]
    public void PresentationEntries_AvoidTheHardwareLogarithmAndExponent(){
        // M0100：GPU の log2 は近似値である。RTX 4060 Ti では 4 µm の面の放射深度に 3 % の誤差が生じ、
        // 20 km 離れた 2 つの面の順序が 5 cm の間隔で逆転した。WARP の値は正確なので、WARP テストでは検出できない。このチェックでは
        // すべてのアダプターで同じように評価される float 演算の放射深度 (RadialLog2OnePlus) を維持する。
        var Code=Regex.Replace(StagePresentationShaderSource.Source,@"//[^\n]*",string.Empty);
        Assert.DoesNotMatch(@"\b(log|log2|log10|exp|exp2|pow)\s*\(",Code);
    }
}
