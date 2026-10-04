using System.Globalization;
using CSharpGameEngine.SceneAnalysis.Json;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// The VSIX (.NET Framework) writes a float in scene JSON with the same text as the tools (.NET). .NET Framework rounds a
/// float to nine digits before it rounds to fewer, so formatting the float itself wrote 5.0000052 as "5.0000053": the
/// same value, a different text, and every Scene Designer edit rewrote such numbers elsewhere in the document.
/// </summary>
public sealed class SceneJsonNumbersRuntimeTests {
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
