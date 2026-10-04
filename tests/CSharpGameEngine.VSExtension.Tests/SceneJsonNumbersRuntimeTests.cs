using System.Globalization;
using CSharpGameEngine.SceneAnalysis.Json;
using CSharpGameEngine.SceneAnalysis.Tests;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// The VSIX (.NET Framework) writes numbers in scene JSON with the same text as the tools (.NET). .NET Framework rounds
/// to more digits before it rounds to fewer (5.0000052 became "5.0000053"), rounds ties away from zero (312985.125
/// became "312985.13", .NET writes "312985.12") and does not parse doubles correctly (some of its double texts read back
/// as another double): the same value, a different text, and every Scene Designer edit rewrote such numbers elsewhere.
/// </summary>
public sealed class SceneJsonNumbersRuntimeTests {
    /// <summary>The texts of the shared samples, which SceneModel.Tests compares one by one with .NET's formatting.</summary>
    [Fact]
    public void SampleDigest_IsTheSameAsOnDotNet()=>Assert.Equal(SceneJsonNumberSamples.Digest,SceneJsonNumberSamples.ComputeDigest());

    [Theory]
    [InlineData(312985.125f,"312985.12")]
    [InlineData(1595310.25f,"1595310.2")]
    [InlineData(312985.375f,"312985.38")]
    public void SingleTies_GoToEvenAsOnDotNet(float Value,string Expected)=>Assert.Equal(Expected,SceneJsonNumbers.FormatSingle(Value));

    [Theory]
    [InlineData(593.2224905685277,"593.2224905685277")]
    [InlineData(-2.2272678626374972E-281,"-2.2272678626374972E-281")]
    [InlineData(0.3333333333333333,"0.3333333333333333")]
    public void DoubleSpelling_IsTheSameAsOnDotNet(double Value,string Expected)=>Assert.Equal(Expected,SceneJsonNumbers.FormatOther(Value));

    [Fact]
    public void AngleTies_GoToEvenAsOnDotNet()=>Assert.Equal("311.64062",SceneJsonAngles.FormatDegrees(311.640625f,SceneAngleUnit.Degrees));

    [Theory]
    [InlineData(5.0000052f,"5.0000052")]
    [InlineData(24.896297f,"24.896297")]
    [InlineData(18.62638f,"18.62638")]
    [InlineData(0.2f,"0.2")]
    [InlineData(3.0201263f,"3.0201263")]
    [InlineData(44.765083f,"44.765083")]
    [InlineData(-5.722046E-06f,"-5.722046E-06")]
    [InlineData(-0f,"0")]
    public void SingleSpelling_IsTheSameAsOnDotNet(float Value,string Expected){
        var Text=SceneJsonNumbers.FormatSingle(Value);
        Assert.Equal(Expected,Text);
        Assert.Equal(Value==0f?0f:Value,float.Parse(Text,NumberStyles.Float,CultureInfo.InvariantCulture));
    }
}
