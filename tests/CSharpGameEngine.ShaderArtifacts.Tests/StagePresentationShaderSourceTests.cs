using System.Text.RegularExpressions;

namespace CSharpGameEngine.ShaderArtifacts.Tests;

public sealed class StagePresentationShaderSourceTests {
    [Fact]
    public void PresentationEntries_AvoidTheHardwareLogarithmAndExponent(){
        // M0100: a GPU's log2 is an approximation. On an RTX 4060 Ti it put a 4 µm surface's radial depth 3 % off and
        // reordered surfaces 5 cm apart at 20 km. WARP's is exact, so no WARP test can see it; this check keeps the
        // radial depth on float arithmetic (RadialLog2OnePlus) that every adapter evaluates alike.
        var Code=Regex.Replace(StagePresentationShaderSource.Source,@"//[^\n]*",string.Empty);
        Assert.DoesNotMatch(@"\b(log|log2|log10|exp|exp2|pow)\s*\(",Code);
        Assert.Contains("float RadialLog2OnePlus(float x)",Code,StringComparison.Ordinal);
    }
}
